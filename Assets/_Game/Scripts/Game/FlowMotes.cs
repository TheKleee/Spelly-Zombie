using UnityEngine;

namespace SpellyZombie
{
    /// INK GOING SOMEWHERE, DRAWN THE SAME WAY EVERYWHERE .
    ///
    /// designed this language on the wand - motes flying OUT mean you are
    /// losing, motes flying IN mean you are gaining, and how BIG they are is how
    /// fast. It reads instantly, needs no text, and survives translation.
    ///
    /// The cauldron needs exactly the same sentence: "the green ink should have
    /// an evaporation effect when acolytes are not nearby and a regeneration
    /// effect when they are, to tell them what's happening." Same meaning, so
    /// the same visual - a player who learned it on their wand already knows it
    /// on the pot, which is worth more than either effect being individually
    /// prettier.
    ///
    /// Not a MonoBehaviour: it owns a handful of pooled spheres and is driven by
    /// whoever holds it, so a component can add flow motes without inheriting
    /// anything or spawning per-frame garbage.
    public class FlowMotes
    {
        readonly Transform[] _motes;
        readonly Renderer[] _rends;
        float _phase;
        Color _tint = Color.clear;

        public FlowMotes(int count, int layer)
        {
            _motes = new Transform[count];
            _rends = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m.name = "FlowMote";
                Object.Destroy(m.GetComponent<Collider>());
                m.layer = layer;
                _motes[i] = m.transform;
                _rends[i] = m.GetComponent<Renderer>();
                m.SetActive(false);
            }
        }

        public void Dispose()
        {
            foreach (var m in _motes)
                if (m != null) Object.Destroy(m.gameObject);
        }

        /// One frame. `rate` is signed: NEGATIVE flies outward (losing),
        /// POSITIVE flies inward (gaining). `spread` fans them around `dir`.
        public void Tick(Vector3 origin, Vector3 dir, float rate, Color colour,
            float reach, float spread, float fullRate, float minSize, float maxSize,
            float deadzone, float cycle)
        {
            float mag = Mathf.Abs(rate);
            bool on = mag > deadzone;
            for (int i = 0; i < _motes.Length; i++)
                if (_motes[i] != null && _motes[i].gameObject.activeSelf != on)
                    _motes[i].gameObject.SetActive(on);
            if (!on) return;

            if (colour != _tint)
            {
                _tint = colour;
                var mat = MatterFX.Get(colour, MoteShade.Opaque);
                foreach (var r in _rends) if (r != null) r.sharedMaterial = mat;
            }

            // HOW FAST DECIDES HOW BIG - the half of the language that turns a
            // direction into a measurement, and on the wand into a compass.
            float hot = Mathf.Clamp01(mag / Mathf.Max(0.0001f, fullRate));
            float size = Mathf.Lerp(minSize, maxSize, hot);

            _phase += Time.deltaTime / Mathf.Lerp(cycle, cycle * 0.45f, hot);
            if (_phase > 1f) _phase -= 1f;

            dir = dir.sqrMagnitude < 0.0001f ? Vector3.up : dir.normalized;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, dir);

            for (int i = 0; i < _motes.Length; i++)
            {
                if (_motes[i] == null) continue;
                float t = Mathf.Repeat(_phase + i / (float)_motes.Length, 1f);

                // the SAME path, run backwards when the flow reverses
                float along = rate < 0f ? t : 1f - t;

                float a = (i / (float)_motes.Length) * Mathf.PI * 2f;
                Vector3 fan = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (spread * along);
                _motes[i].position = origin + fan + dir * (reach * along);
                _motes[i].localScale = Vector3.one * (size * (1f - along * 0.6f));
            }
        }
    }
}
