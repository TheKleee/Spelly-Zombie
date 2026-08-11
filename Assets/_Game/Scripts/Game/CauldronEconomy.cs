using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE POT (Marko's economy, ruled Aug 8-9). One ink pool, no exceptions:
    /// every wand refill bills it, and nothing ever refills it — ink lost is
    /// lost. HE ADDS THIS to his cauldron object; nothing is auto-found.
    ///
    ///  - Starts CLOSED for PotPrepSeconds (the gather phase — everyone
    ///    scatters for objects, then races back to protect it or steal it),
    ///    then OPENS full. Closed = inert: no flows, no corruption, no spill.
    ///  - BLACK pot: refills every wizard's wand FROM ANYWHERE, fastest up
    ///    close, floor rate far away (the wand's own effect is the tell).
    ///    Inside the close radius with a FULL wand the tap keeps running and
    ///    the pot pays — the spill. A MELTED wand only regrows up close
    ///    (protects the wandless-terror moment; far refill needs a wand).
    ///  - GREEN pot: fills no wands. It drains constantly (the bomb ticking),
    ///    wizard presence up close DEFUSES the colour back to black (CS
    ///    defuse time), and an acolyte lingering near FILLS it back up with
    ///    their own corruption — the babysitting tax. Full pot = no special
    ///    rule, it just clamps.
    ///  - An acolyte at a BLACK pot for the CS plant time corrupts it.
    ///
    /// His visual: the liquid ink inside the cauldron is a REAL object — drag
    /// his ink-surface child into the slot; its height scales with the ink
    /// and its colour is the owning team's. Sim runs on the authority (solo/
    /// host); clients mirror via NetSync and award their own wands locally.
    public class CauldronEconomy : MonoBehaviour
    {
        [Header("Marko's art — dragged in, never generated")]
        [Tooltip("The liquid ink object inside your cauldron model. Its Y scale becomes the ink level; its colour becomes the owning team's (black/green). Your weighted liquid ball works — it gets tinted and squashed like anything else. Leave empty and the pot still WORKS, it just shows nothing inside.")]
        public Transform InkSurface;

        [Tooltip("The BOWL mesh of your cauldron (the MeshFilter with the hollow). Needed so the liquid stays INSIDE once the pot has been lifted: lifting makes the main collider convex — a bowl with no hollow — and this spawns a follower carrying the true concave shape that only the liquid's physics feels. Leave empty and the pot behaves as before (fine until someone lifts it).")]
        public MeshFilter Bowl;

        [Tooltip("YOUR ink material (black). Start from MI_CauldronInk_SZ in Art/3D/Materials and edit freely — the code only ever swaps between these two assets, it never builds a material of its own.")]
        public Material InkMaterial;
        [Tooltip("YOUR corrupted ink material (green). MI_CauldronInkCorrupt_SZ is the starter. Empty = the black one gets a green tint via property block instead.")]
        public Material CorruptInkMaterial;

        VesselShell _shell;   // the true-bowl follower, when his Bowl slot is filled

        Renderer[] _inkRends;   // the ink's renderers, found once

        /// 0 = black, 1 = green, and the JOURNEY between them is the indicator:
        /// rises while an acolyte's touch corrupts, falls while a defuse holds.
        public float Greenness { get; private set; }

        /// The lobby pot skips prep and rebrews on a 10s clock — the practice
        /// well with real behaviour and a failsafe.
        bool _lobby;
        float _lobbyRefreshIn;

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

        /// Every pot in the scene. With ONE the game behaves exactly as before;
        /// with TWO OR MORE the ink HOPS between them (Marko Aug 11: "the ink
        /// disappears and the effect shows where it was... turn off from one
        /// cauldron and turn on in another after 10 seconds") — and the cycle
        /// runs in the lobby too, which is where players learn to read it.
        public static readonly List<CauldronEconomy> All = new List<CauldronEconomy>();

        void OnEnable()
        {
            All.Add(this);
            // first pot awake holds the ink; later ones are dormant vessels
            // until the hop chooses them
            if (Active == null) Active = this;

            _prep = DrawingConfig.PotPrepSeconds;
            _open = false;
            _corrupt = false;
            _ink = 0f;
            if (InkSurface != null) _surfaceScale0 = InkSurface.localScale;
            _blk = new MaterialPropertyBlock();

            // the true-bowl follower (see VesselShell) — HIS mesh, dragged in.
            // InkSurface rides along as the cargo the shell must keep cupping.
            if (Bowl != null && _shell == null)
                _shell = VesselShell.Attach(Bowl.transform, Bowl.sharedMesh, transform, InkSurface);

            // NO BONE PHYSICS ON THE INK (Marko Aug 11, after trying it live:
            // "either reduce the influence or remove it completely — I'll put
            // the bones in place"). His posed bones ARE the shape, untouched.
            // JiggleBones survives as an opt-in component with an Influence
            // slider — add it to the Blob yourself if you ever want a whisper
            // of slosh back; the code will never add it for you again.
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Active == this) Active = null;
            if (All.Count == 0) { _cycleArmed = false; _hopGap = false; } // scene change resets the cycle
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // asked every frame, not cached at OnEnable — RoundDirector.Instance
            // can be born after us, and a pot that cached "not lobby" sat
            // through a 30s prep. The lobby BEHAVIOUR lives in Simulate now.
            _lobby = RoundDirector.InLobby;
            if (NetGame.IsAuthority) Simulate(dt);
            LocalWandTick(dt);

            // the HUD reads one set of statics wherever the truth came from
            CauldronHUD.Fill = Fill01;
            CauldronHUD.Corrupt = IsCorrupt;
            CauldronHUD.TimerSeconds = PrepRemaining > 0f ? PrepRemaining : -1f;
            PaintSurface();
            FlowTick(dt);
        }

        // ------------------------------------------- the hop cycle (2+ pots) --
        static float _hopTimer;
        static bool _hopGap;      // the 10s vacuum: no pot pours, wizards bleed
        static bool _cycleArmed;

        /// THE FULL CYCLE (Marko Aug 11): ink lives in one pot, the beam rises
        /// where it LEFT, ten seconds of drought, then the beam falls and the
        /// splash lands where it ARRIVES — "showing where it is across the
        /// map" (his call: the landing IS telegraphed). Ink amount and
        /// corruption ride along: it is the same ink, somewhere else.
        void HopTick(float dt)
        {
            if (All.Count < 2) { _hopGap = false; _cycleArmed = false; return; }
            if (this != All[0]) return;   // one conductor, not one per pot

            if (!_cycleArmed)
            {
                _cycleArmed = true;
                _hopTimer = DrawingConfig.PotHopLiveSeconds;
            }

            _hopTimer -= dt;
            if (_hopTimer > 0f) return;

            if (!_hopGap)
            {
                // the ink LEAVES — beam up where it was, then darkness
                var old = Active;
                if (old != null)
                {
                    SkyBeam.Up(old.transform.position,
                        old._corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor);
                    old._open = false;
                }
                _hopGap = true;
                _hopTimer = DrawingConfig.PotHopGapSeconds;
            }
            else
            {
                // the ink ARRIVES — a different pot, the falling beam, the
                // splash "as if it fell to the ground, just like liquid does"
                // COMPLETELY RANDOM, same pot allowed (his rule: "it can fall
                // back into the same cauldron it left") — the beam still rises
                // and falls, so even a repeat reads as an event
                var old = Active;
                var next = All[Random.Range(0, All.Count)];

                next._ink = old != null ? Mathf.Max(old._ink, 1f) : DrawingConfig.PotCapacityInk;
                next._corrupt = old != null && old._corrupt;
                next._defuse = 0f;
                if (old != null) { old._ink = 0f; old._corrupt = false; }

                Active = next;
                next._open = true;
                Color c = next._corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
                SkyBeam.Down(next.transform.position, c);
                if (FxLibrary.I != null)
                    FxLibrary.Spawn(FxLibrary.I.Splash, next.transform.position + Vector3.up * 0.6f);
                Juice.Chime(next.transform.position);

                _hopGap = false;
                _hopTimer = DrawingConfig.PotHopLiveSeconds;
            }
        }

        // ------------------------------------------------------- authority --
        void Simulate(float dt)
        {
            HopTick(dt);

            // A DORMANT VESSEL DOES NOTHING: no prep, no drains, no corruption.
            // It is scenery holding a bowl until the ink chooses it.
            if (Active != this) { _open = false; return; }

            // THE VACUUM FREEZES EVERYTHING (Marko Aug 11: "during those 10
            // seconds the ink size doesn't change. We keep track of how much
            // ink is left so that the new cauldron regenerates the ink").
            // Without this return the closed pot fell into the PREP path below,
            // declared itself brewed, and resurrected at FULL capacity mid-gap
            // — wiping the drought and the carried pool in one bug.
            if (_hopGap) { PushNet(); return; }

            // THE LOBBY POT IS THE REAL POT WITH A GUARDIAN (Marko Aug 11: "the
            // lobby cauldron will automatically refill itself every 10 seconds
            // => it cannot be deprived... but you can see it visually depriving.
            // It can be corrupted normally but after the 10 second mark it will
            // be refreshed completely and turned back into the black ink").
            //
            // So: no prep, open at once — and every rule below runs for real.
            // It drains as wizards drink, turns green to an acolyte's touch,
            // evaporates while green — and on the 10s mark the failsafe rebrews
            // it: full, black, defuse clock cleared. Practice with real physics,
            // stakes with a net.
            if (_lobby && !_hopGap)   // the vacuum outranks the failsafe: the drought must be FELT, even here
            {
                _prep = 0f;
                if (!_open) { _open = true; _ink = DrawingConfig.PotCapacityInk; }
                _lobbyRefreshIn -= dt;
                if (_lobbyRefreshIn <= 0f)
                {
                    _lobbyRefreshIn = DrawingConfig.LobbyPotRefreshSeconds;
                    bool restored = _corrupt || _ink < DrawingConfig.PotCapacityInk - 0.5f;
                    _ink = DrawingConfig.PotCapacityInk;
                    _corrupt = false;
                    _defuse = 0f;
                    // NO opening wipe here — that beat belongs to a round's
                    // brew-up, and a lobby full of test zombies must survive it
                    if (restored) Juice.Chime(transform.position);
                }
            }

            if (!_open)
            {
                _prep -= dt;
                if (_prep <= 0f)
                {
                    _prep = 0f;
                    _open = true;
                    _ink = DrawingConfig.PotCapacityInk; // it OPENS full — the brew-up beat

                    // THE OPENING WIPE (Marko Aug 9): "when ink ore first
                    // appears it will destroy all of the active zombies" — a
                    // wizard hounded through the gather phase is saved by the
                    // brew itself, and the match proper starts on a clean board.
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
                // THE RITUAL IS VISIBLE (Marko Aug 11: "there needs to be a
                // visual indicator that the cauldron is being corrupted or
                // reformed") — greenness runs 1 → 0 as the defuse holds, and
                // PaintSurface lerps the ink's colour by it, so both sides
                // watch the reclaim happen instead of it snapping at the end.
                Greenness = 1f - Mathf.Clamp01(_defuse / Mathf.Max(0.1f, DrawingConfig.PotDefuseSeconds));

                // the bomb ticking: green evaporates constantly.
                //
                // ⛔ DIVIDED BY THE ACOLYTE HEADCOUNT (Marko Aug 11): the rates
                // are authored PER ONE PLAYER and split across the team, so the
                // pot's clock does not care how many hands are on it. This is
                // his self-balancing law in one line — "if we played 1 vs 11,
                // an acolyte that steals the cauldron can easily win due to the
                // fact that stealing the cauldron is way harder": one acolyte's
                // plant burns at FULL rate (the hard steal pays in full), while
                // eleven acolytes' trivial plant burns at a crawl.
                _ink -= DrawingConfig.PotCorruptDrainPerSec * dt
                    / Mathf.Max(1, Sides.CountOn(Side.Acolyte));

                float defusers = 0f, babysitters = 0f;
                EachPlayer((side, pos) =>
                {
                    float d = Vector3.Distance(pos, transform.position);
                    if (side == Side.Wizard && d <= DrawingConfig.PotCloseRadius) defusers += 1f;
                    if (side == Side.Acolyte && d <= DrawingConfig.PotAcolyteFillRadius) babysitters += 1f;
                });

                // the babysitting tax: their corruption FILLS what they need
                // empty — normalized the same way (per-one-acolyte, split by
                // the team), so a full team crowding its own pot refills it at
                // exactly the authored rate, not team-size times it
                if (babysitters > 0f)
                    _ink += DrawingConfig.PotAcolyteFillPerSec * babysitters * dt
                        / Mathf.Max(1, Sides.CountOn(Side.Acolyte));

                if (defusers > 0f)
                {
                    _defuse += dt; // presence is presence — more wizards don't defuse faster (CS rule)
                    if (_defuse >= DrawingConfig.PotDefuseSeconds)
                    {
                        _corrupt = false;
                        _defuse = 0f;
                        Juice.Chime(transform.position);
                        DrawingWorld.Instance?.LogEvent("the cauldron is BLACK again");
                    }
                }
                else _defuse = 0f; // stepping off resets the defuse, CS style
            }
            else
            {
                // ...and 0 → 1 while an acolyte's touch corrupts, the mirror
                Greenness = Mathf.Clamp01(_corruptTouch / Mathf.Max(0.1f, DrawingConfig.PotCorruptSeconds));

                // an acolyte at a black pot for the plant time turns it
                float touching = 0f;
                EachPlayer((side, pos) =>
                {
                    if (side == Side.Acolyte
                        && Vector3.Distance(pos, transform.position) <= DrawingConfig.PotCloseRadius)
                        touching += 1f;
                });
                // THE DEAD CARRY THE CORRUPTION TOO (Marko Aug 11: "you can use
                // the zombies to reach near the cauldron... zombies near the
                // cauldron can corrupt it but many times slower than the actual
                // Acolyte"). The arrow-march is the delivery system: send the
                // horde, it loiters, the ink creeps green — and the greenness
                // lerp makes the creep VISIBLE, so wizards learn that zombies
                // at the pot are not a nuisance but a fuse. Presence-based like
                // the acolyte's own touch (a horde plants no faster than one),
                // demons excluded as everywhere.
                bool zombieTouch = false;
                foreach (var z in Zombie.All)
                {
                    if (z == null || z.IsDemon) continue;
                    if (Vector3.Distance(z.transform.position, transform.position)
                        <= DrawingConfig.PotCloseRadius) { zombieTouch = true; break; }
                }

                if (touching > 0f) _corruptTouch += dt;                    // the acolyte's own hand: full speed
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

                // the spill: the LOCAL wizard loitering with a full wand.
                // (Remote wands' fullness is theirs to know — their machines
                // bill their refills through PotDrink instead.)
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

            // (lobby immortality is the 10s refresh at the top now — a per-frame
            // clamp here would hide the very depletion he wants seen)
            _ink = Mathf.Clamp(_ink, 0f, DrawingConfig.PotCapacityInk); // full = no rule, it clamps
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
            if (Active != this) return; // a dormant vessel pours nothing — only the ink's pot feeds wands
            if (PrepRemaining > 0f || IsCorrupt || Fill01 <= 0f) return; // closed/green/dry pours nothing
            var p = LocalPlayer();
            if (p == null || Sides.Of(Grimoire.LocalPlayerId) != Side.Wizard) return;
            var ink = p.GetComponent<PlayerInk>();
            if (ink == null || ink.Fraction >= 0.999f) return; // far + full just stops drinking

            float d = Vector3.Distance(p.transform.position, transform.position);
            var wand = p.GetComponent<WandState>();
            bool melted = wand != null && !wand.HasWand;
            // a MELTED wand regrows only at the pot — the wandless terror stays real
            if (melted && d > DrawingConfig.PotCloseRadius) return;

            // ⛔ EXCLUSIVE MODES (Marko Aug 11: "when you're using the ink it's
            // not being added, only when you stop using it — unless of course
            // you're too close to the cauldron"). A pen that drains while the
            // pot pours made the wand "confused — trying to remove and add ink
            // at the same time", and worse, drawing beside the pot was a net
            // ink hose. So: pen down = SPENDING mode, no refill — except inside
            // the close radius, where the pour overpowers everything (and
            // WandState blocks the pen there anyway, so the modes never overlap).
            if (SurfaceDrawer.IsPenActive && d > DrawingConfig.PotCloseRadius) return;

            float close = DrawingConfig.PotCloseRadius;
            float range = Mathf.Max(close + 1f, DrawingConfig.PotRefillRange);
            float t = Mathf.Clamp01((d - close) / (range - close));
            float falloff = (1f - t) * (1f - t); // near fast, far crawls
            float rate = Mathf.Lerp(DrawingConfig.PotRefillFloorPerSec,
                DrawingConfig.PotRefillNearPerSec, falloff);

            // ⛔ PER ONE WIZARD, SPLIT BY THE TEAM (Marko Aug 11: "so that 10
            // wizards wouldn't instantly empty the cauldron"). Every machine
            // runs this for its own wand, so each drinks 1/Nth and the POT
            // feels the same total draw at any team size — while each wizard's
            // personal regen slows as the team grows, which is its own
            // pressure: a big team shares one well.
            rate /= Mathf.Max(1, Sides.CountOn(Side.Wizard));

            float amount = rate * dt;
            ink.Award(amount);

            // one pool, no exceptions: every drop came out of the pot
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

        /// Local player + every remote avatar, with the side each is known to
        /// hold. (Remote sides default to Wizard until sides sync — flagged.)
        void EachPlayer(System.Action<Side, Vector3> visit)
        {
            var p = LocalPlayer();
            if (p != null) visit(Sides.Of(Grimoire.LocalPlayerId), p.transform.position);
            NetSync.EachRemotePlayer((owner, pos) => visit(Sides.Of(owner), pos));
        }

        // ------------------------------------------------------ his liquid --
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// HIS ink object inside the pot: height = the ink, colour = the team.
        // ---- THE POT SAYS WHICH WAY ITS INK IS GOING ------------------------
        FlowMotes _flow;
        float _lastFill = -1f;
        float _flowShown;   // eased rate, so refresh jumps read as pours

        /// THE GREEN POT TELLS ACOLYTES WHAT IS HAPPENING (Marko Aug 11: "the
        /// green cauldron ink should also have an evaporation effect when
        /// acolytes are not nearby and regeneration effect when they are").
        ///
        /// Their whole win condition is the pot running dry, and the cruel twist
        /// he designed is that STANDING NEAR IT REFILLS IT from their own
        /// corruption — so the one thing an acolyte must know is which way it is
        /// going right now, and their instinct to guard it is the mistake.
        ///
        /// Same language as the wand, deliberately: motes rising OFF the surface
        /// mean it is evaporating (good for them), motes falling INTO it mean
        /// they are filling it (bad). Anyone who learned the wand already reads
        /// this, which matters more than either effect being prettier alone.
        void FlowTick(float dt)
        {
            if (InkSurface == null) return;
            float fill = Fill01;
            if (_lastFill < 0f) { _lastFill = fill; return; }

            float rate = (fill - _lastFill) / Mathf.Max(0.0001f, dt);
            _lastFill = fill;
            if (PrepRemaining > 0f) rate = 0f;

            // eased, so the lobby's 10s rebrew (an instant jump to full) reads
            // as a strong pour rather than a one-frame flicker
            _flowShown = Mathf.MoveTowards(_flowShown, rate,
                DrawingConfig.PotFlowFullRate * 6f * dt);

            // THE WAND'S LANGUAGE AT POT SCALE (Marko Aug 11: "use the same
            // particle effect as on wands but larger for cauldron... and more
            // balls... when filling/evaporating"). BOTH lives now, not only the
            // green one: a black pot bleeding to ten drinking wizards shows
            // black motes rising; a green pot evaporating shows green; filling
            // runs the same path inward. Anyone who learned the wand reads it.
            if (_flow == null) _flow = new FlowMotes(6, gameObject.layer);

            // up off the surface — and the SAME sign convention as the wand,
            // no inversion (Marko Aug 11 caught it backwards: a drinking wizard
            // made the pot look like it was FILLING). Negative = losing =
            // motes travel OUTWARD along up = rising away, evaporation.
            // Positive = gaining = the same path inward = falling into the pot.
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

        void PaintSurface()
        {
            // FAIL LOUDLY, NEVER SILENTLY (his standing rule). An unassigned
            // InkSurface meant the pot had ink mechanically and showed nothing,
            // and the return below said so to nobody — "when I said ink in
            // cauldron I meant visually as well". Said once, not per frame.
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
            var s = _surfaceScale0;
            s.y *= f;
            InkSurface.localScale = s;
            // THE INK WEARS THE LIVING LIQUID MATERIAL (Marko Aug 11: "why is
            // it not deforming like a liquid would?") — the jelly is not
            // physics, it is the SZParticle shader's vertex wobble, the exact
            // material every conjured liquid blob already wears (Matter.cs uses
            // 0.11/0.7 for liquids). So the answer to "do I need to add
            // something to the blob?" is NO: the pot dresses its own ink, and
            // MatterFX caches one material per colour, so this stays cheap.
            // ⛔ HIS MATERIAL IS LAW (Marko Aug 11: "show me the material you're
            // using and let me edit it myself. You cannot do this"). The code
            // NEVER builds a material for the ink again — the runtime-generated
            // one detonated at his pot's scale and could not be edited anyway.
            // Three behaviours, all his to choose in the Inspector:
            //   both slots filled  → swap between HIS black and HIS green asset
            //   only black filled  → his asset, green property-block tint while corrupt
            //   no slots           → whatever material HE authored on the blob
            //                        stays untouched; corruption is the only
            //                        thing painted over it (colour tint, MPB),
            //                        cleared again the moment it is defused
            // cached — GetComponentsInChildren allocated a fresh array EVERY
            // FRAME per pot, garbage that stacks with several cauldrons placed
            if (_inkRends == null) _inkRends = InkSurface.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _inkRends)
            {
                if (r == null) continue;
                var want = IsCorrupt && CorruptInkMaterial != null ? CorruptInkMaterial : InkMaterial;
                if (want != null && r.sharedMaterial != want) r.sharedMaterial = want;

                // MID-RITUAL, THE COLOUR IS THE PROGRESS BAR (Marko Aug 11):
                // while greenness sits between the extremes the ink lerps
                // black↔green over whichever material is on, so a plant is
                // watched creeping in and a defuse watched draining out.
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
                    r.SetPropertyBlock(_blk); // at rest = his exact authored look
                }
            }
        }
    }
}
