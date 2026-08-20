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
        public static CollectionManager I { get; private set; }

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

        [Tooltip("Overrides for particles that are shaped differently. Name is the particle or " +
                 "fusion name - Flame, Lightning, Meteor, Plasma, Cloud - and anything not listed " +
                 "quietly uses the blob. Add rows as new combinations appear; no code changes.")]
        [SerializeField] ParticleShape[] _particleShapes;

        [System.Serializable]
        public struct ParticleShape
        {
            public string Name;
            public GameObject Prefab;
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
        public static GameObject ParticleShapeFor(string name)
        {
            if (I == null || I._particleShapes == null || string.IsNullOrEmpty(name)) return null;
            foreach (var s in I._particleShapes)
                if (s.Prefab != null && string.Equals(s.Name, name,
                        System.StringComparison.OrdinalIgnoreCase))
                    return s.Prefab;
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
