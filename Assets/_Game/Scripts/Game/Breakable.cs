using UnityEngine;

namespace SpellyZombie
{
    /// DEATH → DEBRIS, and every part of it is YOURS (AXIOM, Marko Jul 25:
    /// "I create trees and colliders and I decide what log prefabs will look
    /// like and what particle effect will spawn").
    ///
    /// Drop this beside a Damageable on anything breakable — your tree, your
    /// rock, your fence — and fill in as much or as little as you like:
    ///
    ///   DebrisPrefabs  — YOUR logs / rock chunks. Empty = code-built chunks.
    ///   DebrisOrigins  — optional empties marking where they appear.
    ///   BreakFx        — YOUR particle effect. Empty = the default poof.
    ///   Standing       — optional stump / cracked rock left behind.
    ///
    /// Nothing you author is ever overwritten: your prefab's own Rigidbody,
    /// colliders, materials and material tag are ADOPTED, and code only adds
    /// what is genuinely missing.
    ///
    /// PERFORMANCE: this component has NO Update. A forest of these costs
    /// nothing until something actually breaks — the work happens once, on
    /// the OnDeath event. (The thing to keep watching is Thermal, which DOES
    /// tick per-frame: let spells add it on contact, never pre-attach it to
    /// scenery.)
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
        [Tooltip("LOBBY ONLY: seconds before this object rebuilds itself after breaking. 0 = the global LobbyRespawnSeconds. The cauldron wants 3 (Marko: \"it can just spawn after 3 seconds\").")]
        public float LobbyRespawnOverride = 0f;

        void Awake()
        {
            var dmg = GetComponent<Damageable>();
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

            // ---- YOUR effect, or the fallback ----
            if (BreakFx != null) Instantiate(BreakFx, b.center, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, b.center);
            if (DefaultSound) Juice.Thud(b.center);

            // ---- what's left standing (your stump), if you made one ----
            if (Standing != null)
                Instantiate(Standing, transform.position, transform.rotation, transform.parent);

            int count = DebrisMax > 0
                ? Random.Range(Mathf.Max(1, DebrisMin), DebrisMax + 1)
                : Mathf.Clamp(Mathf.RoundToInt(1f + maxDim * 1.5f), 2, 5);

            if (DebrisPrefabs != null && DebrisPrefabs.Length > 0) SpawnYourDebris(count, b, mat);
            else SpawnCodeChunks(count, b, mat);

            if (CodeSplinters) Splinters(b, mat);

            // THE LOBBY PUTS ITSELF BACK TOGETHER (Marko Aug 10). It is the
            // practice ground, so it has to survive being practised on —
            // acolytes need shapes left to scan and wizards need things left to
            // break. In a real round, broken stays broken.
            if (RoundDirector.InLobby)
                LobbyRespawn.Take(gameObject, LobbyRespawnOverride > 0f
                    ? LobbyRespawnOverride : DrawingConfig.LobbyRespawnSeconds);
        }

        /// YOUR prefabs become real, physical, chemistry-aware debris — with
        /// everything you authored on them left exactly as you made it.
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
                    rot = prefab.transform.rotation; // YOUR authored orientation
                }

                var piece = Instantiate(prefab, at, rot);

                // adopt, never dictate: your rigidbody/collider/tag win
                var rb = Adopt.Component<Rigidbody>(piece, out bool madeRb);
                if (madeRb && piece.GetComponentInChildren<Collider>() == null)
                    Debug.LogWarning($"[SpellyZombie] Debris '{prefab.name}' has no collider: " +
                                     "it will fall through the world. Add one to the prefab.", prefab);
                rb.linearVelocity = Random.onUnitSphere * DebrisSpread + Vector3.up * (DebrisSpread * 0.5f);
                rb.angularVelocity = Random.insideUnitSphere * 4f;

                // make it a chemistry citizen only if you didn't already
                if (piece.GetComponentInChildren<SurfaceMaterialTag>() == null)
                    piece.AddComponent<SurfaceMaterialTag>().Material = mat;

                if (DebrisLifetime > 0f) Destroy(piece, DebrisLifetime);
            }
        }

        /// The fallback when you haven't made debris yet — real material
        /// chunks, so burning the fence still leaves you the coal.
        void SpawnCodeChunks(int count, Bounds b, SurfaceMaterialType mat)
        {
            for (int i = 0; i < count; i++)
            {
                var chunk = Matter.Spawn(mat, MatterPhase.Solid, Random.Range(0.1f, 0.17f),
                    b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.7f));
                if (chunk != null && chunk.TryGetComponent<Rigidbody>(out var rb))
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
