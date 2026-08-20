using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Plays emotes on an EmoteRig - third person only (first person number
    /// keys select weapons). A slot key strikes the pose while movement stays
    /// free; F melts back to idle; X clears the custom binding on that slot.
    /// A held pose closes a body seal; releasing it re-arms the ink.
    [RequireComponent(typeof(EmoteRig))]
    public class EmotePlayer : MonoBehaviour
    {
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
            // plain first person never holds a pose; open draw modes keep theirs
            if (IsPosing && !SimpleFPSController.ThirdPersonActive
                && !SelfPaint.IsActive && !HeldWeapon.DrawMode)
                StopToRest();

            ReadHotkeys();
            ShowPoseHint(); // the hint is cosmetic, it goes last
        }

        /// The pose is stamped after the animator, every frame: the animator
        /// keeps running and the pose overwrites only its own joints.
        void LateUpdate() => Animate();

        /// While holding a pose the outs are offered as chips; idle third
        /// person is ModeGuide's job.
        void ShowPoseHint()
        {
            if (!SimpleFPSController.ThirdPersonActive) return;
            if (PoseStudio.IsOpen || SelfPaint.IsActive || Powerups.IsChoosing || GameMenu.IsOpen) return;
            if (PoseGrab.IsOpen)
            {
                // only the undiscoverable is hinted: hold-to-save
                UIPrompt.Offer("R", Loc.T("chip.done"));
                UIPrompt.Offer("HOLD 1-9", Loc.T("shape.save"));
                return;
            }
            if (ActiveSlot < 0) return;
            // while posing this row carries the crossroads (ModeGuide stands
            // down); the R chip is wizard-only (acolyte R belongs to the shape)
            UIPrompt.Offer("F", Loc.T("chip.melt"));
            UIPrompt.Offer("TAB", Loc.T("chip.first"));
            if (Sides.Of(Grimoire.LocalPlayerId) != Side.Acolyte)
                UIPrompt.Offer("R", Loc.T("chip.pose"));
        }

        void ReadHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // emotes live in THIRD person - first person numbers are weapon slots
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

            // F melts back to idle unless the grimoire has a target (declare/absorb owns F)
            if (kb.fKey.wasPressedThisFrame && ActiveSlot >= 0
                && !GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach)
            {
                StopToRest();
                return;
            }

            // X forgets YOUR pose on the active slot - the built-in returns
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
                    $"Slot {slot} already runs the built-in emote, which can't be removed");
                return;
            }
            EmoteLibrary.ClearSlot(slot);
            var def = EmoteLibrary.DefaultForSlot(slot);
            if (def != null) Play(def, slot);
            else StopToRest();
            DrawingWorld.Instance?.LogEvent($"Custom emote cleared, key {slot} runs the built-in again");
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
                DrawingWorld.Instance?.LogEvent($"Key {slot} has no pose. Make one in the Pose Studio (B)");
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

        /// The pose editor grabbed a joint - stop animating so we don't fight it.
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
                    EmoteRig.Constrain(j); // saved files obey the hinges too
                }
                return;
            }

            // transition done - keep stamping every frame or the animator
            // overwrites the held pose
            foreach (var p in frame.poses)
            {
                var j = _rig.Find(p.joint);
                if (j?.T == null) continue;
                j.T.localRotation = Quaternion.Euler(p.euler);
                EmoteRig.Constrain(j);
            }

            // frame reached - hold, then advance / loop / stay on the pose
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
