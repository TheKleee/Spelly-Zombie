using UnityEngine;

namespace SpellyZombie
{
    /// THE WAND SAYS WHICH WAY ITS INK IS GOING (Marko Aug 10, with his sketch):
    /// "3 particles going away from the wand => deteriorating.
    ///  3 particles going towards the wand => regenerating.
    ///  Same but green effect for the acolyte."
    ///
    /// The wand already shrinks and grows, but SILENTLY — you had to watch its
    /// length for a second to work out which way it was going, and by then the
    /// thing you wanted to know (am I gaining or bleeding?) had already been
    /// decided for you. Three motes at the TIP answer it instantly, and the
    /// DIRECTION carries the whole meaning: outward is loss, inward is gain.
    /// No text, no bar, readable at a glance and in any language.
    ///
    /// MARKO'S CONTRACT, same shape as WandInk's: name a child "Tip" inside the
    /// wand and the motes play there. No "Tip" child and it measures the far end
    /// of the wand's own bounds instead — a fallback for geometry, not for a
    /// decision, so nothing he authored is being guessed at.
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
        float _dir;          // +1 gaining, -1 losing, 0 idle
        float _shown;        // eased, so a single frame's jitter cannot flicker it
        Color _tint = Color.white;

        void Start()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == "Tip") { _tip = t; break; }

            if (_tip == null)
            {
                // ⛔ A GUESS, AND IT SAYS SO. His art knows where the tip is and
                // this does not — the honest fix is one empty child, not better
                // maths. Measured in LOCAL space: the earlier version pushed a
                // WORLD bounds point through InverseTransformPoint, which at the
                // wand's scale threw the motes over a metre off the model.
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

            // ⛔ THE MOTES ARE NOT CHILDREN OF THE TIP (Marko Aug 11: "I added a
            // tip and the effect no longer exists").
            //
            // They used to be, which meant they inherited whatever scale the Tip
            // sat under. Parent it beneath a stretched Shaft — a cylinder at
            // 0.01 x 0.05 x 0.01, entirely normal for wand geometry — and motes
            // sized 0.012 come out at a ten-thousandth of a metre. Invisible.
            // His placement was correct; the code had no business depending on
            // where in the hierarchy he put it.
            //
            // So the Tip is read as a POSE REFERENCE ONLY — position and
            // forward — and the motes live unparented in world space, sized in
            // real metres. Put the Tip anywhere, under anything, at any scale.
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

        /// Called by WandInk, which already knows the fraction and its change.
        public void Report(float deltaPerSec)
        {
            _dir = Mathf.Abs(deltaPerSec) < 0.01f ? 0f : Mathf.Sign(deltaPerSec);
        }

        void OnDestroy()
        {
            // unparented, so they do not die with the wand — clean them up
            if (_motes == null) return;
            foreach (var m in _motes) if (m != null) Destroy(m.gameObject);
        }

        void LateUpdate()
        {
            if (_motes == null || _tip == null) return;

            // eased so a frame of noise cannot strobe the motes
            _shown = Mathf.MoveTowards(_shown, _dir, Time.deltaTime * 6f);
            bool on = Mathf.Abs(_shown) > 0.15f;

            for (int i = 0; i < Motes; i++)
                if (_motes[i] != null && _motes[i].gameObject.activeSelf != on)
                    _motes[i].gameObject.SetActive(on);
            if (!on) return;

            // HIS COLOURS: a wizard's ink is black, an acolyte's is corrupt
            // green. Asked per body so a second player's wand is right too.
            Color want = Sides.IsAcolytePlayer(_pilot)
                ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            if (want != _tint)
            {
                _tint = want;
                var mat = MatterFX.Get(want, MoteShade.Opaque);
                foreach (var r in _rends) if (r != null) r.sharedMaterial = mat;
            }

            _phase += Time.deltaTime / Cycle;
            if (_phase > 1f) _phase -= 1f;

            // read purely as a pose — where the point is and which way it faces
            Vector3 origin = _tip.position;
            Vector3 fwd = _tip.forward, right = _tip.right, up = _tip.up;

            for (int i = 0; i < Motes; i++)
            {
                if (_motes[i] == null) continue;
                float t = Mathf.Repeat(_phase + i / (float)Motes, 1f);

                // OUTWARD when losing, INWARD when gaining — the direction IS
                // the message, so it simply runs the same path backwards.
                float along = _shown < 0f ? t : 1f - t;

                // fanned like his sketch: three motes leaving on their own arcs
                float a = (i / (float)Motes) * Mathf.PI * 2f;
                Vector3 fan = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (Travel * 0.45f * along);
                _motes[i].position = origin + fan + fwd * (Travel * along);

                // fade out at the far end so they dissolve rather than vanish
                _motes[i].localScale = Vector3.one * (MoteSize * (1f - along * 0.6f));
            }
        }
    }
}
