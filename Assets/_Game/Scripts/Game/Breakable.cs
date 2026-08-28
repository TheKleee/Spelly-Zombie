using UnityEngine;

namespace SpellyZombie
{
    /// Death debris: goes beside a Element and spawns authored debris/FX
    /// (code-built fallbacks when slots are empty), adopting any authored
    /// components. No Update - the work happens once, on OnDeath.
    public class Breakable : MonoBehaviour
    {
        [Header("WHAT IT LEAVES BEHIND: your prefabs")]
        [Tooltip("Your log / chunk prefabs. One is picked at random per piece. Leave EMPTY to use code-built material chunks.")]
        public GameObject[] DebrisPrefabs;
        [Tooltip("How many pieces. 0/0 = scale the count to the object's size.")]
        public int DebrisMin = 0, DebrisMax = 0;
        [Tooltip("Optional empties marking where debris appears. None = spread through the object's bounds.")]
        public Transform[] DebrisOrigins;
        [Tooltip("How hard the pieces are thrown out.")]
        public float DebrisSpread = 2.5f;
        [Tooltip("Seconds before debris despawns. 0 = it stays (logs are meant to be picked up).")]
        public float DebrisLifetime = 0f;

        [Header("THE MOMENT IT BREAKS")]
        [Tooltip("Your particle effect, spawned at the break point. Empty = the default poof.")]
        public GameObject BreakFx;
        [Tooltip("Optional stump or cracked rock left standing. Empty = the whole thing goes.")]
        public GameObject Standing;
        [Tooltip("Code-built splinters, a placeholder. Turn OFF once your own debris looks right.")]
        public bool CodeSplinters = true;
        [Tooltip("Play the default thud. Off if your effect brings its own sound.")]
        public bool DefaultSound = true;
        [Tooltip("LOBBY ONLY: seconds before this object rebuilds itself after breaking. 0 = the global LobbyRespawnSeconds.")]
        public float LobbyRespawnOverride = 0f;

        void Awake()
        {
            var dmg = GetComponent<Element>();
            if (dmg != null) dmg.OnDeath += _ => Shatter();
        }

        Bounds MyBounds()
        {
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(transform.position, Vector3.one * 0.5f);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        void Shatter()
        {
            Bounds b = MyBounds();
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var tag = GetComponentInParent<SurfaceMaterialTag>();
            var mat = tag != null ? tag.Material : SurfaceMaterialType.Wood;

            // ---- authored effect, or the fallback ----
            if (BreakFx != null) Instantiate(BreakFx, b.center, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, b.center);
            if (DefaultSound) Juice.Thud(b.center);

            // ---- optional standing piece ----
            if (Standing != null)
                Instantiate(Standing, transform.position, transform.rotation, transform.parent);

            int count = DebrisMax > 0
                ? Random.Range(Mathf.Max(1, DebrisMin), DebrisMax + 1)
                : Mathf.Clamp(Mathf.RoundToInt(1f + maxDim * 1.5f), 2, 5);

            if (DebrisPrefabs != null && DebrisPrefabs.Length > 0) SpawnYourDebris(count, b, mat);
            else SpawnCodeChunks(count, b, mat);

            if (CodeSplinters) Splinters(b, mat);

            // lobby objects respawn after breaking; in a round broken stays broken
            if (RoundDirector.InLobby)
                LobbyRespawn.Take(gameObject, LobbyRespawnOverride > 0f
                    ? LobbyRespawnOverride : DrawingConfig.LobbyRespawnSeconds);
        }

        /// Spawns authored debris prefabs; everything authored on them is kept.
        void SpawnYourDebris(int count, Bounds b, SurfaceMaterialType mat)
        {
            for (int i = 0; i < count; i++)
            {
                var prefab = DebrisPrefabs[Random.Range(0, DebrisPrefabs.Length)];
                if (prefab == null) continue;

                Vector3 at;
                Quaternion rot;
                if (DebrisOrigins != null && DebrisOrigins.Length > 0)
                {
                    var origin = DebrisOrigins[i % DebrisOrigins.Length];
                    if (origin == null) { at = b.center; rot = prefab.transform.rotation; }
                    else { at = origin.position; rot = origin.rotation; }
                }
                else
                {
                    at = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.7f);
                    rot = prefab.transform.rotation; // authored orientation
                }

                var piece = Instantiate(prefab, at, rot);

                // adopt, never dictate: authored rigidbody/collider/tag win
                var rb = Adopt.Component<Rigidbody>(piece, out bool madeRb);
                if (madeRb && piece.GetComponentInChildren<Collider>() == null)
                    Debug.LogWarning($"[SpellyZombie] Debris '{prefab.name}' has no collider: " +
                                     "it will fall through the world. Add one to the prefab.", prefab);
                rb.linearVelocity = Random.onUnitSphere * DebrisSpread + Vector3.up * (DebrisSpread * 0.5f);
                rb.angularVelocity = Random.insideUnitSphere * 4f;

                if (piece.GetComponentInChildren<SurfaceMaterialTag>() == null)
                    piece.AddComponent<SurfaceMaterialTag>().Material = mat;

                if (DebrisLifetime > 0f) Destroy(piece, DebrisLifetime);
            }
        }

        /// Fallback: real material chunks, so debris still joins the chemistry.
        void SpawnCodeChunks(int count, Bounds b, SurfaceMaterialType mat)
        {
            // blame already names whoever broke this - the chunks keep it
            var blame = GetComponent<Element>();
            for (int i = 0; i < count; i++)
            {
                var chunk = Matter.Spawn(mat, MatterPhase.Solid, Random.Range(0.1f, 0.17f),
                    b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.7f));
                if (chunk == null) continue;
                if (blame != null) chunk.StampOwner(blame.Owner);
                if (chunk.TryGetComponent<Rigidbody>(out var rb))
                    rb.linearVelocity = Random.onUnitSphere * DebrisSpread + Vector3.up * 1.5f;
            }
        }

        void Splinters(Bounds b, SurfaceMaterialType mat)
        {
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
        }
    }
}
