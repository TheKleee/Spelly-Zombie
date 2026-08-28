using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE THREE TEAMS (his rule): Wizard, Acolyte, and Neutral - the
    /// map/environment's own team. All three are against each other.
    public enum Team { Neutral, Wizard, Acolyte }

    /// THE ONE LABEL. Who belongs where: players by their side, zombies with
    /// the acolytes, golems/particles/matter by whoever made them, untouched
    /// world things Neutral. Every friend-or-foe question routes here.
    public static class Teams
    {
        public static Team OfOwner(int ownerId) =>
            ownerId < 0 ? Team.Neutral
            : Sides.IsAcolyte(ownerId) ? Team.Acolyte : Team.Wizard;

        public static Team Of(Component thing)
        {
            if (thing == null) return Team.Neutral;

            var pilot = thing.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
                return Sides.IsAcolytePlayer(pilot) ? Team.Acolyte : Team.Wizard;

            if (thing.GetComponentInParent<Zombie>() != null) return Team.Acolyte;

            var golem = thing.GetComponentInParent<Golem>();
            if (golem != null) return OfOwner(golem.OwnerId);

            var mote = thing.GetComponentInParent<SpellParticle>();
            if (mote != null) return OfOwner(mote.OwnerId);

            var matter = thing.GetComponentInParent<Matter>();
            if (matter != null) return OfOwner(matter.TeamOwner);

            var el = thing.GetComponentInParent<Element>();
            if (el != null) return OfOwner(el.Owner);

            return Team.Neutral;
        }

        /// Three teams, all against each other - different team = enemy.
        public static bool Enemies(Team a, Team b) => a != b;
        public static bool SameTeam(Component a, Component b) => Of(a) == Of(b);
    }
}
