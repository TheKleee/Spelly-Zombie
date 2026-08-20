using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The one shared ink pool. Closed for PotPrepSeconds, then opens full.
    /// Black pot refills wizard wands; acolyte presence corrupts it green.
    /// Green pot drains, wizards defuse, acolytes refill. Authority simulates; clients mirror via NetSync.
    public class CauldronEconomy : MonoBehaviour
    {
        [Header("the art — dragged in, never generated")]
        [Tooltip("The liquid ink object inside your cauldron model. Its Y scale becomes the ink level; its colour becomes the owning team's (black/green). Your weighted liquid ball works — it gets tinted and squashed like anything else. Leave empty and the pot still WORKS, it just shows nothing inside.")]
        public Transform InkSurface;

        [Tooltip("The BOWL mesh of your cauldron (the MeshFilter with the hollow). Needed so the liquid stays INSIDE once the pot has been lifted: lifting makes the main collider convex — a bowl with no hollow — and this spawns a follower carrying the true concave shape that only the liquid's physics feels. Leave empty and the pot behaves as before (fine until someone lifts it).")]
        public MeshFilter Bowl;

        [Tooltip("YOUR ink material (black). Start from MI_CauldronInk_SZ in Art/3D/Materials and edit freely — the code only ever swaps between these two assets, it never builds a material of its own.")]
        public Material InkMaterial;
        [Tooltip("YOUR corrupted ink material (green). MI_CauldronInkCorrupt_SZ is the starter. Empty = the black one gets a green tint via property block instead.")]
        public Material CorruptInkMaterial;

        VesselShell _shell;   // the true-bowl follower, when the Bowl slot is filled

        Renderer[] _inkRends;   // the ink's renderers, found once

        /// 0 = black, 1 = green; rises while corruption progresses, falls during a defuse.
        public float Greenness { get; private set; }

        bool _lobby;
        float _lobbySkyIn = -1f;  // lobby: seconds until the ink falls from the sky
        bool _cometSent;          // comet spawned; ink lands when it arrives
        bool _warnedMortal;

        /// The endgame puddle at the InkGrave, built by Ground() when every cauldron is broken.
        [System.NonSerialized] public bool Grounded;

        Damageable _hp;

        void Awake()
        {
            // Subscribed once for the object's lifetime; SetActive cycles must
            // not stack handlers. All Damageables under the pot count.
            _hp = GetComponentInChildren<Damageable>(true);
            var mine = new System.Collections.Generic.HashSet<Damageable>(
                GetComponentsInChildren<Damageable>(true));
            foreach (var d in mine)
                d.OnDeath += _ => OnPotBroken();
            // the Damageable may also sit above this component
            var above = GetComponentInParent<Damageable>();
            if (above != null && !mine.Contains(above))
            {
                if (_hp == null) _hp = above;
                above.OnDeath += _ => OnPotBroken();
            }
        }

        public static CauldronEconomy Active { get; private set; }

        // authority truth (solo/host); clients mirror the synced copies below
        float _ink;              // ink units, 0..PotCapacityInk
        bool _corrupt;
        bool _open;
        float _prep;
        float _corruptTouch, _defuse;

        // client mirrors, written by NetSync.ApplyPot
        static float _syncFill = -1f;
        static bool _syncCorrupt;
        static float _syncPrep;
        public static void ApplyNet(float fill01, bool corrupt, float prep)
        {
            _syncFill = fill01;
            _syncCorrupt = corrupt;
            _syncPrep = prep;
        }

        /// Current truth for anyone asking (HUD, bots): 0..1 and the owner.
        public static float Fill01 => Active == null ? 1f
            : NetGame.IsAuthority ? (Active._open ? Active._ink / Mathf.Max(1f, DrawingConfig.PotCapacityInk) : 0f)
            : Mathf.Max(0f, _syncFill);
        public static bool IsCorrupt => Active == null ? false
            : NetGame.IsAuthority ? Active._corrupt : _syncCorrupt;
        public static float PrepRemaining => Active == null ? 0f
            : NetGame.IsAuthority ? Active._prep : _syncPrep;

        Vector3 _surfaceScale0;
        MaterialPropertyBlock _blk;
        float _pushTimer, _drinkBill, _billTimer;

        /// Every pot in the scene. With two or more, the ink hops between them.
        public static readonly List<CauldronEconomy> All = new List<CauldronEconomy>();

        void OnEnable()
        {
            All.Add(this);
            // first pot awake holds the ink; later ones are dormant until the hop picks them
            if (Active == null) Active = this;

            _prep = DrawingConfig.PotPrepSeconds;
            _open = false;
            _corrupt = false;
            _ink = 0f;
            if (InkSurface != null) _surfaceScale0 = InkSurface.localScale;
            _blk = new MaterialPropertyBlock();

            // concave-bowl follower so the liquid stays inside once lifted (see VesselShell)
            if (Bowl != null && _shell == null)
                _shell = VesselShell.Attach(Bowl.transform, Bowl.sharedMesh, transform, InkSurface);

        }

        void OnDisable()
        {
            All.Remove(this);
            if (Active == this) Active = null;
            // no _hopGap reset here: GapTick must still land mid-flight ink
            // after the last pot dies. Scene changes reset it in GapTick.
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // read every frame: RoundDirector.Instance can appear after OnEnable
            _lobby = RoundDirector.InLobby;
            if (NetGame.IsAuthority) Simulate(dt);
            LocalWandTick(dt);

            CauldronHUD.Fill = Fill01;
            CauldronHUD.Corrupt = IsCorrupt;
            CauldronHUD.TimerSeconds = VacuumRemaining >= 0f ? VacuumRemaining
                : _lobbySkyIn > 0f ? _lobbySkyIn
                : PrepRemaining > 0f ? PrepRemaining : -1f;
            PaintSurface();
            FlowTick(dt);

            if (!_lobby && !Grounded && !_warnedMortal)
            {
                _warnedMortal = true;
                if (_hp == null)
                    Debug.LogWarning($"[SpellyZombie] Cauldron '{name}' has no Damageable — it can never " +
                        "shatter, the ink can never flee, and the InkGrave endgame can never happen. Add " +
                        "Damageable (big HP — 'more durable') + Breakable to the prefab.", this);
            }
        }

        // ------------------------------------------- the ink flees (breaks) --
        static float _hopTimer;
        static bool _hopGap;       // the 10s vacuum after a shatter: no pot pours
        static float _fleeInk;     // ink carried mid-flight
        static bool _fleeCorrupt;  // breaking never cleanses
        static Vector3 _lastBrokenAt;

        /// Seconds left in the vacuum, -1 when none.
        public static float VacuumRemaining => _hopGap ? Mathf.Max(0f, _hopTimer) : -1f;

        /// Counts down the post-shatter vacuum, then lands the ink in a random
        /// surviving pot, or grounds it at the InkGrave when none survive.
        /// Driven from SideBootstrap: the last pot's death must still land the ink.

        public static void GapTick(float dt)
        {
            if (!NetGame.IsAuthority || !_hopGap) return;
            if (RoundDirector.InLobby) { _hopGap = false; return; } // lobby falls its own way

            _hopTimer -= dt;
            if (_hopTimer > 0f) return;
            _hopGap = false;

            // the comet chases a surviving pot; ink lands on arrival, not on a timer
            if (All.Count == 0) { Ground(); return; }
            var pick = All[Random.Range(0, All.Count)];
            SkyBeam.Down(pick.transform,
                _fleeCorrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor,
                () => LandMatchInk(pick));
        }

        /// Comet arrival mid-match: the fled ink moves in. If the target died
        /// during the fall, another survivor takes it, or the ink grounds.
        static void LandMatchInk(CauldronEconomy pot)
        {
            var next = pot != null && All.Contains(pot) ? pot
                : All.Count > 0 ? All[Random.Range(0, All.Count)] : null;
            if (next == null) { Ground(); return; }
            next._ink = Mathf.Max(_fleeInk, 1f);
            next._corrupt = _fleeCorrupt;
            next._defuse = 0f;
            next._prep = 0f;
            next._open = true;
            Active = next;
            if (FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Splash, next.transform.position + Vector3.up * 0.6f,
                    next._corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor);
            Juice.Chime(next.transform.position);
        }

        /// Lobby comet arrival: the ink appears. A broken or already-open pot resets the cycle.
        void LandLobbyInk()
        {
            if (this == null) return;
            _cometSent = false;
            _lobbySkyIn = -1f;
            var grave = GetComponent<LobbyRespawn>();
            if ((grave != null && grave.Hidden) || !isActiveAndEnabled)
            {
                // pot died mid-fall: the ink grounds at the map center instead
                _fleeInk = DrawingConfig.PotCapacityInk;
                _fleeCorrupt = false;
                Ground();
                return;
            }
            if (_open) return;
            _open = true;
            _ink = DrawingConfig.PotCapacityInk;
            _corrupt = false;
            _defuse = 0f;
            if (FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Splash, transform.position + Vector3.up * 0.6f,
                    DrawingConfig.InkColor);
            Juice.Chime(transform.position);
        }

        /// Every pot broken: the ink pools at the InkGrave and can never move
        /// again. All pot rules keep running on the puddle.
        static void Ground()
        {
            Vector3 at;
            if (InkGrave.I != null) at = InkGrave.I.transform.position;
            else
            {
                Debug.LogError("[SpellyZombie] Every cauldron is broken but this map has NO InkGrave " +
                    "marker! Add an empty GameObject with the InkGrave component at the arena center. " +
                    "Using the last pot's grave instead.");
                at = _lastBrokenAt;
            }

            var go = new GameObject("~GroundedInk");
            go.transform.position = at;
            var pool = go.AddComponent<CauldronEconomy>();
            pool.Grounded = true;
            pool._ink = Mathf.Max(_fleeInk, 1f);
            pool._corrupt = _fleeCorrupt;
            pool._open = true;
            pool._prep = 0f;
            Active = pool;

            // the authored InkGrave blob becomes the pool's ink surface
            if (InkGrave.I != null)
            {
                InkGrave.I.Reveal();
                pool.InkSurface = InkGrave.I.transform;
                pool._surfaceScale0 = InkGrave.I.transform.localScale;
            }

            Color c = pool._corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            SkyBeam.Down(at, c);
            if (FxLibrary.I != null) FxLibrary.SpawnTinted(FxLibrary.I.Splash, at + Vector3.up * 0.3f, c);
            Juice.Thud(at);
            DrawingWorld.Instance?.LogEvent("no cauldron left. the ink pools at the heart of the map");
        }

        /// Lobby pot: resets for a sky-fall rebirth. Match pot: gone for good;
        /// if it held the ink, the ink flees.
        void OnPotBroken()
        {
            _lastBrokenAt = transform.position;
            if (_lobby)
            {
                // LobbyRespawn rebuilds the pot; the ink falls back after it stands again
                if (_open && _ink > 0.5f)
                    SkyBeam.Up(transform.position,
                        _corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor);
                _open = false; _ink = 0f; _corrupt = false;
                _defuse = 0f; _corruptTouch = 0f;
                _lobbySkyIn = -1f;
                LobbyRespawn.Take(gameObject, DrawingConfig.LobbyRespawnSeconds);
                return;
            }
            if (!NetGame.IsAuthority) return; // clients mirror via PushNet
            if (Active == this)
            {
                _fleeInk = Mathf.Max(_ink, 1f);
                _fleeCorrupt = _corrupt;
                SkyBeam.Up(transform.position,
                    _corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor);
                Active = null;
                _hopGap = true;
                _hopTimer = DrawingConfig.PotHopGapSeconds;
                DrawingWorld.Instance?.LogEvent("the cauldron shatters. the ink flees");
            }
            _open = false;
        }

        // ------------------------------------------------------- authority --
        void Simulate(float dt)
        {
            // dormant vessels simulate nothing
            if (Active != this) { _open = false; return; }

            // the vacuum freezes the sim; without this return the prep path
            // below would reopen the pot at full capacity mid-gap
            if (_hopGap) { PushNet(); return; }

            // lobby pot: no prep, every rule runs for real; refills from the sky when dry
            if (_lobby && !_hopGap)   // the vacuum outranks the lobby refill
            {
                _prep = 0f;

                // empty lobby pot: the ink falls from the sky after LobbyPotRefreshSeconds
                if (!_open)
                {
                    // LobbyRespawn hides without disabling; the timer counts only once the pot stands again
                    var grave = GetComponent<LobbyRespawn>();
                    if (grave != null && grave.Hidden) { PushNet(); return; }

                    if (_lobbySkyIn < 0f)
                    {
                        _lobbySkyIn = DrawingConfig.LobbyPotRefreshSeconds;
                        _cometSent = false;
                    }
                    _lobbySkyIn -= dt;
                    if (_lobbySkyIn > 0f) { PushNet(); return; }
                    if (!_cometSent)
                    {
                        _cometSent = true;
                        SkyBeam.Down(transform, DrawingConfig.InkColor, LandLobbyInk);
                    }
                    PushNet();
                    return; // an empty vessel until the comet actually lands
                }
                // refill only when truly dry, never periodic; half a unit counts as dry
                if (_ink <= 0.5f)
                {
                    _ink = 0f;
                    _open = false;
                    _corrupt = false;
                    _defuse = 0f;
                    Greenness = 0f;
                    _lobbySkyIn = -1f; // the empty-vessel path arms the comet
                    DrawingWorld.Instance?.LogEvent("the pot ran dry. new ink will fall from the sky");
                }
            }

            if (!_open)
            {
                _prep -= dt;
                if (_prep <= 0f)
                {
                    _prep = 0f;
                    _open = true;
                    _ink = DrawingConfig.PotCapacityInk; // opens full

                    // opening wipes all active zombies
                    for (int i = Zombie.All.Count - 1; i >= 0; i--)
                    {
                        var z = Zombie.All[i];
                        if (z != null)
                            z.GetComponent<Damageable>()?.TakeDamage(99999f, "the cauldron awakens");
                    }

                    Juice.Chime(transform.position);
                    DrawingWorld.Instance?.LogEvent("the cauldron opens");
                }
                PushNet();
                return;
            }

            if (_corrupt)
            {
                // greenness runs 1 -> 0 as the defuse holds; PaintSurface lerps the colour by it
                Greenness = 1f - Mathf.Clamp01(_defuse / Mathf.Max(0.1f, DrawingConfig.PotDefuseSeconds));

                float defusers = 0f, babysitters = 0f;
                EachPlayer((side, pos) =>
                {
                    float d = Vector3.Distance(pos, transform.position);
                    if (side == Side.Wizard && d <= DrawingConfig.PotCloseRadius) defusers += 1f;
                    if (side == Side.Acolyte && d <= DrawingConfig.PotAcolyteFillRadius) babysitters += 1f;
                });

                // tended green pot grows, abandoned one evaporates; rates are
                // per one player, split by the acolyte headcount
                if (babysitters > 0f)
                    _ink = Mathf.Min(
                        _ink + DrawingConfig.PotAcolyteFillPerSec * babysitters * dt
                            / Mathf.Max(1, Sides.CountOn(Side.Acolyte)),
                        DrawingConfig.PotCapacityInk);
                else
                    _ink -= DrawingConfig.PotCorruptDrainPerSec * dt
                        / Mathf.Max(1, Sides.CountOn(Side.Acolyte));

                if (defusers > 0f)
                {
                    _defuse += dt; // more wizards don't defuse faster
                    if (_defuse >= DrawingConfig.PotDefuseSeconds)
                    {
                        _corrupt = false;
                        _defuse = 0f;
                        Juice.Chime(transform.position);
                        DrawingWorld.Instance?.LogEvent("the cauldron is BLACK again");
                    }
                }
                else _defuse = 0f; // stepping off resets the defuse
            }
            else
            {
                // greenness 0 -> 1 while corruption progresses
                Greenness = Mathf.Clamp01(_corruptTouch / Mathf.Max(0.1f, DrawingConfig.PotCorruptSeconds));

                // an acolyte at a black pot for the plant time turns it
                float touching = 0f;
                EachPlayer((side, pos) =>
                {
                    if (side == Side.Acolyte
                        && Vector3.Distance(pos, transform.position) <= DrawingConfig.PotCloseRadius)
                        touching += 1f;
                });
                // zombies corrupt too, slower (PotZombieCorruptFactor);
                // presence-based, demons excluded
                bool zombieTouch = false;
                foreach (var z in Zombie.All)
                {
                    if (z == null || z.IsDemon) continue;
                    if (Vector3.Distance(z.transform.position, transform.position)
                        <= DrawingConfig.PotCloseRadius) { zombieTouch = true; break; }
                }

                if (touching > 0f) _corruptTouch += dt;                    // full speed
                else if (zombieTouch) _corruptTouch += dt * DrawingConfig.PotZombieCorruptFactor;
                else _corruptTouch = 0f;

                if (_corruptTouch >= DrawingConfig.PotCorruptSeconds)
                {
                    _corrupt = true;
                    _corruptTouch = 0f;
                    _defuse = 0f;
                    Juice.Thud(transform.position);
                    DrawingWorld.Instance?.LogEvent("the cauldron turns GREEN");
                }

                // the spill: the local wizard loitering close with a full wand;
                // remote machines bill their own refills through PotDrink
                var p = LocalPlayer();
                if (p != null && Sides.Of(Grimoire.LocalPlayerId) == Side.Wizard)
                {
                    var ink = p.GetComponent<PlayerInk>();
                    if (ink != null && ink.Fraction >= 0.999f
                        && Vector3.Distance(p.transform.position, transform.position)
                            <= DrawingConfig.PotCloseRadius)
                        _ink -= DrawingConfig.PotSpillPerSec * dt
                            / Mathf.Max(1, Sides.CountOn(Side.Wizard)); // per-one-wizard, split by the team
                }
            }

            _ink = Mathf.Clamp(_ink, 0f, DrawingConfig.PotCapacityInk);
            PushNet();
        }

        /// Host bills a client's refill (PotDrink message).
        public void BillInk(float amount)
        {
            if (!NetGame.IsAuthority || !_open || _corrupt) return;
            _ink = Mathf.Max(0f, _ink - Mathf.Max(0f, amount));
        }

        void PushNet()
        {
            if (!NetGame.Connected || !NetGame.IsHost) return;
            _pushTimer -= Time.deltaTime;
            if (_pushTimer > 0f) return;
            _pushTimer = 0.5f;
            NetSync.PushPot(Fill01, _corrupt, _prep);
        }

        // ------------------------------------------- every machine, own wand --
        void LocalWandTick(float dt)
        {
            if (Active != this) return; // only the active pot feeds wands
            if (PrepRemaining > 0f || IsCorrupt || Fill01 <= 0f) return; // closed/green/dry pours nothing
            var p = LocalPlayer();
            if (p == null || Sides.Of(Grimoire.LocalPlayerId) != Side.Wizard) return;
            var ink = p.GetComponent<PlayerInk>();
            if (ink == null || ink.Fraction >= 0.999f) return; // far + full just stops drinking

            float d = Vector3.Distance(p.transform.position, transform.position);
            var wand = p.GetComponent<WandState>();
            bool melted = wand != null && !wand.HasWand;
            // a melted wand regrows only up close
            if (melted && d > DrawingConfig.PotCloseRadius) return;

            // pen down = no refill, except inside the close radius
            if (SurfaceDrawer.IsPenActive && d > DrawingConfig.PotCloseRadius) return;

            float close = DrawingConfig.PotCloseRadius;
            float range = Mathf.Max(close + 1f, DrawingConfig.PotRefillRange);
            float t = Mathf.Clamp01((d - close) / (range - close));
            float falloff = (1f - t) * (1f - t); // near fast, far crawls
            float rate = Mathf.Lerp(DrawingConfig.PotRefillFloorPerSec,
                DrawingConfig.PotRefillNearPerSec, falloff);

            // rate split by wizard count: the pot feels the same total draw at any team size
            rate /= Mathf.Max(1, Sides.CountOn(Side.Wizard));

            float amount = rate * dt;
            ink.Award(amount);

            // every awarded drop is billed to the pot
            if (NetGame.IsAuthority) _ink = Mathf.Max(0f, _ink - amount);
            else
            {
                _drinkBill += amount;
                _billTimer -= dt;
                if (_billTimer <= 0f && _drinkBill > 0.01f)
                {
                    _billTimer = 0.5f;
                    NetSync.SendPotDrink(_drinkBill);
                    _drinkBill = 0f;
                }
            }
        }

        static SimpleFPSController LocalPlayer() =>
            SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;

        /// Local player + every remote avatar with their side. Remote sides
        /// default to Wizard until sides sync.
        void EachPlayer(System.Action<Side, Vector3> visit)
        {
            var p = LocalPlayer();
            if (p != null) visit(Sides.Of(Grimoire.LocalPlayerId), p.transform.position);
            NetSync.EachRemotePlayer((owner, pos) => visit(Sides.Of(owner), pos));
        }

        // ------------------------------------------------------ the liquid --
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // ------------------------------------------------------ flow motes --
        FlowMotes _flow;
        float _lastFill = -1f;
        float _flowShown;   // eased rate, so refresh jumps read as pours

        /// Flow motes above the ink: rising = the pot is losing ink, falling
        /// in = filling. Same visual language as the wand.
        void FlowTick(float dt)
        {
            if (InkSurface == null) return;
            float fill = Fill01;
            if (_lastFill < 0f) { _lastFill = fill; return; }

            float rate = (fill - _lastFill) / Mathf.Max(0.0001f, dt);
            _lastFill = fill;
            if (PrepRemaining > 0f) rate = 0f;

            // eased so instant refills read as a pour, not a one-frame flicker
            _flowShown = Mathf.MoveTowards(_flowShown, rate,
                DrawingConfig.PotFlowFullRate * 6f * dt);

            if (_flow == null) _flow = new FlowMotes(6, gameObject.layer);

            // same sign convention as the wand: negative = losing = motes rise
            // away; positive = gaining = motes fall in
            Vector3 top = InkSurface.position + Vector3.up * 0.05f;
            _flow.Tick(top, Vector3.up, _flowShown,
                IsCorrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor,
                reach: 0.9f, spread: 0.32f,
                fullRate: DrawingConfig.PotFlowFullRate,
                minSize: 0.055f, maxSize: 0.16f,
                deadzone: DrawingConfig.PotFlowDeadzone, cycle: 0.8f);
        }

        void OnDestroy() { _flow?.Dispose(); }

        static bool _warnedNoSurface;

        Transform _poolDisc;

        /// Placeholder disc for the grounded pool: sized by fill, tinted black to green.
        void PaintGroundPool()
        {
            if (_poolDisc == null)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                d.name = "PoolDisc";
                Destroy(d.GetComponent<Collider>()); // no collision on the pool
                d.transform.SetParent(transform, false);
                d.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                d.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(DrawingConfig.InkColor, MoteShade.Opaque);
                _poolDisc = d.transform;
            }
            float f = Mathf.Max(0.06f, Fill01);
            float r = 2.6f * Mathf.Sqrt(f);
            _poolDisc.localScale = new Vector3(r, 0.09f, r);

            var rend = _poolDisc.GetComponent<Renderer>();
            Color c = Color.Lerp(DrawingConfig.InkColor, DrawingConfig.CorruptInkColor, Greenness);
            rend.GetPropertyBlock(_blk);
            _blk.SetColor(BaseColorId, c);
            _blk.SetColor(ColorId, c);
            rend.SetPropertyBlock(_blk);
        }

        void PaintSurface()
        {
            // grounded puddle without an assigned blob uses the placeholder
            // disc; the missing-InkSurface error is for real pots only
            if (Grounded && InkSurface == null) { PaintGroundPool(); return; }

            // missing InkSurface: error once, not per frame
            if (InkSurface == null)
            {
                if (!_warnedNoSurface)
                {
                    _warnedNoSurface = true;
                    Debug.LogError("[SpellyZombie] CauldronEconomy has no InkSurface assigned, so the " +
                        "pot's ink is INVISIBLE. Drag the liquid object from inside your cauldron into " +
                        "the InkSurface slot — its height becomes the ink level and its colour the " +
                        "owning team. Put its PIVOT AT THE BOTTOM so it drains downward.", this);
                }
                return;
            }
            float f = PrepRemaining > 0f ? 0f : Fill01;
            bool show = f > 0.01f;
            if (InkSurface.gameObject.activeSelf != show) InkSurface.gameObject.SetActive(show);
            if (!show) return;
            // whole-body scale down to a speck so near-empty reads as nearly gone
            InkSurface.localScale = _surfaceScale0 * Mathf.Lerp(0.05f, 1f, f);
            // material slots: both filled = swap black/green assets; only black
            // filled = green MPB tint while corrupt; none = authored material untouched
            if (_inkRends == null) _inkRends = InkSurface.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _inkRends)
            {
                if (r == null) continue;
                var want = IsCorrupt && CorruptInkMaterial != null ? CorruptInkMaterial : InkMaterial;
                if (want != null && r.sharedMaterial != want) r.sharedMaterial = want;

                // mid-transition the colour lerps black to green as the progress bar
                bool transitioning = Greenness > 0.02f && Greenness < 0.98f;
                bool tintOverlay = IsCorrupt && CorruptInkMaterial == null;
                if (transitioning)
                {
                    Color mid = Color.Lerp(DrawingConfig.InkColor,
                        DrawingConfig.CorruptInkColor, Greenness);
                    r.GetPropertyBlock(_blk);
                    _blk.SetColor(BaseColorId, mid);
                    _blk.SetColor(ColorId, mid);
                    r.SetPropertyBlock(_blk);
                }
                else if (tintOverlay)
                {
                    r.GetPropertyBlock(_blk);
                    _blk.SetColor(BaseColorId, DrawingConfig.CorruptInkColor);
                    _blk.SetColor(ColorId, DrawingConfig.CorruptInkColor);
                    r.SetPropertyBlock(_blk);
                }
                else
                {
                    _blk.Clear();
                    r.SetPropertyBlock(_blk); // at rest = the exact authored look
                }
            }
        }
    }
}
