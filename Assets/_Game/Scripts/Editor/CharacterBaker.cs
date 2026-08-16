using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// the CONTINUE-FROM-HERE BUTTON (the ruling: "let me copy the
    /// CURRENT zombie and player, edit them, and never have your system
    /// recreate them wrongly again").
    ///
    /// In play mode, one click clones the LIVE assembled body - mesh, runtime
    /// materials (saved as real .mat assets), googly eyes, mouth, sockets,
    /// worn pieces, wand and grimoire - strips only the components the game
    /// must own (physics rigs, cloth, flavor scripts), and saves it as
    /// Resources/Custom/ZombieBody.prefab or PlayerBody.prefab.
    ///
    /// From then on the game ADOPTS the prefab instead of rebuilding those
    /// parts: the edits to it are law. Bake again anytime to re-capture.
    public static class CharacterBaker
    {
        const string Dir = "Assets/_Game/Resources/Custom";
        const string MatDir = Dir + "/Materials";

        [MenuItem("Spelly Zombie/Bake ZOMBIE Body To Prefab (play mode)")]
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

        [MenuItem("Spelly Zombie/Bake PLAYER Body To Prefab (play mode)")]
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

        /// THE WHOLE PLAYER, NOT JUST THE BODY. "I don't want
        /// scripts to be created at run time... I feel like in multiplayer this
        /// would be felt even more. So having a prefab is a must." is right:
        /// FishNet wants registered prefabs, and a player assembled by code at
        /// spawn is awkward to replicate and impossible to inspect.
        ///
        /// Unlike the BODY bake this strips NOTHING. Colliders, the
        /// CharacterController, every script and the animator controller all
        /// survive, because the point is a player you can drop in a scene and
        /// press play on. It only turns the runtime-generated materials into
        /// real assets, which is the one thing that does not survive leaving
        /// play mode.
        ///
        /// CharacterRig adopts an existing "Body" child, so the saved prefab does
        /// NOT rebuild itself on load.
        [MenuItem("Spelly Zombie/Bake WHOLE PLAYER To Prefab (play mode)")]
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

            // STRIP WHAT THE RUNTIME REBUILDS. Baking these froze throwaway
            // state into the prefab and, worse, forced SaveRuntimeMaterials to
            // write every scratch MatterFX material out as an asset, which is
            // where the pile of "Universal..." materials came from.
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

        /// Runtime materials (MatterFX makes them with `new Material(...)`) are not
        /// assets, so a prefab referencing them loses them the moment play mode
        /// ends. Write each one out as a real file, keeping the live reference
        /// valid so the prefab points at the saved asset.
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

            // THE BAKE MUST NOT WELD THE FIT INTO THE SPECIES: the runtime
            // MULTIPLIES the per-kind width (charger 1.25 / runner 0.72) and
            // the flavor jitter onto the prefab's authored scale. Baking a
            // charger and re-baking would compound it (1.25  1.56  …) and
            // every walker would inherit it. Root scale returns to neutral;
            // the authored scale inside the prefab is untouched.
            clone.transform.localScale = Vector3.one;

            // the game OWNS these - they re-add (or re-adopt) at runtime, and
            // baked copies would double up or serialize broken
            Strip<CharacterJoint>(clone);   // joints before their rigidbodies
            Strip<Cloth>(clone);
            // COLLIDERS/BODIES: the PLAYER rig re-adopts them at runtime, but
            // nothing rebuilds them on a zombie - stripping there would eat
            // hand-authored physics on every re-bake, permanently.
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
            // AXIOM : NEVER delete the asset before the new one
            // exists - a failed save used to mean the prefab was simply GONE.
            // SaveAsPrefabAsset overwrites in place and keeps the GUID, so
            // every scene reference survives. Back up the old one first.
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
