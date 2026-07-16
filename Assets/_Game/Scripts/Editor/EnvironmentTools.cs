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
        // menu removed (scene-modifying — Marko owns scene lighting/atmosphere)
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
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            // fog: light daytime haze — depth without hiding the horde
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.008f;
            RenderSettings.fogColor = new Color(0.76f, 0.83f, 0.92f);

            // ambient: bright day, warm bounce
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.76f, 0.72f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.34f, 0.31f);

            // MARKO'S RULE (updated July 11): the skybox is PT_Skybox_mat.mat.
            // Fall back to Sky.mat, then to a generated one, if missing.
            Directory.CreateDirectory(ArtDir);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Skybox_mat.mat");
            if (sky == null)
                sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Polytope Studio/Sky.mat");
            if (sky == null)
            {
                string skyPath = ArtDir + "/SZ_Skybox.mat";
                sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null)
                {
                    sky = new Material(Shader.Find("Skybox/Procedural"));
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
            }
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

        // ------------------------------------------------------ fx library --
        const string CFXR = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";

        [MenuItem("Spelly Zombie/Art/7 — Wire FX Library (JMO)")]
        public static void WireFxLibrary()
        {
            Directory.CreateDirectory("Assets/_Game/Resources");
            const string path = "Assets/_Game/Resources/FxLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<FxLibrary>(path);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<FxLibrary>();
                AssetDatabase.CreateAsset(lib, path);
            }
            lib.Fire = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Fire/CFXR Fire.prefab");
            lib.Explosion = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Explosions/CFXR Explosion 1.prefab");
            lib.Poof = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Misc/CFXR Magic Poof.prefab");
            lib.ElectricHit = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Electric/CFXR Electrified 3.prefab");
            lib.IceHit = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Ice/CFXR3 Hit Ice B (Air).prefab");
            lib.RunicAura = AssetDatabase.LoadAssetAtPath<GameObject>(CFXR + "Magic Misc/CFXR3 Magic Aura A (Runic).prefab");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] FxLibrary wired: fire={(lib.Fire != null)} explosion={(lib.Explosion != null)} " +
                      $"poof={(lib.Poof != null)} electric={(lib.ElectricHit != null)}");
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
