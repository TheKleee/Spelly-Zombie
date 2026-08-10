using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// BODY PAINT (Marko's spec): R in third person — or in first person with
    /// the wand (slot 1) selected — parks you IDLING exactly as you stand
    /// (current emote pose included) and turns the camera into an easel orbit:
    ///
    ///   MMB-drag → rotate all the way around (even the feet)
    ///   WASD → move the camera up/down/left/right · scroll → zoom
    ///   LMB inks, RMB erases — brush cursor, no aiming
    ///   R (or Esc) → done, camera returns
    ///
    /// You can't walk while painting, but you're NOT safe: gravity, shoves and
    /// damage keep applying (the controller keeps simulating), and getting
    /// floored kicks you out of the mode.
    /// Your own body is invisible to the pen in normal play (Ignore Raycast);
    /// while painting it flips to Default so the ray can land, then flips back.
    public class SelfPaint : MonoBehaviour
    {
        public static bool IsActive { get; private set; }

        /// The painter's root while the mode is open — the pen raycasts ONLY
        /// against this hierarchy (a miss draws NOTHING; never ink on the
        /// world behind you = no accidental spells).
        public static Transform ActiveRoot { get; private set; }

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        EmotePlayer _emotes;
        float _rebakeIn = -1f; // >0 while a just-loaded pose settles before the shell re-bakes
        Camera _cam;
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;
        float _yaw, _pitch, _dist;
        Vector3 _pan; // WASD camera slide (world offset from the body), MMB rotates
        readonly List<GameObject> _relayered = new List<GameObject>();
        readonly List<(CapsuleCollider col, float radius)> _slimmed
            = new List<(CapsuleCollider, float)>();

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (PoseStudio.IsOpen || GameMenu.IsOpen || Powerups.IsChoosing || UIKit.Typing)
            {
                if (IsActive) Exit();
                return;
            }

            // a zombie swipe ends the art session
            if (IsActive && _pilot != null
                && (_pilot.IsDowned || _pilot.IsSprawled || _pilot.IsAirTumbling))
            {
                Exit();
                return;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                if (IsActive) { Exit(); return; }
                if (CanEnter()) Enter();
            }
            if (IsActive && kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (!IsActive) return;

            PosePicker(kb);
            Orbit();

            // I = DRINK THE BODY INK (Marko: sacrifice every drawing on your
            // body to regrow the wand — a deliberate, out-of-the-way key).
            // Enough ink = full wand, surplus wasted; less = a partial wand.
            var wand = GetComponent<WandState>();
            if (wand != null && !wand.HasWand)
                UIPrompt.Show("I", "drink body ink to regrow your wand",
                    new Color(0.85f, 0.8f, 1f));
            if (kb.iKey.wasPressedThisFrame) DrinkBodyInk();
        }

        static readonly System.Collections.Generic.List<Stroke> _drunkStrokes =
            new System.Collections.Generic.List<Stroke>(); // reused buffer (no-alloc law)

        /// Every persistent drawing on THIS body flows back into the well.
        /// The worth is exactly the ink the lines are made of (length ×
        /// cost-per-meter); Award clamps at the well's ceiling, so surplus
        /// is honestly wasted — Marko's rule.
        void DrinkBodyInk()
        {
            var world = DrawingWorld.Instance;
            if (world == null) return;
            var ink = GetComponent<PlayerInk>();
            if (ink == null) return;

            float drunk = 0f;
            int strokes = 0;
            _drunkStrokes.Clear();
            for (int i = world.Strokes.Count - 1; i >= 0; i--)
            {
                var s = world.Strokes[i];
                if (s == null || !s.Alive || !s.Persistent) continue;
                if (s.OwnerId != Grimoire.LocalPlayerId) continue;
                var f = s.First;
                if (f == null || !f.transform.IsChildOf(transform)) continue; // MY body only
                drunk += s.PathLength() * DrawingConfig.InkCostPerMeter;
                strokes++;
                _drunkStrokes.Add(s); // remembered BEFORE Burn — friends' copies must die too
                s.Burn();
            }
            // the copies riding my avatar on every other machine (parity law)
            NetSync.OnLocalInkBurned(_drunkStrokes);
            if (strokes == 0)
            {
                DrawingWorld.Instance?.LogEvent("no body ink to drink");
                return;
            }
            ink.Award(drunk);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent(
                $"the body ink flows back into the wand ({strokes} drawing(s) drunk)");
        }

        /// While painting the body: 1-9 drop into a SAVED POSE so you can draw
        /// seals in the shape they'll fire in; F relaxes back to idle. The
        /// skin-paint shell re-bakes once the pose settles so ink lands on the
        /// posed body; ink already drawn rides the bones into the new pose.
        void PosePicker(Keyboard kb)
        {
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            if (_emotes == null) return;

            for (int slot = 1; slot <= 9; slot++)
            {
                var key = kb[(Key)((int)Key.Digit1 + (slot - 1))];
                if (key != null && key.wasPressedThisFrame) { LoadPose(slot); return; }
            }
            // F relaxes the pose — unless the grimoire has a target under the
            // cursor (declare/absorb owns F for that press)
            if (kb.fKey.wasPressedThisFrame && _emotes.IsPosing
                && !GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach)
            {
                _emotes.StopToRest();
                _rebakeIn = PoseSettle;
            }

            if (_rebakeIn > 0f)
            {
                _rebakeIn -= Time.deltaTime;
                if (_rebakeIn <= 0f) RebakeShell();
            }
        }

        const float PoseSettle = 0.4f; // let the emote blend land before re-baking

        void LoadPose(int slot)
        {
            _emotes.ToggleSlot(slot); // load (or toggle off) that saved pose
            _rebakeIn = PoseSettle;
        }

        /// Move ink drawn in the old pose onto the bones (rides the new pose),
        /// then re-bake the paint shell against the body's current shape.
        void RebakeShell()
        {
            var body = GetComponent<CharacterRig>();
            if (body == null || !body.HasBody) return;
            body.EndBodyPaint();  // rebase existing ink to bones
            body.BeginBodyPaint(); // fresh shell for the settled pose
        }

        /// FIRST person + the wand slot only: with a weapon selected R engraves
        /// the WEAPON, and in third person R belongs to POSE MODE (PoseGrab) —
        /// Marko's split: draw on yourself with the wand, pose from outside.
        bool CanEnter()
        {
            // ACOLYTES CANNOT DRAW ON THEIR OWN BODY (his Aug 8 ruling) — and
            // R belongs to rotating the worn shape for them, so body paint
            // must never steal the key
            if (Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte) return false;
            if (_pilot != null
                && (_pilot.IsDowned || _pilot.IsSprawled || _pilot.IsAirTumbling)) return false;
            if (SimpleFPSController.ThirdPersonActive) return false;
            if (_slots == null) _slots = GetComponent<WeaponSlots>();
            return _slots == null || _slots.PenSelected;
        }

        void Enter()
        {
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_cam == null) return;
            IsActive = true;

            _camLocalPos = _cam.transform.localPosition;
            _camLocalRot = _cam.transform.localRotation;

            // start facing the front, slightly above eye line
            _yaw = transform.eulerAngles.y + 180f;
            _pitch = 12f;
            _dist = 2.3f;
            _pan = Vector3.zero;

            // the pen paints the LIMB CAPSULES on the bones — the body's
            // official ink surfaces (one stroke per limb, joints bridged by
            // strokes meeting: the seal design). The skinned mesh is never a
            // pen target; it's looks-only. Relax the pose first so the canvas
            // is a neutral stance that holds perfectly still.
            var body = GetComponent<CharacterRig>();
            if (body != null && body.HasBody) body.RelaxForPaint();
            _relayered.Clear();
            _slimmed.Clear();

            // THE CANVAS IS THE SKIN: the frozen pose bakes into an invisible
            // collider glued over the visible mesh — ink lands exactly where
            // you see it, capsules never enter into it. Only when the bake is
            // impossible (mesh not readable) do the ragdoll capsules step in.
            bool skin = body != null && body.HasBody && body.BeginBodyPaint();
            Debug.Log(skin
                ? "[SpellyZombie] CANVAS: skin shell ('PaintShell' in the Hierarchy)"
                : "[SpellyZombie] CANVAS: limb capsules. The skin shell was unavailable, see the message above");
            if (!skin)
            {
                bool skipRoot = body != null && body.HasBody; // fat CC capsule stays pen-proof
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == 2 && !(skipRoot && t == transform))
                    {
                        _relayered.Add(t.gameObject);
                        t.gameObject.layer = 0;

                        if (skipRoot)
                        {
                            var cap = t.GetComponent<CapsuleCollider>();
                            if (cap != null)
                            {
                                // the auto-fit capsules are fat (ragdoll
                                // padding) — slim them so ink sits nearer
                                // the skin, restored on exit. The TORSO
                                // capsules barely clear the mesh already:
                                // halving them sank chest ink UNDER the
                                // skin (invisible strokes), so they keep
                                // most of their girth.
                                _slimmed.Add((cap, cap.radius));
                                cap.radius *= body != null && body.IsTorsoBone(t) ? 0.85f : 0.5f;
                            }
                        }
                    }
            }

            ActiveRoot = transform;
            Orbit(); // snap the easel into place this frame
            Debug.Log("[SpellyZombie] Body paint: MMB-drag rotates, WASD moves the camera, scroll zooms, LMB ink / RMB erase, R done");
        }

        void Exit()
        {
            IsActive = false;
            ActiveRoot = null;
            // skin-shell ink moves onto the bones so it rides the animation;
            // fallback capsule ink already lives there
            GetComponent<CharacterRig>()?.EndBodyPaint();
            foreach (var go in _relayered)
                if (go != null) go.layer = 2;
            _relayered.Clear();
            foreach (var (cap, radius) in _slimmed)
                if (cap != null) cap.radius = radius; // ragdoll gets its padding back
            _slimmed.Clear();
            if (_cam != null)
            {
                _cam.transform.localPosition = _camLocalPos;
                _cam.transform.localRotation = _camLocalRot;
            }
        }

        void OnDestroy()
        {
            if (IsActive) IsActive = false;
            if (ActiveRoot == transform) ActiveRoot = null;
        }

        void Orbit()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null) return;

            // MMB-drag rotates; WASD MOVES the camera (Marko's spec) — the ONE
            // shared easel language (EaselOrbit, same as pose mode)
            var rot = EaselOrbit.Tick(Keyboard.current, mouse,
                ref _yaw, ref _pitch, ref _dist, ref _pan,
                allowZoom: true, zoomMin: 0.9f);

            // player root sits mid-body (the CC is centered on it) — orbiting
            // this point keeps head AND feet reachable
            EaselOrbit.Apply(_cam, transform.position + _pan, rot, _dist);
        }
    }
}
