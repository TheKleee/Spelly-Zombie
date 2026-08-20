using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Who starts where, decided from the map's own biomes rather than from
    /// placed markers. ONE biome is the wizards' home and they all begin
    /// inside it; every other biome is acolyte ground and they scatter across
    /// all of them - usually landing apart, sometimes sharing one, never
    /// arranged.
    /// Both halves are guaranteed: mark none and one is chosen, mark them all
    /// and one is released back, so no map can ship with a side that has
    /// nowhere to stand.
    public static class SpawnPlan
    {
        static readonly List<Biome> _acolyte = new List<Biome>();

        /// The wizards' biome for this map, or null when there are no biomes.
        public static Biome WizardBiome { get; private set; }

        /// Every biome acolytes may appear in.
        public static IReadOnlyList<Biome> AcolyteBiomes => _acolyte;

        /// Read the marks and fill the gaps. Call once the biomes exist,
        /// with the map seed so an unmarked map still picks the same home
        /// for everyone in the lobby.
        public static void Build(IReadOnlyList<Biome> biomes, int seed)
        {
            WizardBiome = null;
            _acolyte.Clear();
            if (biomes == null || biomes.Count == 0) return;

            var marked = new List<Biome>();
            foreach (var b in biomes)
            {
                if (b == null) continue;
                if (b.WizardSpawn) marked.Add(b);
                else _acolyte.Add(b);
            }

            var rng = new System.Random(seed);

            if (marked.Count == 0)
            {
                // nobody marked one: promote a biome, preferring a protected
                // core (that is what a home biome IS) and never a liquid one
                Biome pick = null;
                foreach (var b in _acolyte)
                    if (b is GroundBiome && b.ProtectedCore > 0.5f) { pick = b; break; }
                if (pick == null)
                    foreach (var b in _acolyte)
                        if (b is GroundBiome) { pick = b; break; }
                if (pick == null) pick = _acolyte[rng.Next(_acolyte.Count)];
                _acolyte.Remove(pick);
                marked.Add(pick);
                Debug.Log($"[SpellyZombie] No wizard spawn biome marked - '{pick.name}' promoted.");
            }

            // more than one marked: the first is home, the rest go back to the
            // acolytes so the map does not quietly lose their ground
            WizardBiome = marked[0];
            for (int i = 1; i < marked.Count; i++) _acolyte.Add(marked[i]);

            if (_acolyte.Count == 0)
            {
                // every biome was marked: release one, else acolytes cannot spawn
                var give = WizardBiome;
                if (biomes.Count > 1)
                {
                    foreach (var b in biomes)
                        if (b != null && b != WizardBiome) { give = b; break; }
                    _acolyte.Add(give);
                }
                else _acolyte.Add(WizardBiome); // a one-biome map: everyone shares it
                Debug.Log($"[SpellyZombie] Every biome was wizard-marked - '{give.name}' released to acolytes.");
            }
        }

        /// A point inside the wizards' biome. They all start here, scattered.
        public static bool WizardPoint(System.Random rng, out Vector3 at) =>
            PointIn(WizardBiome, rng, out at);

        /// A point for one acolyte: a biome picked at random, then a spot in it.
        /// Random per acolyte, so they usually separate without being placed.
        public static bool AcolytePoint(System.Random rng, out Vector3 at)
        {
            at = default;
            if (_acolyte.Count == 0) return false;
            return PointIn(_acolyte[rng.Next(_acolyte.Count)], rng, out at);
        }

        /// Drops a ray inside the box and takes the ground it finds.
        static bool PointIn(Biome b, System.Random rng, out Vector3 at)
        {
            at = default;
            if (b == null) return false;
            var area = b.Area;
            for (int tries = 0; tries < 24; tries++)
            {
                float x = Mathf.Lerp(area.min.x, area.max.x, (float)rng.NextDouble());
                float z = Mathf.Lerp(area.min.z, area.max.z, (float)rng.NextDouble());
                if (SpellyMap.BiomeAt(new Vector3(x, 0f, z)) != b) continue; // layers cut
                var from = new Vector3(x, area.max.y + 2f, z);
                if (!Physics.Raycast(from, Vector3.down, out var hit, area.size.y + 6f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (!(hit.collider is TerrainCollider)) continue;
                at = hit.point + Vector3.up * 0.4f;
                return true;
            }
            return false;
        }
    }
}
