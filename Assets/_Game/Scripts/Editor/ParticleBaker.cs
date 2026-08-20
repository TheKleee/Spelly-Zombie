using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Bakes live spell particles into prefabs: runtime MatterFX materials are
    /// saved as .mat assets and rewired first. In play mode, cast the particles,
    /// then run the menu item. One prefab per ParticleKind, written to
    /// Assets/_Game/Prefabs/Particles; existing prefabs are never overwritten.
    public static class ParticleBaker
    {
        const string Folder = "Assets/_Game/Prefabs/Particles";
        const string MatFolder = Folder + "/Materials";

        [MenuItem("Spelly Zombie/Bake SPELL PARTICLES to prefabs (play mode)")]
        static void Bake()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SpellyZombie] Enter play mode and cast the particles first.");
                return;
            }

            EnsureFolder(Folder);
            EnsureFolder(MatFolder);

            var baked = new HashSet<ParticleKind>();
            var matAssets = new Dictionary<Material, Material>();
            int made = 0, skipped = 0;

            foreach (var p in SpellParticle.Living)
            {
                if (p == null || p.Dead || baked.Contains(p.Kind)) continue;
                baked.Add(p.Kind);

                string path = $"{Folder}/P_{p.Kind}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    skipped++;
                    continue; // existing prefab wins
                }

                var copy = Object.Instantiate(p.gameObject);
                copy.name = $"P_{p.Kind}";

                // behavior stays in code; the prefab is the look only
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                    Object.DestroyImmediate(mb);

                foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null)
                            mats[i] = SaveMaterial(mats[i], matAssets);
                    r.sharedMaterials = mats;
                }

                PrefabUtility.SaveAsPrefabAsset(copy, path);
                Object.DestroyImmediate(copy);
                made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SpellyZombie] Particles baked: {made} new prefab(s), {skipped} already existed, "
                + $"{matAssets.Count} material asset(s). Folder: {Folder}. "
                + "Kinds not baked were not present in the scene - cast them and run again.");
            if (made > 0) EditorUtility.RevealInFinder(Folder);
        }

        static Material SaveMaterial(Material runtime, Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(runtime, out var saved) && saved != null) return saved;
            if (AssetDatabase.Contains(runtime)) return runtime; // already an asset

            var clone = Object.Instantiate(runtime);
            string name = string.IsNullOrEmpty(runtime.name) ? "Mote" : runtime.name.Replace("(Instance)", "").Trim();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MatFolder}/{name}.mat");
            AssetDatabase.CreateAsset(clone, path);
            cache[runtime] = clone;
            return clone;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }
    }
}
