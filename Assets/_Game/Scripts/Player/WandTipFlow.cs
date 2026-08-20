using UnityEngine;

namespace SpellyZombie
{
    /// Three motes at the wand tip show ink flow: outward = losing, inward =
    /// gaining (green for acolytes). Contract: name a child "Tip" inside the
    /// wand; without one, the far end of the wand's bounds is measured.
    [RequireComponent(typeof(WandInk))]
    public class WandTipFlow : MonoBehaviour
    {
        const int Motes = 3;
        const float Travel = 0.055f;   // how far a mote runs, WORLD metres
        const float MoteSize = 0.014f; // WORLD metres, not affected by the wand's scale
        const float Cycle = 0.45f;     // seconds per loop

        Transform _tip;
        Transform[] _motes;
        Renderer[] _rends;
        SimpleFPSController _pilot;

        float _phase;
        float _rate;         // signed ink fraction per second, straight from WandInk
        float _shown;        // eased, so a single frame's jitter cannot flicker it
        Color _tint = Color.white;

        void Start()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == "Tip") { _tip = t; break; }

            if (_tip == null)
            {
                // fallback guess, measured in LOCAL space (world bounds break
                // at the wand's scale)
                Bounds local = new Bounds(Vector3.zero, Vector3.one * 0.1f);
                var mf = GetComponentInChildren<MeshFilter>();
                var smr = GetComponentInChildren<SkinnedMeshRenderer>();
                if (mf != null && mf.sharedMesh != null) local = mf.sharedMesh.bounds;
                else if (smr != null && smr.sharedMesh != null) local = smr.sharedMesh.bounds;

                // the far end along whichever local axis the wand is longest on
                Vector3 e = local.extents;
                Vector3 far = e.z >= e.x && e.z >= e.y ? new Vector3(0f, 0f, e.z)
                    : e.y >= e.x ? new Vector3(0f, e.y, 0f)
                    : new Vector3(e.x, 0f, 0f);

                var go = new GameObject("Tip");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = local.center + far;
                _tip = go.transform;

                Debug.LogWarning("[SpellyZombie] Wand has no child named \"Tip\", so the ink-flow " +
                    "motes are being placed by measuring the mesh — which will not be exactly the " +
                    "point of YOUR wand. Add an empty child called \"Tip\" at the wand's point and " +
                    "it is used instead, permanently.", this);
            }

            // the Tip is a pose reference only; motes live unparented in world
            // space so a scaled parent cannot shrink them
            _motes = new Transform[Motes];
            _rends = new Renderer[Motes];
            for (int i = 0; i < Motes; i++)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m.name = "TipMote";
                Destroy(m.GetComponent<Collider>());
                m.transform.localScale = Vector3.one * MoteSize;
                m.layer = gameObject.layer;   // layer 2: the pen must not hit it
                _motes[i] = m.transform;
                _rends[i] = m.GetComponent<Renderer>();
                m.SetActive(false);
            }

            _pilot = GetComponentInParent<SimpleFPSController>();
        }

        /// Called by WandInk with the ink fraction's rate of change per second.
        public void Report(float deltaPerSec) => _rate = deltaPerSec;

        void OnDestroy()
        {
            // unparented, so they do not die with the wand - clean them up
            if (_motes == null) return;
            foreach (var m in _motes) if (m != null) Destroy(m.gameObject);
        }

        void LateUpdate()
        {
            if (_motes == null || _tip == null) return;

            // eased so a frame of noise cannot strobe the motes
            _shown = Mathf.MoveTowards(_shown, _rate, Time.deltaTime * 1.5f);
            float mag = Mathf.Abs(_shown);
            bool on = mag > DrawingConfig.WandFlowDeadzone;

            for (int i = 0; i < Motes; i++)
                if (_motes[i] != null && _motes[i].gameObject.activeSelf != on)
                    _motes[i].gameObject.SetActive(on);
            if (!on) return;

            // the COLOURS: a wizard's ink is black, an acolyte's is corrupt
            // green. Asked per body so a second player's wand is right too.
            Color want = Sides.IsAcolytePlayer(_pilot)
                ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            if (want != _tint)
            {
                _tint = want;
                var mat = MatterFX.Get(want, MoteShade.Opaque);
                foreach (var r in _rends) if (r != null) r.sharedMaterial = mat;
            }

            // mote size scales with flow rate - it doubles as a hot-cold
            // compass to the pot, since refill speeds up as you near it
            float hot = Mathf.Clamp01(mag / DrawingConfig.WandFlowFullRate);
            float moteSize = Mathf.Lerp(DrawingConfig.WandMoteMin, DrawingConfig.WandMoteMax, hot);

            // cycle speeds up with flow strength
            _phase += Time.deltaTime / Mathf.Lerp(Cycle, Cycle * 0.45f, hot);
            if (_phase > 1f) _phase -= 1f;

            // read purely as a pose - where the point is and which way it faces
            Vector3 origin = _tip.position;
            Vector3 fwd = _tip.forward, right = _tip.right, up = _tip.up;

            for (int i = 0; i < Motes; i++)
            {
                if (_motes[i] == null) continue;
                float t = Mathf.Repeat(_phase + i / (float)Motes, 1f);

                // outward when losing, inward when gaining - same path backwards
                float along = _shown < 0f ? t : 1f - t;

                // three motes fan out on their own arcs
                float a = (i / (float)Motes) * Mathf.PI * 2f;
                // fan and run both grow with the flow
                float reach = Travel * Mathf.Lerp(0.55f, 1.35f, hot);
                Vector3 fan = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (reach * 0.45f * along);
                _motes[i].position = origin + fan + fwd * (reach * along);

                // fade out at the far end so they dissolve rather than vanish
                _motes[i].localScale = Vector3.one * (moteSize * (1f - along * 0.6f));
            }
        }
    }
}
