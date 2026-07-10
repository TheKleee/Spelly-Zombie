using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpellyZombie
{
    /// Environment pipeline, part two (the Quaternius era):
    ///   4 — Set Atmosphere: storybook dusk — warm low sun, mauve fog,
    ///       trilight ambient, procedural sunset skybox, and a URP post
    ///       volume (soft bloom so ink/particles glow, gentle vignette).
    ///       Works on the graybox TODAY and under the village kit later.
    ///   5 — Prepare Imported Packs: run after dropping Quaternius packs
    ///       into Assets/_Game/Art/Packs/ — enables mesh colliders on every
    ///       model (EVERY SURFACE MUST BE DRAWABLE — ink is a raycast) and
    ///       converts Standard-shader materials to URP Lit (no pink).
    ///   6 — Auto-Tag Scene By Name: gives placed pack objects their
    ///       SurfaceMaterialTag by name heuristics (wood burns, stone melts).
    public static class EnvironmentTools
    {
        const string ArtDir = "Assets/_Game/Art";
        const string PacksDir = ArtDir + "/Packs";

        // ------------------------------------------------------ atmosphere --
        [MenuItem("Spelly Zombie/Art/4 — Set Atmosphere (open scene)")]
        public static void SetAtmosphere()
        {
            // the sun: low, warm, long goofy shadows
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                var sunGo = new GameObject("SZ Sun");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(26f, -128f, 0f);
            sun.color = new Color(1f, 0.76f, 0.55f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;

            // fog: mauve dusk — depth without hiding the horde
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.014f;
            RenderSettings.fogColor = new Color(0.72f, 0.58f, 0.60f);

            // ambient: purple sky, warm bounce, dark ground
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.47f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.58f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.24f, 0.28f);

            // procedural sunset skybox (asset, so it survives)
            Directory.CreateDirectory(ArtDir);
            string skyPath = ArtDir + "/SZ_Skybox.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetColor("_SkyTint", new Color(0.55f, 0.42f, 0.55f));
            sky.SetColor("_GroundColor", new Color(0.35f, 0.28f, 0.30f));
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_AtmosphereThickness", 1.15f); // reddens the low sun
            RenderSettings.skybox = sky;
            EditorUtility.SetDirty(sky);

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                cam.clearFlags = CameraClearFlags.Skybox;

            // URP post: bloom makes additive ink/particles GLOW at dusk
            string profilePath = ArtDir + "/SZ_Atmosphere.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(0.95f);
            if (!profile.TryGet(out Vignette vig)) vig = profile.Add<Vignette>();
            vig.intensity.Override(0.2f);
            if (!profile.TryGet(out ColorAdjustments grade)) grade = profile.Add<ColorAdjustments>();
            grade.postExposure.Override(0.05f);
            grade.saturation.Override(10f);
            EditorUtility.SetDirty(profile);

            var volumeGo = GameObject.Find("SZ Atmosphere Volume");
            if (volumeGo == null) volumeGo = new GameObject("SZ Atmosphere Volume");
            var volume = volumeGo.GetComponent<Volume>();
            if (volume == null) volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[SpellyZombie] Atmosphere set: storybook dusk (save the scene to keep it).");
        }

        // ---------------------------------------------------- pack import --
        [MenuItem("Spelly Zombie/Art/5 — Prepare Imported Packs (colliders + URP)")]
        public static void PreparePacks()
        {
            if (!Directory.Exists(PacksDir))
            {
                Debug.LogWarning($"[SpellyZombie] No {PacksDir} yet — drop the Quaternius packs there first (use their FBX folders).");
                return;
            }

            int models = 0, materials = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { PacksDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.addCollider)
                {
                    mi.addCollider = true; // every surface is a drawing canvas
                    mi.SaveAndReimport();
                    models++;
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { PacksDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (!mat.shader.name.Contains("Standard")) continue; // already URP
                var tex = mat.mainTexture;
                var color = mat.HasProperty("_Color") ? mat.color : Color.white;
                mat.shader = Shader.Find("Universal Render Pipeline/Lit");
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(mat);
                materials++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Packs prepared: colliders on {models} models, {materials} materials converted to URP.");
        }

        // ------------------------------------------------------ auto-tags --
        // name → chemistry. Wood burns to coal, stone melts to lava, straw is
        // kindling — imported scenery joins the reaction table by its name.
        static readonly (string key, SurfaceMaterialType mat)[] NameRules =
        {
            ("stone", SurfaceMaterialType.Stone), ("rock", SurfaceMaterialType.Stone),
            ("brick", SurfaceMaterialType.Stone), ("cobble", SurfaceMaterialType.Stone),
            ("well", SurfaceMaterialType.Stone), ("tower", SurfaceMaterialType.Stone),
            ("pillar", SurfaceMaterialType.Stone), ("statue", SurfaceMaterialType.Stone),
            ("metal", SurfaceMaterialType.Metal), ("anvil", SurfaceMaterialType.Metal),
            ("sword", SurfaceMaterialType.Metal), ("lantern", SurfaceMaterialType.Metal),
            ("chain", SurfaceMaterialType.Metal), ("armor", SurfaceMaterialType.Metal),
            ("gold", SurfaceMaterialType.Gold), ("coin", SurfaceMaterialType.Gold),
            ("bone", SurfaceMaterialType.Bone), ("skull", SurfaceMaterialType.Bone),
            ("water", SurfaceMaterialType.Water),
            ("dirt", SurfaceMaterialType.Earth), ("ground", SurfaceMaterialType.Earth),
            ("terrain", SurfaceMaterialType.Earth), ("path", SurfaceMaterialType.Earth),
            // everything wooden — the default for village kit is honestly wood
            ("wood", SurfaceMaterialType.Wood), ("house", SurfaceMaterialType.Wood),
            ("wall", SurfaceMaterialType.Wood), ("roof", SurfaceMaterialType.Wood),
            ("fence", SurfaceMaterialType.Wood), ("barrel", SurfaceMaterialType.Wood),
            ("crate", SurfaceMaterialType.Wood), ("table", SurfaceMaterialType.Wood),
            ("chair", SurfaceMaterialType.Wood), ("cart", SurfaceMaterialType.Wood),
            ("plank", SurfaceMaterialType.Wood), ("log", SurfaceMaterialType.Wood),
            ("tree", SurfaceMaterialType.Wood), ("stump", SurfaceMaterialType.Wood),
            ("beam", SurfaceMaterialType.Wood), ("door", SurfaceMaterialType.Wood),
            ("sign", SurfaceMaterialType.Wood), ("hay", SurfaceMaterialType.Wood),
            ("straw", SurfaceMaterialType.Wood), ("stall", SurfaceMaterialType.Wood),
            ("bridge", SurfaceMaterialType.Wood),
        };

        [MenuItem("Spelly Zombie/Art/6 — Auto-Tag Scene By Name")]
        public static void AutoTagScene()
        {
            int tagged = 0;
            foreach (var rend in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (rend.GetComponentInParent<SurfaceMaterialTag>() != null) continue; // already chemical
                if (rend.GetComponentInParent<Creature>() != null) continue;
                if (rend.GetComponentInParent<Matter>() != null) continue;

                string n = rend.gameObject.name.ToLowerInvariant();
                foreach (var rule in NameRules)
                {
                    if (!n.Contains(rule.key)) continue;
                    rend.gameObject.AddComponent<SurfaceMaterialTag>().Material = rule.mat;
                    tagged++;
                    break;
                }
            }
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[SpellyZombie] Auto-tagged {tagged} scene objects into the chemistry (save the scene).");
        }
    }
}
