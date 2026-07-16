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
        Transform _target;
        Rigidbody _rb;
        Animator _anim;
        Creature _creature;
        GameObject _body;
        float _halfHeight;
        bool _wasGettingUp, _socketed;
        float _fidgetIn = 6f;

        /// Returns null when the model or zombie controller isn't wired —
        /// the graybox capsule look continues unchanged.
        public static ZombieDress DressUp(Zombie z, Color skin, float widthMul, GooglyEyes eyes)
        {
            var prefab = CharacterLibrary.Model;
            var ctrl = CharacterLibrary.ZombieAnim;
            if (prefab == null || ctrl == null || z == null) return null;

            var go = new GameObject(z.name + "_Dress");
            var d = go.AddComponent<ZombieDress>();
            d._target = z.transform;
            d._rb = z.GetComponent<Rigidbody>();
            d._creature = z.GetComponent<Creature>();

            var body = Object.Instantiate(prefab, go.transform);
            d._body = body;
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity; // humanoid retarget faces +Z

            // measure the model, scale it to the capsule's height, then widen
            // or slim it per kind (stocky charger, lanky runner)
            Transform head = null, crown = null, feetProbe = null;
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "mixamorig:Head") head = t;
                else if (t.name.Contains("HeadTop")) crown = t;
                else if (t.name.EndsWith("LeftToeBase")) feetProbe = t;
            }
            float modelHeight = crown != null && feetProbe != null
                ? Mathf.Max(0.5f, crown.position.y - go.transform.position.y)
                : 1.42f;
            float capsuleHeight = z.transform.localScale.y * 2f;
            float s = capsuleHeight / modelHeight;
            body.transform.localScale = new Vector3(s * widthMul, s, s * widthMul);
            d._halfHeight = capsuleHeight * 0.5f;

            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);
                smr.updateWhenOffscreen = true;
            }

            d._anim = body.GetComponent<Animator>();
            if (d._anim != null)
            {
                d._anim.runtimeAnimatorController = ctrl;
                d._anim.applyRootMotion = false;
            }

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

            if (head != null)
            {
                // the googly soul moves onto the animated head (Marko's fit)
                if (eyes != null)
                {
                    eyes.transform.SetParent(head, false);
                    eyes.transform.localPosition = CharacterRig.EyeLocalPos; // one knob for all eyes
                    eyes.transform.localRotation = Quaternion.identity;
                }
                // the wizard hat rides the head bone too (collect first —
                // reparenting while enumerating children throws)
                var hats = new System.Collections.Generic.List<Transform>();
                foreach (Transform c in z.transform)
                    if (c.name == "Hat") hats.Add(c);
                foreach (var hat in hats)
                    hat.SetParent(head, true);
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
                // look without a single extra byte (B6 wires that side)
                Wardrobe.DressZombie(SocketSet.Build(_body, transform), 0.35f,
                    gameObject.GetInstanceID());
            }

            if (_anim == null) return;

            float speed = 0f;
            if (_rb != null)
            {
                Vector3 v = _rb.linearVelocity;
                v.y = 0f;
                speed = v.magnitude;
                _anim.SetFloat("Speed", speed);
            }

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
            // feet at the capsule's bottom END — when a knockdown releases the
            // constraints and the capsule topples, the body topples with it
            transform.rotation = _target.rotation;
            transform.position = _target.position - _target.up * _halfHeight;
        }
    }
}
