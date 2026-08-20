using UnityEngine;

namespace SpellyZombie
{
    /// What a particle IS: five signed axes and a phase. Runes push axes,
    /// combining ADDS payloads, and named spells are THRESHOLD REGIONS over
    /// these numbers - never a recipe lookup. SpellTable reads this and says
    /// what the numbers currently mean.
    [System.Serializable]
    public struct SpellPayload
    {
        public float Heat;      // + fire  - chill
        public float Lum;       // + light - dark
        public float Weight;    // + compress - spread
        public float Stick;     // + sticky - slick
        public float Affinity;  // + attract (arrow) - repel (Y)
        public int Phase;       // 0 none · 1 solid · 2 liquid · 3 gas

        public static SpellPayload operator +(SpellPayload a, SpellPayload b) => new SpellPayload
        {
            Heat = a.Heat + b.Heat,
            Lum = a.Lum + b.Lum,
            Weight = a.Weight + b.Weight,
            Stick = a.Stick + b.Stick,
            Affinity = a.Affinity + b.Affinity,
            // phases do not add; the reaction table owns phase change
            Phase = a.Phase != 0 ? a.Phase : b.Phase
        };

        public SpellPayload Scaled(float k) => new SpellPayload
        {
            Heat = Heat * k, Lum = Lum * k, Weight = Weight * k,
            Stick = Stick * k, Affinity = Affinity * k, Phase = Phase
        };

        /// One rune's push. THE ONLY rune-to-payload mapping in the game.
        public static SpellPayload Of(RuneType rune, float power = 1f)
        {
            var p = new SpellPayload();
            switch (rune)
            {
                case RuneType.HeatUp: p.Heat = power; break;
                case RuneType.HeatDown: p.Heat = -power; break;
                case RuneType.LuminanceUp: p.Lum = power; break;
                case RuneType.LuminanceDown: p.Lum = -power; break;
                case RuneType.DensityUp: p.Weight = power; break;
                case RuneType.DensityDown: p.Weight = -power; break;
                case RuneType.StickyUp: p.Stick = power; break;
                case RuneType.StickyDown: p.Stick = -power; break;
                case RuneType.DirectionAway: p.Affinity = power; break;   // attract: moves the target where it pointed
                case RuneType.DirectionToward: p.Affinity = -power; break;// repel: swaps the force to negative
                case RuneType.StateSolid: p.Phase = 1; break;
                case RuneType.StateLiquid: p.Phase = 2; break;
            }
            return p;
        }

        /// The axis that defines this payload right now - biggest deviation
        /// wins, which is also the order sources teach runes.
        public float Strongest =>
            Mathf.Max(Mathf.Abs(Heat), Mathf.Abs(Lum), Mathf.Abs(Weight),
                Mathf.Abs(Stick), Mathf.Abs(Affinity));

        /// His palette, blended by weight - the particle's colour IS its stats.
        public Color Tint()
        {
            Color sum = Color.black; float w = 0f;
            void Add(Color c, float amount)
            {
                float a = Mathf.Abs(amount);
                if (a < 0.05f) return;
                sum += c * a; w += a;
            }
            Add(Heat > 0f ? new Color(0.95f, 0.25f, 0.15f) : new Color(0.92f, 0.96f, 1f), Heat);
            Add(Lum > 0f ? new Color(1f, 0.93f, 0.35f) : new Color(0.08f, 0.07f, 0.10f), Lum);
            Add(Weight > 0f ? new Color(0.45f, 0.50f, 0.42f) : new Color(0.80f, 0.85f, 0.88f), Weight);
            Add(Stick > 0f ? new Color(0.85f, 0.60f, 0.15f) : new Color(0.78f, 0.72f, 0.95f), Stick);
            if (Phase == 2) Add(new Color(0.55f, 0.80f, 1f), 1f);
            return w > 0.05f ? sum / w : Color.white;
        }
    }
}
