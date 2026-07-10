using UnityEngine;

namespace SpellyZombie
{
    /// Physical identity of a material — the "ingredient" half of spell
    /// resolution. Objects are marked with SurfaceMaterialTag; anything unmarked
    /// resolves to Unknown (conjures Stone / Water by default).
    ///
    /// The whole Noita-style reaction table lives in these numbers:
    ///   Liquid rune on X  → X's liquid form (Stone→Lava, Flesh→Blood, Coal→Oil…)
    ///   Heat past Ignite  → burns (Wood burns down to Coal)
    ///   Heat past Melt    → melts into the liquid form
    ///   Liquid cooled     → re-solidifies (lava→stone) / freezes (water→ice)
    ///   Density-up long enough → transmutes into DenserForm (Coal→Diamond)
    ///   Density-down on solid  → fragments into smaller pieces of the same stuff
    public struct MaterialInfo
    {
        public SurfaceMaterialType Type;
        public bool Flammable;              // catches fire at IgnitePoint
        public bool Meltable;               // becomes LiquidColor matter at MeltPoint
        public float IgnitePoint;           // °C — where a flammable solid starts burning
        public float MeltPoint;             // °C — where a meltable solid liquefies
        public float BoilPoint;             // °C — where its liquid becomes gas
        public float HeatCapacity;          // resistance to temperature change (bigger = slower)
        public float BaseStickiness;        // friction/clumping its matter starts with (oil ~0 = slick)
        public float Strength;              // toughness/damage multiplier of conjured blocks
        public Color SolidColor;
        public Color LiquidColor;
        public string LiquidName;           // what the liquid form is called ("Lava", "Blood", "Oil")
        public SurfaceMaterialType DenserForm; // Density-up transmutation target (== Type when maxed out)
    }

    public static class SurfaceMaterialDB
    {
        /// Which material a collider is made of — walks up for a SurfaceMaterialTag,
        /// falls back to Unknown when nothing is marked.
        public static SurfaceMaterialType Resolve(Collider col)
        {
            if (col == null) return SurfaceMaterialType.Unknown;
            var tag = col.GetComponentInParent<SurfaceMaterialTag>();
            return tag != null ? tag.Material : SurfaceMaterialType.Unknown;
        }

        public static SurfaceMaterialType Resolve(GameObject go)
        {
            if (go == null) return SurfaceMaterialType.Unknown;
            var tag = go.GetComponentInParent<SurfaceMaterialTag>();
            return tag != null ? tag.Material : SurfaceMaterialType.Unknown;
        }

