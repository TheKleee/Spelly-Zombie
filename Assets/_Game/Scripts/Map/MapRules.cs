using System.Collections.Generic;

namespace SpellyZombie
{
    /// ★ WHAT THE STANDING MAP SAYS ABOUT TEAMS AND WINNING (his WC3 model),
    /// read the same on every machine. Only a Map Creator map on its own scene
    /// has a say; anywhere else it is the classic match, wizards against
    /// acolytes with the environment counting for nothing.
    public static class MapRules
    {
        static List<TeamDef> Teams => TeamsOn(ActiveScene.Name);

        static List<TeamDef> TeamsOn(string scene)
        {
            var map = MapDef.Active;
            if (map == null || map.Teams == null || map.Teams.Count == 0) return null;
            return scene == map.Base ? map.Teams : null;
        }

        /// The standing map set its own teams.
        public static bool Custom => Teams != null;

        /// Wizards and acolytes play as one team.
        public static bool Together => Has(Teams, TeamWho.Everyone);

        /// This team is in the race for victory on the standing map.
        public static bool Counts(TeamWho who) => Counts(Teams, who);

        /// The same for a map being made.
        public static bool Counts(MapDef map, TeamWho who) =>
            Counts(map != null && map.Teams != null && map.Teams.Count > 0 ? map.Teams : null, who);

        /// The five Wizards vs Acolytes endings (pot, bells, sweep, no wizards)
        /// decide only while wizards and acolytes are two counting teams.
        public static bool ClassicEndings =>
            !Together && Counts(TeamWho.Wizards) && Counts(TeamWho.Acolytes);

        /// The match start banner's words on this scene.
        public static string StartWords(string scene)
        {
            var teams = TeamsOn(scene);
            if (teams == null) return Loc.T("round.versus");
            if (Counts(teams, TeamWho.Environment)) return Loc.T("round.beatboss");
            return !Has(teams, TeamWho.Everyone) && Counts(teams, TeamWho.Wizards) && Counts(teams, TeamWho.Acolytes)
                ? Loc.T("round.versus") : Loc.T("round.playaround");
        }

        /// Every player on one team, and the environment to beat.
        public static List<TeamDef> TogetherTeams() => new List<TeamDef>
        {
            new TeamDef { Who = TeamWho.Everyone },
            new TeamDef { Who = TeamWho.Environment },
        };

        /// The classic split written out: both sides count, the environment does not.
        public static List<TeamDef> SplitTeams() => new List<TeamDef>
        {
            new TeamDef { Who = TeamWho.Wizards },
            new TeamDef { Who = TeamWho.Acolytes },
            new TeamDef { Who = TeamWho.Environment, Counts = false },
        };

        static bool Counts(List<TeamDef> teams, TeamWho who)
        {
            if (teams == null) return who == TeamWho.Wizards || who == TeamWho.Acolytes;
            foreach (var t in teams) if (t != null && t.Who == who) return t.Counts;
            return false;
        }

        static bool Has(List<TeamDef> teams, TeamWho who)
        {
            if (teams == null) return false;
            foreach (var t in teams) if (t != null && t.Who == who) return true;
            return false;
        }
    }
}
