using UnityEngine;

namespace SpellyZombie
{
    /// What the ground made you. Anything born in a biome carries that place's
    /// payload for life - a zombie raised on the peak stays a frost thing even
    /// when it walks into a desert, and dies there.
    /// One payload, three jobs: what it RESISTS, what it RADIATES, and how it
    /// LOOKS. Ambient drift is separate - that is where you are standing NOW.
    public class BiomeStamp : MonoBehaviour
    {
        public float Heat, Light, Density, Stick;
        public MatterPhase Phase = MatterPhase.Solid;

        /// True once a biome actually wrote this; unstamped things are natural.
        public bool Stamped { get; private set; }

        /// Stamp from wherever this thing was born. Safe to call anywhere -
        /// with no generated map (the lobby) it does nothing and the thing
        /// stays natural.
        public static BiomeStamp Apply(GameObject go, Vector3 at)
        {
            if (go == null) return null;
            var b = SpellyMap.BiomeAt(at);
            if (b == null) return null;

            var s = go.GetComponent<BiomeStamp>();
            if (s == null) s = go.AddComponent<BiomeStamp>();
            s.Heat = b.HeatOffset;
            s.Light = b.LightOffset;
            s.Density = b.DensityOffset;
            s.Stick = b.StickOffset;
            s.Phase = b.NaturalPhase;
            s.Stamped = true;

            // the ground's ceiling caps what it raised, same rule as bodies
            var dmg = go.GetComponent<Damageable>();
            if (dmg != null && b.StrengthCap > 0f)
            {
                dmg.MaxStrength = Mathf.Min(
                    dmg.MaxStrength > 0f ? dmg.MaxStrength : dmg.Health, b.StrengthCap);
                dmg.Health = Mathf.Min(dmg.Health, dmg.MaxStrength);
            }

            s.Show();
            return s;
        }

        /// How strongly this thing disagrees with an ambient value on one axis.
        /// 0 = at home here, 1 = as wrong as it gets. Match protects, mismatch
        /// bites - "a peak rock melts near the volcano".
        public float Mismatch(float mine, float ambient, float scale) =>
            Mathf.Clamp01(Mathf.Abs(mine - ambient) / Mathf.Max(0.01f, scale));

        /// Resistance to an axis, always PARTIAL - a shape never hard-counters
        /// a spell school (his rule: helpful, never immunity).
        public float ResistanceTo(float axisAmount, float mine)
        {
            if (!Stamped) return 0f;
            // carrying the same sign as the incoming push means you are used to it
            float agree = Mathf.Sign(axisAmount) == Mathf.Sign(mine) ? Mathf.Abs(mine) : 0f;
            return Mathf.Clamp01(agree / Mathf.Max(0.01f, DrawingConfig.ResistFullAt))
                * DrawingConfig.ResistMaxCut;
        }

        /// Phase through the shared StateView. Colour is NOT written here when
        /// something already paints itself (SummonedZombie): that painter asks
        /// for Shift() instead, so one writer owns body colour.
        public void Show()
        {
            var view = GetComponent<StateView>();
            if (view == null) view = gameObject.AddComponent<StateView>();
            view.Set(Phase);

            if (GetComponent<SummonedZombie>() != null) return; // it paints itself
            view.Tint = Shift(Color.white);
            view.DriveTint = true;
        }

        /// The biome's pull on a colour, BOUNDED - the base survives, so a
        /// ranged zombie still reads as ranged after a frost biome pales it.
        public Color Shift(Color baseColor) =>
            Stamped ? Color.Lerp(baseColor, AxisColor(), DrawingConfig.BiomeTintStrength)
                    : baseColor;

        /// His palette, weighted by how far each axis actually moved.
        Color AxisColor()
        {
            Color sum = Color.black;
            float w = 0f;
            void Add(Color c, float amount)
            {
                float a = Mathf.Abs(amount);
                if (a < 0.001f) return;
                sum += c * a; w += a;
            }
            Add(Heat > 0f ? Warm : Cold, Heat);
            Add(Light > 0f ? Bright : Dark, Light);
            Add(Density > 0f ? Heavy : Airy, Density);
            Add(Stick > 0f ? Tacky : Slippy, Stick);
            if (Phase == MatterPhase.Liquid) Add(Wet, 1f);
            return w > 0.001f ? sum / w : Color.white;
        }

        static readonly Color Warm = new Color(0.95f, 0.25f, 0.15f);  // heat up
        static readonly Color Cold = new Color(0.92f, 0.96f, 1f);     // heat down
        static readonly Color Bright = new Color(1f, 0.93f, 0.35f);   // luminance
        static readonly Color Dark = new Color(0.08f, 0.07f, 0.10f);  // darkness
        static readonly Color Heavy = new Color(0.45f, 0.50f, 0.42f); // solid/dense
        static readonly Color Airy = new Color(0.80f, 0.85f, 0.88f);
        static readonly Color Tacky = new Color(0.85f, 0.60f, 0.15f); // sticky: amber
        static readonly Color Slippy = new Color(0.78f, 0.72f, 0.95f);// slick: lilac
        static readonly Color Wet = new Color(0.55f, 0.80f, 1f);      // liquid
    }
}
