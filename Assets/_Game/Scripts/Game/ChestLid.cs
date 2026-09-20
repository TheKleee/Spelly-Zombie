using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A chest opens once, by E, and stays open. What it holds is rolled the
    /// moment it opens, from a seed the host draws and hands to everyone:
    /// nothing, a rune, or the inside field's random items. The rune is one
    /// of his absorbables. Nothing sits in a closed chest.
    public class ChestLid : MonoBehaviour
    {
        [Tooltip("The lid bone or object that swings. Required.")]
        public Transform Lid;
        [Tooltip("Lid local rotation while closed, degrees.")]
        public Vector3 ClosedEuler = Vector3.zero;
        [Tooltip("Lid local rotation once open, degrees.")]
        public Vector3 OpenEuler = new Vector3(-122f, 0f, 0f);
        [Tooltip("Seconds the lid takes to swing.")]
        public float SwingSeconds = 0.35f;
        [Tooltip("How close the E offer stands.")]
        public float Range = 2.6f;

        [Header("WHAT IT HOLDS")]
        [Tooltip("The chest's inside Interior Field. Its Details are the random items. Required.")]
        public InteriorField Inside;
        [Tooltip("Absorbables (Prefabs/Absorbables) the chest may hold. One is drawn at random when the roll says rune.")]
        public GameObject[] Runes;
        [Tooltip("Chance the chest holds nothing at all.")]
        [Range(0f, 1f)] public float EmptyChance = 0.5f;
        [Tooltip("Chance the chest holds a rune. Whatever is left after Empty and Rune goes to random items from the Inside field.")]
        [Range(0f, 1f)] public float RuneChance = 0.25f;

        public enum Holding { Unrolled, Nothing, Rune, Items }
        public Holding Holds { get; private set; }

        public bool Open { get; private set; }

        /// The same on every machine: carved from where the chest was born.
        public int Id { get; private set; }

        static readonly Dictionary<int, ChestLid> _all = new Dictionary<int, ChestLid>();
        public static ChestLid Find(int id) => _all.TryGetValue(id, out var c) ? c : null;

        float _k; // 0 closed, 1 open
        GameObject _runePrefab;
        GameObject _rune;
        bool _revealing; // the inside field fills only inside Reveal

        void Awake()
        {
            Vector3 p = transform.position;
            Id = Element.IdFor($"chest:{Mathf.RoundToInt(p.x * 10f)}:{Mathf.RoundToInt(p.y * 10f)}:{Mathf.RoundToInt(p.z * 10f)}");
            if (Lid == null)
            {
                Debug.LogError($"[SpellyZombie] ChestLid on '{name}': Lid is EMPTY. Assign the lid bone (Chest_Top).", this);
                enabled = false;
            }
            if (Inside == null)
                Debug.LogError($"[SpellyZombie] ChestLid on '{name}': Inside is EMPTY. Assign the chest's Interior Field, or it can hold nothing.", this);
            if (RuneChance > 0f && (Runes == null || Runes.Length == 0))
                Debug.LogError($"[SpellyZombie] ChestLid on '{name}': Rune Chance is {RuneChance:0%} but Runes is EMPTY.", this);
        }

        void OnEnable() { _all[Id] = this; }
        void OnDisable() { if (Find(Id) == this) _all.Remove(Id); }

        /// The inside field asks before it fills. In the editor preview the
        /// chest rolls right there and shows the result; in the game the field
        /// waits for Reveal, which fills it with the host's seed.
        public bool Gate(System.Random rng, Transform preview)
        {
            if (preview != null) return Roll(rng, preview);
            return _revealing;
        }

        bool Roll(System.Random rng, Transform preview)
        {
            double r = rng.NextDouble();
            if (r < EmptyChance) Holds = Holding.Nothing;
            else if (r < EmptyChance + RuneChance)
            {
                _runePrefab = Runes != null && Runes.Length > 0 ? Runes[rng.Next(Runes.Length)] : null;
                if (_runePrefab == null)
                {
                    Debug.LogError($"[SpellyZombie] ChestLid on '{name}': the roll said rune but Runes has no prefab there. Holds nothing.", this);
                    Holds = Holding.Nothing;
                }
                else Holds = Holding.Rune;
            }
            else Holds = Holding.Items;

            if (preview != null)
            {
                string what = Holds == Holding.Rune ? $"a rune ({_runePrefab.name})" : Holds == Holding.Items ? "items" : "nothing";
                Debug.Log($"[ChestLid] {name} preview: holds {what}", this);
                if (Holds == Holding.Rune) Place(preview);
            }
            return Holds == Holding.Items;
        }

        /// The absorbable at the middle of the inside field, its own size under
        /// the chest's scaled root, carried along when the chest is lifted.
        GameObject Place(Transform parent)
        {
            Vector3 at = Inside.FieldToWorld.MultiplyPoint3x4(Inside.Center);
            var go = Instantiate(_runePrefab, at, SpellyMap.Facing(_runePrefab, 0f), parent);
            Vector3 ps = parent.lossyScale;
            go.transform.localScale = Vector3.Scale(_runePrefab.transform.localScale,
                new Vector3(1f / Mathf.Max(1e-4f, ps.x), 1f / Mathf.Max(1e-4f, ps.y), 1f / Mathf.Max(1e-4f, ps.z)));
            Element.Refile(go.transform);
            return go;
        }

        /// The local player opened it: offline it rolls here, online the host rolls.
        public void OpenByPlayer()
        {
            if (Open) return;
            NetSync.ChestOpen(this);
        }

        /// The roll and the swing, on every machine with the same seed.
        public void Reveal(int seed)
        {
            if (Open) return;
            Open = true;
            var rng = new System.Random(seed);
            if (Inside != null && Roll(rng, null) && Holds == Holding.Items)
            {
                _revealing = true;
                Inside.Fill(rng, null, false); // a reward, not a biome rider
                _revealing = false;
            }
            else if (Holds == Holding.Rune && Inside != null && _rune == null)
                _rune = Place(Inside.transform);
            if (!Juice.Sound(Sfx.Chest, transform.position)) Juice.Chime(transform.position);
            if (FxLibrary.I != null && Lid != null) FxLibrary.Spawn(FxLibrary.I.Poof, Lid.position);
        }

        // after the animation pass, so a clip keying the lid cannot hold it shut
        void LateUpdate()
        {
            if (Lid == null) return;
            float target = Open ? 1f : 0f;
            _k = Mathf.MoveTowards(_k, target, Time.deltaTime / Mathf.Max(0.01f, SwingSeconds));
            Lid.localRotation = Quaternion.Slerp(Quaternion.Euler(ClosedEuler), Quaternion.Euler(OpenEuler),
                Mathf.SmoothStep(0f, 1f, _k));
        }
    }
}
