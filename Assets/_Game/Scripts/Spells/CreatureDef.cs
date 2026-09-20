using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ A CREATURE (the Creature Creator). What stands up: its body, size and
    /// shape, what it is born as, how it lives, what it can do and how it
    /// looks. A summoning spell, a map placement or another creature's cast
    /// raises it, and whoever raised it decides the team it fights for.
    [Serializable]
    public class CreatureDef
    {
        public string Name = "New creature";
        /// Golem or Zombie.
        public SpellBody Body = SpellBody.Golem;
        /// Times the body's own size, before a seal or a placement scales it.
        public float Size = 1f;

        /// What it is BORN AS, in human units: its natural state, which it
        /// drifts from like anything else in the world. Strength is its health.
        public int[] Axis = new int[SpellPayload.AxisCount];

        /// Charge is the one move; every other entry is a spell from the book
        /// it casts, a summoning spell included.
        public List<string> Abilities = new List<string>();
        /// The clip a move plays at its moment. Empty = the body's built-in tell.
        public List<MoveAnim> MoveAnims = new List<MoveAnim>();

        /// The area a golem drops around itself as it walks.
        public string Aoe = "";
        /// The state material's sliders.
        public SpellTable.Look Skin;

        public CreatureBehaviour Behaviour = CreatureBehaviour.Roams;
        /// For Guards: how far from where it stood it goes after someone, in metres.
        public float GuardRange = 10f;
        /// Everyone sees its health; a counting environment team stands while one lives (BossMark).
        public bool Boss;

        /// A golem's shape: where its handles were dragged to. Empty = the plain blob.
        public List<BonePose> Pose = new List<BonePose>();
        /// A zombie body's shape, 1 = as built.
        public float Height = 1f, Width = 1f, Head = 1f, Arms = 1f, Legs = 1f;

        public const float SizeMin = 0.2f, SizeMax = 6f, BuildMin = 0.5f, BuildMax = 2f;

        public bool HasAoe => !string.IsNullOrEmpty(Aoe);

        /// The authored units as world numbers, the way a spell's are.
        public SpellPayload Payload
        {
            get
            {
                var p = new SpellPayload();
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    p[i] = SpellPayload.FromHuman(i, Axis[i]);
                return p;
            }
        }

        public AnimationClip MoveClip(string move)
        {
            foreach (var m in MoveAnims)
                if (m.Move == move) return m.Clip;
            return null;
        }

        /// A body has its own colour and the numbers only shade it.
        public Color PreviewTint()
        {
            var pay = Payload;
            return pay.Strongest > 0.05f
                ? Color.Lerp(SpellDef.BaseSkin(Body), pay.Tint(), DrawingConfig.BiomeTintStrength)
                : SpellDef.BaseSkin(Body);
        }

        /// Whatever an older or hand-edited file left short or out of range.
        public void Repair()
        {
            Axis = SpellBook.Fit(Axis);
            if (Abilities == null) Abilities = new List<string>();
            if (MoveAnims == null) MoveAnims = new List<MoveAnim>();
            if (Pose == null) Pose = new List<BonePose>();
            if (Aoe == null) Aoe = "";
            if (Body == SpellBody.Particle) Body = SpellBody.Golem;
            if (Size <= 0f) Size = 1f;
            Size = Mathf.Clamp(Size, SizeMin, SizeMax);
            if (GuardRange < 3f) GuardRange = 10f;
            GuardRange = Mathf.Min(GuardRange, 40f);
            Height = Build(Height);
            Width = Build(Width);
            Head = Build(Head);
            Arms = Build(Arms);
            Legs = Build(Legs);
        }

        static float Build(float v) => v <= 0f ? 1f : Mathf.Clamp(v, BuildMin, BuildMax);
    }
}
