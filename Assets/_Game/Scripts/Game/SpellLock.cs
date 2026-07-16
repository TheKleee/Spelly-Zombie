using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE FABLE GATE (Marko's design): a barrier that demands a SPECIFIC
    /// spell — draw a seal ON the gate containing every demanded rune and it
    /// opens. The demanded glyphs hover in front of it in the player's own
    /// handwriting (no words — the localization rule), so the door SHOWS you
    /// the spell it hungers for. Zombies drop rune cards, so a player who
    /// lacks the runes farms the horde until it coughs them up.
    ///
    /// MARKO'S SIDE: put this component on any blocking object (door, rubble,
    /// portcullis — anything with a collider so it's drawable), pick the
    /// demanded runes in the inspector. Opened gates sink into the ground.
    /// When a map contains gates, the DEMO WIN becomes: open them all, then
    /// clear the round (RoundDirector checks AllOpen).
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

        /// The demanded glyphs, floating in a row before the gate — dim ember
        /// sigils in the player's own recorded handwriting.
        void BuildSigils()
        {
            var center = _col != null ? _col.bounds.center : transform.position;
            float width = (Demands.Length - 1) * 0.42f;
            for (int i = 0; i < Demands.Length; i++)
            {
                var tex = Wardrobe.RuneIcon(Demands[i], new Color(1f, 0.45f, 0.15f, 0.95f));
                if (tex == null) continue;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "LockSigil";
                Destroy(quad.GetComponent<Collider>()); // never catches the pen
                quad.transform.SetParent(transform, true);
                quad.transform.position = center + Vector3.up * 0.2f
                    + Vector3.right * 0f; // laid out in Bob() facing the viewer
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

            // sigils hover, bob, and face the nearest player — readable from
            // whichever side the wizard approaches
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

        /// DrawingWorld calls this for every activated seal — each gate checks
        /// "was that ON me, and does it contain everything I demand?"
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
                if (!found) return; // the gate stays hungry
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
            // gone from play, but the component stays REGISTERED so the
            // all-gates-open win check still counts it
            if (_col != null) _col.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }
    }
}
