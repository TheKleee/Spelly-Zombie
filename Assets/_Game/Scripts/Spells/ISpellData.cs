using UnityEngine;

namespace SpellyZombie
{
    /// THE DATA CONTAINER. A biome carries it, a spell carries it, and so does
    /// every destructible thing standing in the world - players, creatures,
    /// map objects. Terrain does not: it cannot be destroyed.
    ///
    /// There is no spell dispatch anywhere. A thing holds numbers, the numbers
    /// change, and the thing changes with them. Things burn because they are
    /// hot, not because something cast fire on them.
    public interface ISpellData
    {
        /// What it was BORN as, stamped once from its spawn biome. Capacities
        /// are measured against this, so a naturally fearless thing is not the
        /// same as a coward standing somewhere safe.
        SpellPayload Natural { get; }

        /// What it is NOW.
        SpellPayload Data { get; set; }

        /// WHO IS RESPONSIBLE for what this thing currently is. -1 = nobody.
        /// Rides along every transfer, including spreading, so a fire that
        /// jumps three fences still credits the wizard who lit the first one.
        /// His rule: "all spells carry who the owner is even when they spread
        /// so that we can tell who killed who."
        int Owner { get; set; }

        /// Where it is standing. MonoBehaviour satisfies this for free.
        Transform transform { get; }
    }

    /// THE ONE TICK, written once so nobody grows their own version.
    /// Sample where you are, drift toward it, take what touches you.
    /// What the numbers then MEAN is read off the threshold set, never
    /// looked up and dispatched.
    public static class SpellLaw
    {
        /// What is natural at this thing's feet: the map biome, plus every
        /// artificial biome covering the point (additive, by ruling).
        /// No map at all - the lobby - imposes nothing and caps nothing, so
        /// you are simply yourself.
        public static SpellPayload Here(ISpellData thing)
        {
            Vector3 at = thing.transform.position;
            // EVERY biome you are standing in, added together. Overlaps make a
            // new place all by themselves - nothing authors the intersection.
            // BiomeAt picks one winner by layer, which is right for terrain and
            // wrong for what the air is like.
            var composite = Biome.CompositeAt(at, out bool inAny);
            var ground = inAny ? composite : thing.Natural;
            // map biomes, spell-made biomes, and lvl3 PARTICLES - all three
            // are just places that impose, and they add
            return ground + ArtificialBiome.SampleAt(at) + SpellParticle.SampleAt(at);
        }

        /// THE AXES ARE NOT ON ONE SCALE. Temp is carried in degrees - a spark
        /// is 25, a room is 18, wood ignites in the hundreds - while every
        /// other axis is carried in units, where a light mote is 1. A single
        /// drift rate would erase a light in a fifth of a second and barely
        /// cool a spark, so each axis relaxes at its own scale's rate.
        /// The degree rate is the one Matter already relaxes at, so a mote and
        /// a crate cool at the same speed - one phenomenon, one number.
        public static float RateFor(int axis) => axis == 0
            ? DrawingConfig.AmbientDriftPerSec * 0.4f   // degrees per second
            : DrawingConfig.CapacityDriftPerSec;        // units per second

        /// Move toward what the place says. Imposed axes take the place's word
        /// past whatever is natural for you - that is how burning and freezing
        /// happen at all; capacities take the lesser. SpellPayload.TargetFor is
        /// the only thing that knows the difference.
        public static void Drift(ISpellData thing, float dt)
        {
            var natural = thing.Natural;
            var here = Here(thing);
            var d = thing.Data;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                // STRENGTH DOES NOT DRIFT. Drifting it back toward natural IS
                // regeneration, and that is its own system with its own rate
                // (the biome's RegenScale, and "the lower your maximum the
                // faster you recover"). Left in here it would quietly heal
                // everything - walls included - a quarter point a second, and
                // undo damage as fast as fire could deal it.
                if (i == 6) continue;
                d[i] = Mathf.MoveTowards(d[i],
                    SpellPayload.TargetFor(i, natural[i], here[i]), RateFor(i) * dt);
            }
            thing.Data = d.Clamped();
        }

        /// CONTACT. Everything that is not a particle does this: it keeps its
        /// own body and takes the numbers in. A zombie burns for exactly the
        /// same reason a crate does.
        public static void Absorb(ISpellData taker, ISpellData food, float share = 1f)
        {
            taker.Data = (taker.Data + food.Data.Scaled(share)).Clamped();
            // BLAME TRAVELS WITH THE NUMBERS. Whoever caused this push owns
            // what it does next - that is what makes a spread kill attributable
            // instead of dissolving into "the fire did it".
            if (food.Owner >= 0) taker.Owner = food.Owner;
        }
    }
}
