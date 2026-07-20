using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// The cave workflow, Marko's way:
    ///   1. "Generate NEW Layout" until a graph feels right (each click = new seed)
    ///   2. move / add / delete the NAMED nodes in the hierarchy by hand
    ///   3. "Rebuild Geometry From Nodes" — the rock re-forms around your edit
    ///   4. "Add Player Here" — drop a playable rig into the start room
    public static class CaveBuilderMenu
    {
        [MenuItem("Spelly Zombie/Cave: Generate NEW Layout")]
        public static void GenerateNew()
        {
            var cave = Object.FindAnyObjectByType<CaveExcavation>();
            if (cave == null)
            {
                var go = new GameObject("CaveExcavation");
                cave = go.AddComponent<CaveExcavation>();
                Undo.RegisterCreatedObjectUndo(go, "Create Cave");
            }
            cave.Seed = 0; // fresh roll
            cave.Build();
            Selection.activeObject = cave.gameObject;
            if (cave.StartRoom != null) SceneView.lastActiveSceneView?.Frame(
                new Bounds(cave.StartRoom.position, Vector3.one * 24f), false);
        }

        [MenuItem("Spelly Zombie/Cave: Rebuild Geometry From Nodes")]
        public static void Rebuild()
        {
            var cave = Object.FindAnyObjectByType<CaveExcavation>();
            if (cave == null) { GenerateNew(); return; }
            cave.RebuildGeometry();
        }

        [MenuItem("Spelly Zombie/Cave: Add Player Here")]
        public static void AddPlayer()
        {
            var cave = Object.FindAnyObjectByType<CaveExcavation>();

            // the drawing system must exist for the pen to work at all
            if (Object.FindAnyObjectByType<DrawingWorld>() == null)
                new GameObject("DrawingWorld").AddComponent<DrawingWorld>();

            var existing = GameObject.Find("SZ_Player");
            if (existing == null)
            {
                var root = new GameObject("CaveTest").transform;
                TestSandboxBuilder.BuildBeanPlayer(root);
                existing = GameObject.Find("SZ_Player");
                if (existing != null && existing.GetComponent<CharacterRig>() == null)
                    existing.AddComponent<CharacterRig>(); // the real body takes over in play mode
            }
            if (existing != null && cave != null && cave.StartRoom != null)
            {
                // ON the rock floor, not hovering at the node center — the node
                // is the corridor centerline, the floor is the flat cut below it
                Vector3 at = cave.StartRoom.position;
                float fy = at.y - cave.CorridorRadius * cave.FloorLevel;
                if (Physics.Raycast(at + Vector3.up * 4f, Vector3.down, out var hit, 20f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    fy = hit.point.y;
                existing.transform.position = new Vector3(at.x, fy + 1.05f, at.z);
            }
            Selection.activeObject = existing;
        }
    }
}
