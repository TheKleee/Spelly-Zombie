using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// Rune Studio: a gallery with 12 walls, one per rune. Draw samples on a
    /// wall, E saves the wall, the eraser deletes mistakes; every scene load
    /// repaints what's saved.
    public static class RuneStudioBuilder
    {
        const string ScenePath = "Assets/_Game/Scenes/RuneStudio.unity";

        /// Trims every rune's recorded pool to its first drawing. Works in or
        /// out of play mode; the walls redraw on the next Rune Studio load.
        [MenuItem("Spelly Zombie/Runes - Keep ONLY the first drawing per rune")]
        public static void KeepFirstDrawingOnly()
        {
            if (!EditorUtility.DisplayDialog("Trim rune drawings",
                "Keep only the FIRST recorded drawing for every rune and delete the rest?\n\n" +
                "This rewrites sz_rune_templates.json and cannot be undone.",
                "Trim them", "Cancel")) return;
            RuneLibrary.KeepOnlyFirstSample();
        }

        /// The audit is O(samples²), tens of seconds on a full library, so it
        /// does not run on scene load. Run after re-recording; result is cached.
        [MenuItem("Spelly Zombie/Runes - Re-audit templates (health check)")]
        public static void ReAuditTemplates() => RuneLibrary.ReAudit();

        [MenuItem("Spelly Zombie/Build Rune Studio Scene (replaces the open scene)")]
        public static void Build()
        {
            // NewScene does not prompt; ask about unsaved scenes first
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string target = ScenePath;
            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Build Rune Studio",
                    ScenePath + " already exists. Building REPLACES it (walls you moved, lights you tuned).\n\n" +
                    "Your recorded rune samples are NOT in the scene. They live in sz_rune_templates.json and are safe either way.",
                    "Overwrite it", "Save as a NEW scene"))
                target = AssetDatabase.GenerateUniqueAssetPath(ScenePath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lit = Shader.Find("Universal Render Pipeline/Lit");

            // calm, even light
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
                // the label doesn't inherit its squash
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
                labelGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                // the root is turned 180° to face the walkway; the label must
                // turn WITH the readable face or it shows mirrored
                labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var label = labelGo.AddComponent<TMPro.TextMeshPro>();
                label.text = RuneLibrary.Icon(runes[i]); // emoji needs TMP sprites
                label.fontSize = 2.6f;
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.color = new Color(0.15f, 0.13f, 0.1f);
                label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                label.rectTransform.sizeDelta = new Vector2(4.4f, 1.2f);

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

            InstallCollectionManager();

            EditorSceneManager.SaveScene(scene, target);
            Debug.Log($"[SpellyZombie] Rune Studio built → {target}");
            Debug.Log("[SpellyZombie] RUNE STUDIO ready. Play, walk to a wall, draw samples of " +
                "that wall's rune, press E to save the wall. Erase + re-save to delete a bad one. " +
                "Saved drawings repaint on every load.");
        }

        /// The grimoire pages, authored bodies and particle shapes all live in
        /// the Lobby scene's CollectionManager - a scene played directly never
        /// met it. This lifts HIS manager (slots untouched) into a prefab once
        /// and drops it into the open scene.
        [MenuItem("Spelly Zombie/Studio/Install Collection Manager Here")]
        public static void InstallCollectionManager()
        {
            if (Object.FindFirstObjectByType<CollectionManager>(FindObjectsInactive.Include) != null)
            {
                Debug.Log("[SpellyZombie] CollectionManager already in this scene.");
                return;
            }
            const string prefabPath = "Assets/_Game/Prefabs/CollectionManager.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                var lobby = EditorSceneManager.OpenScene(
                    "Assets/_Game/Scenes/Lobby.unity", OpenSceneMode.Additive);
                CollectionManager src = null;
                foreach (var root in lobby.GetRootGameObjects())
                {
                    src = root.GetComponentInChildren<CollectionManager>(true);
                    if (src != null) break;
                }
                if (src == null)
                {
                    EditorSceneManager.CloseScene(lobby, true);
                    Debug.LogError("[SpellyZombie] No CollectionManager found in the Lobby scene.");
                    return;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(src.gameObject, prefabPath);
                EditorSceneManager.CloseScene(lobby, true); // untouched, just read
                Debug.Log($"[SpellyZombie] CollectionManager saved as prefab → {prefabPath}");
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab, EditorSceneManager.GetActiveScene());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[SpellyZombie] '{inst.name}' installed - grimoire pages and authored bodies now exist in this scene.");
        }
    }
}
