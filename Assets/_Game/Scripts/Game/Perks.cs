using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// PERK CAULDRONS (CoD perk machines wearing witch hats): walk up, press E,
    /// pay Riches, drink the brew. One perk per cauldron type, they last the
    /// whole run and vanish on run end (wipe or victory). Effects are LOCAL
    /// buffs consulted by the systems they touch — same accepted-divergence
    /// model as Powerups until B4 host authority lands.
    ///
    ///   SURVIVAL — "Iron Gut":    max health 160, faster regen, +60 hp now
    ///   DRAWING  — "Steady Hand": ink well ×1.5, strokes cost 40% less
    ///   SPELL    — "Double Brew": +1 particle per burst, potency ×1.25
    ///   WEAPON   — "Quick Hands": slide racks ×1.6 faster, longer pickup reach
    public static class Perks
    {
        static readonly System.Collections.Generic.HashSet<CauldronType> _owned =
            new System.Collections.Generic.HashSet<CauldronType>();

        public static bool Has(CauldronType t) => _owned.Contains(t);

        public static int Cost(CauldronType t)
        {
            switch (t)
            {
                case CauldronType.Survival: return 50;
                case CauldronType.Drawing: return 35;
                case CauldronType.Spell: return 60;
                default: return 40; // Weapon
            }
        }

        public static string NameOf(CauldronType t)
        {
            switch (t)
            {
                case CauldronType.Survival: return "IRON GUT";
                case CauldronType.Drawing: return "STEADY HAND";
                case CauldronType.Spell: return "DOUBLE BREW";
                default: return "QUICK HANDS";
            }
        }

        /// The dials the rest of the game reads. No perk = exactly the old values.
        public static float MaxHealth => Has(CauldronType.Survival) ? 160f : 100f;
        public static float HealthRegenPerSec => Has(CauldronType.Survival) ? 12f : 8f;
        public static float InkMax => DrawingConfig.InkMax * (Has(CauldronType.Drawing) ? 1.5f : 1f);
        public static float InkCostMul => Has(CauldronType.Drawing) ? 0.6f : 1f;
        public static int ExtraParticles => Has(CauldronType.Spell) ? 1 : 0;
        public static float PotencyMul => Has(CauldronType.Spell) ? 1.25f : 1f;
        public static float RackSpeedMul => Has(CauldronType.Weapon) ? 1.6f : 1f;
        public static float PickupRangeMul => Has(CauldronType.Weapon) ? 1.4f : 1f;

        public static bool TryBuy(CauldronType t, SimpleFPSController buyer)
        {
            if (Has(t))
            {
                DrawingWorld.Instance?.LogEvent($"{NameOf(t)} is already in your blood");
                return false;
            }
            int cost = Cost(t);
            if (Wallet.Riches < cost)
            {
                DrawingWorld.Instance?.LogEvent($"{NameOf(t)} costs {cost} riches — you have {Wallet.Riches}");
                Juice.Thud(buyer != null ? buyer.transform.position : Vector3.zero);
                return false;
            }
            Wallet.Riches -= cost;
            _owned.Add(t);
            if (t == CauldronType.Survival && buyer != null)
                buyer.Health = Mathf.Min(MaxHealth, buyer.Health + 60f); // the first gulp
            if (buyer != null) Juice.Chime(buyer.transform.position);
            DrawingWorld.Instance?.LogEvent($"{NameOf(t)} brewed! ({PerkBlurb(t)})");
            return true;
        }

        static string PerkBlurb(CauldronType t)
        {
            switch (t)
            {
                case CauldronType.Survival: return "tougher, faster mending";
                case CauldronType.Drawing: return "deeper ink well, cheaper strokes";
                case CauldronType.Spell: return "bigger bursts, stronger brews";
                default: return "faster racking, longer reach";
            }
        }

        /// A one-letter badge per owned perk for the HUD.
        public static string HudTag()
        {
            if (_owned.Count == 0) return "";
            string tag = "";
            foreach (CauldronType t in System.Enum.GetValues(typeof(CauldronType)))
                if (Has(t)) tag += NameOf(t)[0];
            return tag;
        }

        /// Wipe or victory — the brews wear off with the run.
        public static void ResetRun() => _owned.Clear();
    }

    /// Attached automatically to every CauldronMarker when a scene loads —
    /// Marko's hand-placed cauldrons become shops with zero re-wiring.
    public class PerkCauldron : MonoBehaviour
    {
        const float Reach = 2.4f;

        CauldronMarker _marker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded += (_, __) => AttachAll();
            AttachAll();
        }

        static void AttachAll()
        {
            foreach (var m in Object.FindObjectsByType<CauldronMarker>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (m.GetComponent<PerkCauldron>() == null)
                    m.gameObject.AddComponent<PerkCauldron>();
        }

        void Awake() => _marker = GetComponent<CauldronMarker>();

        void Update()
        {
            if (_marker == null) return;

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null || player.IsDowned) return;
            if ((player.transform.position - transform.position).sqrMagnitude > Reach * Reach) return;

            // the cauldron is the INK WELL (Marko's ship rule): standing at
            // the pot refills the wand fast — the anchor you keep returning
            // to, in any camera mode, prompt or no prompt
            var ink = player.GetComponent<PlayerInk>();
            if (ink != null) ink.Award(DrawingConfig.CauldronInkPerSec * Time.deltaTime);

            if (PoseStudio.IsOpen || GameMenu.IsOpen) return;
            if (SimpleFPSController.ThirdPersonActive || SelfPaint.IsActive) return;

            // one quiet CoD-style purchase prompt — only while standing at the pot
            UIPrompt.Show("E", Perks.Has(_marker.Type)
                ? Loc.F("perk.brewed", Perks.NameOf(_marker.Type))
                : Loc.F("perk.drink", Perks.NameOf(_marker.Type), Perks.Cost(_marker.Type)));

            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
                Perks.TryBuy(_marker.Type, player);
        }
    }
}
