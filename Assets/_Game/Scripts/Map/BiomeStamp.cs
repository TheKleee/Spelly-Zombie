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
        /// WHAT THIS THING WAS BORN AS. Stamped once and never re-read: a
        /// forest rock carried to the frozen peak keeps forest as its natural
        /// point, which is exactly why it suffers there. Thresholds are
        /// measured from here, not from wherever it happens to be standing.
        /// ★ THE ELEMENT'S, not a second copy. Element.Awake already composes
        /// what a thing was born as - itself plus its ground - and having this
        /// hold its own meant two answers to one question, with whichever ran
        /// last quietly winning.
        ///
        /// What survives here is the LOOK: turning those numbers into the
        /// colour a thing wears.
        public SpellPayload Natural
        {
            get => El != null ? El.Natural : _loose;
            set { if (El != null) El.Natural = value; else _loose = value; }
        }
        SpellPayload _loose;

        Element _el;
        Element El
        {
            get
            {
                if (_el == null) _el = GetComponent<Element>();
                if (_el == null && this != null) _el = gameObject.AddComponent<Element>();
                return _el;
            }
        }

        /// The medium it was born in, read off the state number like everywhere
        /// else - solid, liquid and gas are regions, never a stored label.
        public MatterPhase Phase
        {
            get => SpellPayload.PhaseOf(Natural.State);
            set { var n = Natural; n.State = value == MatterPhase.Solid ? 1f
                                          : value == MatterPhase.Liquid ? 0f : -1f;
                  Natural = n; }
        }

        // the old four, kept as views so nothing that reads them has to change
        public float Heat => Natural.Temp;
        public float Light => Natural.Lum;
        public float Density => Natural.Pressure;
        public float Stick => Natural.Balance;

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

            // ELEMENT.AWAKE ALREADY DID THE STAMPING - itself plus its ground,
            // capacities taking the lesser, its ceiling capped by the place.
            // Repeating it here applied the biome a second time and doubled
            // every offset the ground carried.
            var s = go.GetComponent<BiomeStamp>();
            if (s == null) s = go.AddComponent<BiomeStamp>();
            s.Stamped = true;
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
