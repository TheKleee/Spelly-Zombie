using UnityEngine;

namespace SpellyZombie
{
    /// B4: client-side stand-in for a HOST-simulated zombie - no brain, lerps to snapshots, valid spell target, damage RELAYED to the host; vanishes when snapshots stop listing it.
    public class NetZombieProxy : MonoBehaviour
    {
        Vector3 _targetPos;
        float _targetYaw;

        public static NetZombieProxy Build(int id, ZombieKind kind, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "NetZombie_" + kind;
            go.transform.position = pos;
            go.transform.localScale = Zombie.KindScale(kind); // shared table - host/client looks can't drift
            Color skin = Zombie.KindSkin(kind);
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
            head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
            head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);
            GooglyEyes.Attach(head.transform, 0f, DrawingConfig.ZombieEyeScale);

            // kinematic body: snapshots own the position, but particle triggers
            // still fire against it and heat/impacts still land
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Flesh;
            go.AddComponent<PersistentInkSurface>(); // you can still doodle on them

            var dmg = go.AddComponent<Damageable>();
            dmg.Health = 100000f;      // the HOST owns real health
            dmg.Destructible = false;  // never dies locally - snapshots decide

            var proxy = go.AddComponent<NetZombieProxy>();
            proxy._targetPos = pos;
            dmg.OnDamaged += (amount, cause) => NetSync.SendZombieHit(id, amount);
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

        /// Snapshot stopped listing it: the host says it's gone. Poof politely.
        public void Vanish()
        {
            for (int i = 0; i < 4; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff";
                Destroy(puff.GetComponent<Collider>());
                puff.transform.position = transform.position + Vector3.up * 0.8f
                    + Random.insideUnitSphere * 0.3f;
                puff.transform.localScale = Vector3.one * Random.Range(0.1f, 0.2f);
                puff.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                    new Color(0.5f, 0.6f, 0.45f, 0.6f), MoteShade.Transparent);
                var rb = puff.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Vector3.up * Random.Range(0.5f, 1.2f) + Random.insideUnitSphere * 0.4f;
                Destroy(puff, 0.6f);
            }
            Destroy(gameObject);
        }
    }
}
