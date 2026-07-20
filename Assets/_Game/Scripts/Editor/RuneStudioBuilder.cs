using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// THE RUNE STUDIO (Marko's recording design): a quiet gallery with 12
    /// walls, one per rune. Draw samples on a wall, E saves the wall, the
    /// eraser deletes mistakes, and every scene load repaints what's saved.
    /// Replaces the F-key recording entirely.
    public static class RuneStudioBuilder
    {
        const string ScenePath = "Assets/_Game/Scenes/RuneStudio.unity";

        [MenuItem("Spelly Zombie/Build Rune Studio Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lit = Shader.Find("Universal Render Pipeline/Lit");

            // calm, even light — this is a study, not a dungeon
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.95f;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(8f, 1f, 3f); // 80 × 30 m
            floor.transform.position = new Vector3(0f, 0f, 4f);
            if (lit != null)
                floor.GetComponent<Renderer>().sharedMaterial =
                    new Material(lit) { color = new Color(0.5f, 0.49f, 0.46f) };
            floor.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

            new GameObject("SZ_DrawingWorld").AddComponent<DrawingWorld>();

            var wallMat = lit != null
                ? new Material(lit) { color = new Color(0.84f, 0.82f, 0.77f) } : null;

            var runes = RuneLibrary.RecordableRunes;
            for (int i = 0; i < runes.Length; i++)
            {
                // root carries RuneWall + label; the scaled cube is a child so
                // the TextMesh doesn't inherit its squash
                var root = new GameObject($"RuneWall_{runes[i]}");
                root.transform.position = new Vector3((i - (runes.Length - 1) * 0.5f) * 5f, 1.55f, 8f);
                root.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the walkway

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Slab";
                slab.transform.SetParent(root.transform, false);
                slab.transform.localScale = new Vector3(4.4f, 2.6f, 0.15f);
                if (wallMat != null) slab.GetComponent<Renderer>().sharedMaterial = wallMat;
                slab.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(root.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 1.75f, 0f);
                var label = labelGo.AddComponent<TextMesh>();
                label.text = RuneLibrary.ShortName(runes[i]);
                label.fontSize = 64;
                label.characterSize = 0.045f;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = new Color(0.15f, 0.13f, 0.1f);

                var wall = root.AddComponent<RuneWall>();
                wall.Rune = runes[i];
            }

            var playerRoot = new GameObject("StudioRoot").transform;
            TestSandboxBuilder.BuildBeanPlayer(playerRoot);
            var player = GameObject.Find("SZ_Player");
            if (player != null)
            {
                player.transform.position = new Vector3(0f, 1.05f, 2f);
                if (player.GetComponent<CharacterRig>() == null)
                    player.AddComponent<CharacterRig>(); // the real body takes over in play
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SpellyZombie] RUNE STUDIO ready — play, walk to a wall, draw samples of " +
                "that wall's rune, press E to save the wall. Erase + re-save to delete a bad one. " +
                "Saved drawings repaint on every load.");
        }
    }
}
