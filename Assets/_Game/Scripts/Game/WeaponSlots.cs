using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The hands : slot 1 is the wand + grimoire - always there,
    /// never droppable, and the ONLY slot whose left/right click is the pen.
    /// Slots 2-3 hold picked-up weapons, each with its own click mechanics.
    ///
    /// E       take the weapon you're standing near (max 3 carried)
    /// F       drop the selected weapon (slot 1 refuses)
    /// 1/2/3   select a slot - FIRST person only; in third person those
    ///            same keys play emotes and the weapon selection is untouched
    public class WeaponSlots : MonoBehaviour
    {
        public const int MaxSlots = 3;
        const float PickupRange = 2.6f;
        const float AimCosine = 0.78f; // ~39° cone - forgiving enough for small items in motion

        readonly HeldWeapon[] _held = new HeldWeapon[MaxSlots + 1]; // 1-based; [1] stays null (wand is innate)
        public int Current { get; private set; } = 1;

        public bool PenSelected => Current == 1;

        /// The weapon in the selected slot (null for slot 1 - the wand is innate).
        public HeldWeapon CurrentWeapon => _held[Current];

        SimpleFPSController _pilot;

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        // WEAPONS ARE OUT OF THE GAME ("remove weapons from the
        // game completely... they do not belong to this game at all right now.
        // They might belong in some future game modes"). This component stays
        // only because the baked Player prefab and SimpleFPSController (the
        // file) reference it - but it DOES NOTHING: no slot keys, no ground
        // offers, no E pickup, no F drop. Slot 1 (wand + grimoire) is the only
        // hand there is. The engine below is kept for the possible future
        // mode, unreachable until a ruling brings it back.
        void Update() { }

        void Select(int slot)
        {
            if (slot == Current) return;
            if (slot != 1 && _held[slot] == null)
            {
                DrawingWorld.Instance?.LogEvent($"Slot {slot} is empty. E picks up a weapon");
                return;
            }
            HeldWeapon.CancelDrawMode();
            Current = slot;
            DrawingWorld.Instance?.LogEvent(slot == 1
                ? "Wand + grimoire, the pen draws again"
                : $"Weapon slot {slot}");
        }

        /// AIM, NOT PROXIMITY : E takes what you're LOOKING AT within reach - most centred in the cone wins.
        HeldWeapon FindPickup()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot == null) return null;

            HeldWeapon best = null;
            float reach = PickupRange * Perks.PickupRangeMul; // Quick Hands stretches
            float bestAim = 0f;
            foreach (var w in HeldWeapon.All)
            {
                if (w == null || w.Held) continue;
                float aim = _pilot.AimScore(w.transform.position, reach, AimCosine, w.transform);
                if (aim > bestAim) { bestAim = aim; best = w; } // most CENTRED wins
            }
            return best;
        }

        void TryPickup()
        {
            var best = FindPickup();
            if (best == null) return;

            int free = _held[2] == null ? 2 : _held[3] == null ? 3 : -1;
            if (free < 0)
            {
                DrawingWorld.Instance?.LogEvent("Hands full (3 max). F drops the current weapon first");
                return;
            }
            _held[free] = best;
            best.EquipTo(_pilot);
            Current = free;
            DrawingWorld.Instance?.LogEvent($"Picked up → slot {free} (F drops it, 1 returns to the wand)");
        }

        void DropCurrent()
        {
            if (Current == 1)
            {
                DrawingWorld.Instance?.LogEvent("The wand and grimoire never leave your hands");
                return;
            }
            var w = _held[Current];
            _held[Current] = null;
            Current = 1;
            if (w != null)
                w.Drop(transform.position + transform.forward * 1.2f + Vector3.up * 0.35f);
        }

        void ApplyVisibility()
        {
            // in third person (and while body-painting) the held weapon would
            // float in mid-air next to the pulled-back camera - hide it
            bool showHeld = !SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive;
            for (int slot = 2; slot <= MaxSlots; slot++)
            {
                var w = _held[slot];
                if (w == null) continue;
                bool show = showHeld && slot == Current;
                if (w.gameObject.activeSelf != show) w.gameObject.SetActive(show);
            }
        }
    }
}