        public static MaterialInfo Info(SurfaceMaterialType type)
        {
            switch (type)
            {
                case SurfaceMaterialType.Stone: // → Lava when liquid; compresses to Diamond
                    return new MaterialInfo { Type = type, Meltable = true, MeltPoint = 700f, BoilPoint = 2000f,
                        HeatCapacity = 1.4f, BaseStickiness = 0.35f, Strength = 1.2f,
                        SolidColor = new Color(0.5f, 0.5f, 0.55f), LiquidColor = new Color(1f, 0.35f, 0.05f, 0.95f),
                        LiquidName = "Lava", DenserForm = SurfaceMaterialType.Diamond };

                case SurfaceMaterialType.Wood: // burns down to Coal; liquid form is sticky Sap
                    return new MaterialInfo { Type = type, Flammable = true, IgnitePoint = 200f, BoilPoint = 400f,
                        HeatCapacity = 0.8f, BaseStickiness = 0.4f, Strength = 0.8f,
                        SolidColor = new Color(0.6f, 0.4f, 0.2f), LiquidColor = new Color(0.85f, 0.6f, 0.15f, 0.85f),
                        LiquidName = "Sap", DenserForm = SurfaceMaterialType.Coal };

                case SurfaceMaterialType.Earth: // → Mud when liquid; compresses to Stone (→ Diamond)
                    return new MaterialInfo { Type = type, Meltable = true, MeltPoint = 900f, BoilPoint = 2000f,
                        HeatCapacity = 1.2f, BaseStickiness = 0.55f, Strength = 0.7f,
                        SolidColor = new Color(0.5f, 0.4f, 0.28f), LiquidColor = new Color(0.42f, 0.3f, 0.18f, 0.9f),
                        LiquidName = "Mud", DenserForm = SurfaceMaterialType.Stone };

                case SurfaceMaterialType.Metal: // melts bright; compresses to Gold (alchemy!)
                    return new MaterialInfo { Type = type, Meltable = true, MeltPoint = 900f, BoilPoint = 2500f,
                        HeatCapacity = 2.2f, BaseStickiness = 0.25f, Strength = 1.5f,
                        SolidColor = new Color(0.7f, 0.72f, 0.8f), LiquidColor = new Color(1f, 0.55f, 0.15f, 0.95f),
                        LiquidName = "Molten Metal", DenserForm = SurfaceMaterialType.Gold };

                case SurfaceMaterialType.Water: // freezes to Ice, boils to Steam; compresses to Slime
                    return new MaterialInfo { Type = type, BoilPoint = 100f,
                        HeatCapacity = 3.0f, BaseStickiness = 0.25f, Strength = 0.3f,
                        SolidColor = new Color(0.72f, 0.88f, 1f), LiquidColor = new Color(0.3f, 0.55f, 0.95f, 0.7f),
                        LiquidName = "Water", DenserForm = SurfaceMaterialType.Slime };

                case SurfaceMaterialType.Flesh: // liquid form is Blood; compresses to Bone
                    return new MaterialInfo { Type = type, Flammable = true, IgnitePoint = 150f, BoilPoint = 150f,
                        HeatCapacity = 1.0f, BaseStickiness = 0.45f, Strength = 0.6f,
                        SolidColor = new Color(0.7f, 0.3f, 0.3f), LiquidColor = new Color(0.5f, 0.05f, 0.05f, 0.9f),
                        LiquidName = "Blood", DenserForm = SurfaceMaterialType.Bone };

                case SurfaceMaterialType.Coal: // burns long & hot; liquid form is slick flammable Oil; compresses to Diamond
                    return new MaterialInfo { Type = type, Flammable = true, IgnitePoint = 250f, BoilPoint = 350f,
                        HeatCapacity = 1.1f, BaseStickiness = 0.3f, Strength = 0.9f,
                        SolidColor = new Color(0.12f, 0.12f, 0.13f), LiquidColor = new Color(0.08f, 0.07f, 0.05f, 0.95f),
                        LiquidName = "Oil", DenserForm = SurfaceMaterialType.Diamond };

                case SurfaceMaterialType.Diamond: // endpoint — nearly indestructible, worth clipping
                    return new MaterialInfo { Type = type, BoilPoint = 4000f,
                        HeatCapacity = 4f, BaseStickiness = 0.15f, Strength = 3f,
                        SolidColor = new Color(0.75f, 0.95f, 1f), LiquidColor = new Color(0.75f, 0.95f, 1f, 0.8f),
                        LiquidName = "Diamond", DenserForm = SurfaceMaterialType.Diamond };

                case SurfaceMaterialType.Gold: // soft, heavy, melts easily — endpoint
                    return new MaterialInfo { Type = type, Meltable = true, MeltPoint = 500f, BoilPoint = 2000f,
                        HeatCapacity = 1.6f, BaseStickiness = 0.3f, Strength = 0.8f,
                        SolidColor = new Color(1f, 0.78f, 0.1f), LiquidColor = new Color(1f, 0.65f, 0.1f, 0.95f),
                        LiquidName = "Molten Gold", DenserForm = SurfaceMaterialType.Gold };

                case SurfaceMaterialType.Bone: // brittle — fragments readily — endpoint
                    return new MaterialInfo { Type = type, BoilPoint = 1500f,
                        HeatCapacity = 1.2f, BaseStickiness = 0.35f, Strength = 0.5f,
                        SolidColor = new Color(0.92f, 0.89f, 0.8f), LiquidColor = new Color(0.8f, 0.75f, 0.6f, 0.9f),
                        LiquidName = "Marrow", DenserForm = SurfaceMaterialType.Bone };

                case SurfaceMaterialType.Slime: // very sticky gel — endpoint
                    return new MaterialInfo { Type = type, BoilPoint = 200f,
                        HeatCapacity = 2f, BaseStickiness = 0.9f, Strength = 0.4f,
                        SolidColor = new Color(0.35f, 0.85f, 0.3f), LiquidColor = new Color(0.3f, 0.8f, 0.25f, 0.75f),
                        LiquidName = "Slime", DenserForm = SurfaceMaterialType.Slime };

                default: // Unknown — neutral; conjuring resolves it to Stone / Water
                    return new MaterialInfo { Type = SurfaceMaterialType.Unknown, Meltable = true, MeltPoint = 800f, BoilPoint = 2000f,
                        HeatCapacity = 1.2f, BaseStickiness = 0.35f, Strength = 1f,
                        SolidColor = new Color(0.6f, 0.6f, 0.6f), LiquidColor = new Color(0.5f, 0.5f, 0.5f, 0.85f),
                        LiquidName = "Sludge", DenserForm = SurfaceMaterialType.Stone };
            }
        }
    }
}
