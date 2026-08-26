using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// POSES, NOT MODELS. A particle shape is the blob with its bones moved -
    /// so a shape asset only has to remember WHERE THE BONES GO. It carries no
    /// mesh, no renderer, no material and no Animator, because nothing ever
    /// instantiates it: SpellParticle reads the bone transforms off it by name
    /// and eases its own bones there.
    ///
    /// Seven empties instead of a whole blob per spell, and no animation
    /// clips to keep in step with the rig.
    public static class PoseTools
    {
        const string PoseFolder = "Assets/_Game/Prefabs/Particle Shapes";

        /// Pose the blob in the scene, select it, run this. The rig is copied
        /// out on its own and saved under the name you give it.
        [MenuItem("Spelly Zombie/Particles/Capture Pose From Selection")]
        static void Capture()
        {
            var src = Selection.activeGameObject;
            if (src == null)
            {
                Debug.LogWarning("[SpellyZombie] Select the posed blob in the scene first.");
                return;
            }

            var rig = FindRig(src.transform);
            if (rig == null)
            {
                Debug.LogWarning($"[SpellyZombie] {src.name} has no bone rig under it - " +
                    "select the blob itself, not the mesh.", src);
                return;
            }

            string name = src.name.Replace("(Clone)", "").Trim();
            Directory.CreateDirectory(PoseFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{PoseFolder}/{name}.prefab");

            var copy = new GameObject(name);
            CopyBones(rig, copy.transform);
            PrefabUtility.SaveAsPrefabAsset(copy, path);
            Object.DestroyImmediate(copy);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SpellyZombie] pose saved: {path}\n" +
                "Drop it into CollectionManager > Particle Shapes. The name IS the key - " +
                "it must match the spell or axis it is for.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// The rig root: whatever holds the bones the skinned mesh uses.
        static Transform FindRig(Transform root)
        {
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.rootBone != null) return smr.rootBone;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("_Rig") || t.name == "Root") return t;
            return null;
        }

        /// Bones only - names and local transforms, nothing else comes across.
        static void CopyBones(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
            to.name = from.name;
            foreach (Transform child in from)
            {
                var t = new GameObject(child.name).transform;
                t.SetParent(to, false);
                CopyBones(child, t);
            }
        }

        // ------------------------------------------------------------------
        /// Blockout poses for every axis and every spell, generated from the
        /// blob's own rig so the bone names always match. These are STARTING
        /// POINTS to be dragged into shape by hand - they get the silhouette
        /// roughly right and nothing more.
        [MenuItem("Spelly Zombie/Particles/Generate Blockout Poses")]
        static void Blockouts()
        {
            var blob = CollectionManager.ParticleBlob;
            if (blob == null)
            {
                Debug.LogWarning("[SpellyZombie] CollectionManager has no Particle Blob assigned.");
                return;
            }
            var rig = FindRig(blob.transform);
            if (rig == null)
            {
                Debug.LogWarning("[SpellyZombie] The blob has no rig under it.");
                return;
            }

            Directory.CreateDirectory(PoseFolder);
            var made = new List<string>();

            foreach (var kv in Shapes)
            {
                var copy = new GameObject(kv.Key);
                CopyBones(rig, copy.transform);
                copy.name = kv.Key;
                Apply(copy.transform, kv.Value);

                string path = $"{PoseFolder}/{kv.Key}.prefab";
                if (File.Exists(path)) { Object.DestroyImmediate(copy); continue; }  // never clobber yours
                PrefabUtility.SaveAsPrefabAsset(copy, path);
                Object.DestroyImmediate(copy);
                made.Add(kv.Key);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SpellyZombie] blockout poses written: {made.Count}\n  " +
                string.Join(", ", made) +
                "\nExisting files were left alone. Drop them into Particle Shapes and reshape by hand.");
        }

        /// up · out · twist. Tall and thin, squat and wide, and how much the
        /// side bones swing round - enough to tell the silhouettes apart.
        static readonly Dictionary<string, Vector3> Shapes = new Dictionary<string, Vector3>
        {
            // the twelve axes
            { "Heat",     new Vector3( 1.5f, 0.7f,  15f) },  // licking upward
            { "Chill",    new Vector3( 0.6f, 1.3f, -25f) },  // squat, spiky
            { "Light",    new Vector3( 1.2f, 1.4f,   0f) },  // full and open
            { "Dark",     new Vector3( 0.7f, 0.6f,   0f) },  // drawn in
            { "Sticky",   new Vector3( 0.5f, 1.5f,   0f) },  // flattened, spread
            { "Slick",    new Vector3( 1.1f, 0.8f,  40f) },  // sheared
            { "Compress", new Vector3( 0.4f, 0.5f,   0f) },  // small and dense
            { "Spread",   new Vector3( 1.8f, 1.8f,   0f) },  // blown out
            { "Solid",    new Vector3( 1.0f, 1.0f,   0f) },  // the plain blob
            { "Liquid",   new Vector3( 0.7f, 1.2f,   8f) },  // sagging
            { "Attract",  new Vector3( 1.6f, 0.4f,  55f) },  // funnel: tall, pinched, twisted
            { "Repel",    new Vector3( 0.5f, 1.7f, -55f) },  // burst outward

            // spells with a silhouette worth having
            { "Flame",        new Vector3( 1.7f, 0.6f,  20f) },
            { "Lightning",    new Vector3( 2.0f, 0.3f,   0f) },  // a jagged spike
            { "Meteor",       new Vector3( 1.3f, 1.1f,   0f) },  // a lump with a tail
            { "Goo",          new Vector3( 0.5f, 1.4f,   0f) },  // a splat
            { "Plasma",       new Vector3( 1.4f, 1.4f,  30f) },
            { "Steam",        new Vector3( 1.5f, 1.5f,  12f) },
            { "Cloud",        new Vector3( 1.0f, 1.9f,   0f) },
            { "Hook",         new Vector3( 1.9f, 0.35f,  0f) },  // a barb
            { "Meteor Shower",new Vector3( 1.2f, 1.0f,   0f) },
            { "Debris",       new Vector3( 0.6f, 0.6f,   0f) },
        };

        /// Bones are named by the direction they push - D_Up, D_Dn, D_Xp and so
        /// on - so a pose is "how far up, how far out, how much round".
        static void Apply(Transform rig, Vector3 s)
        {
            float up = s.x, out_ = s.y, twist = s.z;
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!n.StartsWith("D_")) continue;
                Vector3 p = t.localPosition;

                bool vertical = n.EndsWith("Up") || n.EndsWith("Dn");
                t.localPosition = vertical ? p * up : p * out_;

                if (!vertical && Mathf.Abs(twist) > 0.01f)
                    t.localRotation = Quaternion.Euler(0f, twist, 0f) * t.localRotation;
            }
        }
    }
}
