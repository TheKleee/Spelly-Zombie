using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The one place prefabs are handed to code. Drop this on a GameObject in
    /// the scene and fill the slots by hand - nothing is searched for, nothing
    /// comes from Resources (which ships readable to everyone), and no
    /// code-built stand-in is ever substituted. An empty slot is a loud error
    /// the first time something asks for it, never a silent fallback.
    [DisallowMultipleComponent]
    public class CollectionManager : MonoBehaviour
    {
        static CollectionManager _i;

        /// FINDS ITSELF IN THE EDITOR TOO. Awake only runs in play mode, so
        /// anything asking outside it - the Spell Creator, any editor tool -
        /// got null and was told the slots were empty when they were full.
        /// It looks in the open scene when nobody has set it.
        public static CollectionManager I
        {
            get
            {
#if UNITY_EDITOR
                if (_i == null && !Application.isPlaying)
                    _i = Object.FindFirstObjectByType<CollectionManager>(FindObjectsInactive.Include);
#endif
                return _i;
            }
            private set => _i = value;
        }

        [Header("BODIES")]
        [Tooltip("The WHOLE player - controller, camera, scripts. Scenes do not need one sitting in them; every local player is spawned from this. A split-screen or co-op mode just asks for more.")]
        [SerializeField] GameObject _player;

        [Tooltip("The player body - ONE prefab for both sides. Side is worn, not modelled. This is the MODEL only: no controller, no camera.")]
        [SerializeField] GameObject _playerBody;

        [Tooltip("The zombie body. Moving this here is what gets it out of Resources.")]
        [SerializeField] GameObject _zombieBody;

        [Header("CREATURES")]
        [Tooltip("The golem: blob body with eyes on a bone. Every golem is this prefab.")]
        [SerializeField] GameObject _golem;

        [Header("PARTICLES")]
        [Tooltip("THE FALLBACK EVERY PARTICLE USES - the blob. Its bones can be posed into any " +
                 "shape, so most particles need nothing below.")]
        [SerializeField] GameObject _particleBlob;

        [Tooltip("Particles shaped differently from the blob. THE PREFAB'S NAME IS THE KEY - " +
                 "name the asset after the particle and drop it in, nothing else to fill. " +
                 "Anything not listed quietly uses the blob.")]
        [SerializeField] GameObject[] _particleShapes;

        [Header("GRIMOIRE PAGES")]
        [Tooltip("Every page the book can show, 1024x742. THE FILE'S NAME IS THE KEY - just drop " +
                 "the images in. The book asks for GrimoirePage_<Rune>, an optional " +
                 "GrimoirePage_<Rune>_Acolyte, and the loose GrimoirePage_Seal, _Absorb, _Scan, " +
                 "_Lesson, _Flip. " +
                 "Set here, these live with the game and show in EVERY scene including the lobby. " +
                 "A map may add its own for its own runes; those come and go with the map.")]
        [SerializeField] Texture2D[] _bookPages;

        /// The page art under this name, or null. Names are free text, so a
        /// rune invented later needs no enum and no code - just a row.
        public static Texture2D PageNamed(string name)
        {
            if (I == null || I._bookPages == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in I._bookPages)
                if (t != null && string.Equals(t.name, name,
                        System.StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() { if (I == this) I = null; }

        public static GameObject Player => Slot(I?._player, "Player");
        public static GameObject PlayerBody => Slot(I?._playerBody, "Player Body");
        public static GameObject ZombieBody => Slot(I?._zombieBody, "Zombie Body");
        public static GameObject Golem => Slot(I?._golem, "Golem");

        /// The blob every particle falls back to. Warns once if unset.
        public static GameObject ParticleBlob => Slot(I?._particleBlob, "Particle Blob");

        /// An authored shape for this particle, or null when it should just be
        /// a blob. Names are free text, so fusions and anything invented later
        /// need no enum and no code - just a row in the list.
        /// ★ THE SHAPE'S NUMBER ON THE WIRE. The list is an authored asset, so
        /// every machine running this build has it in the same order - which is
        /// what lets a client wear the same posed blob the host is wearing
        /// without sending a name for every particle, every snapshot.
        /// 255 = nothing authored, use the plain blob.
        public static byte ParticleShapeIndex(string name)
        {
            if (I == null || I._particleShapes == null || string.IsNullOrEmpty(name)) return 255;
            for (int i = 0; i < I._particleShapes.Length && i < 255; i++)
                if (I._particleShapes[i] != null && string.Equals(I._particleShapes[i].name, name,
                        System.StringComparison.OrdinalIgnoreCase))
                    return (byte)i;
            return 255;
        }

        /// What that number means here.
        public static GameObject ParticleShapeAt(byte index)
        {
            if (I == null || I._particleShapes == null || index == 255) return null;
            return index < I._particleShapes.Length ? I._particleShapes[index] : null;
        }

        /// Take a shape out of the list. Returns true if it was there.
        public bool RemoveParticleShape(GameObject go)
        {
            if (_particleShapes == null || go == null) return false;
            var list = new List<GameObject>(_particleShapes);
            bool had = list.Remove(go);
            if (had) _particleShapes = list.ToArray();
            return had;
        }

        /// Everything in the shape list, for the editor's library.
        public IEnumerable<GameObject> ParticleShapesAll =>
            _particleShapes ?? System.Array.Empty<GameObject>();

        public static GameObject ParticleShapeFor(string name)
        {
            if (I == null || I._particleShapes == null || string.IsNullOrEmpty(name)) return null;
            foreach (var go in I._particleShapes)
                if (go != null && string.Equals(go.name, name,
                        System.StringComparison.OrdinalIgnoreCase))
                    return go;
            return null;
        }

        /// What a particle should actually wear: its own shape if one is
        /// authored, otherwise the blob.
        public static GameObject ParticleBodyFor(string name)
        {
            var shaped = ParticleShapeFor(name);
            return shaped != null ? shaped : ParticleBlob;
        }

        /// One complaint per empty slot, naming what is missing and how to fix it.
        static GameObject Slot(GameObject prefab, string name)
        {
            if (prefab != null) return prefab;
            if (I == null)
            {
                if (_warned.Add("__none"))
                    Debug.LogError("[SpellyZombie] No CollectionManager in the scene. " +
                                   "Add one and fill its slots - prefabs are never loaded by path.");
                return null;
            }
            if (_warned.Add(name))
                Debug.LogError($"[SpellyZombie] CollectionManager: the '{name}' slot is empty. " +
                               "Drop the prefab in - nothing is built in code to cover for it.", I);
            return null;
        }

        static readonly HashSet<string> _warned = new HashSet<string>();
    }
}
