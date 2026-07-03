using System.Collections.Generic;
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
            var rig = BuildMannequinShared(new Vector3(0.9f, 0f, 0.9f), 200f);
            rig.gameObject.AddComponent<EmotePlayer>();

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
            // pose studio: menu-style posing screen, opened with B in the graybox
            var studioCamGo = new GameObject("SZ_StudioCamera");
            var studioCam = studioCamGo.AddComponent<Camera>();
            studioCam.nearClipPlane = 0.05f;
            studioCamGo.AddComponent<AudioListener>();
            studioCamGo.SetActive(false);
            var studio = player.AddComponent<PoseStudio>();
            studio.StudioCamera = studioCam;
            studio.Target = rig;

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

        /// Shared graybox character: torso, head on a neck pivot, two arms on
        /// shoulder pivots — all registered as named EmoteRig joints with grab
        /// hints at the hands/head for the Pose Studio's visible grab points.
        /// Used by the test scene, the main menu, and the island map builders.
        internal static EmoteRig BuildMannequinShared(Vector3 position, float yawDegrees)
        {
            var mannequin = new GameObject("SZ_Mannequin");
            mannequin.transform.position = position;
            mannequin.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
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
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "shoulder.L",
                T = shoulderL.transform,
                GrabHint = MakeGrabHint(shoulderL.transform, "Grab.HandL", new Vector3(0f, -0.72f, 0f))
            });
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "shoulder.R",
                T = shoulderR.transform,
                GrabHint = MakeGrabHint(shoulderR.transform, "Grab.HandR", new Vector3(0f, -0.72f, 0f))
            });
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "neck",
                T = neck.transform,
                GrabHint = MakeGrabHint(neck.transform, "Grab.Head", new Vector3(0f, 0.22f, 0f))
            });
            return rig;
        }

        static Transform MakeGrabHint(Transform parent, string name, Vector3 localPos)
        {
            var hint = new GameObject(name);
            hint.transform.SetParent(parent, false);
            hint.transform.localPosition = localPos;
            return hint.transform;
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

        /// Builds the main menu per the design sketch: title + server buttons on
        /// the left (MainMenu component), the player's character on a pedestal
        /// right-of-center with grab points, poseable via PoseStudio (AlwaysOpen).
        /// Open Assets/_Game/Scenes/Menu.unity first, then run this.
        [MenuItem("Spelly Zombie/Build Main Menu Scene")]
        public static void BuildMenu()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (go != null && go.scene.IsValid() && go.transform.parent == null && go.name.StartsWith("SZ_"))
                    Object.DestroyImmediate(go);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.SetActive(false);

            // ---- stage ----
            var sun = new GameObject("SZ_Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

            MakeBlock("SZ_Floor", PrimitiveType.Plane, Vector3.zero, new Vector3(4, 1, 4), new Color(0.24f, 0.25f, 0.29f));
            MakeBlock("SZ_Backdrop", PrimitiveType.Cube, new Vector3(0f, 2.5f, 2.2f), new Vector3(9f, 5f, 0.3f), new Color(0.16f, 0.17f, 0.22f));

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "SZ_Pedestal";
            pedestal.transform.position = new Vector3(0.55f, 0.06f, 0f);
            pedestal.transform.localScale = new Vector3(1.1f, 0.06f, 1.1f);
            var pedShader = Shader.Find("Universal Render Pipeline/Lit");
            if (pedShader != null)
                pedestal.GetComponent<Renderer>().sharedMaterial = new Material(pedShader) { color = new Color(0.38f, 0.38f, 0.44f) };

            var accent = new GameObject("SZ_AccentLight");
            var accentLight = accent.AddComponent<Light>();
            accentLight.type = LightType.Point;
            accentLight.intensity = 1.4f;
            accentLight.range = 7f;
            accentLight.color = new Color(1f, 0.95f, 0.85f);
            accent.transform.position = new Vector3(0f, 2.3f, -1.6f);

            // ---- the character, facing the camera ----
            var rig = BuildMannequinShared(new Vector3(0.55f, 0.12f, 0f), 180f);

            // ---- camera ----
            var camGo = new GameObject("SZ_MenuCamera");
            camGo.transform.position = new Vector3(0f, 1.35f, -2.6f);
            camGo.transform.LookAt(new Vector3(0.12f, 1.12f, 0f));
            camGo.tag = "MainCamera";
            var camLocal = camGo.AddComponent<Camera>();
            camLocal.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            // ---- menu logic ----
            var menu = new GameObject("SZ_MainMenu");
            menu.AddComponent<MainMenu>();
            var studio = menu.AddComponent<PoseStudio>();
            studio.StudioCamera = camLocal;
            studio.Target = rig;
            studio.AlwaysOpen = true;
            studio.ManageCameras = false;

            EnsureBuildScenes();
            Selection.activeGameObject = menu;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SpellyZombie] Main menu built. Press Play: pose your character, save poses, Create Server loads the Game scene.");
        }

        /// Scene loading by name needs Build Settings entries; keep them in sync.
        static void EnsureBuildScenes()
        {
            string[] wanted =
            {
                "Assets/_Game/Scenes/Menu.unity",
                "Assets/_Game/Scenes/Lobby.unity",
                "Assets/_Game/Scenes/Game.unity"
            };
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var path in wanted)
                if (System.IO.File.Exists(path) && !list.Exists(s => s.path == path))
                    list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
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
