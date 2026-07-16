using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// "Spelly Zombie → Wardrobe → Create Wardrobe Catalogs": makes the three
    /// SocketWardrobe assets (Player / Zombie / Demon) in _Game/Resources,
    /// pre-filled with empty slot rows for every socket so Marko only drags
    /// his Blender prefabs into the lists. Safe to re-run — existing assets
    /// are left untouched.
    public static class WardrobeCatalogs
    {
        static readonly string[] AllSockets =
        {
            "Hat", "Head", "Cape", "Chest", "Belt",
            "ShoulderL", "ShoulderR", "HandL", "HandR", "LegL", "LegR"
        };

        [MenuItem("Spelly Zombie/Wardrobe/Create Wardrobe Catalogs")]
        static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources"))
                AssetDatabase.CreateFolder("Assets/_Game", "Resources");

            int made = 0;
            made += Make("SZ_WardrobePlayer");
            made += Make("SZ_WardrobeZombie");
            made += Make("SZ_WardrobeDemon");
            AssetDatabase.SaveAssets();
            Debug.Log(made > 0
                ? $"[SpellyZombie] Created {made} wardrobe catalog(s) in Assets/_Game/Resources — drag prefabs into each socket's Options list (index 0 = default look)."
                : "[SpellyZombie] All three wardrobe catalogs already exist.");
        }

        static int Make(string name)
        {
            string path = $"Assets/_Game/Resources/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<SocketWardrobe>(path) != null) return 0;
            var so = ScriptableObject.CreateInstance<SocketWardrobe>();
            so.Slots = new List<SocketWardrobe.SocketSlot>();
            foreach (var s in AllSockets)
                so.Slots.Add(new SocketWardrobe.SocketSlot { Socket = s });
            AssetDatabase.CreateAsset(so, path);
            return 1;
        }

        // ---- one-click: selected scene object → prefab → catalog slot -------
        // Select Marko's model(s) in the scene (or prefabs in the Project),
        // pick a catalog; the socket is read from the NAME (WizardHat → Hat,
        // WizardCape → Cape...). Scene objects are saved into
        // _Game/Prefabs/Costume with their rotation/scale kept and their
        // stray scene position cleared so they sit ON the socket pivot.

        [MenuItem("Spelly Zombie/Wardrobe/Add Selection To PLAYER Catalog")]
        static void AddToPlayer() => AddSelection("SZ_WardrobePlayer");

        [MenuItem("Spelly Zombie/Wardrobe/Add Selection To ZOMBIE Catalog")]
        static void AddToZombie() => AddSelection("SZ_WardrobeZombie");

        [MenuItem("Spelly Zombie/Wardrobe/Add Selection To DEMON Catalog")]
        static void AddToDemon() => AddSelection("SZ_WardrobeDemon");

        static void AddSelection(string catalogName)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SocketWardrobe>(
                $"Assets/_Game/Resources/{catalogName}.asset");
            if (catalog == null)
            {
                Debug.LogError($"[SpellyZombie] {catalogName} is missing — run Spelly Zombie → Wardrobe → Create Wardrobe Catalogs first.");
                return;
            }
            if (Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[SpellyZombie] Nothing selected — pick your model(s) in the Hierarchy or Project first.");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Prefabs"))
                AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Prefabs/Costume"))
                AssetDatabase.CreateFolder("Assets/_Game/Prefabs", "Costume");

            int added = 0;
            foreach (var go in Selection.gameObjects)
            {
                string socket = InferSocket(go.name);
                if (socket == null)
                {
                    Debug.LogWarning($"[SpellyZombie] '{go.name}': can't tell the socket from the name — include one of Hat, Head, Cape, Chest, Belt, ShoulderL/R, HandL/R, LegL/R (or a word like helm/cloak/mask/torso) and retry.");
                    continue;
                }
                GameObject prefab = null;
                try { prefab = ToPrefab(go); }
                catch (System.Exception e)
                {
                    // one bad selection must not abort the rest of the batch
                    Debug.LogError($"[SpellyZombie] '{go.name}' couldn't be saved: {e.Message}");
                }
                if (prefab == null) continue;
                EnsureReadable(prefab); // cloth + on-body ink read vertices

                var slot = catalog.Slot(socket);
                if (slot == null)
                {
                    slot = new SocketWardrobe.SocketSlot { Socket = socket };
                    catalog.Slots.Add(slot);
                }
                if (slot.Options.Contains(prefab))
                {
                    Debug.Log($"[SpellyZombie] '{prefab.name}' is already in {catalogName} → {socket}.");
                    continue;
                }
                slot.Options.Add(prefab);
                added++;
                if (socket == "Cape") WarnIfFlat(prefab);
                Debug.Log($"[SpellyZombie] '{prefab.name}' → {catalogName} · {socket} · option {slot.Options.Count - 1}"
                    + (slot.Options.Count == 1 ? " (the DEFAULT look)" : ""));
            }
            if (added > 0)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
        }

        static string InferSocket(string name)
        {
            string n = name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
            // exact socket names win (covers the L/R pairs)
            foreach (var s in AllSockets)
                if (n.Contains(s.ToLowerInvariant())) return s;
            if (n.Contains("hat") || n.Contains("helm") || n.Contains("crown") || n.Contains("hood")) return "Hat";
            if (n.Contains("cape") || n.Contains("cloak")) return "Cape";
            if (n.Contains("mask") || n.Contains("beard") || n.Contains("glasses") || n.Contains("face")) return "Head";
            if (n.Contains("torso") || n.Contains("vest") || n.Contains("armor") || n.Contains("robe")) return "Chest";
            if (n.Contains("waist") || n.Contains("pouch")) return "Belt";
            return null;
        }

        const string CostumeDir = "Assets/_Game/Prefabs/Costume";

        static GameObject ToPrefab(GameObject go)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(go)) return go; // picked in Project

            if (PrefabUtility.IsPartOfNonAssetPrefabInstance(go))
            {
                // a child inside an instance can't be saved on its own —
                // take the whole piece instead of throwing mid-batch
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (outer != null && outer != go)
                {
                    Debug.Log($"[SpellyZombie] '{go.name}' is part of '{outer.name}' — saving the whole piece.");
                    go = outer;
                }
                // re-run on a piece already in the costume folder: UPDATE the
                // existing prefab with the scene tweaks instead of minting
                // 'Name 1.prefab' duplicates forever
                string existing = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(existing) &&
                    existing.Replace('\\', '/').StartsWith(CostumeDir + "/"))
                {
                    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);
                    SyncRoot(existing, go.transform); // root pos/rot/scale aren't overrides
                    Debug.Log($"[SpellyZombie] '{go.name}' updated its existing prefab ({existing}).");
                    return AssetDatabase.LoadAssetAtPath<GameObject>(existing);
                }
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{CostumeDir}/{go.name}.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                go, path, InteractionMode.UserAction);
            if (prefab == null)
            {
                Debug.LogError($"[SpellyZombie] Couldn't save '{go.name}' as a prefab.");
                return null;
            }
            SyncRoot(path, go.transform);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// The asset root's transform is what Lock() instantiates at the
        /// socket: rotation and scale are Marko's orientation fixes and come
        /// from the scene object; position is just where it sat in the level —
        /// junk once the piece locks on, so it's cleared.
        static void SyncRoot(string path, Transform sceneRoot)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            var t = contents.transform;
            if (t.localPosition != Vector3.zero
                || t.localRotation != sceneRoot.localRotation
                || t.localScale != sceneRoot.localScale)
            {
                t.localPosition = Vector3.zero;
                t.localRotation = sceneRoot.localRotation;
                t.localScale = sceneRoot.localScale;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// Cloth and on-body ink READ VERTICES at runtime — Blender exports
        /// default to Read/Write OFF and would throw mid-dressing. Flip the
        /// source model's import setting once, here, automatically.
        static void EnsureReadable(GameObject prefab)
        {
            var meshes = new List<Mesh>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) meshes.Add(smr.sharedMesh);

            foreach (var mesh in meshes)
            {
                if (mesh.isReadable) continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                importer.isReadable = true;
                importer.SaveAndReimport();
                Debug.Log($"[SpellyZombie] '{System.IO.Path.GetFileName(path)}': Read/Write enabled (cloth/ink need it).");
            }
        }

        /// Cloth pins the TOP 15% of verts by local Y — a cape authored lying
        /// flat has its whole surface inside that band and turns into a rigid
        /// board. Catch it at import time instead of in-game.
        static void WarnIfFlat(GameObject prefab)
        {
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            Vector3 ext = Vector3.Scale(mesh.bounds.size, mf.transform.lossyScale);
            ext = new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z));
            float wide = Mathf.Max(ext.x, ext.z);
            if (ext.y < wide * 0.6f)
                Debug.LogWarning($"[SpellyZombie] '{prefab.name}' looks FLAT ({ext.x:0.00} x {ext.y:0.00} x {ext.z:0.00}) — a cape must HANG: tall in Y, pivot at the top where it meets the shoulders, at least a 12x16 vertex grid. Rotate/re-export and it'll flow.");
            if (mesh.vertexCount < 12)
                Debug.LogWarning($"[SpellyZombie] '{prefab.name}' has only {mesh.vertexCount} vertices — cloth needs 12+, and it only bends where there are vertices. Subdivide it (a 12x16 grid moves nicely).");
        }
    }
}
