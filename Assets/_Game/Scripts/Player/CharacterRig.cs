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
        // ---------------------------------------------------------------------

        public Transform HatSocket { get; private set; }
        public Transform CapeSocket { get; private set; }
        /// Weapons glue themselves into this (the standard HandR socket —
        /// plain character space, so Marko's grip-pivot weapons drop in).
        public Transform GripSocketR => _sockets != null ? _sockets.Get("HandR") : null;

        /// True once the real model is worn (SelfPaint then skips the fat
        /// invisible controller capsule and paints the limbs directly).
        public bool HasBody => _smr != null;

        /// The hips/chest capsules are deliberately fat (they sit just OUTSIDE
        /// the mesh) — SelfPaint's fallback must not slim them under the skin.
        public bool IsTorsoBone(Transform t) => t == _hips || t == _spine1;

        static readonly Color SkinColor = new Color(0.93f, 0.87f, 0.72f); // temp — Marko restyles materials

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        EmotePlayer _emotes;
        Animator _anim;
        SkinnedMeshRenderer _smr;
        Transform _hips, _spine1, _head;
        Transform _armL, _armR, _foreL, _foreR, _handL, _handR;
        readonly List<Rigidbody> _ragdoll = new List<Rigidbody>();
        readonly List<(Transform t, Vector3 pos)> _boneHome
            = new List<(Transform, Vector3)>(); // full-skeleton structural positions
        readonly List<(Transform t, Vector3 pos, Quaternion rot)> _bind =
            new List<(Transform, Vector3, Quaternion)>();
        Quaternion _bindSpine1, _bindHead;
        bool _ragdolling;
        float _bob;
        float _pitchShown; // eased head-follows-aim pitch (relaxes in 3rd person)
        Vector3 _lastPos;

        void Start()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _slots = GetComponent<WeaponSlots>();
            var prefab = CharacterLibrary.Model;
            if (prefab == null || _pilot == null) return; // bean life continues

            RemovePlaceholder();
            BuildBody(prefab);
        }

        /// Only the KNOWN graybox parts — anything Marko added by hand survives.
        void RemovePlaceholder()
        {
            foreach (var childName in new[] { "Bean", "ShoulderPivot.L", "ShoulderPivot.R", "NeckPivot" })
            {
                var t = transform.Find(childName);
                if (t != null) Destroy(t.gameObject);
            }
        }

        void BuildBody(GameObject prefab)
        {
            var cc = GetComponent<CharacterController>();
            float bottom = cc != null ? cc.center.y - cc.height * 0.5f : -0.9f;

            var model = Instantiate(prefab, transform);
            _model = model;
            model.name = "Body";
            model.transform.localPosition = new Vector3(0f, bottom, 0f); // origin at feet
            model.transform.localRotation = Quaternion.identity; // FaceForward aligns by anatomy below
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 2; // the pen ignores our own body (body paint flips it)

            _smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_smr != null)
            {
                _smr.sharedMaterial = MatterFX.Get(SkinColor, MoteShade.Opaque);
                _smr.updateWhenOffscreen = true; // bones move it; bounds lie
            }

            var allBones = model.GetComponentsInChildren<Transform>(true);
            Transform Bone(string boneName)
            {
                foreach (var t in allBones)
                    if (t.name == "mixamorig:" + boneName) return t;
                foreach (var t in allBones)
                    if (t.name.EndsWith(boneName)) return t;
                foreach (var t in allBones) // HeadTop is exported as HeadTop_End
                    if (t.name.Contains(boneName)) return t;
                return null;
            }

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
                Debug.LogWarning("[SpellyZombie] CharacterRig: mixamorig bones not found — keeping whatever body exists.");
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
            // cache the WHOLE skeleton for the runaway-limb guard. At Start the
            // hierarchy under the hips is pure bone; props, sockets and ink
            // all attach later and stay unguarded on purpose.
            _boneHome.Clear();
            foreach (var t in _hips.GetComponentsInChildren<Transform>(true))
                if (t != _hips) _boneHome.Add((t, t.localPosition));

            // ---- the face and the wardrobe: Marko's hand-tuned eye fit, in
            // HEAD-LOCAL space so it's immune to whatever the body rotation is
            var eyes = GooglyEyes.Attach(_head, 0f, EyeScale);
            eyes.transform.localPosition = EyeLocalPos; // ← the knobs up top
            eyes.transform.localRotation = Quaternion.identity;
            eyes.transform.localScale = Vector3.one * EyeRigScale;
            _pilot.ReplaceEyes(eyes);

            HatSocket = new GameObject("Socket.Hat").transform;
            HatSocket.SetParent(_head, false);
            if (headTop != null) HatSocket.position = headTop.position;
            CapeSocket = new GameObject("Socket.Cape").transform;
            CapeSocket.SetParent(spine2 != null ? spine2 : _spine1, false);
            // (sockets + pen props are built in the first LateUpdate — the
            // bind pose faces backwards until the animator's first frame, and
            // building before that puts everything in the wrong hands)

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

        // ---------------------------------------------------- pen & grimoire --
        GameObject _wand, _book;
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

            // Marko's prefab first (Resources/Custom/Wand), then the weapon
            // skin library, then the primitive placeholder
            var wandSkin = PrefabVault.Get("Wand");
            if (wandSkin == null) wandSkin = Wardrobe.WeaponSkin("Wand");
            if (wandSkin != null)
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
                shaft.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.32f, 0.2f, 0.12f), MoteShade.Opaque);
            }

            var bookSkin = PrefabVault.Get("Grimoire");
            if (bookSkin == null) bookSkin = Wardrobe.WeaponSkin("Grimoire");
            if (bookSkin != null)
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

            _book.AddComponent<GrimoirePages>(); // seal lesson + rune spreads; G opens, , . turn
            _wand.AddComponent<WandInk>();       // the wand IS the mana bar (Ink child drains)
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
        public bool BeginBodyPaint()
        {
            if (_paintShell != null) return true;
            if (_smr == null)
            {
                Debug.LogError("[SpellyZombie] Body paint: no SkinnedMeshRenderer on the rig — capsules catch the pen.");
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
                    Debug.LogError("[SpellyZombie] Body paint: bake produced no vertices — capsules catch the pen.");
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
                foreach (var cand in candidates)
                {
                    var b = cand.col.bounds;
                    float sizeMiss = SizeMismatch(b.size, truth.size);
                    float centerMiss = Vector3.Distance(b.center, truth.center) / height;
                    float score = (sizeMiss - 1f) + centerMiss;
                    report.Add($"  {cand.tag}: size x{sizeMiss:F2}, center off {centerMiss:F2} body-heights");
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
                Destroy(best.col.sharedMesh == meshLocal ? meshScaled : meshLocal);

                float finalSize = SizeMismatch(best.col.bounds.size, truth.size);
                float finalCenter = Vector3.Distance(best.col.bounds.center, truth.center);
                bool good = finalSize < 1.3f && finalCenter < height * 0.35f;

                // a shell that doesn't cover the visible body is WORSE than no
                // shell — the pen inks thin air around you. Reject it and let
                // the limb capsules catch the pen (drawing always works).
                if (finalSize >= 1.6f || finalCenter >= height * 0.5f)
                {
                    if (best.col.sharedMesh != null) Destroy(best.col.sharedMesh);
                    Destroy(best.go);
                    Debug.LogWarning($"[SpellyZombie] Paint shell REJECTED ({best.tag}) — capsules catch the pen:\n"
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
                Debug.LogError($"[SpellyZombie] Paint shell bake FAILED ({e.Message}) — run 'Spelly Zombie → Build Character Rig' once (it enables Read/Write on SZ_Body). Limb capsules catch the pen for now.");
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

        /// Session over: every ink node on the shell is handed to its nearest
        /// bone, so the drawing rides the animation forever; the shell dies.
        public void EndBodyPaint()
        {
            if (_paintShell == null) return;
            foreach (var node in _paintShell.GetComponentsInChildren<DrawNode>(true))
            {
                // nearest LIMB SURFACE, not nearest bone origin — chest ink
                // near a shoulder is closer to the arm's PIVOT than to the
                // spine's, and parenting it to the arm made it orbit the
                // shoulder with every swing
                Transform best = null;
                float bestSqr = float.MaxValue;
                foreach (var rb in _ragdoll)
                {
                    if (rb == null) continue;
                    var limbCol = rb.GetComponent<Collider>();
                    Vector3 at = limbCol != null
                        ? limbCol.ClosestPoint(node.transform.position)
                        : rb.transform.position;
                    float d = (at - node.transform.position).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = rb.transform; }
                }
                if (best != null) node.Rebase(best);
            }
            var shellCol = _paintShell.GetComponent<MeshCollider>();
            if (shellCol != null && shellCol.sharedMesh != null)
                Destroy(shellCol.sharedMesh); // no leaked bakes
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
        Rigidbody Limb(Transform bone, Transform child, float radius, float mass, Rigidbody parent)
        {
            if (bone == null) return null;
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = mass;
            // big falls fired ragdoll bones straight through the floor
            // (discrete checks skip thin geometry at speed) — the body then
            // "vanished" under the map. Speculative sweeps stop that.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Vector3 local = child != null
                ? bone.InverseTransformPoint(child.position)
                : Vector3.up * radius * 2f;
            float len = Mathf.Max(local.magnitude, radius * 1.6f);
            var col = bone.gameObject.AddComponent<CapsuleCollider>();
            col.radius = radius;
            col.height = len + radius;
            col.center = local * 0.5f;
            col.direction = DominantAxis(local);

            if (parent != null)
            {
                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.enablePreprocessing = false;
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
                    _teamShown = MatchLobby.LocalTeam;
                    _capeStamped = StartingRuneChooser.HasChosen;
                    // the chest + hips capsules keep the cloth cape off the body
                    var clothCols = new[]
                    {
                        _spine1 != null ? _spine1.GetComponent<CapsuleCollider>() : null,
                        _hips != null ? _hips.GetComponent<CapsuleCollider>() : null,
                    };
                    _costume = Wardrobe.DressPlayer(_sockets, TeamColor(_teamShown),
                        _capeStamped ? StartingRuneChooser.ChosenCard : (RuneCardType?)null,
                        clothCols);
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
                if (_book != null && _book.activeSelf != showPen) _book.SetActive(showPen);
            }

            // ---- ragdoll on knockdowns ----
            bool wantRagdoll = _pilot.IsSprawled || _pilot.IsDowned || _pilot.IsAirTumbling;
            if (wantRagdoll != _ragdolling) SetRagdoll(wantRagdoll);
            if (_anim != null) _anim.enabled = !doll && !_ragdolling;
            if (_ragdolling)
            {
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
            // and strafes each get their own clip; crouch is its own state
            if (animated)
            {
                _anim.SetFloat("MoveX", lv.x);
                _anim.SetFloat("MoveZ", lv.z);
                _anim.SetBool("Crouched", _pilot.IsCrouched);
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
            float bob = Mathf.Sin(_bob * 2f) * 2.8f;
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
            if (!animated && firstPerson && _slots != null)
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
                if (_anim != null) _anim.enabled = false; // physics owns the doll now
                GetComponent<EmotePlayer>()?.Interrupt();
                Vector3 vel = (transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
                foreach (var rb in _ragdoll)
                {
                    if (rb == null) continue;
                    rb.isKinematic = false;
                    rb.linearVelocity = vel;
                }
                // the capsule (and camera) now CHASES the doll — you watch
                // your own body sail off and land, instead of losing it
                _pilot.SetRagdollFollow(_hips != null ? _hips.GetComponent<Rigidbody>() : null);
                return;
            }
            _pilot.SetRagdollFollow(null);
            foreach (var rb in _ragdoll)
                if (rb != null) rb.isKinematic = true;
            foreach (var (t, pos, rot) in _bind) // snap home; emotes re-own the bones
            {
                if (t == null) continue;
                t.localPosition = pos;
                t.localRotation = rot;
            }
        }
    }

    /// The hands work through the Animator's IK pass — upper body grips while
    /// the legs keep running (Marko's spec). A held weapon gets both hands on
    /// it; the wand slot holds PEN + GRIMOIRE positions in front of the view,
    /// so you always see your hands while drawing. Lives on the model object
    /// (OnAnimatorIK must share the Animator's GameObject).
    public class HandIK : MonoBehaviour
    {
        public WeaponSlots Slots;
        public Transform Pivot; // the camera pivot — pen/grimoire anchor

        Animator _anim;
        float _weight;
        float _supportWeight; // the book hand: raised only while the grimoire is OPEN
        Vector3 _grip, _support; // last targets, for the ease-out

        // pen stance blends between READING (book up, you consult it) and
        // CASTING (wand hand thrusts at the surface, book tucks away)
        Vector3 _penGrip = ReadGrip, _penSupport = ReadSupport;
        static readonly Vector3 ReadGrip = new Vector3(0.17f, -0.25f, 0.38f);
        static readonly Vector3 ReadSupport = new Vector3(-0.11f, -0.17f, 0.44f); // book low-left: readable, never hogging the view
        static readonly Vector3 CastGrip = new Vector3(0.14f, -0.16f, 0.56f);
        static readonly Vector3 CastSupport = new Vector3(-0.25f, -0.36f, 0.28f);

        void Awake() => _anim = GetComponent<Animator>();

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null) return;
            var weapon = Slots != null ? Slots.CurrentWeapon : null;
            bool weaponHold = weapon != null && weapon.gameObject.activeInHierarchy;
            bool penHold = !weaponHold && Slots != null && Slots.PenSelected && Pivot != null
                && (!SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive);

            if (weaponHold)
            {
                // the weapon is GLUED to this hand — the IK holds the hand at
                // the camera-anchored aim point, so the weapon points where
                // you look while the animation adds the sway. In draw mode the
                // weapon sits at screen center; the hands follow it there.
                _grip = HeldWeapon.DrawMode || Pivot == null
                    ? weapon.transform.TransformPoint(new Vector3(0.02f, -0.08f, -0.1f))
                    : Pivot.TransformPoint(new Vector3(0.3f, -0.26f, 0.55f));
                _support = weapon.transform.TransformPoint(new Vector3(-0.12f, 0f, 0.05f));
            }
            else if (penHold)
            {
                // ink flowing = the wand hand lunges forward and the book gets
                // out of the way; otherwise an OPEN grimoire is held up to
                // READ (G raised it) — closed, the book hand hangs free and
                // the book rides low in the palm (Marko's G toggle)
                bool casting = SurfaceDrawer.IsPenActive;
                _penGrip = Vector3.Lerp(_penGrip, casting ? CastGrip : ReadGrip,
                    Time.deltaTime * 7f);
                _penSupport = Vector3.Lerp(_penSupport, casting ? CastSupport : ReadSupport,
                    Time.deltaTime * 7f);
                _grip = Pivot.TransformPoint(_penGrip);
                _support = Pivot.TransformPoint(_penSupport);
            }

            bool bookUp = weaponHold || (penHold && GrimoirePages.BookOpen);
            _weight = Mathf.MoveTowards(_weight, weaponHold || penHold ? 1f : 0f,
                Time.deltaTime * 5f);
            _supportWeight = Mathf.MoveTowards(_supportWeight, bookUp ? 1f : 0f,
                Time.deltaTime * 5f);
            if (_weight <= 0.001f && _supportWeight <= 0.001f)
            {
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                return;
            }
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _weight);
            _anim.SetIKPosition(AvatarIKGoal.RightHand, _grip);
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _supportWeight); // full when the book is UP
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, _support);
        }
    }
}
