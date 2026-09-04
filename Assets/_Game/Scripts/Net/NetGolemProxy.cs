using UnityEngine;

namespace SpellyZombie
{
    /// Client-side stand-in for a host-simulated golem: no wandering, no
    /// charge, no decisions - it lerps to snapshots, takes spells as a valid
    /// target, and relays the damage to the host. Same shape as
    /// NetZombieProxy, and the same law: what the host raised is what every
    /// screen shows.
    public class NetGolemProxy : MonoBehaviour
    {
        Vector3 _targetPos;
        float _targetYaw;
        Color _skin = Color.gray;
        public int OwnerId = -1; // from the snapshot, for the ghost and the achievements
        public int Id;           // the host's instance id, the snapshot key

        /// Where a rider looks from: the eyes, else the top of the body.
        public Vector3 HeadAt
        {
            get
            {
                var eyes = GetComponentInChildren<GooglyEyes>(true);
                return eyes != null ? eyes.transform.position
                    : transform.position + Vector3.up * (transform.localScale.y * 0.95f);
            }
        }

        /// Where a rider sits: inside the body, hat out the top, same as the host's golem.
        public Vector3 SeatAt => transform.position
            + Vector3.up * (transform.localScale.y * 0.95f - 0.22f);

        public void ShowEyes(bool on)
        {
            var eyes = GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null) return;
            foreach (var r in eyes.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        public static NetGolemProxy Build(int id, Vector3 pos, Vector3 scale, Color skin)
        {
            var prefab = CollectionManager.Golem;
            if (prefab == null) return null;   // the slot's own error already said so

            var go = Instantiate(prefab, pos, Quaternion.identity);
            go.name = "NetGolem";
            if (scale.sqrMagnitude > 0.0001f) go.transform.localScale = scale;

            // the host's own colour: a client cannot re-derive the biome that
            // stamped it, so the tint rides the snapshot instead
            var view = go.GetComponent<StateView>();
            if (view == null) view = go.AddComponent<StateView>();
            view.DriveTint = true;
            view.Tint = skin;

            // it is a target, not a walker: snapshots own where it stands, but
            // spells, heat and impacts still have to land on it
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            // no brain on a proxy - the host does the deciding
            var brain = go.GetComponent<Golem>();
            if (brain != null) Destroy(brain);
            var charge = go.GetComponent<ChargeAttack>();
            if (charge != null) Destroy(charge);
            var split = go.GetComponent<DensitySplit>();
            if (split != null) Destroy(split);

            if (go.GetComponent<SurfaceMaterialTag>() == null)
                go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;
            if (go.GetComponent<PersistentInkSurface>() == null)
                go.AddComponent<PersistentInkSurface>();   // you can doodle on them

            var dmg = go.GetComponent<Element>();
            if (dmg == null) dmg = go.AddComponent<Element>();
            dmg.Rename(id);            // the HOST's name for it, so hits find it
            dmg.Health = 100000f;      // the HOST owns real strength
            dmg.RemoveOnDeath = false;  // never dies locally - snapshots decide

            var proxy = go.AddComponent<NetGolemProxy>();
            proxy.Id = id;
            proxy._targetPos = pos;
            proxy._skin = skin;
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

        /// Snapshot stopped listing it: the host says it came apart. Same burst
        /// the host plays, so a death reads the same on every screen.
        public void Vanish()
        {
            Vector3 at = transform.position + Vector3.up * 0.2f;
            GrammarFX.PuffBurst(at, _skin, 7);
            if (FxLibrary.I != null) FxLibrary.SpawnTinted(FxLibrary.I.Poof, at, _skin);
            Juice.Thud(transform.position);
            Destroy(gameObject);
        }
    }
}
