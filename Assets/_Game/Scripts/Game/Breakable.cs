using UnityEngine;

namespace SpellyZombie
{
    /// Death → DEBRIS. Anything breakable bursts into real material chunks
    /// (Matter — full chemistry citizens: burn the fence, keep the coal) plus
    /// short-lived cosmetic splinters and a cartoon poof. Attached by the map
    /// builders next to Damageable on every breakable prop.
    public class Breakable : MonoBehaviour
    {
        void Awake()
        {
            var dmg = GetComponent<Damageable>();
            if (dmg != null) dmg.OnDeath += _ => Shatter();
        }

        void Shatter()
        {
            var tag = GetComponentInParent<SurfaceMaterialTag>();
            var mat = tag != null ? tag.Material : SurfaceMaterialType.Wood;

            var rends = GetComponentsInChildren<Renderer>();
            Bounds b = rends.Length > 0 ? rends[0].bounds
                : new Bounds(transform.position, Vector3.one * 0.5f);
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

            // real chunks of the material it was made of
            int chunks = Mathf.Clamp(Mathf.RoundToInt(1f + maxDim * 1.5f), 2, 5);
            for (int i = 0; i < chunks; i++)
            {
                var chunk = Matter.Spawn(mat, MatterPhase.Solid, Random.Range(0.1f, 0.17f),
                    b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.7f));
                if (chunk != null && chunk.TryGetComponent<Rigidbody>(out var rb))
                    rb.linearVelocity = Random.onUnitSphere * Random.Range(1.5f, 3f) + Vector3.up * 1.5f;
            }

            // splinters — pure spectacle, gone in seconds
            Color shard = SurfaceMaterialDB.Info(mat).SolidColor * 0.85f;
            shard.a = 1f;
            for (int i = 0; i < 6; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.name = "Splinter";
                s.transform.position = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.8f);
                s.transform.rotation = Random.rotation;
                s.transform.localScale = new Vector3(0.04f, Random.Range(0.1f, 0.24f), 0.04f);
                s.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(shard, MoteShade.Opaque);
                var srb = s.AddComponent<Rigidbody>();
                srb.mass = 0.05f;
                srb.linearVelocity = Random.onUnitSphere * Random.Range(2f, 4f) + Vector3.up * 2f;
                srb.angularVelocity = Random.insideUnitSphere * 8f;
                Destroy(s, Random.Range(3f, 5f));
            }

            var lib = FxLibrary.I;
            if (lib != null) FxLibrary.Spawn(lib.Poof, b.center);
            Juice.Thud(b.center);
        }
    }
}
