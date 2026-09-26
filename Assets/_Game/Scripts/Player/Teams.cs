using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE THREE TEAMS (his rule): Wizard, Acolyte, and Neutral - the
    /// map/environment's own team. All three are against each other.
    public enum Team { Neutral, Wizard, Acolyte }

    /// THE ONE LABEL. Who belongs where: players by their side, zombies with
    /// the acolytes (by whoever raised them on a map with its own teams),
    /// golems/particles/matter by whoever made them, untouched world things
    /// Neutral. Every friend-or-foe question routes here.
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

            var zombie = thing.GetComponentInParent<Zombie>();
            if (zombie != null) return MapRules.Custom ? OfOwner(zombie.OwnerId) : Team.Acolyte;

            return OfOwner(OwnerPastZombie(thing, out _));
        }

        /// Who answers for a thing that is not a player, the owner its team comes from; -1 for the
        /// world. viaMinion: a zombie's deed, credited to its summoner through the minion. A golem
        /// stood up from a player's spell is that player's own.
        public static int OwnerOf(Component thing, out bool viaMinion)
        {
            viaMinion = false;
            if (thing == null) return -1;
            var zombie = thing.GetComponentInParent<Zombie>();
            if (zombie != null) { viaMinion = zombie.OwnerId >= 0; return zombie.OwnerId; }
            return OwnerPastZombie(thing, out viaMinion);
        }

        static int OwnerPastZombie(Component thing, out bool viaMinion)
        {
            viaMinion = false;
            var golem = thing.GetComponentInParent<Golem>();
            if (golem != null) return golem.OwnerId;

            var mote = thing.GetComponentInParent<SpellParticle>();
            if (mote != null) { viaMinion = mote.FromMinion; return mote.OwnerId; }

            var matter = thing.GetComponentInParent<Matter>();
            if (matter != null) return matter.TeamOwner;

            var el = thing.GetComponentInParent<Element>();
            return el != null ? el.Owner : -1;
        }

        /// Three teams, all against each other - different team = enemy. A map
        /// that puts wizards and acolytes together (MapRules) makes them one.
        public static bool Enemies(Team a, Team b) =>
            a != b && !(MapRules.Together && a != Team.Neutral && b != Team.Neutral);
        public static bool SameTeam(Component a, Component b) => Of(a) == Of(b);
    }
}
