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

        // AXIOM (Marko Jul 25): this used to FORCE all 35 roles back to the CFXR
        // defaults — and it is called as a side effect by five scene builders,
        // so building a test scene silently discarded every FX prefab he had
        // assigned. It now FILLS BLANKS ONLY. The destructive path lives on its
        // own menu item (Art/8) where he chooses it deliberately.
        static bool _fxReset;   // true only inside the explicit reset menu item
        static int _fxFilled, _fxKept;

        static void Wire(ref GameObject slot, string assetPath)
        {
            if (slot != null && !_fxReset) { _fxKept++; return; }
            var found = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (found == null) { Debug.LogWarning($"[SpellyZombie] FX prefab not found: {assetPath}"); return; }
            slot = found;
            _fxFilled++;
        }

        [MenuItem("Spelly Zombie/Art/8 - FX: RESET Library To CFXR Defaults (overwrites your picks)")]
        static void ResetFxLibrary()
        {
            if (!EditorUtility.DisplayDialog("Reset FX Library",
                    "This OVERWRITES every FX role with the CFXR default, including prefabs you assigned yourself.\n\nThis cannot be undone.",
                    "Overwrite everything", "Cancel")) return;
            _fxReset = true;
            try { WireFxLibrary(); } finally { _fxReset = false; }
        }

        [MenuItem("Spelly Zombie/Art/7 - Wire FX Library (JMO) - fills blanks only")]
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
            _fxFilled = 0; _fxKept = 0;
            Wire(ref lib.Fire, CFXR + "Fire/CFXR Fire.prefab");
            Wire(ref lib.Explosion, CFXR + "Explosions/CFXR Explosion 1.prefab");
            Wire(ref lib.Poof, CFXR + "Misc/CFXR Magic Poof.prefab");
            Wire(ref lib.ElectricHit, CFXR + "Electric/CFXR Electrified 3.prefab");
            Wire(ref lib.IceHit, CFXR + "Ice/CFXR3 Hit Ice B (Air).prefab");
            Wire(ref lib.RunicAura, CFXR + "Magic Misc/CFXR3 Magic Aura A (Runic).prefab");
            // the full mapping (Marko approved Jul 22)
            Wire(ref lib.Sun, CFXR + "Fire/CFXR4 Sun.prefab");
            Wire(ref lib.FireBurst, CFXR + "Explosions/CFXR3 Fire Explosion B.prefab");
            Wire(ref lib.HealShine, CFXR + "Misc/CFXR2 Shiny Item (Loop).prefab");
            Wire(ref lib.SoulsOut, CFXR + "Eerie/CFXR2 Souls Escape.prefab");
            Wire(ref lib.Flash, CFXR + "Misc/CFXR Flash.prefab");
            Wire(ref lib.Stars, CFXR + "Magic Misc/CFXR4 Falling Stars.prefab");
            Wire(ref lib.TimeDome, CFXR + "Misc/CFXR3 Ambient Glows.prefab");
            Wire(ref lib.Scuffle, CFXR + "Misc/CFXR2 Cartoon Fight (Loop).prefab");
            Wire(ref lib.WindTrails, CFXR + "Nature/CFXR4 Wind Trails.prefab");
            Wire(ref lib.Ripples, CFXR + "Liquids/CFXR Water Ripples.prefab");
            Wire(ref lib.Splash, CFXR + "Liquids/CFXR Water Splash (Smaller).prefab");
            Wire(ref lib.Smoke, CFXR + "Misc/CFXR Smoke Source 3D.prefab");
            Wire(ref lib.GasCloud, CFXR + "Misc/CFXR2 Poison Cloud.prefab");
            Wire(ref lib.Shield, CFXR + "Nature/CFXR3 Shield Leaves A (Lit).prefab");
            Wire(ref lib.DemonBoom, CFXR + "Eerie/CFXR2 WW Enemy Explosion.prefab");
            Wire(ref lib.SkullHead, CFXR + "Eerie/CFXR2 Skull Head Alt.prefab");
            Wire(ref lib.BrokenHeart, CFXR + "Misc/CFXR2 Broken Heart.prefab");
            Wire(ref lib.GroundHit, CFXR + "Impacts/CFXR2 Ground Hit.prefab");
            Wire(ref lib.TextBoom, CFXR + "Texts/CFXR _BOOM_.prefab");
            Wire(ref lib.TextBoing, CFXR + "Texts/CFXR _BOING_.prefab");
            Wire(ref lib.TextWow, CFXR + "Texts/CFXR3 _WOW_.prefab");
            Wire(ref lib.TextFrozen, CFXR + "Texts/CFXR4 _FROZEN_.prefab");
            Wire(ref lib.TextPow, CFXR + "Texts/CFXR _POW_.prefab");
            Wire(ref lib.TextWham, CFXR + "Texts/CFXR2 _WHAM_ 3.prefab");
            Wire(ref lib.HitSpark, CFXR + "Fire/CFXR3 Hit Fire B (Air).prefab");
            Wire(ref lib.HitLight, CFXR + "Light/CFXR3 Hit Light B (Air).prefab");
            Wire(ref lib.HitVector, CFXR + "Impacts/CFXR Hit D 3D (Yellow).prefab");
            Wire(ref lib.HitThud, CFXR + "Impacts/CFXR2 Ground Hit.prefab");
            Wire(ref lib.Blood, CFXR + "Liquids/CFXR2 Blood Shape Splash.prefab");

            if (_fxFilled > 0) { EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets(); }
            int missing = 0;
            foreach (var f in typeof(FxLibrary).GetFields())
                if (f.FieldType == typeof(GameObject) && f.GetValue(lib) == null) missing++;
            Debug.Log($"[SpellyZombie] FxLibrary: filled {_fxFilled} empty role(s), " +
                      $"LEFT {_fxKept} of YOUR assignments untouched" +
                      (missing > 0 ? $". {missing} role(s) still empty." : "."));
        }

        // ---------------------------------------------------- pack import --
        [MenuItem("Spelly Zombie/Art/5 - Prepare Imported Packs (colliders + URP)")]
        public static void PreparePacks()
        {
            if (!Directory.Exists(PacksDir))
            {
                Debug.LogWarning($"[SpellyZombie] No {PacksDir} yet. Drop the Quaternius packs there first (use their FBX folders).");
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

        [MenuItem("Spelly Zombie/Art/6 - Auto-Tag Scene By Name")]
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
