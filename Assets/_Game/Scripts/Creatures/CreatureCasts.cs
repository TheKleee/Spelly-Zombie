using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHAT A CREATURE CASTS (its Can do list): every entry but Charge is a
    /// spell from the book, used in turn. A spell leaves its head; a summoning
    /// spell raises its creature in front of it, for the same team, within a
    /// budget the whole map shares so summoners cannot flood it.
    public static class CreatureCasts
    {
        static readonly List<GameObject> _raised = new List<GameObject>();
        const float SummonSpread = 50f; // degrees either side of the aim a summon may stand

        /// How far out a summon stands from this body: past its own edge, and a stride more.
        public static float ClearReach(Transform body)
        {
            Bounds b = ShapeShift.FindObjectBounds(body);
            return Mathf.Max(b.extents.x, b.extents.z) + 2f;
        }

        /// The spell whose turn it is, or null when it casts nothing.
        public static string Pick(List<string> abilities, int turn)
        {
            if (abilities == null) return null;
            int count = CastableCount(abilities);
            if (count == 0) return null;
            int want = ((turn % count) + count) % count;
            foreach (var a in abilities)
                if (Castable(a) && want-- == 0) return a;
            return null;
        }

        static bool Castable(string ability) => ability != Zombie.Charge && SpellBook.Live.Spell(ability) != null;

        /// How many of these it can cast (Charge is a move, not a cast).
        public static int CastableCount(List<string> abilities)
        {
            int count = 0;
            if (abilities != null) foreach (var a in abilities) if (Castable(a)) count++;
            return count;
        }

        /// Seconds until the next cast: raising a creature takes longer than a throw.
        public static float Cooldown(string spellName)
        {
            var def = SpellBook.Live.Spell(spellName);
            return def != null && def.IsSummon ? DrawingConfig.CreatureSummonCooldown : DrawingConfig.GooThrowCooldown;
        }

        /// Cast it for `owner`: a spell from `muzzle` along `aim`, a summon `reach` metres
        /// in front of `feet`. False when nothing came of it.
        public static bool Cast(string spellName, int owner, Vector3 muzzle, Vector3 aim, Vector3 feet, float reach,
            Transform caster = null)
        {
            var def = SpellBook.Live.Spell(spellName);
            if (def == null)
            {
                Debug.LogWarning($"[SpellyZombie] a creature wants to cast '{spellName}' but the book has no such spell.");
                return false;
            }
            if (def.IsSummon)
            {
                Vector3 flat = new Vector3(aim.x, 0f, aim.z);
                if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
                // clear of its body, somewhere in front, so the raised never stand on each other
                flat = Quaternion.Euler(0f, Random.Range(-SummonSpread, SummonSpread), 0f) * flat.normalized;
                return Summon(def, owner, feet + flat * reach);
            }

            var p = SpellParticle.Emit(ParticleKind.Push, muzzle, aim, 1.4f);
            if (p == null) return false;
            // thrown like a player's throw: it leaves through the caster and
            // its areas raise where it lands, never at the head
            p.PrimeToBlow(caster);
            var dart = MischiefLaw.KindOfRow(def.Name);
            if (MischiefLaw.IsDart(dart))
            {
                // an acolyte's dart: what it hits decides the spell, it carries no numbers
                p.Mischief = (byte)dart;
                p.WearRow(def);
                Juice.Sound(MischiefLaw.SoundOf(dart), muzzle, 0.45f, 1.35f); // leaving its head as a dart leaves a hand
            }
            // NOT Clamped(): the clamp floors Strength at 0, which stripped
            // the goo's -9 bite and stopped it ever fusing into Goo at all
            else p.Data = def.Payload;
            p.OwnerId = owner;
            p.FromMinion = true;
            p.SrcSize = DrawingConfig.RuneSizeMin * 2f;
            p.GrammarLevel = def.Level;   // a lvl2 hit lands on its whole area
            p.Vel = aim * 16f;
            p.Wake();
            p.RefreshIdentity_Public();
            return true;
        }

        // ----------------------------------------------------------- feeding --
        /// A sleeping spell touched a body: a summoned golem or zombie of the spell's side eats it.
        public static bool TryFeed(Collider body, SpellParticle meal)
        {
            if (body == null || meal == null) return false;
            var g = body.GetComponentInParent<Golem>();
            if (g != null) return g.TryEat(meal);
            var z = ZombieOwner.From(body);
            if (z == null) z = body.GetComponentInParent<Zombie>();
            return z != null && z.TryEat(meal);
        }

        /// Somebody's summon, a sleeping spell of the same side, on the machine that runs the world.
        /// Never one in somebody's hand: a held spell is the hand's (holding is stasis).
        public static bool CanFeed(int bodyOwner, SpellParticle meal) =>
            NetGame.IsAuthority && bodyOwner >= 0 && meal != null && !meal.Dead && meal.Dormant && meal.OwnerId >= 0
            && meal.Holder == null
            && !Teams.Enemies(Teams.OfOwner(bodyOwner), Teams.OfOwner(meal.OwnerId));

        /// ★ WHAT EATING DOES (his idea): the spells the meal was join what the body casts, on top of
        /// what it had, and the meal's numbers become its own nature and colour, so a golem that ate
        /// a flame is a fire being at home in its own heat. Returns what it learned, for the log.
        public static string Feed(List<string> abilities, Element body, StateView view, SpellParticle meal)
        {
            string learned = "";
            void Learn(string spell)
            {
                if (string.IsNullOrEmpty(spell)) return;
                if (!abilities.Contains(spell)) abilities.Add(spell);
                learned += (learned.Length > 0 ? " + " : "") + spell;
            }

            if (meal.Mischief != 0)
            {
                // a dart is its spell, not its numbers: every dart it carried
                for (int k = 1; k <= (int)MischiefKind.Transformation; k++)
                    if (k == meal.Mischief || (meal.MischiefMore & (1 << k)) != 0)
                        Learn(MischiefLaw.RowName((MischiefKind)k));
                return learned;
            }

            // the named combinations (the ones with an area); a bare rune when that is all it was
            bool named = false;
            foreach (var s in meal.Fusions) if (!s.IsSummon && (s.HasAoe || s.AnyBiome)) named = true;
            foreach (var s in meal.Fusions)
                if (!s.IsSummon && (!named || s.HasAoe || s.AnyBiome)) Learn(s.Name);

            if (body != null)
            {
                var food = meal.PayloadNow;
                var nature = body.Natural;
                var now = body.Data;
                for (int i = 0; i < 6; i++) { nature[i] += food[i]; now[i] += food[i]; } // a mote's heat is already a difference
                if (food.Strength > 0f) { nature.Strength += food.Strength; now.Strength += food.Strength; }
                float hp = now.Strength;
                body.Natural = nature.Clamped();
                now = now.Clamped();
                now.Strength = hp; // the clamp is not a heal and not a wound
                body.Data = now;
                if (view != null)
                {
                    view.Tint = Color.Lerp(view.Tint, food.Tint(), DrawingConfig.FedGolemTint);
                    view.DriveTint = true;
                }
            }
            return learned;
        }

        // ---------------------------------------------------------- rampages --
        static readonly HashSet<Object> _rampaging = new HashSet<Object>();

        /// A body says whether it rampages (on every Wear, and false when it goes).
        public static void MarkRampaging(Object body, bool on)
        {
            if (on) _rampaging.Add(body);
            else _rampaging.Remove(body);
        }

        /// ★ A CALAMITY (Rampages): a random spell from its list at a random spot
        /// on the ground around it, whoever is there. `next` is the body's clock.
        /// True when it cast.
        public static bool Rampage(List<string> abilities, int owner, Vector3 head, Transform body, ref float next,
            float every = -1f, float nearest = 2f)
        {
            if (Time.time < next) return false;
            next = Time.time + (every > 0f ? every : DrawingConfig.RampageCastEvery) * Random.Range(0.67f, 1.33f);
            int count = CastableCount(abilities);
            if (count == 0) return false;
            string spell = Pick(abilities, Random.Range(0, count));
            // aimed at the ground: a level throw runs out of speed in the air and hovers there
            float turn = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 spot = body.position + new Vector3(Mathf.Sin(turn), 0f, Mathf.Cos(turn))
                * Random.Range(nearest, Mathf.Max(nearest + 0.5f, DrawingConfig.RampageReach));
            Vector3 aim = spot - head;
            if (aim.sqrMagnitude < 0.01f) return false;
            aim.Normalize();
            return Cast(spell, owner, head + aim * 0.4f, aim, body.position, ClearReach(body), body);
        }

        /// ★ A CALAMITY WALKS THROUGH ITS OWN SIDE'S SPELLS (his ask): a rampaging
        /// body takes nothing from spells of its team, its own barrage included.
        public static bool Spares(Element body, int spellOwner)
        {
            if (_rampaging.Count == 0 || body == null) return false;
            int team;
            if (body.TryGetComponent(out Golem g) && _rampaging.Contains(g)) team = g.OwnerId;
            else if (body.TryGetComponent(out Zombie z) && _rampaging.Contains(z)) team = z.OwnerId;
            else return false;
            return !Teams.Enemies(Teams.OfOwner(spellOwner), Teams.OfOwner(team));
        }

        static bool Summon(SpellDef def, int owner, Vector3 spot)
        {
            var creature = SpellBook.Live.Creature(def.Creature);
            if (creature == null) return false;
            _raised.RemoveAll(g => g == null);
            if (_raised.Count >= DrawingConfig.CreatureSummonBudget) return false;
            // on the floor, never on the ink or inside a shell
            int floorMask = Physics.DefaultRaycastLayers
                & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);
            if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out var stand, 10f, floorMask, QueryTriggerInteraction.Ignore)
                && stand.normal.y > 0.55f)
                spot = stand.point + Vector3.up * 0.15f;
            var body = Spell.RaiseBody(creature, owner, spot, 1f);
            if (body == null) return false;
            _raised.Add(body);
            return true;
        }
    }
}
