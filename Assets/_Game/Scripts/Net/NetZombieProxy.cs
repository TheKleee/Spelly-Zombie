using UnityEngine;

namespace SpellyZombie
{
    /// Client-side stand-in for a host-simulated zombie - no brain, lerps to snapshots, valid spell target, damage relayed to the host; vanishes when snapshots stop listing it.
    public class NetZombieProxy : MonoBehaviour
    {
        Vector3 _targetPos;
        float _targetYaw;
        bool _ranged;
        public int Id;             // the host's instance id, the snapshot key
        public int OwnerId = -1;   // the acolyte who drew it, from the snapshot

        /// Where a rider's camera sits: the eyes, else the top of the body.
        public Vector3 HeadAt
        {
            get
            {
                var eyes = GetComponentInChildren<GooglyEyes>(true);
                return eyes != null ? eyes.transform.position
                    : transform.position + Vector3.up * (transform.localScale.y * 0.95f);
            }
        }

        /// The rider sits inside the head: the eyes get out of the lens.
        public void ShowEyes(bool on)
        {
            var eyes = GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null) return;
            foreach (var r in eyes.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        public static NetZombieProxy Build(int id, Vector3 pos, Vector3 scale, bool ranged = false)
        {
            // the same two colours SummonedZombie paints on the host, so a
            // ranged zombie reads as ranged on every screen
            Color skin = ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;
            GameObject go;

            // THE SAME BODY THE HOST RAISED. Clients used to get a capsule with
            // a cube head, so a friend's screen showed a different creature
            // than yours - and there was no mesh for their pen to land on.
            var custom = CollectionManager.ZombieBody;
            if (custom != null)
            {
                go = Instantiate(custom, pos, Quaternion.identity);
                go.name = "NetZombie";
                // the host's own scale, so kind shape AND summon size match
                go.transform.localScale = scale.sqrMagnitude > 0.0001f
                    ? scale : Zombie.BodyScale;

                // you can draw on a remote zombie too: same shell as the host's
                ZombieDress.AttachPaintShell(go.GetComponentInChildren<SkinnedMeshRenderer>(true));
                if (go.GetComponentInChildren<Collider>(true) == null) Zombie.FitCollider(go);

                // an authored body brings its own eyes; a second pair is the
                // baked-prefab trap
                if (go.GetComponentInChildren<GooglyEyes>(true) == null)
                {
                    var face = FindHead(go.transform) ?? go.transform;
                    GooglyEyes.Attach(face, 0f, DrawingConfig.ZombieEyeScale);
                }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "NetZombie";
                go.transform.position = pos;
                go.transform.localScale = Zombie.BodyScale; // one body - host/client looks can't drift
                go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
                head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
                head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);
                GooglyEyes.Attach(head.transform, 0f, DrawingConfig.ZombieEyeScale);
            }

            // kinematic body: snapshots own the position, but particle triggers
            // still fire against it and heat/impacts still land
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Flesh;
            go.AddComponent<PersistentInkSurface>(); // you can still doodle on them

            var dmg = go.AddComponent<Element>();
            dmg.Rename(id);            // the HOST's name for it, so hits find it
            dmg.Health = 100000f;      // the HOST owns real health
            dmg.RemoveOnDeath = false;  // never dies locally - snapshots decide

            var proxy = go.AddComponent<NetZombieProxy>();
            proxy.Id = id;
            proxy._targetPos = pos;
            proxy._ranged = ranged;
            return proxy;
        }

        public void Target(Vector3 pos, float yaw)
        {
            _targetPos = pos;
            _targetYaw = yaw;
        }

        void Update()
        {
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.Euler(0f, _targetYaw, 0f), Time.deltaTime * 8f);
        }

        /// The first bone whose name reads as a head, so code-built eyes land
        /// on a face rather than a hip.
        static Transform FindHead(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            return null;
        }

        /// Snapshot stopped listing it: the host says it's gone. Same burst the
        /// host plays, so a death reads the same on every screen.
        public void Vanish()
        {
            Color c = _ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;
            Vector3 at = transform.position + Vector3.up * transform.localScale.y * 0.4f;
            GrammarFX.PuffBurst(at, c, 7);
            if (FxLibrary.I != null) FxLibrary.SpawnTinted(FxLibrary.I.Poof, at, c);
            Juice.Pop(transform.position);
            Juice.Thud(transform.position);
            Destroy(gameObject);
        }

    }
}
