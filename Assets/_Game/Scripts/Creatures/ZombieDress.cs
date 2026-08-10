using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The zombie's WARDROBE: dresses the physics capsule in the shared
    /// character model with the zombie animation set. The capsule stays the
    /// physics/chemistry/ink body (nothing about trance, tagging or crushing
    /// changes) — the model is a pure visual that follows it.
    ///
    /// Lives OUTSIDE the zombie hierarchy on purpose: the capsule's per-kind
    /// scale is non-uniform (0.7,1,0.7…), and skinned bones rotating under a
    /// non-uniform parent shear. A world-space follower stays clean and
    /// self-destructs when its zombie pops.
    public class ZombieDress : MonoBehaviour
    {
        // warn ONCE per authored prefab, never per zombie (a horde would spam)
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

        /// SZ_ZombieAnim runs THREE DISCRETE STATES (Idle / Walk / Run), not a
        /// blend — see CharacterWizard for why. Speed only picks which one; how
        /// fast is carried by Animator.speed against the clip's own authored
        /// ground speed, which is what stops the feet sliding.
        const float RunAt = 3.0f;         // the Walk→Run threshold in the controller
        const float WalkClipSpeed = 1.4f; // ground speed walking.fbx was authored at
        const float RunClipSpeed = 4.5f;  // ditto zombie running.fbx
        bool _wasGettingUp, _socketed;
        static MaterialPropertyBlock _tintBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        bool _customBody; // HIS prefab is dressing this zombie — hands off

        /// HIS prefab is wearing this body, so no code may recolour it. Read by
        /// SummonedZombie, which tints code-built zombies green and must leave
        /// his materials completely alone.
        public bool IsCustomBody => _customBody;
        float _fidgetIn = 6f;

        /// The instantiated body model (the CharacterBaker clones this).
        public GameObject BodyGO => _body;

        /// Returns null when the model or zombie controller isn't wired —
        /// the graybox capsule look continues unchanged.
        public static ZombieDress DressUp(Zombie z, Color skin, float widthMul, GooglyEyes eyes)
        {
            // MARKO'S ZOMBIE, HIS WAY: a prefab at Resources/Custom/ZombieBody
            // replaces the shared model entirely — his mesh, his materials,
            // untouched by code (no tint, no placeholder mouth). Rig it on the
            // same Mixamo skeleton (or copy SZ_Body's avatar on import) and
            // the zombie animation set plays on it as-is.
            var custom = PrefabVault.Get("ZombieBody");
            var prefab = custom != null ? custom : CharacterLibrary.Model;
            var ctrl = CharacterLibrary.ZombieAnim;
            if (prefab == null || z == null) return null;
            // HIS BODY IS WORN EVEN WITH NO CONTROLLER (axiom: an unwired
            // animator must never silently discard his prefab — it dresses,
            // stands still, and the console says why)
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
            // AXIOM: his prefab's authored scale is a decision, not a mistake —
            // the fit MULTIPLIES it instead of assigning over it (assigning
            // meant a body authored at 0.5 came out double height).
            Vector3 authoredScale = body.transform.localScale;

            // measure the model, scale it to the capsule's height, then widen
            // or slim it per kind (stocky charger, lanky runner).
            // AXIOM: the head bone is found by HUMANOID first, then loose name
            // tiers — the old exact "mixamorig:Head" match meant a Blender rig
            // with a bone simply named "Head" silently lost its eyes and hat.
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
                // no crown bone on HIS rig — don't invent a height, keep his
                // authored proportions and say so once
                s = 1f;
                if (_warnedNoCrown.Add(prefab.GetInstanceID()))
                    Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no 'HeadTop' bone. " +
                        "keeping YOUR authored scale (no auto-fit). Add a HeadTop_End bone if you want " +
                        "zombies auto-fitted to the capsule.", prefab);
            }
            body.transform.localScale = Vector3.Scale(authoredScale, new Vector3(s * widthMul, s, s * widthMul));
            d._halfHeight = capsuleHeight * 0.5f;
            // THE CAPSULE CAN BE RESIZED AFTER IT IS DRESSED (Marko Aug 10: "the
            // zombie is now floating"). A summon multiplies the zombie's scale
            // once Spawn has already returned, so a one-time _halfHeight left the
            // outfit hanging 0.82*(SizeMul-1) above the feet — floating for a big
            // draw, sunk into the floor for a small one. Remember the fit so Sync
            // can re-derive both numbers from the capsule's LIVE scale.
            d._fitCapsuleY = z.transform.localScale.y;
            d._fitBodyScale = body.transform.localScale;

            // EVERY skinned renderer, not just the first — a multi-material or
            // multi-piece body would otherwise leave the rest culling wrongly
            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // COLOUR PER KIND, WITHOUT TOUCHING HIS MATERIAL (Marko, Aug 6: "All
            // my zombies are brown now. Can you make them change color?").
            //
            // He baked ONE ZombieBody, a brown Charger, and the old rule here was
            // that his prefab keeps his materials always, so every kind wore that
            // one colour. Swapping his material out would throw away his shader
            // and textures, and making a material asset per kind is five files he
            // has to maintain.
            //
            // A MaterialPropertyBlock is neither: his material, his shader, his
            // maps, with only the colour overridden per renderer. Clearing the
            // block puts his exact look back.
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
            if (skins.Length == 0 && _warnedNoSkin.Add(prefab.GetInstanceID()))
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no SkinnedMeshRenderer. " +
                    "a static mesh can't be animated by the zombie rig.", prefab);

            // ANIMATOR ANYWHERE IN HIS HIERARCHY (it needn't sit on the root —
            // silently missing it meant a T-posing statue with no explanation)
            d._anim = body.GetComponentInChildren<Animator>(true);
            if (d._anim != null)
            {
                if (ctrl != null) d._anim.runtimeAnimatorController = ctrl;
                d._anim.applyRootMotion = false;
            }
            else if (_warnedNoAnim.Add(prefab.GetInstanceID()))
                Debug.LogWarning($"[SpellyZombie] ZombieBody '{prefab.name}' has no Animator. " +
                    "it will stand still. Add one (humanoid avatar) to the prefab root.", prefab);

            // the zombie-ness: seeded posture/variation over the body. With
            // Marko's custom body the LOOK layers (tint, placeholder mouth)
            // stand down — only motion variety remains.
            // AXIOM: ADOPT his ZombieFlavor if he put one on the prefab — a
            // second component would run with default switches while HIS
            // configured one sat inert (his posture/jitter toggles dead).
            var flavor = Adopt.Component<ZombieFlavor>(body);
            flavor.Init(z.Kind, z.gameObject.GetInstanceID(),
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

            // a BAKED body brings its own googly eyes (Marko edited them on
            // the prefab) — the spawn-built pair bows out and the brain's
            // mood system re-points at his. A DEAD baked rig (bake lost the
            // wiring, or the eyeballs were renamed) is discarded instead, so
            // the zombie never ends up with frozen saucer eyes — same guard
            // the player rig uses.
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
                // the googly soul moves onto the animated head (Marko's fit).
                // AXIOM: IsCustom eyes carry HIS placement — mount them and
                // touch nothing else (the contract on GooglyEyes says so).
                if (eyes != null && eyes != bakedEyes)
                {
                    eyes.transform.SetParent(head, false);
                    if (!eyes.IsCustom)
                    {
                        eyes.transform.localPosition = CharacterRig.EyeLocalPos; // one knob for all eyes
                        eyes.transform.localRotation = Quaternion.identity;
                    }
                }
                // the wizard hat rides the head bone too (collect first —
                // reparenting while enumerating children throws). Its
                // coordinates were authored against the GRAYBOX capsule, so
                // on his own rig it must be re-seated on the head instead of
                // keeping a world position that belongs to another body.
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
                Destroy(gameObject); // the zombie popped; the outfit follows
                return;
            }
            Sync();

            // first LateUpdate: the zombie animator has posed the body — now
            // the costume sockets are safe to build (undead fashion optional)
            if (!_socketed && _body != null)
            {
                _socketed = true;
                // seed = this zombie's instance id — the SAME id rides the
                // zombie snapshots, so client proxies can roll the identical
                // look without a single extra byte (B6 wires that side).
                // AXIOM: on HIS body the random costume rolls stand down
                // entirely — a socket he deliberately left bare stays bare,
                // exactly as tint and the placeholder mouth already do.
                // Pieces he parented to sockets on the prefab are untouched
                // (an occupied socket is never rolled over anyway).
                Wardrobe.DressZombie(SocketSet.Build(_body, transform),
                    _customBody ? 0f : 0.35f, gameObject.GetInstanceID());
            }

            if (_anim == null) return;

            float speed = 0f;
            if (_rb != null)
            {
                Vector3 v = _rb.linearVelocity;
                v.y = 0f;
                speed = v.magnitude;
            }

            // LOCOMOTION IN THE BODY'S OWN UNITS (Marko Aug 10: "they barely
            // move their legs yet somehow their bodies travel in a direction").
            //
            // The Shamble tree is authored in m/s for a 1.0-scale body — idle 0,
            // walk 1.4, run 4.5 — and the raw world speed went straight into it.
            // Two things were wrong with that:
            //
            //   a WANDERING zombie only moves at 0.55 * WalkSpeed = 0.72 m/s
            //   (ZombieBrain), which lands squarely between idle and walk. Half
            //   the leg motion, all of the travel: it slides.
            //
            //   the body is SCALED — 0.82 base, times whatever a summon
            //   multiplied it by — so a giant covering 1.3 m/s is barely moving
            //   relative to its own legs and blends even further toward idle.
            //
            // So: measure in body-lengths, let the controller pick ONE whole
            // clip, and put how-fast into the playback rate. A shambler takes
            // slow full strides instead of twitching in place, and a giant
            // strides slowly instead of freezing mid-glide.
            float grow = _fitCapsuleY > 0.0001f
                ? Mathf.Max(0.0001f, _target.localScale.y) / _fitCapsuleY : 1f;
            float bodySpeed = speed / grow;
            _anim.SetFloat("Speed", bodySpeed);

            // RATE AGAINST THE CLIP'S OWN GROUND SPEED, not the zombie's stat:
            // the feet stop sliding only when the cycle advances at the rate the
            // animator authored it to cover ground at. One-shots (attack, hit,
            // fidget) ride this too, so it stays in a range that never reads as
            // fast-forward.
            float authored = bodySpeed >= RunAt ? RunClipSpeed : WalkClipSpeed;
            _anim.speed = bodySpeed > 0.06f
                ? Mathf.Clamp(bodySpeed / authored, 0.5f, 1.6f) : 1f;

            // struggled back to its feet — play the climb
            if (_creature != null)
            {
                bool gettingUp = _creature.GettingUp;
                if (gettingUp && !_wasGettingUp) _anim.SetTrigger("StandUp");
                _wasGettingUp = gettingUp;
            }

            // idle boredom: scratch that itch every so often
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
            // the capsule's CURRENT size, not the one it wore when it was
            // dressed — a summon resizes it after the fact and the outfit has
            // to grow with it or hover over its own feet
            float capsuleY = _target.localScale.y;
            if (!Mathf.Approximately(capsuleY, _fitCapsuleY) && _fitCapsuleY > 0.0001f)
            {
                _halfHeight = capsuleY;
                if (_body != null)
                    _body.transform.localScale = _fitBodyScale * (capsuleY / _fitCapsuleY);
            }

            // feet at the capsule's bottom END — when a knockdown releases the
            // constraints and the capsule topples, the body topples with it
            transform.rotation = _target.rotation;
            transform.position = _target.position - _target.up * _halfHeight;
        }
    }
}
