using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S CONTINUE-FROM-HERE BUTTON (his ruling: "let me copy the
    /// CURRENT zombie and player, edit them, and never have your system
    /// recreate them wrongly again").
    ///
    /// In play mode, one click clones the LIVE assembled body — mesh, runtime
    /// materials (saved as real .mat assets), googly eyes, mouth, sockets,
    /// worn pieces, wand and grimoire — strips only the components the game
    /// must own (physics rigs, cloth, flavor scripts), and saves it as
    /// Resources/Custom/ZombieBody.prefab or PlayerBody.prefab.
    ///
    /// From then on the game ADOPTS the prefab instead of rebuilding those
    /// parts: his edits to it are law. Bake again anytime to re-capture.
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
                Debug.LogError("[SpellyZombie] No dressed zombie alive to bake — press Z, let one spawn, then bake.");
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
                Debug.LogError("[SpellyZombie] No built player body to bake — enter play with the character wired.");
                return;
            }
            Bake(rig.ModelGO, "PlayerBody");
        }

        static bool Playing()
        {
            if (Application.isPlaying) return true;
            Debug.LogError("[SpellyZombie] Baking captures the LIVE body — enter play mode first.");
            return false;
        }

        static void Bake(GameObject src, string bakeName)
        {
            if (src.GetComponentInChildren<DrawNode>() != null)
            {
                Debug.LogError($"[SpellyZombie] '{src.name}' carries INK — erase body ink before baking " +
                               "(or the doodles become part of the species).");
                return;
            }

            var clone = Object.Instantiate(src);
            clone.name = bakeName;

            // the game OWNS these — they re-add (or re-adopt) at runtime, and
            // baked copies would double up or serialize broken
            Strip<CharacterJoint>(clone);   // joints before their rigidbodies
            Strip<Cloth>(clone);
            Strip<Collider>(clone);
            Strip<Rigidbody>(clone);
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
            AssetDatabase.DeleteAsset(path); // re-bake = overwrite, that's the loop
            PrefabUtility.SaveAsPrefabAsset(clone, path, out bool ok);
            Object.DestroyImmediate(clone);
            AssetDatabase.SaveAssets();

            Debug.Log(ok
                ? $"[SpellyZombie] {bakeName} baked → {path} (materials in {MatDir}). " +
                  "Stop play, edit the prefab freely — meshes, materials, eyes, mouth, worn pieces. " +
                  "The game now adopts it as-is and only adds behavior. Bake again anytime."
                : $"[SpellyZombie] {bakeName} bake FAILED — see console.");
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
