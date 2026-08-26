using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Dresses the zombie capsule in the shared character model; the capsule
    /// stays the physics/ink body, the model is a visual follower. Lives outside
    /// the zombie hierarchy: skinned bones shear under a non-uniform parent scale.
    public class ZombieDress : MonoBehaviour
    {
        // warn once per prefab, not per zombie
        static readonly HashSet<int> _warnedNoCrown = new HashSet<int>();
        static readonly HashSet<int> _warnedNoHead = new HashSet<int>();
        static readonly HashSet<int> _warnedNoAnim = new HashSet<int>();
        static readonly HashSet<int> _warnedNoSkin = new HashSet<int>();

        Transform _target;
        Rigidbody _rb;
        Animator _anim;
        Creature _creature;
        GameObject _body;
        float _halfHeight;
        float _fitCapsuleY;      // the capsule scale the fit below was measured against
        Vector3 _fitBodyScale;   // the body scale that fit produced

        bool _wasGettingUp, _socketed;
        static MaterialPropertyBlock _tintBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // the paint freeze: bones settle to this while the pen is on the body
        RestPose _rest;
        float _paintHoldUntil;
        bool _paintHeld;

        /// The pen is on this body - hold the pose the shell was cast in.
        public void PaintHold(float seconds) =>
            _paintHoldUntil = Mathf.Max(_paintHoldUntil, Time.time + seconds);

        /// The pen must hit the VISIBLE mesh, not the physics capsule, so every
        /// zombie carries a shell of its own mesh to draw on. BakeMesh is
        /// unusable on the rig (see CharacterRig), so the shell is the shared
        /// mesh mounted at identity - one collider, built once.
        /// Used by the dressed graybox AND by an authored body, which never
        /// goes through the wardrobe at all.
        public static bool AttachPaintShell(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.sharedMesh == null) return false;
            foreach (Transform t in smr.transform)
                if (t.name == "PaintShell") return false;   // already wearing one

            var shell = new GameObject("PaintShell");
            shell.transform.SetParent(smr.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = Vector3.one;
            shell.AddComponent<MeshCollider>().sharedMesh = smr.sharedMesh;
            // its OWN kinematic body: a concave MeshCollider under the zombie's
            // dynamic Rigidbody is rejected by PhysX outright (the console
            // error - and ink raycasts hitting nothing). A kinematic body of
            // its own detaches it from that compound: legal, raycast-visible,
            // still collision-free on the ink layer.
            var srb = shell.AddComponent<Rigidbody>();
            srb.isKinematic = true;

            // MUST stay on the ink-canvas layer: on Default this solid mesh
            // wraps the zombie's own capsule and PhysX ejects it, flinging the
            // zombie. Layer 30 ignores collisions with every layer but stays
            // raycast-visible; the grab ray and chew SphereCast mask it out.
            shell.layer = InkCanvasLayer.Layer;
            return true;
        }

        /// Spawn rise: plays the StandUp clip.
        public void Rise()
        {
            if (_anim != null) _anim.SetTrigger("StandUp");
        }

        // stride matching: the states by hash, and each clip's authored ground
        // speed (walking.fbx covers ~1.4 m/s, zombie running.fbx ~4.5)
        static readonly int HashWalk = Animator.StringToHash("Walk");
        static readonly int HashRun = Animator.StringToHash("Run");
        const float WalkClipSpeed = 1.4f;
        const float RunClipSpeed = 4.5f;

        bool _customBody; // the prefab is dressing this zombie - hands off
        bool _inPlace;    // the body IS the zombie: no rig to follow, no wardrobe
        float _diagIn;    // temporary walk diagnostic beat

        /// True when a custom prefab body is worn; code must not recolour it.
        public bool IsCustomBody => _customBody;

        /// ★ AN AUTHORED BODY, DRESSED WHERE IT STANDS. Nothing instantiated,
        /// nothing rescaled - this only wires the animator (the shared zombie
        /// controller when the prefab brought none) and drives it: stride,
        /// stand-up, the tells. A baked body used to bypass DressUp entirely
        /// and stood statue-still.
        public static ZombieDress DressInPlace(Zombie z)
        {
            if (z == null) return null;
            var anim = z.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                Debug.LogWarning($"[SpellyZombie] '{z.name}': no Animator anywhere on the body - " +
                    "it cannot animate. Add one to the ZombieBody prefab.", z);
                return null;
            }
            if (anim.runtimeAnimatorController == null)
            {
                var ctrl = CharacterLibrary.ZombieAnim;
                if (ctrl != null) anim.runtimeAnimatorController = ctrl;
                else if (_warnedNoAnim.Add(z.GetInstanceID()))
                    Debug.LogWarning("[SpellyZombie] No zombie animator controller wired in " +
                        "CharacterLibrary. The body is worn but cannot animate.", z);
            }
            anim.applyRootMotion = false;

            var d = z.gameObject.AddComponent<ZombieDress>();
            d._inPlace = true;
            d._customBody = true;
            d._target = z.transform;
            d._rb = z.GetComponent<Rigidbody>();
            d._creature = z.GetComponent<Creature>();
            d._body = z.gameObject;
            d._anim = anim;
            d._socketed = true;   // an authored body never goes through the wardrobe
            return d;
        }
        float _fidgetIn = 6f;

        /// The instantiated body model (the CharacterBaker clones this).
        public GameObject BodyGO => _body;

        /// Returns null when the model or zombie controller isn't wired -
        /// the graybox capsule look continues unchanged.
        public static ZombieDress DressUp(Zombie z, Color skin, float widthMul, GooglyEyes eyes)
        {
            // ZombieBody prefab replaces the shared model, materials untouched;
            // rigged on the same skeleton, the zombie animation set plays as-is
            var custom = CollectionManager.ZombieBody;
            var prefab = custom != null ? custom : CharacterLibrary.Model;
            var ctrl = CharacterLibrary.ZombieAnim;
            if (prefab == null || z == null) return null;
            // no controller: a custom body is still worn, stands still, warns once
            if (ctrl == null)
            {
                if (custom == null) return null;
                if (_warnedNoAnim.Add(prefab.GetInstanceID()))
                    Debug.LogWarning("[SpellyZombie] No zombie animator controller wired in " +
                        "CharacterLibrary. YOUR ZombieBody is worn but cannot animate.", prefab);
            }
            bool customBody = custom != null;

            var go = new GameObject(z.name + "_Dress");
            var d = go.AddComponent<ZombieDress>();
            d._customBody = customBody;
            d._target = z.transform;
            d._rb = z.GetComponent<Rigidbody>();
            d._creature = z.GetComponent<Creature>();

            var body = Object.Instantiate(prefab, go.transform);
            d._body = body;
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity; // humanoid retarget faces +Z
            // the fit MULTIPLIES the authored scale, never assigns over it
            Vector3 authoredScale = body.transform.localScale;

            // measure the model, scale to the capsule height, widen/slim per kind;
            // head bone: humanoid mapping first, then loose name tiers
            Transform head = null, crown = null;
            var bodyAnim = body.GetComponent<Animator>();
            if (bodyAnim != null && bodyAnim.isHuman)
                head = bodyAnim.GetBoneTransform(HumanBodyBones.Head);
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                if (head == null && (t.name == "mixamorig:Head" || t.name == "Head")) head = t;
                if (crown == null && t.name.Contains("HeadTop")) crown = t;
            }
            if (head == null)
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                    if (t.name.EndsWith("Head")) { head = t; break; }
            if (head == null && _warnedNoHead.Add(prefab.GetInstanceID()))
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no head bone " +
                    "(humanoid Head, or a bone named/ending in \"Head\"). eyes and hats have nothing to mount on.", prefab);
            float capsuleHeight = z.transform.localScale.y * 2f;
            float s;
            if (crown != null)
            {
                float modelHeight = Mathf.Max(0.5f, crown.position.y - go.transform.position.y);
                s = capsuleHeight / modelHeight;
            }
            else
            {
                // no crown bone: keep the authored scale, warn once
                s = 1f;
                if (_warnedNoCrown.Add(prefab.GetInstanceID()))
                    Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no 'HeadTop' bone. " +
                        "keeping YOUR authored scale (no auto-fit). Add a HeadTop_End bone if you want " +
                        "zombies auto-fitted to the capsule.", prefab);
            }
            body.transform.localScale = Vector3.Scale(authoredScale, new Vector3(s * widthMul, s, s * widthMul));
            d._halfHeight = capsuleHeight * 0.5f;
            // the capsule can be resized after dressing (summons); remember the
            // fit so Sync re-derives from the live capsule scale
            d._fitCapsuleY = z.transform.localScale.y;
            d._fitBodyScale = body.transform.localScale;

            // rest pose captured before any animator runs: the authored pose the
            // paint shell's sharedMesh describes; the paint freeze settles back to it
            d._rest = RestPose.Capture(body.transform);

            // every skinned renderer, not just the first
            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // per-kind colour via MaterialPropertyBlock: material, shader and maps
            // survive, only the colour is overridden; clearing the block restores it
            if (_tintBlock == null) _tintBlock = new MaterialPropertyBlock();
            foreach (var sk in skins)
            {
                if (!customBody)
                {
                    sk.sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);
                }
                else
                {
                    sk.GetPropertyBlock(_tintBlock);
                    _tintBlock.SetColor(BaseColorId, skin);
                    _tintBlock.SetColor(ColorId, skin);   // built-in shaders
                    sk.SetPropertyBlock(_tintBlock);
                }
                sk.updateWhenOffscreen = true;
            }
            if (AttachPaintShell(smr))
                // the dress rides OUTSIDE the zombie's hierarchy, so ink landing
                // on the shell needs this to find its way back to the zombie.
                // An authored body carries its shell inside, and resolves directly.
                d.gameObject.AddComponent<ZombieOwner>().Of = z;

            if (skins.Length == 0 && _warnedNoSkin.Add(prefab.GetInstanceID()))
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no SkinnedMeshRenderer. " +
                    "a static mesh can't be animated by the zombie rig.", prefab);

            // the animator may sit anywhere in the hierarchy
            d._anim = body.GetComponentInChildren<Animator>(true);
            if (d._anim != null)
            {
                if (ctrl != null) d._anim.runtimeAnimatorController = ctrl;
                d._anim.applyRootMotion = false;
            }
            else if (_warnedNoAnim.Add(prefab.GetInstanceID()))
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no Animator. " +
                    "it will stand still. Add one (humanoid avatar) to the prefab root.", prefab);

            // seeded posture/variation; with a custom body only motion variety
            // remains. Adopt an existing ZombieFlavor rather than adding a second.
            var flavor = Adopt.Component<ZombieFlavor>(body);
            flavor.Init(z.gameObject.GetInstanceID(),
                d._anim, skin, smr, body, customBody);

            // hide the graybox: capsule + head cube renderers off (colliders,
            // rigidbody, ink surface all stay exactly as they were)
            var rootRend = z.GetComponent<MeshRenderer>();
            if (rootRend != null) rootRend.enabled = false;
            var headCube = z.transform.Find("Head");
            if (headCube != null)
            {
                var hr = headCube.GetComponent<MeshRenderer>();
                if (hr != null) hr.enabled = false;
            }

            // baked eyes replace the spawn-built pair; a dead pair (lost wiring)
            // is discarded so the code-built eyes stay
            var bakedEyes = body.GetComponentInChildren<GooglyEyes>(true);
            if (bakedEyes != null && !(bakedEyes.IsAlive && bakedEyes.enabled
                && bakedEyes.gameObject.activeSelf))
            {
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}': its googly eyes are not a " +
                    "working Eye→Pupil pair, so the code-built eyes stay. (Name two children " +
                    "starting with 'Eye', each holding a child named 'Pupil'.)", prefab);
                Object.Destroy(bakedEyes.gameObject);
                bakedEyes = null;
            }
            if (bakedEyes != null && eyes != null && bakedEyes != eyes)
            {
                Object.Destroy(eyes.gameObject);
                eyes = bakedEyes;
                var brain = z.GetComponent<ZombieBrain>();
                if (brain != null) brain.Eyes = bakedEyes;
            }

            if (head != null)
            {
                // eyes mount on the animated head; IsCustom eyes keep their own placement
                if (eyes != null && eyes != bakedEyes)
                {
                    eyes.transform.SetParent(head, false);
                    if (!eyes.IsCustom)
                    {
                        eyes.transform.localPosition = CharacterRig.EyeLocalPos; // one knob for all eyes
                        eyes.transform.localRotation = Quaternion.identity;
                    }
                }
                // the hat rides the head bone (collect first: reparenting while
                // enumerating throws); on a custom rig it is re-seated on the head
                var hats = new System.Collections.Generic.List<Transform>();
                foreach (Transform c in z.transform)
                    if (c.name == "Hat") hats.Add(c);
                foreach (var hat in hats)
                {
                    hat.SetParent(head, !customBody);
                    if (customBody)
                    {
                        hat.localPosition = Vector3.zero;
                        hat.localRotation = Quaternion.identity;
                    }
                }
            }

            d.Sync();
            return d;
        }

        public void Attack()
        {
            if (_anim == null) return;
            _anim.SetFloat("Variant", Random.Range(0, 4)); // punch / kick / headbutt / classic
            _anim.SetTrigger("Attack");
        }

        public void Hit() { if (_anim != null) _anim.SetTrigger("Hit"); }
        public void Scream() { if (_anim != null) _anim.SetTrigger("Scream"); }

        void LateUpdate()
        {
            if (_target == null)
            {
                if (!_inPlace) Destroy(gameObject); // the zombie popped; the outfit follows
                return;
            }
            if (!_inPlace) Sync();

            // first LateUpdate: the animator has posed the body, sockets are safe to build
            if (!_socketed && _body != null)
            {
                _socketed = true;
                // seed = instance id, the same id in the zombie snapshots so client
                // proxies roll the identical look; on a custom body random costume
                // rolls stand down (occupied sockets are never rolled over)
                Wardrobe.DressZombie(SocketSet.Build(_body, transform),
                    _customBody ? 0f : 0.35f, gameObject.GetInstanceID());
            }

            // paint freeze: animator off, bones settle to the rest pose so ink
            // lands on the shell shape; released, the animator resumes
            bool held = Time.time < _paintHoldUntil;
            if (held != _paintHeld)
            {
                _paintHeld = held;
                if (_anim != null) _anim.enabled = !held;
            }
            if (held)
            {
                // a soft settle, not a snap
                _rest?.Settle(Time.deltaTime);
                return; // no stride matching, no fidget - it is a statue
            }

            if (_anim == null) return;

            float speed = 0f;
            if (_rb != null)
            {
                Vector3 v = _rb.linearVelocity;
                v.y = 0f;
                speed = v.magnitude;
            }

            // stride matching: clips are authored in m/s for a 1.0-scale body, so
            // raw world speed makes scaled or slow zombies slide. Guards:
            //   · playback rate touches locomotion states only (Animator.speed gates every clip)
            //   · reference speed is the current state's own authored ground speed
            //   · clamped tight, so only honest stride matching
            _anim.SetFloat("Speed", speed);
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            bool walking = st.shortNameHash == HashWalk;
            bool running = st.shortNameHash == HashRun;
            if ((walking || running) && speed > 0.05f)
            {
                // clips are authored for a 1-scale body: a scaled body's legs
                // cover proportionally different ground, and summons come in
                // many sizes - so the reference speed scales with the body,
                // and the ceiling is high enough to actually catch up
                float scaleY = Mathf.Max(0.2f,
                    _body != null ? _body.transform.lossyScale.y : transform.lossyScale.y);
                float authored = (running ? RunClipSpeed : WalkClipSpeed) * scaleY;
                _anim.speed = Mathf.Clamp(speed / authored, 0.4f, 2.5f);
            }
            else
            {
                // idle, one-shots, and the Shamble blend-tree controller run at 1x
                _anim.speed = 1f;
            }

            // struggled back to its feet - play the climb
            if (_creature != null)
            {
                bool gettingUp = _creature.GettingUp;
                if (gettingUp && !_wasGettingUp) _anim.SetTrigger("StandUp");
                _wasGettingUp = gettingUp;
            }

            // TEMPORARY walk diagnostic: one line a second names the failing
            // leg - state, drive, velocity, height. Delete once walking is right.
            if (_inPlace && (_diagIn -= Time.deltaTime) <= 0f)
            {
                _diagIn = 1f;
                var info = _anim.GetCurrentAnimatorStateInfo(0);
                string stName = info.shortNameHash == HashWalk ? "Walk"
                    : info.shortNameHash == HashRun ? "Run" : "other";
                var hips = _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                Debug.Log($"[SpellyZombie] walk diag: state={stName} speedFloat={_anim.GetFloat("Speed"):0.00} " +
                    $"vel={(_rb != null ? _rb.linearVelocity.magnitude : 0f):0.00} animSpeed={_anim.speed:0.00} " +
                    $"rootY={transform.position.y:0.00} hipsY={(hips != null ? hips.position.y - transform.position.y : 0f):0.00} " +
                    $"canMove={(_creature == null || _creature.CanMove)} human={_anim.isHuman}");
            }

            // idle fidget timer
            if (speed < 0.2f)
            {
                _fidgetIn -= Time.deltaTime;
                if (_fidgetIn <= 0f)
                {
                    _fidgetIn = Random.Range(8f, 16f);
                    _anim.SetTrigger("Fidget");
                }
            }
            else
            {
                _fidgetIn = Random.Range(4f, 9f);
            }
        }

        void Sync()
        {
            // use the capsule's live scale: a summon resizes it after dressing
            float capsuleY = _target.localScale.y;
            if (!Mathf.Approximately(capsuleY, _fitCapsuleY) && _fitCapsuleY > 0.0001f)
            {
                _halfHeight = capsuleY;
                if (_body != null)
                    _body.transform.localScale = _fitBodyScale * (capsuleY / _fitCapsuleY);
            }

            // feet at the capsule's bottom END - when a knockdown releases the
            // constraints and the capsule topples, the body topples with it
            transform.rotation = _target.rotation;
            transform.position = _target.position - _target.up * _halfHeight;
        }
    }
}
