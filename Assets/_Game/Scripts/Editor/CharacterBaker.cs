using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// In play mode, clones the live assembled body, strips components the game
    /// re-adds at runtime, saves runtime materials as real .mat assets, and
    /// writes Resources/Custom/*.prefab. The game adopts the prefab as-is.
    public static class CharacterBaker
    {
        const string Dir = "Assets/_Game/Resources/Custom";
        const string MatDir = Dir + "/Materials";

        [MenuItem("Spelly Zombie/Legacy/Bake ZOMBIE Body To Prefab (play mode)")]
        public static void BakeZombie()
        {
            if (!Playing()) return;
            GameObject src = null;
            foreach (var z in Zombie.All)
                if (z != null && z.Dress != null && z.Dress.BodyGO != null)
                {
                    src = z.Dress.BodyGO;
                    break;
                }
            if (src == null)
            {
                Debug.LogError("[SpellyZombie] No dressed zombie alive to bake. Press Z, let one spawn, then bake.");
                return;
            }
            Bake(src, "ZombieBody");
        }

        [MenuItem("Spelly Zombie/Legacy/Bake PLAYER Body To Prefab (play mode)")]
        public static void BakePlayer()
        {
            if (!Playing()) return;
            var pilot = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var rig = pilot != null ? pilot.GetComponent<CharacterRig>() : null;
            if (rig == null || rig.ModelGO == null)
            {
                Debug.LogError("[SpellyZombie] No built player body to bake. Enter play with the character wired.");
                return;
            }
            Bake(rig.ModelGO, "PlayerBody");
        }

        /// Bakes the whole player. Unlike the body bake this strips nothing; it
        /// only turns runtime-generated materials into real assets. CharacterRig
        /// adopts an existing "Body" child, so the saved prefab does not rebuild.
        [MenuItem("Spelly Zombie/Legacy/Bake WHOLE PLAYER To Prefab (play mode)")]
        public static void BakeWholePlayer()
        {
            if (!Playing()) return;
            var pilot = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (pilot == null)
            {
                Debug.LogError("[SpellyZombie] No player in the scene to bake.");
                return;
            }

            var src = pilot.gameObject;
            if (src.GetComponentInChildren<DrawNode>() != null)
            {
                Debug.LogError("[SpellyZombie] The player carries INK. Erase body ink before baking, " +
                               "or the doodles become part of the prefab.");
                return;
            }

            var clone = Object.Instantiate(src);
            clone.name = "Player";

            // strip what the runtime rebuilds:
            //   Ink     - WandInk's ink column, regenerated from the ink level
            //   Shapes  - ShapeShift's worn-prop holder, per round
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == clone.transform) continue;
                if (t.name == "Ink" || t.name == "Shapes")
                    Object.DestroyImmediate(t.gameObject);
            }

            SaveRuntimeMaterials(clone);

            string path = $"{Dir}/Player.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                System.IO.Directory.CreateDirectory($"{Dir}/_backup");
                AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(
                    $"{Dir}/_backup/Player_{System.DateTime.Now:yyyyMMdd_HHmm}.prefab"));
            }

            PrefabUtility.SaveAsPrefabAsset(clone, path, out bool ok);
            Object.DestroyImmediate(clone);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (ok) Debug.Log($"[SpellyZombie] Whole player baked to {path}. " +
                              "Drag it into a scene in place of SZ_Player.");
            else Debug.LogError($"[SpellyZombie] Failed to save {path} (the old one is untouched).");
        }

        /// Runtime materials are not assets and die when play mode ends. Writes
        /// each one out as a .mat file; the live reference stays valid.
        static void SaveRuntimeMaterials(GameObject clone)
        {
            System.IO.Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();
            foreach (var rend in clone.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                foreach (var m in mats)
                {
                    if (m == null || AssetDatabase.Contains(m)) continue;
                    string matPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{MatDir}/{Sanitize(m.name)}.mat");
                    AssetDatabase.CreateAsset(m, matPath);
                }
                rend.sharedMaterials = mats;
            }
        }

        static bool Playing()
        {
            if (Application.isPlaying) return true;
            Debug.LogError("[SpellyZombie] Baking captures the LIVE body. Enter play mode first.");
            return false;
        }

        static void Bake(GameObject src, string bakeName)
        {
            if (src.GetComponentInChildren<DrawNode>() != null)
            {
                Debug.LogError($"[SpellyZombie] '{src.name}' carries INK. Erase body ink before baking " +
                               "(or the doodles become part of the species).");
                return;
            }

            var clone = Object.Instantiate(src);
            clone.name = bakeName;

            // the runtime multiplies per-kind width and flavor jitter onto the
            // prefab's authored scale; re-baking would compound it. Root scale
            // resets to neutral, authored scale inside the prefab is untouched.
            clone.transform.localScale = Vector3.one;

            // the game OWNS these - they re-add (or re-adopt) at runtime, and
            // baked copies would double up or serialize broken
            Strip<CharacterJoint>(clone);   // joints before their rigidbodies
            Strip<Cloth>(clone);
            // the player rig re-adopts colliders/bodies at runtime; nothing
            // rebuilds them on a zombie, so they are kept there
            if (bakeName != "ZombieBody")
            {
                Strip<Collider>(clone);
                Strip<Rigidbody>(clone);
            }
            Strip<ZombieFlavor>(clone);
            Strip<SocketSet>(clone);
            Strip<HandIK>(clone);
            foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
                anim.runtimeAnimatorController = null; // runtime wires the right set

            System.IO.Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();
            foreach (var rend in clone.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                foreach (var m in mats)
                {
                    if (m == null || AssetDatabase.Contains(m)) continue;
                    string matPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{MatDir}/{Sanitize(m.name)}.mat");
                    AssetDatabase.CreateAsset(m, matPath); // live reference stays valid
                }
                rend.sharedMaterials = mats;
            }

            string path = $"{Dir}/{bakeName}.prefab";
            // never delete the asset before the new one exists. SaveAsPrefabAsset
            // overwrites in place and keeps the GUID; back up the old one first.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                System.IO.Directory.CreateDirectory($"{Dir}/_backup");
                AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(
                    $"{Dir}/_backup/{bakeName}_{System.DateTime.Now:yyyyMMdd_HHmm}.prefab"));
            }
            PrefabUtility.SaveAsPrefabAsset(clone, path, out bool ok);
            Object.DestroyImmediate(clone);
            AssetDatabase.SaveAssets();

            Debug.Log(ok
                ? $"[SpellyZombie] {bakeName} baked → {path} (materials in {MatDir}). " +
                  "Stop play, edit the prefab freely: meshes, materials, eyes, mouth, worn pieces. " +
                  "The game now adopts it as-is and only adds behavior. Bake again anytime."
                : $"[SpellyZombie] {bakeName} bake FAILED. See console.");
        }

        static void Strip<T>(GameObject root) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
                Object.DestroyImmediate(c);
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
