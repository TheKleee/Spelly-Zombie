using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Plays emotes on an EmoteRig - THIRD PERSON ONLY (in first person the
    /// number keys select weapons). Press a slot to strike the pose: the
    /// ANIMATION sticks doll-like in it while you keep walking around freely
    /// . F melts
    /// back to the regular idle; X forgets your custom pose on that slot so
    /// the system default returns. A held pose is also a spell trigger: it
    /// closes a body seal, the spell runs and goes spent, and releasing the
    /// emote opens the loop so the ink re-arms.
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
            // FIRST PERSON NEVER HOLDS A POSE : posing exists to sculpt
            // in third person and to reach skin in the draw modes. The moment
            // you're back in plain first person, regular animations resume.
            // (Open draw modes keep their pose - that's what it was for.)
            if (IsPosing && !SimpleFPSController.ThirdPersonActive
                && !SelfPaint.IsActive && !HeldWeapon.DrawMode)
                StopToRest();

            ReadHotkeys();
            ShowPoseHint(); // the hint is cosmetic, it goes last
        }

        /// THE POSE IS STAMPED AFTER THE ANIMATOR, EVERY FRAME. It used to
        /// run in Update and only write during a frame's transition - so a
        /// held pose was just RESIDUE that survived because the animator had
        /// been switched off. That killed locomotion (and left the hips, the
        /// one bone no emote owns, frozen by omission). Now the animator
        /// keeps running and the pose overwrites its own joints here.
        void LateUpdate() => Animate();

        /// While HOLDING a pose, the outs are one-fact chips (the block
        /// law). Idle third person says nothing here - ModeGuide's chips
        /// cover the crossroads, one voice per state. Pose-grab keeps its
        /// own instruction line untouched.
        void ShowPoseHint()
        {
            if (!SimpleFPSController.ThirdPersonActive) return;
            if (PoseStudio.IsOpen || SelfPaint.IsActive || Powerups.IsChoosing || GameMenu.IsOpen) return;
            if (PoseGrab.IsOpen)
            {
                // ONLY WHAT CANNOT BE DISCOVERED ("They only
                // need info they can't figure out such as Hold 1, 2, 3 to
                // save the pose... Even pressing 1, 2, 3 to pose will be
                // figured out from the hold to save indicator"). Dragging
                // limbs teaches itself; tap-to-recall follows from the hold
                // chip; only the hold itself is invisible.
                UIPrompt.Offer("R", Loc.T("chip.done"));
                UIPrompt.Offer("HOLD 1-9", Loc.T("shape.save"));
                return;
            }
            if (ActiveSlot < 0) return;
            // holding a pose is a state of its own, so ITS row carries the
            // crossroads too ("It should have an R to pose mode there
            // somewhere") — ModeGuide stands down while IsPosing, one voice.
            // R sculpting is the WIZARD's (acolyte R belongs to the shape).
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

            // F melts the doll back into the regular idle - unless the
            // grimoire has a target (declare/absorb owns F for that press)
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

            // TRANSITION DONE - KEEP STAMPING IT. Returning here is what made
            // a held pose depend on the animator being dead: nothing wrote
            // the bones, so the pose was only the last transition's leftovers.
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
