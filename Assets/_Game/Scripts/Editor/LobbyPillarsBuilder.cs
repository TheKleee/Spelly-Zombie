using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Drops two empty marker objects with the pillar components wired: one
    /// for hat coloring, one for switching sides. The components build their
    /// own runtime light beams, so nothing is serialized into the scene; art
    /// parented under a marker replaces its beam.
    public static class LobbyPillarsBuilder
    {
        [MenuItem("Spelly Zombie/Build LOBBY PILLARS (open scene)")]
        static void Build()
        {
            if (GameObject.Find("SZ_LobbyPillars") != null)
            {
                Debug.LogWarning("[SpellyZombie] SZ_LobbyPillars already exists. Delete it to rebuild.");
                return;
            }

            var root = new GameObject("SZ_LobbyPillars");
            Undo.RegisterCreatedObjectUndo(root, "Build Lobby Pillars");

            Marker(root.transform, "HatPillar (E: hat color)", new Vector3(2.5f, 0f, 2.5f))
                .AddComponent<HatPillar>();
            Marker(root.transform, "SidePillar (E: change side)", new Vector3(-2.5f, 0f, 2.5f))
                .AddComponent<SidePillar>();

            Selection.activeGameObject = root;
            Debug.Log("[SpellyZombie] Lobby pillar markers placed near the origin — MOVE THEM where the lobby wants them. They appear as beams of light in play mode; parent your own art under one and its beam steps aside.");
        }

        static GameObject Marker(Transform parent, string name, Vector3 at)
        {
            var m = new GameObject(name);
            m.transform.SetParent(parent, false);
            // ground snap so the beams never float or sink on the terrain
            if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out var hit, 60f))
                at = hit.point;
            m.transform.position = at;
            return m;
        }
    }
}
