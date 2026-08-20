using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A barrier that opens when a seal drawn on it contains every demanded
    /// rune. The demanded glyphs hover in front in the player's recorded
    /// handwriting. Opened gates sink into the ground.
    public class SpellLock : MonoBehaviour
    {
        [Tooltip("Every one of these runes must appear inside one seal drawn ON the gate.")]
        public RuneType[] Demands = { RuneType.HeatUp };

        [Tooltip("How far the gate sinks when it opens (0 = auto from its own height).")]
        public float SinkDepth = 0f;

        public bool Opened { get; private set; }

        public static readonly List<SpellLock> All = new List<SpellLock>();

        /// True when every gate in the scene has been spelled open.
        public static bool AllOpen
        {
            get
            {
                for (int i = 0; i < All.Count; i++)
                    if (All[i] != null && !All[i].Opened) return false;
                return true;
            }
        }

        public static int CountInScene => All.Count;

        readonly List<GameObject> _sigils = new List<GameObject>();
        Collider _col;
        float _bob;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            _col = GetComponentInChildren<Collider>();
            BuildSigils();
        }

        /// The demanded glyphs, floating in a row before the gate.
        void BuildSigils()
        {
            var center = _col != null ? _col.bounds.center : transform.position;
            float width = (Demands.Length - 1) * 0.42f;
            for (int i = 0; i < Demands.Length; i++)
            {
                var tex = Wardrobe.RuneIcon(Demands[i], new Color(1f, 0.45f, 0.15f, 0.95f));
                if (tex == null) continue;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "LockSigil_" + name;
                Destroy(quad.GetComponent<Collider>()); // never catches the pen
                // unparented on purpose: kit-piece parents carry import scale
                // fixes that would blow up localScale and skew the billboard.
                // Update() owns their world pose; Open() destroys them by hand.
                quad.transform.position = center + Vector3.up * 0.2f;
                quad.transform.localScale = Vector3.one * 0.34f;
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    quad.GetComponent<Renderer>().material =
                        new Material(shader) { mainTexture = tex };
                _sigils.Add(quad);
            }
        }

        void Update()
        {
            if (Opened || _sigils.Count == 0) return;

            // sigils hover, bob, and face the nearest player
            _bob += Time.deltaTime;
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var center = _col != null ? _col.bounds.center : transform.position;
            Vector3 toPlayer = p != null ? p.transform.position - center : Vector3.forward;
            toPlayer.y = 0f;
            var face = toPlayer.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(-toPlayer) : Quaternion.identity;
            Vector3 right = face * Vector3.right;

            float x0 = -(_sigils.Count - 1) * 0.21f;
            for (int i = 0; i < _sigils.Count; i++)
            {
                if (_sigils[i] == null) continue;
                _sigils[i].transform.position = center
                    + right * (x0 + i * 0.42f)
                    + Vector3.up * (0.25f + Mathf.Sin(_bob * 1.6f + i * 0.9f) * 0.05f)
                    + face * Vector3.forward * -0.35f; // a hand's width off the gate
                _sigils[i].transform.rotation = face;
            }
        }

        /// DrawingWorld calls this for every activated seal.
        public static void NotifySeal(Seal seal)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && !All[i].Opened) All[i].TrySeal(seal);
        }

        void TrySeal(Seal seal)
        {
            if (_col == null) return;
            var reach = _col.bounds;
            reach.Expand(0.6f); // ink sits a hair off the surface
            if (!reach.Contains(seal.PlaneOrigin)) return;

            // every demanded rune must be present in THIS seal
            foreach (var need in Demands)
            {
                bool found = false;
                foreach (var glyph in seal.Runes)
                    if (glyph.Rune == need) { found = true; break; }
                if (!found) return;
            }
            Open();
        }

        void Open()
        {
            Opened = true;
            foreach (var s in _sigils)
                if (s != null) Destroy(s);
            _sigils.Clear();

            var center = _col != null ? _col.bounds.center : transform.position;
            Juice.Boom(center, 0.7f);
            Juice.Shake(0.35f);
            ComboBanner.Show("THE GATE ACCEPTS", new Color(1f, 0.7f, 0.3f));
            StartCoroutine(Sink());
        }

        System.Collections.IEnumerator Sink()
        {
            float depth = SinkDepth > 0f ? SinkDepth
                : _col != null ? _col.bounds.size.y + 0.3f : 3f;
            Vector3 from = transform.position;
            Vector3 to = from + Vector3.down * depth;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 2.2f;
                transform.position = Vector3.Lerp(from, to, t * t); // accelerating grind
                yield return null;
            }
            // stays registered so the all-gates-open check still counts it
            if (_col != null) _col.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }
    }
}
