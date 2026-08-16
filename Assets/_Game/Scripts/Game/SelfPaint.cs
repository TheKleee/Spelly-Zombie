using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// BODY PAINT : R in third person - or in first person with
    /// the wand (slot 1) selected - parks you IDLING exactly as you stand
    /// (current emote pose included) and turns the camera into an easel orbit:
    ///
    /// MMB-drag  rotate all the way around (even the feet)
    /// WASD  move the camera up/down/left/right · scroll  zoom
    ///   LMB inks, RMB erases - brush cursor, no aiming
    /// R (or Esc)  done, camera returns
    ///
    /// You can't walk while painting, but you're NOT safe: gravity, shoves and
    /// damage keep applying (the controller keeps simulating), and getting
    /// floored kicks you out of the mode.
    /// Your own body is invisible to the pen in normal play (Ignore Raycast);
    /// while painting it flips to Default so the ray can land, then flips back.
    public class SelfPaint : MonoBehaviour
    {
        public static bool IsActive { get; private set; }

        static SelfPaint _live;

        /// BLOWN OFF THE EASEL ("whenever you're in any mode like
        /// posing or shapeshift mode... you drop into either first person or
        /// third person mode so that your body can ragdoll"). A hit big enough
        /// ends the mode instead of being swallowed by it - see Shove.
        public static void Blown() { if (IsActive && _live != null) _live.Exit(); }

        /// The painter's root while the mode is open - the pen raycasts ONLY
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

            // THE BOOK FLOATS AT THE EASEL ("grimoire doesn't
            // open and you can't list it" — it DID open, but it sat at the
            // first-person read anchor, which the orbit camera cannot see).
            // While painting, an open grimoire hangs beside the canvas facing
            // the camera; pages and arrows keep working untouched.
            if (_book == null) _book = GetComponentInChildren<GrimoirePages>(true);
            if (_book != null && GrimoirePages.BookOpen && _cam != null)
            {
                if (!_bookFloated)
                {
                    _bookFloated = true;
                    var bt = _book.transform;
                    FloatingBook = bt; // the pen must not mistake it for skin
                    _bookParent = bt.parent;
                    _bookLocalPos = bt.localPosition;
                    _bookLocalRot = bt.localRotation;
                    _bookLocalScale = bt.localScale;
                    // riding the CAMERA, not pinned per frame: the hand bone
                    // (its real parent) keeps animating and would drag a
                    // world-pose pin into one-frame jitter. Reparent once and
                    // the orbit carries the book perfectly.
                    var ct = _cam.transform;
                    bt.SetParent(ct, true);
                    bt.position = ct.position + ct.forward * 0.8f
                        - ct.right * 0.4f - ct.up * 0.08f;
                    // paper normal is the book's +Y (PageAnchor law: "+Y off
                    // the paper, +Z toward the top edge") — aim the SPREAD at
                    // the lens, top edge up. the model needed the flip (the
                    // first guess floated the book upside down at the easel).
                    bt.rotation = Quaternion.LookRotation(-ct.up, -ct.forward);
                }
            }
            else if (_bookFloated) RestoreBook();

            // THE WAY OUT LIVES ON SCREEN, NOT IN THE CONSOLE (
            // "when painting the body no one knows how to turn it off, or that
            // clicking numbers will pose you") — as one-fact chips. The book's
            // own pages chip takes the third slot of the extreme maximum now
            // ,
            // so the MMB orbit chip retired: dragging is how orbits are found.
            UIPrompt.Offer("R", Loc.T("paint.done"));
            UIPrompt.Offer("1-9", Loc.T("paint.pose"));

            // I = DRINK THE BODY INK (sacrifice every drawing on your
            // body to regrow the wand - a deliberate, out-of-the-way key).
            // Enough ink = full wand, surplus wasted; less = a partial wand.
            // (Shown AFTER the exit line so the rarer, more urgent state wins
            // the single slot: a melted wand outranks the tour.)
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
        /// is honestly wasted - the rule.
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
                _drunkStrokes.Add(s); // remembered BEFORE Burn - friends' copies must die too
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
            // F relaxes the pose - unless the grimoire has a target under the
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
        /// A POSE THE SHELL CANNOT COVER MUST NOT KILL THE PEN (
        /// "not allowing me to erase the old drawing on my body") — this used
        /// to ignore a failed bake, leaving NO canvas at all: no drawing, no
        /// erasing, rays sailing through the body. The capsules step in, and
        /// step back out the moment a later pose bakes an honest shell.
        void RebakeShell()
        {
            var body = GetComponent<CharacterRig>();
            if (body == null || !body.HasBody) return;
            body.EndBodyPaint();  // rebase existing ink to bones
            if (body.BeginBodyPaint()) DisarmCapsules();
            else ArmCapsules(body);
        }

        /// FIRST person + the wand slot only: with a weapon selected R engraves
        /// the WEAPON, and in third person R belongs to POSE MODE (PoseGrab) -
        /// the split: draw on yourself with the wand, pose from outside.
        bool CanEnter()
        {
            // ACOLYTES CANNOT DRAW ON THEIR OWN BODY - and
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
            _live = this;

            // THE BOOK IS ALWAYS OPEN AT THE EASEL ("we can't expect
            // people to learn the runes so quickly - it makes sense for me
            // but not for others"): painting means reading; the reference
            // opens with the mode and refuses to close until you leave.
            GrimoirePages.RequestOpen();

            _camLocalPos = _cam.transform.localPosition;
            _camLocalRot = _cam.transform.localRotation;

            // start facing the front, slightly above eye line
            _yaw = transform.eulerAngles.y + 180f;
            _pitch = 12f;
            _dist = 2.3f;
            _pan = Vector3.zero;

            // the pen paints the LIMB CAPSULES on the bones - the body's
            // official ink surfaces (one stroke per limb, joints bridged by
            // strokes meeting: the seal design). The skinned mesh is never a
            // pen target; it's looks-only. Relax the pose first so the canvas
            // is a neutral stance that holds perfectly still.
            var body = GetComponent<CharacterRig>();
            if (body != null && body.HasBody) body.RelaxForPaint();

            // THE CANVAS IS THE SKIN: the frozen pose bakes into an invisible
            // collider glued over the visible mesh - ink lands exactly where
            // you see it, capsules never enter into it. Only when the bake is
            // impossible (mesh not readable) do the ragdoll capsules step in.
            bool skin = body != null && body.HasBody && body.BeginBodyPaint();
            Debug.Log(skin
                ? "[SpellyZombie] CANVAS: skin shell ('PaintShell' in the Hierarchy)"
                : "[SpellyZombie] CANVAS: limb capsules. The skin shell was unavailable, see the message above");
            if (!skin) ArmCapsules(body);

            ActiveRoot = transform;
            Orbit(); // snap the easel into place this frame
            Debug.Log("[SpellyZombie] Body paint: MMB-drag rotates, WASD moves the camera, scroll zooms, LMB ink / RMB erase, R done");
        }

        /// The grimoire while it hangs at the easel (null otherwise) - the
        /// paint ray skips this subtree so the book never soaks up a stroke.
        public static Transform FloatingBook { get; private set; }

        GrimoirePages _book;
        bool _bookFloated;
        Transform _bookParent;
        Vector3 _bookLocalPos;
        Quaternion _bookLocalRot;
        Vector3 _bookLocalScale;

        /// Hand the book back to its hand socket exactly as it sat. The full
        /// local pose is restored (SetParent's world-stays math compensates
        /// localScale on the way out, so scale must come back too).
        void RestoreBook()
        {
            _bookFloated = false;
            FloatingBook = null;
            if (_book == null) return;
            var bt = _book.transform;
            bt.SetParent(_bookParent, true);
            bt.localPosition = _bookLocalPos;
            bt.localRotation = _bookLocalRot;
            bt.localScale = _bookLocalScale;
        }

        void Exit()
        {
            RestoreBook();
            IsActive = false;
            _live = null;
            ActiveRoot = null;
            // skin-shell ink moves onto the bones so it rides the animation;
            // fallback capsule ink already lives there
            GetComponent<CharacterRig>()?.EndBodyPaint();
            DisarmCapsules();
            if (_cam != null)
            {
                _cam.transform.localPosition = _camLocalPos;
                _cam.transform.localRotation = _camLocalRot;
            }
        }

        bool _capsulesArmed;

        /// The pen's fallback canvas: flip the layer-2 hierarchy pen-visible
        /// and slim the ragdoll capsules toward the skin.
        void ArmCapsules(CharacterRig body)
        {
            if (_capsulesArmed) return;
            _capsulesArmed = true;
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
                            // padding) - slim them so ink sits nearer
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

        void DisarmCapsules()
        {
            if (!_capsulesArmed) return;
            _capsulesArmed = false;
            foreach (var go in _relayered)
                if (go != null) go.layer = 2;
            _relayered.Clear();
            foreach (var (cap, radius) in _slimmed)
                if (cap != null) cap.radius = radius; // ragdoll gets its padding back
            _slimmed.Clear();
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

            // MMB-drag rotates; WASD MOVES the camera - the ONE
            // shared easel language (EaselOrbit, same as pose mode)
            var rot = EaselOrbit.Tick(Keyboard.current, mouse,
                ref _yaw, ref _pitch, ref _dist, ref _pan,
                allowZoom: true, zoomMin: 0.9f);

            // player root sits mid-body (the CC is centered on it) - orbiting
            // this point keeps head AND feet reachable
            EaselOrbit.Apply(_cam, transform.position + _pan, rot, _dist);
        }
    }
}
