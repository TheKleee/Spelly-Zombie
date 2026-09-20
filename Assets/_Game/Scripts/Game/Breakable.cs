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
        [Tooltip("Play a break sound. Off if your effect brings its own sound.")]
        public bool DefaultSound = true;
        [Tooltip("YOUR sound for this object breaking (a bottle, a bell). Empty = picked by what it is made of: wood, stone, bone, ice.")]
        public AudioClip BreakSound;
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

        // every machine breaks the same object with the same roll: the dice
        // are seeded by the element's shared net id, the pieces named so
        // ScenePath and Element.Refile agree on them everywhere
        System.Random _rng;
        string _pieceTag;

        static float Next(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        static Vector3 NextInSphere(System.Random r)
        {
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3(Next(r, -1f, 1f), Next(r, -1f, 1f), Next(r, -1f, 1f));
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector3.zero;
        }

        static Vector3 NextOnSphere(System.Random r)
        {
            var v = NextInSphere(r);
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.up;
        }

        void Shatter()
        {
            Bounds b = MyBounds();
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var tag = GetComponentInParent<SurfaceMaterialTag>();
            var mat = tag != null ? tag.Material : SurfaceMaterialType.Wood;
            var el = GetComponent<Element>();
            int seed = el != null && el.NetId != 0 ? el.NetId : GetInstanceID();
            _rng = new System.Random(seed);
            _pieceTag = $"{name}#{(uint)seed:X8}";

            // ---- authored effect, or the fallback ----
            if (BreakFx != null) Instantiate(BreakFx, b.center, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, b.center);
            if (DefaultSound)
            {
                // every machine breaks its own copy, so the sound stays off the wire; bigger is louder and lower
                float big = Mathf.InverseLerp(0.3f, 4f, maxDim);
                float volume = Mathf.Lerp(0.6f, 1f, big), pitch = Mathf.Lerp(1.12f, 0.85f, big) * Random.Range(0.95f, 1.05f);
                if (BreakSound != null) Juice.Clip3D(BreakSound, b.center, volume, pitch);
                else if (!Juice.Sound(AudioLibrary.BreakOf(mat), b.center, volume, pitch, false)) Juice.Thud(b.center);
            }

            // ---- optional standing piece ----
            if (Standing != null)
            {
                var stump = Instantiate(Standing, transform.position, transform.rotation, transform.parent);
                stump.name = $"{_pieceTag}#stand";
                Element.Refile(stump.transform);
            }

            int count = DebrisMax > 0
                ? _rng.Next(Mathf.Max(1, DebrisMin), DebrisMax + 1)
                : Mathf.Clamp(Mathf.RoundToInt(1f + maxDim * 1.5f), 2, 5);

            if (DebrisPrefabs != null && DebrisPrefabs.Length > 0) SpawnYourDebris(count, b, mat);
            else if (NetGame.IsAuthority) SpawnCodeChunks(count, b, mat); // clients see the host's via MatterSnap

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
                var prefab = DebrisPrefabs[_rng.Next(0, DebrisPrefabs.Length)];
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
                    at = b.center + Vector3.Scale(NextInSphere(_rng), b.extents * 0.7f);
                    rot = prefab.transform.rotation; // authored orientation
                }

                var piece = Instantiate(prefab, at, rot);
                piece.name = $"{_pieceTag}#{i}";
                Element.Refile(piece.transform);

                // adopt, never dictate: authored rigidbody/collider/tag win
                var rb = Adopt.Component<Rigidbody>(piece, out bool madeRb);
                if (madeRb && piece.GetComponentInChildren<Collider>() == null)
                    Debug.LogWarning($"[SpellyZombie] Debris '{prefab.name}' has no collider: " +
                                     "it will fall through the world. Add one to the prefab.", prefab);
                rb.linearVelocity = NextOnSphere(_rng) * DebrisSpread + Vector3.up * (DebrisSpread * 0.5f);
                rb.angularVelocity = NextInSphere(_rng) * 4f;
                NetSync.TrackPropLater(rb); // the host's flight is the one the clients see

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
                var chunk = Matter.Spawn(mat, MatterPhase.Solid, Next(_rng, 0.1f, 0.17f),
                    b.center + Vector3.Scale(NextInSphere(_rng), b.extents * 0.7f));
                if (chunk == null) continue;
                if (blame != null) chunk.StampOwner(blame.Owner);
                if (chunk.TryGetComponent<Rigidbody>(out var rb))
                    rb.linearVelocity = NextOnSphere(_rng) * DebrisSpread + Vector3.up * 1.5f;
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
                s.transform.position = b.center + Vector3.Scale(NextInSphere(_rng), b.extents * 0.8f);
                s.transform.rotation = Quaternion.Euler(Next(_rng, 0f, 360f), Next(_rng, 0f, 360f), Next(_rng, 0f, 360f));
                s.transform.localScale = new Vector3(0.04f, Next(_rng, 0.1f, 0.24f), 0.04f);
                s.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(shard, MoteShade.Opaque);
                var srb = s.AddComponent<Rigidbody>();
                srb.mass = 0.05f;
                srb.linearVelocity = NextOnSphere(_rng) * Next(_rng, 2f, 4f) + Vector3.up * 2f;
                srb.angularVelocity = NextInSphere(_rng) * 8f;
                Destroy(s, Next(_rng, 3f, 5f));
            }
        }
    }
}
