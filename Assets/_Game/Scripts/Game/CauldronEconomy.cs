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
        [Tooltip("The liquid ink object inside your cauldron model. Its Y scale becomes the ink level; its colour becomes the owning team's (black/green). Leave empty and the pot still WORKS, it just shows nothing inside.")]
        public Transform InkSurface;

        /// The lobby pot skips prep and never runs dry — it is the practice well.
        bool _lobby;

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

        void OnEnable()
        {
            Active = this;

            _prep = DrawingConfig.PotPrepSeconds;
            _open = false;
            _corrupt = false;
            _ink = 0f;
            if (InkSurface != null) _surfaceScale0 = InkSurface.localScale;
            _blk = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // ⛔ ASKED EVERY FRAME, NOT ONCE AT ENABLE (Marko Aug 11: "we need to
            // wake up the lobby cauldron cause wizards can't use spells in lobby
            // unless cauldron is active"). RoundDirector.InLobby reads
            // Instance._phase, and OnEnable can easily run BEFORE that Instance
            // exists — so the pot cached "not lobby", sat through a 30-second
            // prep, and then drained like a real round. Evaluated live it wakes
            // the moment the director does.
            //
            // THE LOBBY POT IS ALWAYS OPEN AND ALWAYS FULL: it is the practice
            // ground, so it can never be the reason you cannot test a spell.
            _lobby = RoundDirector.InLobby;
            if (_lobby)
            {
                _prep = 0f;
                _open = true;
                _ink = DrawingConfig.PotCapacityInk;
            }
            if (NetGame.IsAuthority) Simulate(dt);
            LocalWandTick(dt);

            // the HUD reads one set of statics wherever the truth came from
            CauldronHUD.Fill = Fill01;
            CauldronHUD.Corrupt = IsCorrupt;
            CauldronHUD.TimerSeconds = PrepRemaining > 0f ? PrepRemaining : -1f;
            PaintSurface();
            FlowTick(dt);
        }

        // ------------------------------------------------------- authority --
        void Simulate(float dt)
        {
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
                // the bomb ticking: green evaporates constantly
                _ink -= DrawingConfig.PotCorruptDrainPerSec * dt;

                float defusers = 0f, babysitters = 0f;
                EachPlayer((side, pos) =>
                {
                    float d = Vector3.Distance(pos, transform.position);
                    if (side == Side.Wizard && d <= DrawingConfig.PotCloseRadius) defusers += 1f;
                    if (side == Side.Acolyte && d <= DrawingConfig.PotAcolyteFillRadius) babysitters += 1f;
                });

                // the babysitting tax: their corruption FILLS what they need empty
                if (babysitters > 0f)
                    _ink += DrawingConfig.PotAcolyteFillPerSec * babysitters * dt;

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
                // an acolyte at a black pot for the plant time turns it
                float touching = 0f;
                EachPlayer((side, pos) =>
                {
                    if (side == Side.Acolyte
                        && Vector3.Distance(pos, transform.position) <= DrawingConfig.PotCloseRadius)
                        touching += 1f;
                });
                if (touching > 0f)
                {
                    _corruptTouch += dt;
                    if (_corruptTouch >= DrawingConfig.PotCorruptSeconds)
                    {
                        _corrupt = true;
                        _corruptTouch = 0f;
                        _defuse = 0f;
                        Juice.Thud(transform.position);
                        DrawingWorld.Instance?.LogEvent("the cauldron turns GREEN");
                    }
                }
                else _corruptTouch = 0f;

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
                        _ink -= DrawingConfig.PotSpillPerSec * dt;
                }
            }

            // IMMORTAL IN THE LOBBY: everything above may bill it, and then it
            // is simply full again. One line rather than a bypass at every
            // drain, so a drain added later cannot forget the rule.
            if (_lobby) _ink = DrawingConfig.PotCapacityInk;
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

            float close = DrawingConfig.PotCloseRadius;
            float range = Mathf.Max(close + 1f, DrawingConfig.PotRefillRange);
            float t = Mathf.Clamp01((d - close) / (range - close));
            float falloff = (1f - t) * (1f - t); // near fast, far crawls
            float rate = Mathf.Lerp(DrawingConfig.PotRefillFloorPerSec,
                DrawingConfig.PotRefillNearPerSec, falloff);

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

            // only while it is THEIRS — a black pot is the wizards' business and
            // its own HUD bar already covers it
            if (!IsCorrupt || PrepRemaining > 0f) { rate = 0f; }

            if (_flow == null) _flow = new FlowMotes(4, gameObject.layer);

            // up off the surface. Rising = evaporating, falling in = refilling,
            // so the sign is inverted against the wand's outward/inward.
            Vector3 top = InkSurface.position + Vector3.up * 0.05f;
            _flow.Tick(top, Vector3.up, -rate, DrawingConfig.CorruptInkColor,
                reach: 0.55f, spread: 0.22f,
                fullRate: DrawingConfig.PotFlowFullRate,
                minSize: 0.03f, maxSize: 0.09f,
                deadzone: DrawingConfig.PotFlowDeadzone, cycle: 0.9f);
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
            Color c = IsCorrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            foreach (var r in InkSurface.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(_blk);
                _blk.SetColor(BaseColorId, c);
                _blk.SetColor(ColorId, c);
                r.SetPropertyBlock(_blk);
            }
        }
    }
}
