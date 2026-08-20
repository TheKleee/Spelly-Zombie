using System;
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

        /// Stand the local player at their start, once per scene. Called every
        /// frame; it does nothing until it has a body and a scene it has not
        /// already placed them in.
        public static void PlaceLocals()
        {
            string scene = ActiveScene.Name;
            int mine = Grimoire.LocalPlayerId;

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || _placed.Contains(p)) continue;
                Vector3 at;

                // THE HOST DIVIDES THE GROUND. Left to themselves, two clients
                // loading in the same second pick from the same empty scene and
                // land on the same tile - neither can see the other yet.
                if (NetGame.Connected && !Waited())
                {
                    if (!_assigned.TryGetValue(mine, out at)) { Ask(mine); continue; }
                }
                else
                {
                    // seeded off WHICH body it is as well as who owns it, so a
                    // split screen's two players never land on one tile
                    var rng = new System.Random(mine * 7919
                        ^ p.GetInstanceID() ^ scene.GetHashCode() ^ Environment.TickCount);
                    if (!PointFor(Sides.Of(mine), rng, out at))
                    {
                        Debug.LogWarning($"[SpellyZombie] Nowhere to stand in '{scene}' - no "
                            + "biome gave a point and nothing was under the FallCatcher's "
                            + "RespawnPoint. Left where the player was placed.");
                        _placed.Add(p);   // complain once, not every frame
                        continue;
                    }
                }

                FallCatcher.Teleport(p, at);
                _placed.Add(p);
            }
        }

        static float _askedAt, _firstAsk;

        /// The host went quiet. Place yourself rather than stand in the spawn
        /// point forever; a wrong tile beats no body.
        static bool Waited()
        {
            if (_firstAsk <= 0f) return false;
            if (Time.time - _firstAsk < DrawingConfig.SpawnAssignWaitSeconds) return false;
            if (!_gaveUp)
            {
                _gaveUp = true;
                Debug.LogWarning("[SpellyZombie] No spawn point from the host after "
                    + $"{DrawingConfig.SpawnAssignWaitSeconds}s - picking one locally.");
            }
            return true;
        }

        static bool _gaveUp;

        /// Asks once, then at a slow retry - a broadcast per frame would be a
        /// flood for something the host answers once.
        static void Ask(int owner)
        {
            if (_firstAsk <= 0f) _firstAsk = Time.time;
            if (_askedAt > 0f && Time.time - _askedAt < 0.5f) return;
            _askedAt = Time.time;
            NetSync.AskSpawn(owner);
        }

        // host: what it has handed out this scene, so no two land together
        static readonly List<Vector3> _issued = new List<Vector3>();
        static readonly Dictionary<int, Vector3> _assigned = new Dictionary<int, Vector3>();

        /// HOST ONLY: this owner's point, kept clear of everyone already sent
        /// one. Asking twice returns the same answer, so a resend cannot move
        /// somebody who already stood up.
        public static bool IssueFor(int owner, out Vector3 at)
        {
            if (_assigned.TryGetValue(owner, out at)) return true;

            var rng = new System.Random(owner * 7919
                ^ ActiveScene.Name.GetHashCode() ^ Environment.TickCount);
            for (int tries = 0; tries < 12; tries++)
            {
                if (!PointFor(Sides.Of(owner), rng, out at)) continue;
                if (TooClose(at)) continue;
                _issued.Add(at);
                return true;
            }
            return false;   // the asker falls back to its own pick
        }

        /// Record the point the host gave this owner.
        public static void TakeAssigned(int owner, Vector3 at) => _assigned[owner] = at;

        static bool TooClose(Vector3 at)
        {
            float elbow = DrawingConfig.SpawnApartMeters;
            foreach (var p in _issued)
                if ((p - at).sqrMagnitude < elbow * elbow) return true;
            return false;
        }

        static readonly HashSet<SimpleFPSController> _placed
            = new HashSet<SimpleFPSController>();

        /// Every scene load is a fresh start, INCLUDING reloading the one you
        /// were in - a scene-name check meant a lobby you re-entered put you
        /// back on the same tile.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookLoads()
        {
            Reset();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => Reset();
        }

        static void Reset()
        {
            _placed.Clear();
            _issued.Clear();
            _assigned.Clear();
            _askedAt = _firstAsk = 0f;
            _gaveUp = false;
        }

        /// Where this side starts. Biomes decide it wherever the map has them;
        /// a map with none - the lobby - scatters instead. The lobby stays
        /// biome-free on purpose: it is where you stand BEFORE a home biome is
        /// decided, and a biome there would stamp everyone early.
        public static bool PointFor(Side side, System.Random rng, out Vector3 at)
        {
            if (WizardBiome != null || _acolyte.Count > 0)
            {
                bool got = side == Side.Acolyte
                    ? AcolytePoint(rng, out at) : WizardPoint(rng, out at);
                if (got) return true;
            }
            return ScatterPoint(rng, out at);
        }

        /// No biomes to read: scatter on real ground around the scene's own
        /// anchor, the point its FallCatcher was given.
        public static bool ScatterPoint(System.Random rng, out Vector3 at)
        {
            at = default;
            Vector3 home = FallCatcher.Home;
            float radius = DrawingConfig.LobbyScatterRadius;
            int mask = Physics.DefaultRaycastLayers
                & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);

            for (int tries = 0; tries < 32; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                // sqrt spreads them evenly over the disc instead of clumping
                // everyone around the anchor
                float d = radius * Mathf.Sqrt((float)rng.NextDouble());
                Vector3 spot = home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;

                if (!Physics.Raycast(spot + Vector3.up * 25f, Vector3.down, out var hit,
                        80f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y <= 0.6f) continue;   // a wall or a roof edge
                // never inside another body: the local one is a controller,
                // everyone else is a NetAvatar puppet
                if (hit.collider.GetComponentInParent<SimpleFPSController>() != null) continue;
                if (hit.collider.GetComponentInParent<NetAvatar>() != null) continue;
                if (Occupied(hit.point)) continue;
                at = hit.point + Vector3.up * 1.2f;
                return true;
            }
            return false;
        }

        /// Somebody already standing here. Only catches people who have ALREADY
        /// been placed - two clients loading at the same moment can still pick
        /// the same tile, which is why spawns want to be host-assigned.
        static bool Occupied(Vector3 spot)
        {
            const float Elbow = 1.6f;
            foreach (var p in SimpleFPSController.All)
                if (p != null && (p.transform.position - spot).sqrMagnitude < Elbow * Elbow)
                    return true;
            foreach (var a in NetAvatar.All)
                if (a != null && (a.transform.position - spot).sqrMagnitude < Elbow * Elbow)
                    return true;
            return false;
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
