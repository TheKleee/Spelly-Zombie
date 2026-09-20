using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Bootstraps the side system: auto-adds the per-player side components
    /// (the player is built at runtime, nothing to attach by hand).
    /// Side switching is lobby-only; mid-round you change sides by corruption.
    public class SideBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~SideBootstrap");
            go.AddComponent<SideBootstrap>();
            DontDestroyOnLoad(go);
        }

        float _sweep;
        int _kitGivenTo; // the local owner id that got the starting pair

        void OnEnable() => Sides.Changed += OnSideChanged;
        void OnDisable() => Sides.Changed -= OnSideChanged;

        /// Starting kits. Wizards open with push and pull and absorb the rest.
        /// ACOLYTES OPEN WITH NOTHING - all four of theirs are deed unlocks
        /// (AcolyteDeeds), including the arrow and Y.
        static readonly RuneType[] WizardKit =
        {
            RuneType.Attract,     // pull
            RuneType.Repel,       // push
        };

        static readonly RuneType[] AcolyteKit = { };

        static RuneType[] KitFor(Side side) => side == Side.Acolyte ? AcolyteKit : WizardKit;

        /// The match starts every player's book over at their side's kit:
        /// the lobby is practice, what was learned there stays there.
        public static void ResetBooksForMatch()
        {
            var owners = new System.Collections.Generic.List<int> { Grimoire.LocalPlayerId };
            foreach (var id in NetSync.RemoteIds) owners.Add(NetSync.OwnerIdOf(id));
            Grimoire.ResetForMatch(owners, o => KitFor(Sides.Of(o)));
        }

        /// Changing side REPLACES the book, lobby included - you become that
        /// side with that side's starting state. The old lobby-only-adds rule
        /// let a wizard carry push and pull into the acolyte, who owns nothing.
        static void OnSideChanged(int owner, Side side)
        {
            Grimoire.SetKit(owner, side == Side.Acolyte ? AcolyteKit : WizardKit);
        }

        /// The starting pair - WIZARDS ONLY. An acolyte opens with an empty
        /// book and earns even the arrow and Y by deed.
        void GrantStartingKit(int owner)
        {
            if (Sides.Of(owner) == Side.Acolyte) return;
            foreach (var r in WizardKit) Grimoire.UnlockRune(owner, r, quiet: true);
        }

        void Update()
        {
            // broken-pot ink must land even when ZERO pots remain to Update -
            // dead objects can't conduct their own hop (CauldronEconomy.GapTick)
            CauldronEconomy.GapTick(Time.deltaTime);

            // immersive mode: no HUD at all, except screens you opened
            UIKit.TickImmersive();

            // players are SPAWNED from the prefab, not assumed to be sitting
            // in the scene - then stood at their start: a biome on a real map,
            // scattered ground in the lobby.
            PlayerSpawner.Tick();
            SpawnPlan.PlaceLocals();

            // push and pull are in a wizard's book the frame a local player
            // appears. Offline every scene's body is a new owner id.
            int me = Grimoire.LocalPlayerId;
            if (me != 0 && me != _kitGivenTo && Sides.Of(me) != Side.Acolyte)
            {
                _kitGivenTo = me;
                GrantStartingKit(me);
            }

            // keep every player wearing the side components
            _sweep -= Time.deltaTime;
            if (_sweep <= 0f)
            {
                _sweep = 1f;
                foreach (var p in SimpleFPSController.All)
                {
                    if (p == null) continue;
                    if (p.GetComponent<ShapeShift>() == null) p.gameObject.AddComponent<ShapeShift>();
                    if (p.GetComponent<SideLook>() == null) p.gameObject.AddComponent<SideLook>();
                    if (p.GetComponent<ZombieWatch>() == null) p.gameObject.AddComponent<ZombieWatch>();
                    // your own body ink follows you across scenes and sessions
                    if (p.GetComponent<BodyInkKeeper>() == null) p.gameObject.AddComponent<BodyInkKeeper>();
                    // death is a mode, not a menu: the ghost and the rescue
                    if (p.GetComponent<GhostState>() == null) p.gameObject.AddComponent<GhostState>();
                    // the crossroads line: what TAB and R do from here, both sides
                    if (p.GetComponent<ModeGuide>() == null) p.gameObject.AddComponent<ModeGuide>();
                    // the situations that earn an acolyte a mischief glyph
                    if (p.GetComponent<AcolyteDeedWatch>() == null) p.gameObject.AddComponent<AcolyteDeedWatch>();
                    // the chosen hat color survives scene loads and sessions
                    HatColor.Dress(p);

                    // The ceiling follows side, buffs AND the ground you stand
                    // on. Walking into a weak biome does not chop you instantly:
                    // strength SETTLES toward the new ceiling, so leaving in
                    // time saves you. A raised ceiling is never auto-filled -
                    // a buff gives you room, healing fills it.
                    if (!p.IsDowned)
                    {
                        float cap = Sides.MaxHealthFor(Grimoire.LocalPlayerId);
                        if (p.Health <= 0f) p.Health = cap;
                        else if (p.Health > cap)
                            p.Health = Mathf.MoveTowards(p.Health, cap,
                                DrawingConfig.StrengthSettlePerSec * Time.deltaTime);
                    }
                }
            }

        }
    }
}
