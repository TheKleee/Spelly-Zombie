using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// MAKES THE SIDE SYSTEM ACTUALLY EXIST, with nothing to attach by hand.
    ///
    /// Two jobs, both of them deliberately guard-free:
    ///
    /// 1. EVERY PLAYER GETS ShapeShift AND SideLook. The player is built at
    ///    runtime by CharacterRig, which already auto-adds WandInk the same way,
    ///    so nobody should have to remember to drag two components onto an object
    ///    that did not exist until play mode.
    ///
    /// 2. C SWITCHES SIDE. This used to live in MatchLobby, where it sat behind
    ///    four guards that can each silently eat the key: RoundDirector.InLobby
    ///    (needs a RoundDirector in Idle phase), the scene name, a non-null
    ///    player, and UIKit.Typing (true whenever ANY InputField is selected, and
    ///    the lobby has an IP field). It never fired. Here there is nothing to
    ///    fail: the object exists from the first frame of the game and survives
    ///    scene loads.
    ///
    /// Switching is LOBBY ONLY. Mid-round you change sides by being corrupted,
    /// which is the whole mode; a key would make that meaningless.
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
        bool _kitGiven;

        void OnEnable() => Sides.Changed += OnSideChanged;
        void OnDisable() => Sides.Changed -= OnSideChanged;

        /// WHAT EACH SIDE KNOWS BEFORE IT LEARNS ANYTHING.
        ///
        /// PUSH AND PULL ARE UNIVERSAL (Marko: "push and pull is something regular
        /// wizard starts with as well even without absorbing any rune"). Both
        /// sides need them from second zero: a wizard to shove the world around,
        /// an acolyte because the ARROW is how they command their dead, and an
        /// acolyte who cannot draw an arrow cannot play.
        ///
        /// On top of that, an acolyte knows SOLID and LIQUID, their only two
        /// summons. That is the whole of "mostly empty": not literally nothing,
        /// just a four-rune book that never grows.
        static readonly RuneType[] WizardKit =
        {
            RuneType.DirectionAway,     // push
            RuneType.DirectionToward,   // pull
        };

        static readonly RuneType[] AcolyteKit =
        {
            RuneType.DirectionAway,     // push: commands the dead, in green ink
            RuneType.DirectionToward,   // pull
            RuneType.StateSolid,        // a melee zombie
            RuneType.StateLiquid,       // a ranged one
        };

        /// IN THE LOBBY WE ONLY ADD, NEVER TAKE AWAY. The lobby exists so you can
        /// try both halves of the game, and wiping your runes every time you press
        /// C would make the practice room worse at the one job it has. In a round,
        /// conversion is a real loss and does the full swap.
        static void OnSideChanged(int owner, Side side)
        {
            var kit = side == Side.Acolyte ? AcolyteKit : WizardKit;

            if (ActiveScene.Name == "Lobby")
                foreach (var r in kit) Grimoire.UnlockRune(owner, r);
            else
                Grimoire.SetKit(owner, kit);
        }

        /// Everyone needs their starting pair before they ever touch a side key,
        /// so a fresh wizard can shove a crate in the first five seconds.
        void GrantStartingKit(int owner)
        {
            foreach (var r in WizardKit) Grimoire.UnlockRune(owner, r);
        }

        void Update()
        {
            // keep every player wearing the two side components
            _sweep -= Time.deltaTime;
            if (_sweep <= 0f)
            {
                _sweep = 1f;
                foreach (var p in SimpleFPSController.All)
                {
                    if (p == null) continue;
                    if (p.GetComponent<ShapeShift>() == null) p.gameObject.AddComponent<ShapeShift>();
                    if (p.GetComponent<SideLook>() == null) p.gameObject.AddComponent<SideLook>();
                }

                // push and pull, before anyone has absorbed anything
                if (!_kitGiven && Grimoire.LocalPlayerId != 0)
                {
                    _kitGiven = true;
                    GrantStartingKit(Grimoire.LocalPlayerId);
                }
            }

            var kb = Keyboard.current;
            if (kb == null || GameMenu.IsOpen) return;
            if (ActiveScene.Name != "Lobby") return;   // lobby only: in a round you get corrupted, not a key
            if (!kb.cKey.wasPressedThisFrame) return;

            Sides.Toggle(Sides.LocalPlayerId);

            bool acolyte = Sides.LocalIsAcolyte;
            DrawingWorld.Instance?.LogEvent(acolyte
                ? "you are an acolyte now" : "you are a wizard now");

            var me = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (me != null) Juice.Chime(me.transform.position);
        }
    }
}
