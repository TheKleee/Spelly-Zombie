using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Marko's wizard body, integrated (auto-added to every player): swaps the
    /// graybox bean for the Mixamo-rigged model at runtime — his scenes never
    /// change, and without the wired asset the bean quietly stays.
    ///
    /// What the swap does:
    ///  · faces the model the way the player faces (the FBX imports backwards)
    ///  · lowers the arms out of the T-pose — THAT relaxed stance is "rest"
    ///  · fits capsule/camera/crouch to the model (slim capsule — the body
    ///    must be the thing you draw on, not an invisible balloon around it)
    ///  · rebuilds the EmoteRig on real bones, same ids as the bean — saved
    ///    poses carry over; adds spine + leg.L/.R for full-body posing
    ///  · per-limb colliders: ragdoll segments AND body-paint ink surfaces
    ///  · first person: body stays visible, only the HEAD hides (camera lives
    ///    inside it) — look down and you see yourself; arms REACH for the held
    ///    weapon procedurally, so weapon-holding reads without authored anims
    ///  · googly eyes on the head bone; hat/cape sockets await the art
    public class CharacterRig : MonoBehaviour
    {
        // ---- MARKO'S KNOBS — tweak freely, everything reads from here ------
        /// Eye fit in HEAD-BONE local space. Used by players, remote avatars
        /// and zombies alike (one edit fixes all three).
        // Marko's inspector fit, VERBATIM (July 13). Do not drift, do not hide.
        public static Vector3 EyeLocalPos = new Vector3(0f, 0.125f, -0.02f);
        public static float EyeScale = 0.8f;    // googly rig's internal scale
        public static float EyeRigScale = 0.8f; // his transform-scale on top
        /// First-person camera sits this × body-height IN FRONT of the body
        /// axis — far enough that the head/torso stay behind the near plane
        /// while the hands holding the weapon stay in view.
        public static float EyeForwardFactor = 0.18f;
        /// Head-follows-aim: how much of the look pitch the head and spine
        /// take (others see where you look). Bend is capped so staring at
        /// your feet can't fold the chest into the camera.
        public static float HeadFollowPitch = 0.55f;
        public static float SpineFollowPitch = 0.25f;
        public static float FollowPitchCap = 60f;
        /// Hinge direction for the limited joints — flip a sign if a test bend
        /// folds the wrong way on the real rig (one-knob tradition).
        public static float ElbowHingeSign = -1f; // forearms swing FORWARD/up
        public static float KneeHingeSign = 1f;   // shins fold BACKWARD
        // ---------------------------------------------------------------------

        // sockets live in the ONE socket system (SocketSet) — duplicate rig
        // empties confused Marko's hierarchy ("Socket.Cape twice")
        /// Weapons glue themselves into this (the standard HandR socket —
        /// plain character space, so Marko's grip-pivot weapons drop in).
        public Transform GripSocketR => _sockets != null ? _sockets.Get("HandR") : null;

        /// True once the real model is worn (SelfPaint then skips the fat
        /// invisible controller capsule and paints the limbs directly).
        public bool HasBody => _smr != null;

        /// The worn body model (the CharacterBaker clones this).
        public GameObject ModelGO => _model;

        /// The hips/chest capsules are deliberately fat (they sit just OUTSIDE
        /// the mesh) — SelfPaint's fallback must not slim them under the skin.
        public bool IsTorsoBone(Transform t) => t == _hips || t == _spine1;

        [Header("His body — drag it in and the code stops guessing")]
        [Tooltip("The BODY's Skinned Mesh Renderer (SZ_Body). Set this and the rig " +
                 "uses it, full stop. Leave it empty and the rig searches, skipping " +
                 "anything worn or held. Worth setting: a PROP can be a skinned mesh " +
                 "too — his grimoire is — and a baked prefab carries it inside the " +
                 "arm, where a depth-first search reaches it before the body.")]
        public SkinnedMeshRenderer BodyRenderer;

        static readonly Color SkinColor = new Color(0.93f, 0.87f, 0.72f); // temp — Marko restyles materials

        [Header("Natural stride (Marko's root-motion experiment)")]
        [Tooltip("ON: damped blending + cycle playback synced to actual ground speed (the in-place-clip substitute for root motion). OFF: the old raw feel. Flip live to compare.")]
        public bool NaturalStride = true;
        [Tooltip("Ground speed (m/s) the WALK cycle naturally covers. If feet slide at no-shift speed, LOWER this (faster playback). ~2.4 grips a 4.5 m/s stroll.")]
        public float WalkClipSpeed = 2.4f;
        [Tooltip("Ground speed (m/s) the RUN cycle naturally covers. If feet slide at sprint speed, nudge this one. (4.5 = Marko's tuned value.)")]
        public float RunClipSpeed = 4.5f;
        [Tooltip("Mixamo crouch clips sneak at an angle baked into the pose. This yaw (degrees) turns the crouched body back to face its travel. Dial live while crouch-walking until he faces forward; 0 = off.")]
        public float CrouchYawFix = 38f;

        [Tooltip("While posing, the shoulder (clavicle) bone carries this share of the upper arm's travel, a real shoulder girdle instead of a dead one. 0 = old behavior.")]
        public float ClavicleFollow = 0.3f;

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        EmotePlayer _emotes;
        Animator _anim;
        Transform _clavL, _clavR;
        Quaternion _clavLRest, _clavRRest, _armLRest, _armRRest;
        Quaternion _armLWritten, _armRWritten; // what WE last wrote — held poses must not re-redistribute
        float _crouchYawW; // crouch yaw-fix blend weight (0 standing → 1 crouched)

        /// Redistribute the arm's pose rotation: the clavicle takes
        /// ClavicleFollow of it, the arm keeps the remainder — the TOTAL
        /// reach stays what the pose asked for, but the girdle participates.
        /// Only acts when something NEW wrote the arm (a held pose that nobody
        /// rewrites must not be re-eaten frame after frame).
        void FollowClavicle(Transform clav, Quaternion clavRest, Transform arm,
            Quaternion armRest, ref Quaternion lastWritten)
        {
            if (clav == null || arm == null) return;
            Quaternion full = arm.localRotation;
            if (Quaternion.Angle(full, lastWritten) < 0.05f) return;
            Quaternion delta = full * Quaternion.Inverse(armRest);
            // ADDITIVE ONLY (Marko: a loaded pose "must be exactly the same
            // as the pose I saved"): the clavicle DERIVES its share from the
            // arm, the arm keeps precisely what the pose system wrote.
            // Deriving is repeatable — save→load→save can't drift. The old
            // split reduced the arm on every write, so a saved pose (which
            // stores the reduced arm) got reduced AGAIN on load and every
            // loaded pose bent less than the sculpt.
            clav.localRotation = clavRest
                * Quaternion.Slerp(Quaternion.identity, delta, ClavicleFollow);
            lastWritten = full;
        }
        float _airTime;    // seconds of continuous no-ground (slope-flicker filter)
        bool _airChecked, _hasAirParams, _hasCrouch; // which params the controller actually has
        SkinnedMeshRenderer _smr;

        /// The rig's OWN answer to "which renderer is the body" — his
        /// BodyRenderer slot first, the guarded search otherwise. BodyCanvas
        /// reads this instead of running its own GetComponentInChildren, which
        /// is the exact depth-first search that once crowned his grimoire "the
        /// body" and cost a day.
        public SkinnedMeshRenderer BodySmr => _smr;
        Transform _hips, _spine1, _head;
        Transform _armL, _armR, _foreL, _foreR, _handL, _handR;
        readonly List<Rigidbody> _ragdoll = new List<Rigidbody>();
        readonly List<(Transform t, Vector3 pos)> _boneHome
            = new List<(Transform, Vector3)>(); // full-skeleton structural positions
        readonly List<(Transform t, Vector3 pos, Quaternion rot)> _bind =
            new List<(Transform, Vector3, Quaternion)>();
        Quaternion _bindSpine1, _bindHead;
        bool _ragdolling;
        bool _customBody;
        float _bob;
        float _pitchShown; // eased head-follows-aim pitch (relaxes in 3rd person)
        Vector3 _lastPos;

        void Start()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _slots = GetComponent<WeaponSlots>();
            // MARKO'S PLAYER, HIS WAY: a prefab at Resources/Custom/PlayerBody
            // replaces the wired model — his mesh, his materials (the code
            // skin tint stands down). Same Mixamo skeleton = everything works.
            var custom = PrefabVault.Get("PlayerBody");
            _customBody = custom != null;
            var prefab = _customBody ? custom : CharacterLibrary.Model;

            // A player he prefabbed already carries its Body, so it needs NO
            // source prefab at all. Only a bodiless player needs one to build from.
            bool hasBody = transform.Find("Body") != null;
            if (_pilot == null || (prefab == null && !hasBody)) return; // bean life continues
            if (hasBody) _customBody = true;   // his baked body keeps HIS materials

            RemovePlaceholder();
            BuildBody(prefab);

            // Marko's play-mode edits to props/sockets/eyes/IK anchors
            // re-apply here on every build (see CharacterFix — the runtime
            // player CAN now be prefabbed — BuildBody adopts an existing Body —
            // but CharacterFix still re-applies his play-mode prop/socket edits)
            if (GetComponent<CharacterFix>() == null) gameObject.AddComponent<CharacterFix>();
        }

        /// Only the KNOWN graybox parts — anything Marko added by hand survives.
        void RemovePlaceholder()
        {
            // "GooglyEyes" here is the BEAN-ERA pair: SimpleFPSController attaches
            // one to the player ROOT at 1.55m before any body exists, and this rig
            // attaches the real pair to the HEAD BONE afterwards. Nobody removed
            // the first, so every player carried two sets and the orphan floated
            // at chest height. transform.Find only looks at DIRECT children, and
            // the real pair lives deep under the head, so this can only ever hit
            // the placeholder.
            foreach (var childName in new[] { "Bean", "ShoulderPivot.L", "ShoulderPivot.R", "NeckPivot", "GooglyEyes" })
            {
                var t = transform.Find(childName);
                if (t != null) Destroy(t.gameObject);
            }
        }

        void BuildBody(GameObject prefab)
        {
            var cc = GetComponent<CharacterController>();
            float bottom = cc != null ? cc.center.y - cc.height * 0.5f : -0.9f;

            // ---- HIS PREFABBED PLAYER ---------------------------------------
            // Drag the player out of the Hierarchy and it comes with a "Body"
            // child already on it. Instantiating a second one on top is what
            // broke every prefab he ever made: two bodies, one of them unwired,
            // animation on the wrong skeleton.
            //
            // If a Body is already here, ADOPT it. Everything below wires it
            // identically, and his placement stands rather than being reset.
            var already = transform.Find("Body");
            bool adopted = already != null;
            var model = adopted ? already.gameObject : Instantiate(prefab, transform);
            _model = model;
            model.name = "Body";
            if (!adopted)
            {
                model.transform.localPosition = new Vector3(0f, bottom, 0f); // origin at feet
                model.transform.localRotation = Quaternion.identity; // FaceForward aligns by anatomy below
            }
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 2; // the pen ignores our own body (body paint flips it)

            // THE BODY'S RENDERER, NOT THE FIRST SKINNED MESH IN THE TREE.
            //
            // AXIOM (Marko Aug 8, "we're drawing on capsules... the mesh collider
            // that's supposed to be on the body doesn't exist"): this was
            // GetComponentInChildren, a DEPTH-FIRST search. His Blender grimoire
            // is itself a skinned mesh (bones Page_Left/Page_Right) and it hangs
            // in Socket.HandL, deep inside the arm — which the search reaches
            // BEFORE SZ_Body. So _smr became the BOOK: rootBone 'Page_Right',
            // a flat 0.34 x 0.25 x 0.04 mesh at scale 1 instead of the body's
            // 100. Body paint then baked its canvas from the grimoire, got a
            // book-shaped blob, rejected it every time, and fell through to the
            // capsules — which is ink under the skin.
            //
            // Runtime never hit this because it built the body BEFORE the props
            // existed. His baked prefab carries them from frame one. Skip
            // anything worn or held; the body is what is left.
            // ON HIS BODY, HIS SLOT IS THE ONLY ANSWER (Marko Aug 8: "you keep
            // making code that works without my input and that's not allowed").
            // The auto-search IS what broke this: it picked his grimoire, which
            // is a skinned mesh living inside the arm, and body paint spent the
            // day building its canvas out of a book. A guess that silently
            // succeeds is worse than no guess at all.
            //
            // So: a body HE authored must be pointed at. A body CODE built keeps
            // the search, because it has no props yet and nobody exists to fill
            // the slot. Empty slot on his body = a loud stop, never a guess.
            _smr = BodyRenderer;
            if (_smr == null && !adopted)
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin != null && !IsHisProp(skin.transform)) { _smr = skin; break; }
                }
            if (_smr == null)
                Debug.LogError("[SpellyZombie] CharacterRig: Body Renderer is empty. Drag your body's "
                    + "Skinned Mesh Renderer (SZ_Body) into Character Rig > Body Renderer on the Player "
                    + "prefab. Nothing is guessed here on purpose — a prop can be a skinned mesh too, and "
                    + "guessing picked the grimoire.", gameObject);
            if (_smr != null)
            {
                if (!_customBody) // his PlayerBody prefab keeps HIS materials
                    _smr.sharedMaterial = MatterFX.Get(SkinColor, MoteShade.Opaque);
                _smr.updateWhenOffscreen = true; // bones move it; bounds lie
            }

            var allBones = model.GetComponentsInChildren<Transform>(true);
            Transform Bone(string boneName) => SocketSet.FindBone(allBones, boneName);

            _hips = Bone("Hips");
            _spine1 = Bone("Spine1");
            var spine2 = Bone("Spine2");
            _head = Bone("Head");
            var neck = Bone("Neck");
            _armL = Bone("LeftArm");
            _armR = Bone("RightArm");
            _foreL = Bone("LeftForeArm");
            _foreR = Bone("RightForeArm");
            _handL = Bone("LeftHand");
            _handR = Bone("RightHand");
            var upLegL = Bone("LeftUpLeg");
            var upLegR = Bone("RightUpLeg");
            var legL = Bone("LeftLeg");
            var legR = Bone("RightLeg");
            var footL = Bone("LeftFoot");
            var footR = Bone("RightFoot");
            var headTop = Bone("HeadTop");
            if (_hips == null || _head == null || _armL == null)
            {
                Debug.LogWarning("[SpellyZombie] CharacterRig: mixamorig bones not found, keeping whatever body exists.");
                return;
            }

            // (facing is decided AFTER the animator wiring below: a HUMANOID
            // animator re-poses the skeleton to face the model root's +Z, so
            // with animations the root must stay at identity — the toe-based
            // alignment only applies to the animator-less bind pose)

            // ---- fit the gameplay body: bones are truthful at Start (skinned
            // bounds aren't). SLIM capsule — the body itself is the canvas.
            // Camera sits just IN FRONT of the face: the head stays whole (no
            // headless shadows) yet never blocks the first-person view.
            float feetY = transform.TransformPoint(new Vector3(0f, bottom, 0f)).y;
            float crownY = (headTop != null ? headTop : _head).position.y;
            float height = Mathf.Clamp(crownY - feetY, 0.8f, 3f);
            float eyeLocalY = transform.InverseTransformPoint(_head.position).y + 0.05f;
            _pilot.FitBody(height, Mathf.Clamp(height * 0.14f, 0.16f, 0.3f), eyeLocalY,
                height * EyeForwardFactor);

            // ---- out of the T-pose: arms hang mostly down. THIS is rest. ----
            LowerArm(_armL, _handL, 0.75f);
            LowerArm(_armR, _handR, 0.75f);

            // the clavicles — the REAL shoulder bones (Marko: "we do not even
            // grab the shoulder") — follow the arms while posing; remember
            // their rest alongside the arms' rest
            _clavL = Bone("LeftShoulder");
            _clavR = Bone("RightShoulder");
            if (_clavL != null) _clavLRest = _clavL.localRotation;
            if (_clavR != null) _clavRRest = _clavR.localRotation;
            _armLRest = _armL.localRotation;
            _armRRest = _armR.localRotation;
            _armLWritten = _armLRest;
            _armRWritten = _armRRest;

            // ---- the emote rig moves onto real bones (same ids = old poses work) ----
            var rig = GetComponent<EmoteRig>();
            if (rig == null) rig = gameObject.AddComponent<EmoteRig>();
            rig.Joints.Clear();
            void Joint(string id, Transform bone, Transform hint)
            {
                if (bone == null) return;
                rig.Joints.Add(new EmoteRig.JointEntry
                {
                    Id = id, T = bone, GrabHint = hint != null ? hint : bone,
                    Rest = bone.localRotation
                });
            }
            Joint("shoulder.L", _armL, _handL);
            Joint("shoulder.R", _armR, _handR);
            Joint("neck", neck != null ? neck : _head, headTop != null ? headTop : _head);
            Joint("spine", spine2 != null ? spine2 : _spine1, spine2);
            Joint("leg.L", upLegL, footL);
            Joint("leg.R", upLegR, footR);

            // elbows and knees: HINGE-LIMITED (Marko's rule — constrained
            // joints make body-seal placement a puzzle: an inner-elbow seal
            // fires on a curl, an outer-knee one only mid-squat). The hinge
            // axis is the bind pose's side axis expressed in each joint's own
            // rest frame; flip the sign consts if a test bend goes backwards.
            void Hinge(string id, Transform bone, Transform hint, float sign, float maxFlex)
            {
                if (bone == null) return;
                Vector3 sideWorld = Vector3.Cross(Vector3.up, transform.forward);
                rig.Joints.Add(new EmoteRig.JointEntry
                {
                    Id = id, T = bone, GrabHint = hint != null ? hint : bone,
                    Rest = bone.localRotation, Limited = true,
                    HingeAxis = (Quaternion.Inverse(bone.rotation) * (sideWorld * sign)).normalized,
                    MinDeg = -5f, MaxDeg = maxFlex,
                });
            }
            Hinge("elbow.L", _foreL, _handL, ElbowHingeSign, 140f);
            Hinge("elbow.R", _foreR, _handR, ElbowHingeSign, 140f);
            Hinge("knee.L", legL, footL, KneeHingeSign, 135f);
            Hinge("knee.R", legR, footR, KneeHingeSign, 135f);
            if (GetComponent<EmotePlayer>() == null) gameObject.AddComponent<EmotePlayer>();

            // the bake runs on the raw BIND pose — the animator only turns the
            // body to face root-forward at its FIRST frame, later. Derive the
            // bind pose's true facing from the toes, or "right" is the body's
            // LEFT and every baked pose comes out mirrored/backwards.
            Vector3 bindFwd = transform.forward;
            var toeL = Bone("LeftToeBase");
            if (footL != null && toeL != null)
            {
                Vector3 toeDir = toeL.position - footL.position;
                toeDir.y = 0f;
                if (toeDir.sqrMagnitude > 1e-6f) bindFwd = toeDir.normalized;
            }
            Vector3 bindRight = Vector3.Cross(Vector3.up, bindFwd);
            BakeDefaultEmotes(rig, spine2 != null ? spine2 : _spine1,
                neck != null ? neck : _head, bindFwd, bindRight);

            // ---- limb colliders = ragdoll + drawable body parts ----
            float r = height * 0.075f;
            var rbHips = Limb(_hips, _spine1, r * 1.6f, 12f, null);
            var rbChest = Limb(_spine1, neck != null ? neck : _head, r * 1.5f, 12f, rbHips);
            Limb(_head, headTop, r * 1.5f, 6f, rbChest);
            var rbArmL = Limb(_armL, _foreL, r, 2f, rbChest);
            Limb(_foreL, _handL, r * 0.85f, 1.5f, rbArmL);
            var rbArmR = Limb(_armR, _foreR, r, 2f, rbChest);
            Limb(_foreR, _handR, r * 0.85f, 1.5f, rbArmR);
            var rbLegL = Limb(upLegL, legL, r * 1.1f, 5f, rbHips);
            Limb(legL, footL, r * 0.9f, 4f, rbLegL);
            var rbLegR = Limb(upLegR, legR, r * 1.1f, 5f, rbHips);
            Limb(legR, footR, r * 0.9f, 4f, rbLegR);

            // the limbs live INSIDE the controller capsule — they must never
            // collide with it or the ragdoll detonates on activation
            if (cc != null)
                foreach (var rb in _ragdoll)
                    foreach (var col in rb.GetComponents<Collider>())
                        Physics.IgnoreCollision(cc, col, true);

            // bind = the RELAXED stance (ragdoll recovery + emote rest return here)
            foreach (var rb in _ragdoll)
                _bind.Add((rb.transform, rb.transform.localPosition, rb.transform.localRotation));
            if (_spine1 != null) _bindSpine1 = _spine1.localRotation;
            _bindHead = _head.localRotation;

            // EVERY bone's local position is structural (only the hips move) —
            // cache the WHOLE skeleton for the runaway-limb guard.
            //
            // AXIOM (Marko Aug 7): this used to sweep EVERYTHING under the hips
            // on the assumption that "at Start the hierarchy is pure bone —
            // props and sockets attach later". A BAKED PLAYER PREFAB BREAKS
            // THAT: his Socket.HandL/Grimoire, the wand, the hat and the eyes
            // are all present at Start, so the guard adopted them as bones and
            // then rewrote their localPosition EVERY FRAME. He moved the book
            // into the hand, the guard snapped it back, and the book read as
            // floating loose in the world ("your braindead code that moves
            // things instead of keeping them where I left them").
            //
            // The guard exists for BONES ONLY — a ragdoll shove blowing a limb
            // out of its socket. His props are HIS. Skip anything at or under a
            // Socket.*, and the named props, exactly like CharacterFix.IsFixable.
            _boneHome.Clear();
            foreach (var t in _hips.GetComponentsInChildren<Transform>(true))
                if (t != _hips && !IsHisProp(t)) _boneHome.Add((t, t.localPosition));

            // ---- the face and the wardrobe: a BAKED body brings its own
            // eyes (Marko's edit is law); otherwise build them at his
            // hand-tuned fit, in HEAD-LOCAL space
            var eyes = model.GetComponentInChildren<GooglyEyes>(true);
            if (eyes != null && (!eyes.IsAlive || !eyes.enabled || !eyes.gameObject.activeSelf))
            {
                // the bake carried a DEAD eye rig (wiring lost, no usable
                // Eye→Pupil pair, or baked disabled) — frozen decoration that
                // can never react. Marko: "make it work normally" — rebuild.
                Debug.LogWarning("[SpellyZombie] Baked body's eyes are not a working " +
                    "Eye→Pupil pair, rebuilding fresh googly eyes in their place. " +
                    "(Re-bake to keep custom eye placement.)", model);
                Destroy(eyes.gameObject);
                eyes = null;
            }
            if (eyes == null)
            {
                eyes = GooglyEyes.Attach(_head, 0f, EyeScale);
                eyes.transform.localPosition = EyeLocalPos; // ← the knobs up top
                eyes.transform.localRotation = Quaternion.identity;
                eyes.transform.localScale = Vector3.one * EyeRigScale;
            }
            _pilot.ReplaceEyes(eyes);

            // (sockets — hat/cape included — and pen props are built in the
            // first LateUpdate by SocketSet: the bind pose faces backwards
            // until the animator's first frame, and building before that puts
            // everything in the wrong hands)

            // ---- locomotion animations (the wizard builds the controller) ----
            _anim = model.GetComponent<Animator>();
            var ctrl = CharacterLibrary.Anim;
            if (_anim != null && ctrl != null)
            {
                _anim.runtimeAnimatorController = ctrl;
                _anim.applyRootMotion = false; // the CharacterController moves us
                _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var ik = model.AddComponent<HandIK>();
                ik.Slots = _slots;
                ik.Pivot = _pilot.CameraPivot; // pen/grimoire hands hang off the view
            }
            else
            {
                _anim = null; // no clips wired yet — procedural Reach fills in
            }

            // facing: WITH a humanoid animator the retarget faces the model
            // root's +Z — identity is correct by construction. WITHOUT one,
            // the bind pose is what you see, so align it by the toes.
            if (_anim == null)
                FaceForward(model.transform, footL, Bone("LeftToeBase"), transform.forward);

            _lastPos = transform.position;
        }

        /// HIS, not ours: a socket, anything worn or held inside one, or a
        /// named prop. The runaway-limb guard must never write to these — he
        /// places them by hand on the prefab and in play mode, and a snapshot
        /// taken at Start would overrule him for the rest of the session.
        /// Same test CharacterFix.IsFixable uses, so "what Marko may move" and
        /// "what code may not move" are ONE definition, not two that drift.
        static bool IsHisProp(Transform t)
        {
            for (var walk = t; walk != null; walk = walk.parent)
            {
                string n = walk.name;
                if (n.StartsWith("Socket.") || n.StartsWith("IKAnchor_")
                    || n == "Wand" || n == "Grimoire" || n == "GooglyEyes") return true;
                if (n.StartsWith("mixamorig:")) return false; // reached bone again: pure skeleton
            }
            return false;
        }

        // ---------------------------------------------------- pen & grimoire --
        GameObject _wand, _book;
        readonly List<Renderer> _bookRenderers = new List<Renderer>(); // reused buffer (no-alloc law)
        bool _propsBuilt;

        // ------------------------------------------------------- wardrobe --
        GameObject _model;
        SocketSet _sockets;
        System.Collections.Generic.List<GameObject> _costume;
        byte _teamShown = 255;
        bool _capeStamped;

        /// Slot 1's hands are never empty: a wand in the right, the grimoire
        /// in the left. Uses Marko's Weapon_Wand / Weapon_Grimoire skins when
        /// they exist (grip-pivot convention: drop in at identity); primitives
        /// stand in until then. Everything lives in the standard hand sockets.
        void BuildPenProps()
        {
            var gripR = _sockets != null ? _sockets.Get("HandR") : null;
            var palmL = _sockets != null ? _sockets.Get("HandL") : null;
            if (gripR == null || palmL == null) return;

            // a BAKED body may already hold its wand and/or grimoire (Marko
            // edited them on the prefab) — adopt EACH independently; anything
            // his prefab lacks gets the normal build below
            var bakedWand = gripR.Find("Wand");
            var bakedBook = palmL.Find("Grimoire");
            if (bakedWand != null)
            {
                _wand = bakedWand.gameObject;
                if (_wand.GetComponent<WandInk>() == null) _wand.AddComponent<WandInk>();
            }
            if (bakedBook != null)
            {
                _book = bakedBook.gameObject;
                if (_book.GetComponent<GrimoirePages>() == null) _book.AddComponent<GrimoirePages>();
            }
            if (_wand != null && _book != null) return;

            // Marko's prefab first (Resources/Custom/Wand), then the weapon
            // skin library, then the primitive placeholder
            var wandSkin = _wand != null ? null : PrefabVault.Get("Wand");
            if (wandSkin == null && _wand == null) wandSkin = Wardrobe.WeaponSkin("Wand");
            if (_wand != null)
            {
                // adopted from the baked body above — nothing to build
            }
            else if (wandSkin != null)
            {
                _wand = Instantiate(wandSkin, gripR, false);
                _wand.name = "Wand";
            }
            else
            {
                // unscaled root: WandInk's vial hangs here without being
                // crushed by the shaft's non-uniform scale
                _wand = new GameObject("Wand");
                _wand.transform.SetParent(gripR, false);
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shaft.name = "Shaft";
                Destroy(shaft.GetComponent<Collider>());
                shaft.transform.SetParent(_wand.transform, false);
                shaft.transform.localPosition = new Vector3(0f, 0.02f, 0.07f);
                shaft.transform.localRotation = Quaternion.Euler(70f, 0f, 0f); // tip forward-up
                shaft.transform.localScale = new Vector3(0.022f, 0.15f, 0.022f);
                // THE WAND IS INK, SO IT IS BLACK (Marko, twice: "the wand needs
                // to be the same color as the ink => both black, not brown and
                // blue", and Aug 7 "black and green, not brown and green"). This
                // line was still the old brown, so the placeholder wand argued
                // with the ink column bolted to it.
                shaft.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(DrawingConfig.InkColor, MoteShade.Opaque);
            }

            var bookSkin = _book != null ? null : PrefabVault.Get("Grimoire");
            if (bookSkin == null && _book == null) bookSkin = Wardrobe.WeaponSkin("Grimoire");
            if (_book != null)
            {
                // adopted from the baked body above — nothing to build
            }
            else if (bookSkin != null)
            {
                _book = Instantiate(bookSkin, palmL, false);
                _book.name = "Grimoire";
            }
            else
            {
                _book = new GameObject("Grimoire");
                _book.transform.SetParent(palmL, false);
                // rides ON TOP of the palm — the hand must never cover the pages
                _book.transform.localPosition = new Vector3(0f, 0.055f, 0.03f);
                _book.transform.localRotation = Quaternion.Euler(-20f, 8f, 0f);
                _book.transform.localScale = Vector3.one * 0.8f; // placeholder hogged the view
                var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cover.name = "Cover";
                Destroy(cover.GetComponent<Collider>());
                cover.transform.SetParent(_book.transform, false);
                cover.transform.localScale = new Vector3(0.17f, 0.035f, 0.23f);
                cover.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.3f, 0.14f, 0.3f), MoteShade.Opaque); // witchy purple leather
                var pages = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pages.name = "Pages";
                Destroy(pages.GetComponent<Collider>());
                pages.transform.SetParent(_book.transform, false);
                pages.transform.localPosition = new Vector3(0f, 0.012f, 0f);
                pages.transform.localScale = new Vector3(0.155f, 0.018f, 0.215f);
                pages.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.92f, 0.88f, 0.78f), MoteShade.Opaque);
            }

            foreach (var t in _wand.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 2;
            foreach (var t in _book.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 2;

            // seal lesson + rune spreads (G opens, , . turn) / the wand IS the
            // mana bar — guarded: adopted pieces may already carry them
            if (_book.GetComponent<GrimoirePages>() == null) _book.AddComponent<GrimoirePages>();
            if (_wand.GetComponent<WandInk>() == null) _wand.AddComponent<WandInk>();
        }

        // ------------------------------------------------------- body paint --
        Transform _paintShell;
        Vector3 _camHeadLocal;  // the eye point in head-bone space (calibrated)
        bool _camCalibrated;

        /// Body paint's canvas pose: the animator goes quiet RIGHT NOW (the
        /// doll gate keeps it down, but it would still rewrite this frame),
        /// then the emote joints ease to REST — shoulders out, legs apart.
        /// Rest was captured AFTER the animator's first frame, so every value
        /// lives in the animator's own basis: no mixed-space snapping, no
        /// smeared skin. The animator resumes by itself when the mode ends.
        public void RelaxForPaint()
        {
            if (_anim != null) _anim.enabled = false;
            var rig = GetComponent<EmoteRig>();
            if (rig != null)
                foreach (var j in rig.Joints)
                    if (j.T != null) j.T.localRotation = j.Rest;
        }

        /// THE CANVAS IS THE SKIN (Marko: "why can't we just use the mesh
        /// collider?" — we do): the skinned mesh, frozen in the relaxed pose,
        /// baked into an invisible MeshCollider shell glued over the visible
        /// body. Import transforms LIE (SZ_Body ships rotated -90° at scale
        /// 100), so nothing is trusted: both bake variants are measured
        /// against the renderer's real on-screen bounds and only a shell that
        /// matches reality is accepted. Returns false when no honest shell
        /// can be made (mesh not readable → the wizard fixes that) — the
        /// limb capsules then catch the pen instead.
        /// Free a mesh WE created, never one that came off disk. The shell can
        /// legitimately be _smr.sharedMesh, and destroying that is an attempt on
        /// his SZ_Body asset (Unity refuses with "Destroying assets is not
        /// permitted to avoid data loss" — the refusal is the safety net, not
        /// the design).
        void DestroyBake(Mesh m)
        {
            if (m == null) return;
            if (_smr != null && m == _smr.sharedMesh) return;
            Destroy(m);
        }

        public bool BeginBodyPaint()
        {
            if (_paintShell != null) return true;
            if (_smr == null)
            {
                Debug.LogError("[SpellyZombie] Body paint: no SkinnedMeshRenderer on the rig. Capsules catch the pen.");
                return false;
            }
            try
            {
                // BakeMesh semantics differ per import (Marko's rig bakes
                // useScale:false at WORLD size already) — so no more theory:
                // build EVERY plausible mount and let the physics engine
                // report where each collider actually sits (collider.bounds,
                // a measurement) — keep the one covering the visible mesh.
                var meshLocal = new Mesh();
                _smr.BakeMesh(meshLocal, false);
                var meshScaled = new Mesh();
                _smr.BakeMesh(meshScaled, true);
                if (meshLocal.vertexCount < 3)
                {
                    Destroy(meshLocal);
                    Destroy(meshScaled);
                    Debug.LogError("[SpellyZombie] Body paint: bake produced no vertices. Capsules catch the pen.");
                    return false;
                }

                var candidates = new List<(GameObject go, MeshCollider col, string tag)>();
                void Mount(Mesh m, bool asChild, string tag)
                {
                    var go = new GameObject("PaintShellCandidate");
                    if (asChild)
                    {
                        go.transform.SetParent(_smr.transform, false); // inherits import transforms
                    }
                    else
                    {
                        go.transform.SetPositionAndRotation(_smr.transform.position, _smr.transform.rotation);
                        go.transform.SetParent(transform, true); // world mount, scale 1
                    }
                    var c = go.AddComponent<MeshCollider>();
                    c.sharedMesh = m;
                    candidates.Add((go, c, tag));
                }
                Mount(meshLocal, true, "local-bake/child");
                Mount(meshLocal, false, "local-bake/world");
                Mount(meshScaled, true, "scale-bake/child");
                Mount(meshScaled, false, "scale-bake/world");

                // THE MESH ITSELF, NOT A BAKE OF IT (Marko Aug 8: "turn the
                // capsules off and turn the mesh on").
                //
                // AXIOM: BakeMesh is unusable on his rig. Measured from his own
                // console: smr.transform.lossyScale is 1 while the mesh's
                // bindposes were authored at scale 100, so BOTH bakes return the
                // SAME 0.18 x 0.26 x 0.15 blob against a 0.73 x 1.46 x 0.54 body
                // — and because useScale true/false stop differing at scale 1,
                // all four mounts above collapse into one measurement. No mount
                // maths can rescue a bake that is the wrong SHAPE.
                //
                // sharedMesh is the authored asset in the renderer's own space,
                // which is exactly where Unity draws the body at bind pose — and
                // RelaxForPaint has just put the bones at rest, which is that
                // pose. Mounted at identity under the renderer it needs no
                // guessing at all. It still goes through the same measurement
                // below, so if it is wrong it loses like any other candidate.
                if (_smr.sharedMesh != null)
                {
                    var go = new GameObject("PaintShellCandidate");
                    go.transform.SetParent(_smr.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    var c = go.AddComponent<MeshCollider>();
                    c.sharedMesh = _smr.sharedMesh;
                    candidates.Add((go, c, "sharedMesh/renderer"));
                }
                Physics.SyncTransforms(); // make collider.bounds tell the truth NOW

                // TRUTH = the limb capsules' union: they sit ON the bones, so
                // they follow the pose RelaxForPaint just set. The renderer's
                // bounds are a frame STALE here (the arms moved this frame) —
                // scoring fresh bakes against yesterday's pose called every
                // honest shell "2x too wide" and broke body drawing.
                Bounds truth = _smr.bounds; // fallback only (rig without capsules)
                bool firstCol = true;
                foreach (var rb in _ragdoll)
                {
                    var limbCol = rb != null ? rb.GetComponent<Collider>() : null;
                    if (limbCol == null) continue;
                    if (firstCol) { truth = limbCol.bounds; firstCol = false; }
                    else truth.Encapsulate(limbCol.bounds);
                }
                float height = Mathf.Max(truth.size.y, 0.5f);
                (GameObject go, MeshCollider col, string tag) best = default;
                float bestScore = float.MaxValue;
                var report = new List<string>(); // spoken only if the pick smells wrong
                // RAW NUMBERS, not just ratios. A ratio cannot tell you WHICH of
                // the two boxes is wrong, and four mounts that differ by 100x
                // once reported one identical ratio — which is only possible if
                // the mounts collapsed together. Print what was actually measured.
                report.Add($"  TRUTH (capsule union): size {truth.size} center {truth.center}");
                // WHO IS _smr, EXACTLY. The bake comes out a different SHAPE, not
                // just a different size, so the question is no longer "which
                // mount" but "which object, in what space". Print the chain.
                string chain = "";
                for (var w = _smr.transform; w != null; w = w.parent)
                    chain += $"{w.name}(ls {w.localScale.x:F3}) < ";
                report.Add($"  CHAIN: {chain}");
                report.Add($"  smr: name '{_smr.name}' rootBone '{(_smr.rootBone != null ? _smr.rootBone.name : "NULL")}'"
                    + $" | renderer WORLD bounds {_smr.bounds.size} center {_smr.bounds.center}");
                report.Add($"  sharedMesh bounds {_smr.sharedMesh.bounds.size} verts {_smr.sharedMesh.vertexCount}"
                    + $" | bakeLocal {meshLocal.bounds.size} | bakeScaled {meshScaled.bounds.size}");
                foreach (var cand in candidates)
                {
                    var b = cand.col.bounds;
                    float sizeMiss = SizeMismatch(b.size, truth.size);
                    float centerMiss = Vector3.Distance(b.center, truth.center) / height;
                    float score = (sizeMiss - 1f) + centerMiss;
                    report.Add($"  {cand.tag}: bounds {b.size} center {b.center} "
                        + $"| lossyScale {cand.go.transform.lossyScale.x:F3} "
                        + $"| size x{sizeMiss:F2}, center off {centerMiss:F2} body-heights");
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = cand;
                    }
                }
                foreach (var cand in candidates)
                    if (cand.go != best.go)
                    {
                        cand.go.SetActive(false); // collider off this frame
                        Destroy(cand.go);
                    }
                // destroy the bakes we made and NEVER the shared asset — the
                // sharedMesh candidate hands back _smr.sharedMesh itself, and
                // Destroy on that is "Destroying assets is not permitted to
                // avoid data loss" (Unity refuses, but we must not ask)
                if (best.col.sharedMesh != meshLocal) DestroyBake(meshLocal);
                if (best.col.sharedMesh != meshScaled) DestroyBake(meshScaled);

                float finalSize = SizeMismatch(best.col.bounds.size, truth.size);
                float finalCenter = Vector3.Distance(best.col.bounds.center, truth.center);
                bool good = finalSize < 1.3f && finalCenter < height * 0.35f;

                // a shell that doesn't cover the visible body is WORSE than no
                // shell — the pen inks thin air around you. Reject it and let
                // the limb capsules catch the pen (drawing always works).
                if (finalSize >= 1.6f || finalCenter >= height * 0.5f)
                {
                    DestroyBake(best.col.sharedMesh);
                    Destroy(best.go);
                    Debug.LogWarning($"[SpellyZombie] Paint shell REJECTED ({best.tag}). Capsules catch the pen:\n"
                        + string.Join("\n", report));
                    return false;
                }

                // the shell is INVISIBLE in game (the debug ghost served its
                // purpose during bring-up) — the Console keeps the diagnosis
                best.go.name = "PaintShell";
                _paintShell = best.go.transform;
                if (!good) // the healthy path stays silent — no console spam
                    Debug.LogWarning($"[SpellyZombie] Paint shell SUSPICIOUS ({best.tag}):\n"
                        + string.Join("\n", report));
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SpellyZombie] Paint shell bake FAILED ({e.Message}). Run 'Spelly Zombie → Build Character Rig' once (it enables Read/Write on SZ_Body). Limb capsules catch the pen for now.");
                return false;
            }
        }

        /// Worst size ratio between two AABBs (1 = identical). The two boxes
        /// come from DIFFERENTLY ORIENTED spaces (the renderer node ships
        /// rotated -90°, so its local "height" is the world's "depth") —
        /// comparing axis-to-axis faked a 4x mismatch and got every good
        /// shell rejected. Sorting the extents first makes the check
        /// rotation-proof while real scale errors (x100) still scream.
        static float SizeMismatch(Vector3 a, Vector3 b)
        {
            float[] s1 = { Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z) };
            float[] s2 = { Mathf.Abs(b.x), Mathf.Abs(b.y), Mathf.Abs(b.z) };
            System.Array.Sort(s1);
            System.Array.Sort(s2);
            float worst = 1f;
            for (int i = 0; i < 3; i++)
            {
                if (s1[i] < 1e-4f || s2[i] < 1e-4f) continue; // flat axis says nothing
                worst = Mathf.Max(worst, Mathf.Max(s1[i], s2[i]) / Mathf.Min(s1[i], s2[i]));
            }
            return worst;
        }

        /// The limb (ragdoll bone) whose collider surface is closest to a
        /// world point — body ink parents HERE at creation now (Marko: "the
        /// drawing is floating in air and is not linked with the arms"), so
        /// it rides the pose from its first frame instead of hanging on the
        /// static shell until an end-of-session rebase.
        public Transform NearestLimbSurface(Vector3 at)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            foreach (var rb in _ragdoll)
            {
                if (rb == null) continue;
                var col = rb.GetComponent<Collider>();
                Vector3 p = col != null ? col.ClosestPoint(at) : rb.transform.position;
                float d = (p - at).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = rb.transform; }
            }
            return best;
        }

        /// Session over: every ink node on the shell is handed to its nearest
        /// bone, so the drawing rides the animation forever; the shell dies.
        public void EndBodyPaint()
        {
            if (_paintShell == null) return;
            foreach (var node in _paintShell.GetComponentsInChildren<DrawNode>(true))
            {
                // nearest LIMB SURFACE, not nearest bone origin (see
                // NearestLimbSurface — chest ink must not orbit a shoulder)
                Transform best = NearestLimbSurface(node.transform.position);
                if (best != null) node.Rebase(best);
            }
            var shellCol = _paintShell.GetComponent<MeshCollider>();
            if (shellCol != null) DestroyBake(shellCol.sharedMesh); // no leaked bakes
            Destroy(_paintShell.gameObject);
            _paintShell = null;
        }

        /// The built-in emotes, re-baked on THIS skeleton: pose the bones with
        /// direction math (axis-proof), snapshot every joint, restore rest.
        /// The hardcoded bean eulers pointed the wrong way on Mixamo bones.
        void BakeDefaultEmotes(EmoteRig rig, Transform spine, Transform neckBone,
            Vector3 bindFwd, Vector3 bindRight)
        {
            void AimLimb(Transform upper, Transform tip, Vector3 dir)
            {
                if (upper == null || tip == null) return;
                Vector3 cur = tip.position - upper.position;
                if (cur.sqrMagnitude < 1e-6f) return;
                upper.rotation = Quaternion.FromToRotation(cur, dir) * upper.rotation;
            }
            void Bake(int slot, string poseName, System.Action pose)
            {
                pose();
                EmoteLibrary.SetDefault(slot, poseName, rig.CapturePose());
                foreach (var j in rig.Joints) // back to rest for the next bake
                    if (j.T != null) j.T.localRotation = j.Rest;
            }

            Vector3 fwd = bindFwd, up = Vector3.up, right = bindRight;
            Bake(1, "Arm raise", () => AimLimb(_armR, _handR, up));
            Bake(2, "Victory", () =>
            {
                AimLimb(_armL, _handL, (up * 0.8f - right * 0.5f).normalized);
                AimLimb(_armR, _handR, (up * 0.8f + right * 0.5f).normalized);
            });
            Bake(3, "T-pose", () =>
            {
                AimLimb(_armL, _handL, -right);
                AimLimb(_armR, _handR, right);
            });
            Bake(4, "Bow", () =>
            {
                if (spine != null) spine.rotation = Quaternion.AngleAxis(40f, right) * spine.rotation;
                if (neckBone != null) neckBone.rotation = Quaternion.AngleAxis(20f, right) * neckBone.rotation;
                AimLimb(_armL, _handL, (Vector3.down - fwd * 0.3f).normalized);
                AimLimb(_armR, _handR, (Vector3.down - fwd * 0.3f).normalized);
            });
        }

        static Color TeamColor(byte team) =>
            MatchLobby.TeamColors[Mathf.Min(team, (byte)(MatchLobby.TeamColors.Length - 1))];

        /// Yaw the model so its toes point along `forward`.
        public static void FaceForward(Transform model, Transform foot, Transform toe, Vector3 forward)
        {
            if (model == null || foot == null || toe == null) return;
            Vector3 toeDir = toe.position - foot.position;
            toeDir.y = 0f;
            forward.y = 0f;
            if (toeDir.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f) return;
            float angle = Vector3.SignedAngle(toeDir.normalized, forward.normalized, Vector3.up);
            model.Rotate(0f, angle, 0f, Space.World);
        }

        /// Swing the arm from the T-pose toward hanging down; sign-proof (works
        /// whatever the bone axes or the player's spawn yaw are).
        static void LowerArm(Transform upper, Transform hand, float amount)
        {
            if (upper == null || hand == null) return;
            Vector3 dir = hand.position - upper.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 target = Vector3.Slerp(dir.normalized, Vector3.down, amount);
            upper.rotation = Quaternion.FromToRotation(dir, target) * upper.rotation;
        }

        /// A kinematic capsule along the bone toward its child: pen target,
        /// ragdoll segment, and zombie-facing body all in one.
        static PhysicsMaterial _limbGrip;

        Rigidbody Limb(Transform bone, Transform child, float radius, float mass, Rigidbody parent)
        {
            if (bone == null) return null;
            // AXIOM (Marko Jul 25): ADOPT what his rig already carries. A
            // Rigidbody or Collider he authored used to make AddComponent
            // return null → NullReferenceException → the body was left
            // half-built (no eyes, no sockets, no animator) for the session.
            var rb = Adopt.Component<Rigidbody>(bone.gameObject, out bool madeRb);
            if (!madeRb)
                Debug.LogWarning($"[SpellyZombie] Bone '{bone.name}' brought its own Rigidbody " +
                                 $"(mass {rb.mass}), keeping it.", bone);
            else rb.mass = mass;
            rb.isKinematic = true; // ALWAYS — SetRagdoll owns this; an adopted
                                   // non-kinematic body collapses the skeleton on frame 1
            // grippy limbs: a downed doll STOPS where it lands instead of
            // ice-skating away from its rescuer (spell forces still move it —
            // friction only fights sliding, not pushes)
            if (_limbGrip == null)
                _limbGrip = new PhysicsMaterial("SZ_LimbGrip")
                {
                    staticFriction = 0.85f,
                    dynamicFriction = 0.8f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            // big falls fired ragdoll bones straight through the floor
            // (discrete checks skip thin geometry at speed) — the body then
            // "vanished" under the map. Speculative sweeps stop that.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // SMOOTH BONES BETWEEN PHYSICS STEPS (Marko: "camera doesn't
            // follow it exactly"): without interpolation a ragdolling bone
            // only moves 50×/s while the game renders faster — the camera
            // rides the head bone, so the whole view stepped with it.
            // HandGrab already does this for held objects; the doll gets it too.
            rb.interpolation = RigidbodyInterpolation.None; // kinematic bones ride the parent — SetRagdoll turns smoothing on when physics takes over

            Vector3 local = child != null
                ? bone.InverseTransformPoint(child.position)
                : Vector3.up * radius * 2f;
            float len = Mathf.Max(local.magnitude, radius * 1.6f);
            // his collider wins — only fit one when the bone has none
            if (bone.GetComponent<Collider>() == null)
            {
                var col = bone.gameObject.AddComponent<CapsuleCollider>();
                col.radius = radius;
                col.height = len + radius;
                col.center = local * 0.5f;
                col.direction = DominantAxis(local);
                col.material = _limbGrip;
            }
            else
            {
                var kept = bone.GetComponent<Collider>();
                if (kept is MeshCollider mc && !mc.convex)
                    Debug.LogWarning($"[SpellyZombie] Bone '{bone.name}' has a NON-CONVEX MeshCollider. " +
                                     "Ragdoll bones must be convex or physics will refuse it.", bone);
            }

            if (parent != null)
            {
                var joint = Adopt.Component<CharacterJoint>(bone.gameObject, out bool madeJoint);
                if (madeJoint) joint.enablePreprocessing = false;
                if (joint.connectedBody == null) joint.connectedBody = parent;
            }
            _ragdoll.Add(rb);
            return rb;
        }

        static int DominantAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
        }


        void LateUpdate()
        {
            if (_smr == null || _pilot == null) return;

            // first LateUpdate = the animator has evaluated once, the body
            // faces forward, the hands are where hands live — NOW glue props,
            // build the costume sockets, and put on the team outfit
            if (!_propsBuilt)
            {
                _propsBuilt = true;
                if (_model != null)
                {
                    _sockets = SocketSet.Build(_model, transform); // sockets first —
                    BuildPenProps();                               // the props live in them
                    // NOW the pose is real: the animator has evaluated once and
                    // the body faces forward. A baked prefab's EmoteRig already
                    // ran its Awake at scene load and caught the bind pose, so
                    // re-read rest here or body paint relaxes into a T-pose.
                    GetComponent<EmoteRig>()?.CaptureRest();
                    _teamShown = MatchLobby.LocalTeam;
                    _capeStamped = StartingRuneChooser.HasChosen;
                    // CLOTH RETIRED (Marko) — capes are rigid pieces, no
                    // collider bundle to hand the dresser any more
                    _costume = Wardrobe.DressPlayer(_sockets, TeamColor(_teamShown),
                        _capeStamped ? StartingRuneChooser.ChosenCard : (RuneCardType?)null);
                }
            }

            // the outfit follows your choices: pillar picks retint, the
            // starting rune stamps the cape's back the moment it's chosen
            if (_costume != null)
            {
                if (MatchLobby.LocalTeam != _teamShown)
                {
                    _teamShown = MatchLobby.LocalTeam;
                    Wardrobe.Retint(_costume, TeamColor(_teamShown));
                }
                if (!_capeStamped && StartingRuneChooser.HasChosen)
                {
                    _capeStamped = true;
                    foreach (var piece in _costume)
                        if (piece != null && piece.name.StartsWith("Cape"))
                            Wardrobe.StampRune(piece.transform, StartingRuneChooser.ChosenCard);
                }
            }

            // the head stays whole in every mode (shadows included) — the
            // camera rides just in front of the face, so it never blocks view
            bool firstPerson = !SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive;

            // ---- who owns the doll? emotes, the studio, body paint and R
            // pose mode all FREEZE the pose — the animator stands down
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            bool doll = (_emotes != null && _emotes.IsPosing)
                || PoseStudio.IsOpen || SelfPaint.IsActive || PoseGrab.IsOpen;

            // slot 1 shows its tools in FIRST person only — Marko's rule: no
            // weapon of any kind visible in third person, that's the tell
            if (_wand != null && _slots != null)
            {
                bool showPen = _slots.PenSelected && !_ragdolling
                    && (!SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive);
                if (_wand.activeSelf != showPen) _wand.SetActive(showPen);

                // THE BOOK STAYS ALIVE ON THE EASEL (Marko: "I need to open
                // grimoire in character drawing mode - invisible, but I can
                // still see the UI showing what page we're at"). Deactivating
                // the object killed GrimoirePages outright, so G could never
                // open the book while painting. Active + renderers off =
                // invisible but fully working.
                bool bookAlive = showPen
                    || (SelfPaint.IsActive && _slots.PenSelected && !_ragdolling);
                if (_book != null)
                {
                    if (_book.activeSelf != bookAlive) _book.SetActive(bookAlive);
                    // reused buffer, not a fresh array per frame — page content
                    // spawns at runtime (GrimoirePages rebuilds, page-flip fx),
                    // so a one-time renderer cache would go stale
                    if (bookAlive)
                    {
                        _book.GetComponentsInChildren(true, _bookRenderers);
                        foreach (var r in _bookRenderers)
                            if (r.enabled != showPen) r.enabled = showPen;
                    }
                }
            }

            // ---- ragdoll on knockdowns ----
            bool wantRagdoll = _pilot.IsSprawled || _pilot.IsDowned || _pilot.IsAirTumbling;
            if (wantRagdoll != _ragdolling) SetRagdoll(wantRagdoll);
            if (_anim != null) _anim.enabled = !doll && !_ragdolling;
            if (_ragdolling)
            {
                // first person rides the DOLL'S face — the camera used to sit
                // at capsule height inside your own collapsed torso (Marko's
                // beige-screen downed bug); anchored to the head bone you see
                // the world from the floor, not your own insides
                if (firstPerson && _head != null && _camCalibrated)
                    _pilot.SetEyeAnchor(_head.TransformPoint(_camHeadLocal));

                // downed = keep settling: without this a shoved doll slides
                // across the courtyard and nobody can hold E on it
                if (_pilot.IsDowned)
                    foreach (var rb in _ragdoll)
                        if (rb != null && !rb.isKinematic)
                        {
                            // re-assert the heavy downed drag: if we ragdolled from
                            // an air-tumble (light 0.12 flight drag) and THEN got
                            // downed, SetRagdoll never re-ran, so the corpse kept
                            // gliding — SetRagdoll only fires on the on/off edge
                            if (rb.linearDamping < 2.2f) rb.linearDamping = 2.2f;
                            rb.linearVelocity = Vector3.MoveTowards(
                                rb.linearVelocity, Vector3.zero, 6f * Time.deltaTime);
                        }

                // THE DOLL MUST NEVER BE LOST: a bone blown across the map or
                // through the floor (joint explosion, tunneling) snaps the
                // whole doll home onto the capsule and lets physics retry.
                if (_hips != null && (_hips.position
                        - (transform.position + Vector3.up * 0.9f)).sqrMagnitude > 25f)
                {
                    foreach (var (t, pos, rot) in _bind)
                    {
                        if (t == null) continue;
                        t.localPosition = pos;
                        t.localRotation = rot;
                    }
                    foreach (var rb in _ragdoll)
                        if (rb != null && !rb.isKinematic)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                }
                return;
            }

            Vector3 vel = (transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastPos = transform.position;
            Vector3 lv = transform.InverseTransformDirection(vel);
            bool animated = _anim != null && _anim.enabled;

            // death/respawn can destroy the model out from under us — a dead
            // bone reference throws on the next line, so stop puppeteering
            if (_head == null) return;

            // ---- runaway-limb guard: a shove that wedges a limb into
            // geometry can blow a bone's LOCAL POSITION out during the
            // ragdoll — and humanoid animation only writes ROTATIONS (+ hips),
            // so the drift never heals and the skin smears into a beam.
            // Covers the ENTIRE skeleton (hands and toes included — the
            // ragdoll-bones-only version let a hand escape and noodle an arm).
            foreach (var (t, pos) in _boneHome)
            {
                if (t == null) continue;
                if ((t.localPosition - pos).sqrMagnitude > 0.0025f) // drifted 5cm+
                    t.localPosition = pos;
            }

            // locomotion: the 2D blend reads LOCAL velocity — forward, back
            // and strafes each get their own clip; crouch is its own state.
            //
            // NATURAL STRIDE (Marko's root-motion experiment): the clips are
            // in-place, so true root motion has nothing to pull from — this
            // is the standard substitute. (1) The blend values are DAMPED so
            // direction changes glide instead of snapping ("intentional low
            // fps" reads = raw per-frame jumps). (2) The cycle's PLAYBACK
            // SPEED follows the actual ground speed, so the feet cover the
            // ground they pass — no treadmill slide, movement looks OWNED by
            // the legs. Toggle off to compare with the old feel live.
            if (animated)
            {
                // the Animator's own "Apply Root Motion" checkbox is a trap on
                // this rig (in-place clips with root wiggle → the body turns
                // sideways and folds on crouch) — Marko hit it hunting for
                // Natural Stride, so it now heals itself instead of biting
                if (_anim.applyRootMotion) _anim.applyRootMotion = false;

                float speed2d = new Vector2(lv.x, lv.z).magnitude;

                // AIRBORNE, smoothed: a jump reads instantly (upward speed),
                // but stepping off a stair needs 0.12s of real air before the
                // legs leave the run cycle — no slope flicker
                bool rawAir = !_pilot.IsGrounded;
                _airTime = rawAir ? _airTime + Time.deltaTime : 0f;
                bool airborne = rawAir && (_pilot.Velocity.y > 1.2f || _airTime > 0.12f);

                // SHIFT DECIDES THE LOOK (Marko: no shift must never show the
                // run cycle): the animator doesn't see true m/s — it sees
                // speed SHAPED onto the ring the sprint state owns. Walking
                // at full no-shift speed sits exactly on the walk ring; shift
                // glides the same input up to the run ring.
                float ring = _pilot.IsCrouched ? 2.25f : _pilot.IsSprinting ? 4.5f : 2f;
                float fullSpeed = _pilot.IsSprinting ? _pilot.SprintSpeed : _pilot.MoveSpeed;
                if (_pilot.IsCrouched) fullSpeed *= 0.5f;
                float shape = speed2d < 0.05f ? 0f
                    : ring * Mathf.Clamp01(speed2d / Mathf.Max(0.1f, fullSpeed)) / speed2d;
                float ax = lv.x * shape, az = lv.z * shape;

                if (NaturalStride)
                {
                    _anim.SetFloat("MoveX", ax, 0.12f, Time.deltaTime);
                    _anim.SetFloat("MoveZ", az, 0.12f, Time.deltaTime);
                    // stride reference = the pace of the cycle the SHIFT state
                    // chose; playback closes the gap between feet and ground.
                    // In the AIR there's no ground to grip: authored speed.
                    float refSpeed = _pilot.IsSprinting ? RunClipSpeed : WalkClipSpeed;
                    float target = airborne || speed2d < 0.15f ? 1f
                        : Mathf.Clamp(speed2d / Mathf.Max(0.3f, refSpeed), 0.7f, 2.2f);
                    _anim.speed = Mathf.Lerp(_anim.speed, target, 8f * Time.deltaTime);
                }
                else
                {
                    _anim.SetFloat("MoveX", ax);
                    _anim.SetFloat("MoveZ", az);
                    _anim.speed = 1f;
                }
                // params exist only when the controller was built with the
                // matching clips — probe once so mismatched/older controller
                // assets stay silent instead of spamming warnings
                if (!_airChecked)
                {
                    _airChecked = true;
                    foreach (var p in _anim.parameters)
                    {
                        if (p.name == "Airborne") _hasAirParams = true;
                        else if (p.name == "Crouched") _hasCrouch = true;
                    }
                }
                if (_hasCrouch) _anim.SetBool("Crouched", _pilot.IsCrouched);
                if (_hasAirParams)
                {
                    _anim.SetBool("Airborne", airborne);
                    _anim.SetFloat("AirSpeed", speed2d, 0.1f, Time.deltaTime);
                }

                // the crouch clip's baked lean: counter-rotate the MODEL (not
                // the controller) by CrouchYawFix, faded with the crouch state
                // so standing locomotion is untouched
                _crouchYawW = Mathf.MoveTowards(_crouchYawW,
                    _pilot.IsCrouched ? 1f : 0f, Time.deltaTime * 5f);
                _anim.transform.localRotation =
                    Quaternion.Euler(0f, CrouchYawFix * _crouchYawW, 0f);
            }

            // ---- THE SHOULDER FOLLOWS THE ARM (Marko's diagnosis: "we do
            // not even grab the shoulder and that's why the arm looks so
            // ridiculous and shoulders so low"): whenever a pose system owns
            // the arms, the clavicle takes a share of the upper arm's travel
            // and the arm keeps the rest — a shoulder girdle that lifts with
            // the arm instead of hanging dead. Off during plain animation
            // (the clips animate clavicles themselves).
            bool posing = PoseGrab.IsOpen || PoseStudio.IsOpen
                || (_emotes != null && _emotes.IsPosing);
            if (posing && ClavicleFollow > 0.001f)
            {
                FollowClavicle(_clavL, _clavLRest, _armL, _armLRest, ref _armLWritten);
                FollowClavicle(_clavR, _clavRRest, _armR, _armRRest, ref _armRWritten);
            }

            // ---- clumsy life on bones the emotes DON'T own (Spine1 + Head):
            // composed OVER the animation when it runs (the animator rewrites
            // the pose every frame, so per-frame multiply never accumulates);
            // over the bind pose when it doesn't. NOT while the doll is
            // FROZEN (body paint, emote statues, pose mode) — a held pose
            // holds perfectly still, no wobble, no head-follow
            if (doll) return;
            _bob += Time.deltaTime * (2f + vel.magnitude * 1.1f);
            float lean = Mathf.Clamp(lv.z * 3.4f, -16f, 16f);
            float roll = Mathf.Clamp(-lv.x * 2.8f, -13f, 13f);
            // SHAKE BUDGET (Marko: "shaking a bit is fine - funny moments -
            // but this is too much, many people wouldn't be able to draw").
            // The camera rides the head bone, so the head's idle bob WAS the
            // nonstop view shake: now idle keeps a gentle breath, the full
            // drunk-noodle returns with real movement, and drawing steadies
            // the head down to a tremble (never fully still — comedy law).
            float movement = Mathf.Clamp01(vel.magnitude / 2f);
            float calm = Mathf.Lerp(0.3f, 1f, movement);
            if (HeldWeapon.DrawMode || SelfPaint.IsActive)
                calm *= SurfaceDrawer.IsPenActive ? 0.25f : 0.5f;
            float bob = Mathf.Sin(_bob * 2f) * 2.8f * calm;
            // side-to-side waggle only while moving — the drunk-noodle walk
            float sway = Mathf.Sin(_bob * 1.3f) * Mathf.Min(vel.magnitude * 1.4f, 7f);
            var spineWobble = Quaternion.Euler(lean + bob * 0.5f, sway, roll);
            var headWobble = Quaternion.Euler(-lean * 0.6f + bob * 1.4f, -sway * 0.7f, -roll * 0.8f);
            if (_spine1 != null)
                _spine1.localRotation = (animated ? _spine1.localRotation : _bindSpine1) * spineWobble;
            _head.localRotation = (animated ? _head.localRotation : _bindHead) * headWobble;

            // ---- the head follows your AIM (Marko: friends should SEE what
            // you're studying — and it's funnier). Neck bends a little, head
            // does the rest; composed after the animator so it never
            // accumulates. NetSync ships the pitch so remote heads match.
            // EASED toward LookPitch (0 in third person) so the bend relaxes
            // on the emote stage instead of freezing mid-stare.
            _pitchShown = Mathf.MoveTowards(_pitchShown, _pilot.LookPitch, Time.deltaTime * 240f);
            float bend = Mathf.Clamp(_pitchShown, -FollowPitchCap, FollowPitchCap);
            if (Mathf.Abs(bend) > 0.01f)
            {
                if (_spine1 != null)
                    _spine1.rotation = Quaternion.AngleAxis(bend * SpineFollowPitch, transform.right) * _spine1.rotation;
                _head.rotation = Quaternion.AngleAxis(bend * HeadFollowPitch, transform.right) * _head.rotation;
            }

            // ---- the first-person camera rides the HEAD BONE: sprint leans,
            // bob and wobble move the view WITH the face, so the hat and eyes
            // can never swallow the screen. Calibrated ONCE against the static
            // eye point, so the standing view keeps the exact old framing.
            if (_head != null && _pilot != null)
            {
                if (!_camCalibrated)
                {
                    _camCalibrated = true;
                    _camHeadLocal = _head.InverseTransformPoint(_pilot.EyeCenterWorld);
                }
                if (firstPerson)
                    _pilot.SetEyeAnchor(_head.TransformPoint(_camHeadLocal));
            }

            // ---- weapon holding without an animator: procedural reach.
            // (With the animator, HandIK does this properly through the IK pass
            // — upper body grips while the legs keep running.)
            if (!animated && _slots != null)
            {
                // carrying overrides everything: both bean-arms hug the load
                var beanCarried = InkRuneStone.Carried;
                if (beanCarried != null)
                {
                    Vector3 c = beanCarried.transform.position;
                    Vector3 side = transform.right * 0.16f;
                    Reach(_armR, _handR, c + side);
                    Reach(_foreR, _handR, c + side);
                    Reach(_armL, _handL, c - side);
                    Reach(_foreL, _handL, c - side);
                }
                else if (firstPerson)
                {
                    var weapon = _slots.CurrentWeapon;
                    if (weapon != null && weapon.gameObject.activeInHierarchy)
                    {
                        Vector3 grip = weapon.transform.TransformPoint(new Vector3(0.02f, -0.08f, -0.1f));
                        Vector3 support = weapon.transform.TransformPoint(new Vector3(-0.12f, 0f, 0.05f));
                        Reach(_armR, _handR, grip);
                        Reach(_foreR, _handR, grip);
                        Reach(_armL, _handL, support);
                        Reach(_foreL, _handL, support);
                    }
                }
            }

            RecoverBlendTick();
            TrackBoneVelocities();
        }

        /// RAGDOLL RECOVERY (Marko: "doesn't interpolate in getting up" /
        /// "gradually move ... and back"): for a beat after physics lets go,
        /// whatever the animator (or the procedural layers) wrote this frame
        /// is eased in FROM the doll's last real pose — rotations AND the
        /// hips' height, so the body climbs to its feet instead of popping
        /// upright off the floor. Runs LAST in LateUpdate so it composes
        /// over every other writer.
        void RecoverBlendTick()
        {
            if (_recoverT < 0f) return;
            _recoverT += Time.deltaTime;
            float a = Mathf.Clamp01(_recoverT / Mathf.Max(0.05f, RagdollRecoverBlend));
            a = a * a * (3f - 2f * a);
            foreach (var (t, rot) in _recoverFrom)
            {
                if (t == null) continue;
                t.localRotation = Quaternion.Slerp(rot, t.localRotation, a);
            }
            if (_hips != null)
                _hips.localPosition = Vector3.Lerp(_recoverHipsFrom, _hips.localPosition, a);
            if (a >= 1f) _recoverT = -1f;
        }

        /// While ANIMATED, remember where every ragdoll bone was this frame —
        /// the moment physics takes over, each bone inherits its OWN motion.
        void TrackBoneVelocities()
        {
            if (_ragdolling) { _bonePrevValid = false; return; }
            if (_bonePrev == null || _bonePrev.Length != _ragdoll.Count)
                _bonePrev = new Vector3[_ragdoll.Count];
            for (int i = 0; i < _ragdoll.Count; i++)
                if (_ragdoll[i] != null) _bonePrev[i] = _ragdoll[i].transform.position;
            _bonePrevDt = Mathf.Max(Time.deltaTime, 1e-4f);
            _bonePrevValid = true;
        }

        /// One cheap FromToRotation step per frame — converges into a natural
        /// "reaching" pose without an IK system.
        static void Reach(Transform bone, Transform tip, Vector3 target)
        {
            if (bone == null || tip == null) return;
            Vector3 cur = tip.position - bone.position;
            Vector3 want = target - bone.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(cur, want), 0.6f) * bone.rotation;
        }

        void SetRagdoll(bool on)
        {
            _ragdolling = on;
            if (on)
            {
                _recoverT = -1f; // going down mid-recovery cancels the blend
                if (_anim != null) _anim.enabled = false; // physics owns the doll now
                GetComponent<EmotePlayer>()?.Interrupt();
                Vector3 vel = (transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
                // MOMENTUM-TRUE (Marko's final ruling: "simply take your
                // current momentum - standing falls in place, running falls
                // WHILE running"): full velocity inheritance, no artificial
                // topple, no cut. Downed dolls alone settle fast, so rescuers
                // can catch you.
                float drag = _pilot != null && _pilot.IsDowned ? 2.2f : 0.12f;
                // MOMENTUM-TRUE PER BONE (Marko: "gradually move from the
                // current animation to ragdoll"): each bone inherits its OWN
                // last-frame motion — an arm mid-swing keeps swinging, a
                // kicking leg keeps kicking. Pose AND velocity are continuous
                // at the handover, so nothing pops. Root velocity remains the
                // fallback (and the sanity clamp — a hitch frame must not
                // become a cannon).
                float pdt = Mathf.Max(_bonePrevDt, 1e-4f);
                for (int i = 0; i < _ragdoll.Count; i++)
                {
                    var rb = _ragdoll[i];
                    if (rb == null) continue;
                    rb.isKinematic = false;
                    Vector3 v = vel;
                    if (_bonePrevValid && _bonePrev != null && i < _bonePrev.Length)
                        v = Vector3.ClampMagnitude(
                            (rb.transform.position - _bonePrev[i]) / pdt,
                            vel.magnitude + 9f);
                    rb.linearVelocity = v;
                    rb.linearDamping = drag;
                    rb.angularDamping = 3f; // calmer spin — less flail, same slide
                    // smoothing belongs to PHYSICS-driven bones only (see the
                    // matching None below) — this is where it's earned
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                // the capsule (and camera) now CHASES the doll — you watch
                // your own body sail off and land, instead of losing it
                _pilot.SetRagdollFollow(_hips != null ? _hips.GetComponent<Rigidbody>() : null);
                return;
            }
            _pilot.SetRagdollFollow(null);
            // remember the doll's FINAL pose before anything moves — the
            // recovery blend in LateUpdate eases the animation in from here
            // (Marko: "my character doesn't interpolate in getting up")
            _recoverFrom.Clear();
            foreach (var (t, pos, rot) in _bind)
                if (t != null) _recoverFrom.Add((t, t.localRotation));
            _recoverHipsFrom = _hips != null ? _hips.localPosition : Vector3.zero;
            _recoverT = 0f;
            // INTERPOLATION OFF THE MOMENT PHYSICS LETS GO. An interpolated
            // KINEMATIC bone is smoothed toward its own last physics pose in
            // WORLD space — so when the CharacterController walks the root
            // away, the bones lag and fight it, and the body reads as pinned
            // to the spot (Marko: "I do want the body to follow the
            // CharacterController"). Parent-following needs no smoothing.
            foreach (var rb in _ragdoll)
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            foreach (var (t, pos, rot) in _bind) // positions snap home (structural);
            {                                    // ROTATIONS blend in over RecoverBlend
                if (t == null) continue;
                t.localPosition = pos;
                t.localRotation = rot;
            }
        }

        /// Seconds the get-up eases from the doll's last pose into the
        /// animation — the climb reads as a climb, not a teleport.
        public float RagdollRecoverBlend = 0.35f;
        float _recoverT = -1f;
        Vector3 _recoverHipsFrom; // lying hips height → standing, eased too
        readonly List<(Transform t, Quaternion rot)> _recoverFrom =
            new List<(Transform, Quaternion)>();

        // last ANIMATED world position of every ragdoll bone — the entry
        // handover reads per-bone velocity from these
        Vector3[] _bonePrev;
        float _bonePrevDt;
        bool _bonePrevValid;
    }

}
