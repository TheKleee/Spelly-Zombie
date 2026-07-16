using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Plays emotes on an EmoteRig — THIRD PERSON ONLY (in first person the
    /// number keys select weapons). Press a slot to strike the pose: the
    /// ANIMATION sticks doll-like in it while you keep walking around freely
    /// (Marko's rule — pose freezes the rig, never the character). F melts
    /// back to the regular idle; X forgets your custom pose on that slot so
    /// the system default returns. A held pose is also a spell trigger: it
    /// closes a body seal, the spell runs and goes spent, and releasing the
    /// emote opens the loop so the ink re-arms.
    [RequireComponent(typeof(EmoteRig))]
    public class EmotePlayer : MonoBehaviour
    {
        /// Graybox: this instance reacts to T / 1-9 directly.
        public bool ListenToHotkeys = true;

        public int ActiveSlot { get; private set; } = -1;

        /// A pose is being held (rig frozen; the character still moves freely).
        public bool IsPosing => ActiveSlot >= 0;

        EmoteRig _rig;
        EmoteDef _emote;
        int _frame = -1;
        float _t;
        float _holdLeft;
        bool _returningToRest;
        readonly Dictionary<string, Quaternion> _blendFrom = new Dictionary<string, Quaternion>();

        void Awake()
        {
            _rig = GetComponent<EmoteRig>();
        }

        void Update()
        {
            if (ListenToHotkeys) ReadHotkeys();
            Animate();      // the doll animates even if UI code throws —
            ShowPoseHint(); // the hint is cosmetic, it goes last
        }

        /// Third person always tells you where poses are MADE — Marko: "while
        /// in the poses there should be an option to set them".
        void ShowPoseHint()
        {
            if (!ListenToHotkeys || !SimpleFPSController.ThirdPersonActive) return;
            if (PoseStudio.IsOpen || SelfPaint.IsActive || Powerups.IsChoosing || GameMenu.IsOpen) return;
            UIPrompt.Show("R", PoseGrab.IsOpen
                ? Loc.T("pose.grab")
                : ActiveSlot >= 0
                    ? Loc.T("pose.key")
                    : Loc.T("pose.enter"));
        }

        void ReadHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // emotes live in THIRD person — first person numbers are weapon slots
            if (!SimpleFPSController.ThirdPersonActive) return;
            // brush out: the doll must hold still for the painter
            if (SelfPaint.IsActive) return;
            // pose mode owns the number keys (tap = load, hold = save)
            if (PoseGrab.IsOpen) return;
            // in the Pose Studio, number keys bind poses instead of playing them
            if (PoseStudio.IsOpen || UIKit.Typing) return;
            // while choosing a powerup, 1-3 pick cards, not poses
            if (Powerups.IsChoosing) return;
            if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) return;

            // F melts the doll back into the regular idle
            if (kb.fKey.wasPressedThisFrame && ActiveSlot >= 0)
            {
                StopToRest();
                return;
            }

            // X forgets YOUR pose on the active slot — the built-in returns
            if (kb.xKey.wasPressedThisFrame && ActiveSlot >= 0)
            {
                ClearActiveToDefault();
                return;
            }

            if (kb.tKey.wasPressedThisFrame) ToggleSlot(1);
            for (int slot = 1; slot <= 9; slot++)
            {
                var key = kb[(Key)((int)Key.Digit1 + slot - 1)];
                if (key != null && key.wasPressedThisFrame) ToggleSlot(slot);
            }
        }

        void ClearActiveToDefault()
        {
            int slot = ActiveSlot;
            if (!EmoteLibrary.HasCustom(slot))
            {
                DrawingWorld.Instance?.LogEvent(
                    $"Slot {slot} already runs the built-in emote — those can't be removed");
                return;
            }
            EmoteLibrary.ClearSlot(slot);
            var def = EmoteLibrary.DefaultForSlot(slot);
            if (def != null) Play(def, slot);
            else StopToRest();
            DrawingWorld.Instance?.LogEvent($"Custom emote cleared from key {slot} — back to the built-in");
        }

        public void ToggleSlot(int slot)
        {
            if (ActiveSlot == slot)
            {
                StopToRest();
                return;
            }
            var def = EmoteLibrary.GetSlot(slot); // custom binding, or the built-in
            if (def == null || def.frames.Count == 0)
            {
                DrawingWorld.Instance?.LogEvent($"Key {slot} has no pose — the Pose Studio (B) makes and binds one");
                return;
            }
            Play(def, slot);
        }

        public void Play(EmoteDef def, int slot)
        {
            _emote = def;
            ActiveSlot = slot;
            _returningToRest = false;
            EnterFrame(0);
            DrawingWorld.Instance?.LogEvent($"Emote: {def.name} (slot {slot})");
        }

        public void StopToRest()
        {
            ActiveSlot = -1;
            _emote = null;
            _frame = -1;
            _t = 0f;
            _returningToRest = true;
            CaptureBlendFrom();
        }

        /// The pose editor grabbed a joint — stop animating so we don't fight it.
        public void Interrupt()
        {
            ActiveSlot = -1;
            _emote = null;
            _frame = -1;
            _returningToRest = false;
        }

        void EnterFrame(int index)
        {
            _frame = index;
            _t = 0f;
            _holdLeft = _emote.frames[index].hold;
            CaptureBlendFrom();
        }

        void CaptureBlendFrom()
        {
            _blendFrom.Clear();
            foreach (var j in _rig.Joints)
                if (j.T != null) _blendFrom[j.Id] = j.T.localRotation;
        }

        void Animate()
        {
            if (_returningToRest)
            {
                _t += Time.deltaTime;
                float a = Smooth(Mathf.Clamp01(_t / 0.35f));
                foreach (var j in _rig.Joints)
                    if (j.T != null && _blendFrom.TryGetValue(j.Id, out var from))
                        j.T.localRotation = Quaternion.Slerp(from, j.Rest, a);
                if (_t >= 0.35f) _returningToRest = false;
                return;
            }

            if (_emote == null || _frame < 0 || _frame >= _emote.frames.Count) return;

            var frame = _emote.frames[_frame];
            float duration = Mathf.Max(0.05f, frame.transition);

            if (_t < duration)
            {
                _t += Time.deltaTime;
                float a = Smooth(Mathf.Clamp01(_t / duration));
                foreach (var p in frame.poses)
                {
                    var j = _rig.Find(p.joint);
                    if (j?.T == null || !_blendFrom.TryGetValue(p.joint, out var from)) continue;
                    j.T.localRotation = Quaternion.Slerp(from, Quaternion.Euler(p.euler), a);
                }
                return;
            }

            // frame reached — hold, then advance / loop / stay on the pose
            if (_frame >= _emote.frames.Count - 1)
            {
                if (_emote.loop && _emote.frames.Count > 1)
                    EnterFrame(0);
                return; // non-loop: hold the pose until toggled off
            }

            _holdLeft -= Time.deltaTime;
            if (_holdLeft <= 0f)
                EnterFrame(_frame + 1);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
