using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace SpellyZombie
{
    /// Drag the claim's faces in the Scene view; Fit To Model measures again.
    [CustomEditor(typeof(ObjectBox))]
    [CanEditMultipleObjects]
    public class ObjectBoxEditor : Editor
    {
        readonly BoxBoundsHandle _handle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Space(4f);
            if (GUILayout.Button("Fit To Model"))
                foreach (var t in targets)
                {
                    var box = (ObjectBox)t;
                    box.FitToModel();
                    EditorUtility.SetDirty(box);
                }
        }

        void OnSceneGUI()
        {
            var box = (ObjectBox)target;
            using (new Handles.DrawingScope(box.transform.localToWorldMatrix))
            {
                _handle.center = box.Center;
                _handle.size = box.Size;
                _handle.handleColor = new Color(1f, 0.6f, 0.15f, 1f);
                _handle.wireframeColor = Color.clear; // the component's gizmo draws the box
                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box, "Resize Object Box");
                    box.Center = _handle.center;
                    box.Size = _handle.size;
                }
            }
        }
    }

    /// The biome's button: every prefab it spawns gets a claim fitted to its model.
    [CustomEditor(typeof(Biome), true)]
    public class BiomeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Space(8f);
            if (GUILayout.Button("Fit Object Boxes On Props", GUILayout.Height(26f)))
                ObjectBoxTools.FitMissing((Biome)target);
        }
    }

    static class InteriorFieldBake
    {
        /// Stairs and faceless sides are read in the editor and kept on the
        /// fields; this stores them in every prefab so builds carry them.
        [MenuItem("Spelly Zombie/Scenery/Measure Interior Fields In Prefabs")]
        static void MeasureAll()
        {
            int fields = 0, prefabs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<InteriorField>(true) == null) continue;
                using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    foreach (var field in scope.prefabContentsRoot.GetComponentsInChildren<InteriorField>(true))
                    {
                        field.MeasureStairs();
                        field.MeasureOpenSides();
                        fields++;
                    }
                }
                prefabs++;
            }
            Debug.Log($"[SpellyZombie] Measured {fields} Interior Fields in {prefabs} prefabs (stairs and faceless sides).");
        }
    }

    /// Writes the recommended natural values onto the biomes in the open
    /// scene, matched by name. Undoable; save the scene to keep them.
    static class BiomeValues
    {
        struct Values
        {
            public int Heat, Light, Density, Stick, IntCap, Courage;
            public float Regen;
            public Values(int heat, int light, int density, int stick, int intCap, int courage, float regen)
            {
                Heat = heat; Light = light; Density = density; Stick = stick; IntCap = intCap; Courage = courage; Regen = regen;
            }
        }

        // Heat and light are felt from the spawn biome, so acolytes born in the
        // cold burn in normal places and the dark-born see glare there.
        static readonly Dictionary<string, Values> Recommended = new Dictionary<string, Values>
        {
            //                                   heat light dens stick int courage regen
            { "Center Biome",        new Values(  0,   0,   0,    0,  25,  25, 1f) },
            { "Town Biome",          new Values(  0,   0,   0,    0,  25,  25, 2f) },
            { "Beach Biome",         new Values(  8,  12,   0,    0,  25,  50, 1.5f) },
            { "Forest Biome",        new Values(  0, -20,   0,    0,  25,   0, 0.5f) },
            { "Lake Biome",          new Values( -8, -14,   0,    0,  25,  25, 1f) },
            { "Large Stones Biome",  new Values( -8,   0,   8,    0,  25,  25, 1f) },
            { "Medium Stones Biome", new Values(-14,   0,   0,    0,   5,  25, 1f) },
            { "Peak Biome",          new Values(-20,  10, -10,  -15,  25,  25, 0f) },
        };

        [MenuItem("Spelly Zombie/Scenery/Apply Recommended Biome Values")]
        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SpellyZombie] Stop Play Mode first. Values set while playing are lost.");
                return;
            }
            var biomes = Object.FindObjectsByType<Biome>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var log = new System.Text.StringBuilder();
            var found = new HashSet<string>();
            foreach (var b in biomes)
            {
                if (!Recommended.TryGetValue(b.name, out var v))
                {
                    log.AppendLine($"  {b.name}: not on the list, left as it was (heat {b.HeatOffset}, light {b.LightOffset}, stick {b.StickOffset})");
                    continue;
                }
                var so = new SerializedObject(b);
                so.FindProperty(nameof(Biome.HeatOffset)).intValue = v.Heat;
                so.FindProperty(nameof(Biome.LightOffset)).intValue = v.Light;
                so.FindProperty(nameof(Biome.DensityOffset)).intValue = v.Density;
                so.FindProperty(nameof(Biome.StickOffset)).intValue = v.Stick;
                so.FindProperty(nameof(Biome.AffinityOffset)).intValue = 0;
                so.FindProperty(nameof(Biome.IntCap)).intValue = v.IntCap;
                so.FindProperty(nameof(Biome.CourageCap)).intValue = v.Courage;
                so.FindProperty(nameof(Biome.StrengthCap)).floatValue = 0f;
                so.FindProperty(nameof(Biome.RegenScale)).floatValue = v.Regen;
                so.ApplyModifiedProperties(); // records the undo
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
                found.Add(b.name);
                log.AppendLine($"  {b.name}: heat {v.Heat}, light {v.Light}, density {v.Density}, stick {v.Stick}, int cap {v.IntCap}, courage cap {v.Courage}, regen {v.Regen}");
            }
            foreach (var name in Recommended.Keys)
                if (!found.Contains(name)) log.AppendLine($"  {name}: not in the open scene");

            // boxes that share space add their values together there
            for (int i = 0; i < biomes.Length; i++)
                for (int j = i + 1; j < biomes.Length; j++)
                {
                    Bounds a = biomes[i].Area, c = biomes[j].Area;
                    Vector3 shared = Vector3.Min(a.max, c.max) - Vector3.Max(a.min, c.min);
                    if (shared.x > 0.5f && shared.y > 0.5f && shared.z > 0.5f)
                        log.AppendLine($"  {biomes[i].name} and {biomes[j].name} overlap: their values add up there");
                }
            Debug.Log($"[SpellyZombie] Biome values set on {found.Count} biomes. Save the scene to keep them.\n{log}");
        }
    }

    static class ObjectBoxTools
    {
        /// Adds a fitted ObjectBox to every Prop, Source and the Cauldron that
        /// has none. A box already on a prefab is never touched.
        public static void FitMissing(Biome b)
        {
            var seen = new HashSet<GameObject>();
            var all = new List<GameObject>();
            void Take(GameObject g) { if (g != null && seen.Add(g)) all.Add(g); }
            if (b.Props != null) foreach (var g in b.Props) Take(g);
            if (b.Sources != null) foreach (var g in b.Sources) Take(g);
            Take(b.Cauldron);

            var fitted = new List<string>();
            int kept = 0;
            foreach (var g in all)
            {
                if (g.GetComponent<ObjectBox>() != null) { kept++; continue; }
                string path = AssetDatabase.GetAssetPath(g);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) continue;
                using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    var box = scope.prefabContentsRoot.GetComponent<ObjectBox>()
                              ?? scope.prefabContentsRoot.AddComponent<ObjectBox>();
                    box.FitToModel();
                }
                fitted.Add(g.name);
            }
            Debug.Log($"[SpellyZombie] {b.name}: fitted {fitted.Count} Object Boxes"
                + (fitted.Count > 0 ? $" ({string.Join(", ", fitted)})" : "")
                + $", {kept} already had their own.", b);
        }
    }
}
