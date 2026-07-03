using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie.EditorTools
{
    /// One-click graybox: "Spelly Zombie -> Build Test Scene" fills the open scene
    /// with ground, walls, a stone block, pushable crates, a player and the
    /// DrawingWorld. Re-running it rebuilds from scratch (everything it makes is
    /// prefixed SZ_).
    public static class TestSceneBuilder
    {
        [MenuItem("Spelly Zombie/Build Test Scene")]
        public static void Build()
        {
            // clear previous build
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (go != null && go.scene.IsValid() && go.transform.parent == null && go.name.StartsWith("SZ_"))
                    Object.DestroyImmediate(go);

            // mute any template camera so ours is the only one
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.SetActive(false);

            // ---- light ----
            var sun = new GameObject("SZ_Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ---- geometry ----
            MakeBlock("SZ_Ground", PrimitiveType.Plane, new Vector3(0, 0, 0), new Vector3(4, 1, 4), new Color(0.62f, 0.62f, 0.60f));
            MakeBlock("SZ_Wall_North", PrimitiveType.Cube, new Vector3(0, 1.5f, 8), new Vector3(12, 3, 0.4f), new Color(0.75f, 0.72f, 0.66f));
            MakeBlock("SZ_Wall_West", PrimitiveType.Cube, new Vector3(-8, 1.5f, 2), new Vector3(0.4f, 3, 12), new Color(0.75f, 0.72f, 0.66f));
            MakeBlock("SZ_StoneBlock", PrimitiveType.Cube, new Vector3(3, 1, 3), new Vector3(3, 2, 1.5f), new Color(0.45f, 0.45f, 0.50f));

            // ---- pushable crates (proximity-seal test) ----
            Vector3[] cratePos =
            {
                new Vector3(-2.0f, 0.5f, 2.0f),
                new Vector3(-3.5f, 0.5f, 2.2f),
                new Vector3(-2.6f, 0.5f, 3.4f),
                new Vector3(-4.2f, 0.5f, 3.6f)
            };
            for (int i = 0; i < cratePos.Length; i++)
            {
                var crate = MakeBlock($"SZ_Crate_{i}", PrimitiveType.Cube, cratePos[i], Vector3.one * 0.9f, new Color(0.72f, 0.55f, 0.34f));
                var rb = crate.AddComponent<Rigidbody>();
                rb.mass = 5f;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 2f;
            }

            // ---- drawing world ----
            var world = new GameObject("SZ_DrawingWorld");
            world.AddComponent<DrawingWorld>();

            // ---- emote mannequin: self-drawing / custom-emote test dummy ----
            var mannequin = new GameObject("SZ_Mannequin");
            mannequin.transform.position = new Vector3(0.9f, 0f, 0.9f);
            mannequin.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            mannequin.AddComponent<PersistentInkSurface>();

            var skin = new Color(0.85f, 0.70f, 0.55f);
            var shirt = new Color(0.35f, 0.55f, 0.75f);
            MakeChild(mannequin.transform, "Legs", new Vector3(0f, 0.35f, 0f), new Vector3(0.35f, 0.7f, 0.25f), shirt);
            MakeChild(mannequin.transform, "Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.55f, 0.9f, 0.28f), shirt);

            var neck = new GameObject("NeckPivot");
            neck.transform.SetParent(mannequin.transform, false);
            neck.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            MakeChild(neck.transform, "Head", new Vector3(0f, 0.18f, -0.03f), Vector3.one * 0.3f, skin);

            var shoulderL = new GameObject("ShoulderPivot.L");
            shoulderL.transform.SetParent(mannequin.transform, false);
            shoulderL.transform.localPosition = new Vector3(-0.42f, 1.5f, 0.20f);
            MakeChild(shoulderL.transform, "Arm.L", new Vector3(0f, -0.375f, 0f), new Vector3(0.12f, 0.75f, 0.12f), skin);

            var shoulderR = new GameObject("ShoulderPivot.R");
            shoulderR.transform.SetParent(mannequin.transform, false);
            shoulderR.transform.localPosition = new Vector3(0.42f, 1.5f, 0.20f);
            MakeChild(shoulderR.transform, "Arm.R", new Vector3(0f, -0.375f, 0f), new Vector3(0.12f, 0.75f, 0.12f), skin);

            var rig = mannequin.AddComponent<EmoteRig>();
            rig.Joints.Add(new EmoteRig.JointEntry { Id = "shoulder.L", T = shoulderL.transform });
            rig.Joints.Add(new EmoteRig.JointEntry { Id = "shoulder.R", T = shoulderR.transform });
            rig.Joints.Add(new EmoteRig.JointEntry { Id = "neck", T = neck.transform });
            mannequin.AddComponent<EmotePlayer>();

            // ---- player ----
            var player = new GameObject("SZ_Player");
            player.transform.position = new Vector3(0, 1.05f, -5);
            player.layer = 2; // Ignore Raycast: the pen must not hit our own capsule
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;

            var camGo = new GameObject("SZ_Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0.65f, 0);
            camGo.tag = "MainCamera";
            var camLocal = camGo.AddComponent<Camera>();
            camLocal.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var controller = player.AddComponent<SimpleFPSController>();
            controller.CameraPivot = camGo.transform;
            var drawer = player.AddComponent<SurfaceDrawer>();
            drawer.Cam = camLocal;
            var poser = player.AddComponent<PoseEditor>();
            poser.Cam = camLocal;

            Selection.activeGameObject = player;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SpellyZombie] Test scene built. Press Play: LMB draw, Alt precision cursor, R erase, B pose editor, T/1-9 emotes, F1-F12 record rune templates.");
        }

        static GameObject MakeBlock(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var mat = new Material(shader) { color = color };
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }
            return go;
        }

        static GameObject MakeChild(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                go.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
            return go;
        }

        [MenuItem("Spelly Zombie/Delete Saved Rune Templates")]
        public static void DeleteTemplates()
        {
            RuneLibrary.DeleteRecordings();
            Debug.Log("[SpellyZombie] Recorded rune templates deleted — defaults restored on next classify.");
        }

        [MenuItem("Spelly Zombie/Delete Saved Emotes")]
        public static void DeleteEmotes()
        {
            EmoteLibrary.DeleteSave();
            Debug.Log("[SpellyZombie] Saved emotes deleted — default 'Arm raise' reseeded in slot 1 on next use.");
        }
    }
}
