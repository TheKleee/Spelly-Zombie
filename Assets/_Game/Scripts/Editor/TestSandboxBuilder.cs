using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// TEST SANDBOX — built at Marko's explicit request (July 12) for feature
    /// testing: floor, one crate, one slide-weapon, and a BEAN PLAYER with
    /// FAKE POSEABLE ARMS (EmoteRig joints — stand-ins for the real model's
    /// bones later). Tests: Tab 1st↔3rd person, weapon slots (E/F/1-2-3),
    /// R draw modes (weapon engraving / body paint), emotes (3rd person,
    /// F cancels, X reverts a slot), pose editing (B).
    ///
    /// SAFETY: refuses to run in any scene that contains real content
    /// (SZ_Menu / SZ_Village / SZ_GameMap / an existing player). Use
    /// File → New Scene first.
    public static class TestSandboxBuilder
    {
        [MenuItem("Spelly Zombie/Build TEST Sandbox (empty scene only)")]
        public static void Build()
        {
            foreach (var guardName in new[] { "SZ_Menu", "SZ_Village", "SZ_GameMap", "SZ_Player", "SZ_Test" })
                if (GameObject.Find(guardName) != null)
                {
                    Debug.LogError($"[SpellyZombie] REFUSING to build: this scene contains '{guardName}'. " +
                                   "The sandbox only builds into a fresh empty scene (File → New Scene).");
                    return;
                }

            EnvironmentTools.WireFxLibrary();
            var root = new GameObject("SZ_Test").transform;

            // template cameras off — the player's camera takes over
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.SetActive(false);

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("SZ_Sun");
                sun.transform.SetParent(root, false);
                var light = sun.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // floor: one big drawable surface (single collider = seamless ink)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            var floorShader = Shader.Find("Universal Render Pipeline/Lit");
            if (floorShader != null)
                floor.GetComponent<Renderer>().sharedMaterial =
                    new Material(floorShader) { color = new Color(0.62f, 0.62f, 0.6f) };
            floor.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

            // the one object in the middle: a breakable wooden crate
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.transform.SetParent(root, false);
            crate.transform.position = new Vector3(0f, 0.5f, 0f);
            crate.transform.localScale = Vector3.one * 0.95f;
            if (floorShader != null)
                crate.GetComponent<Renderer>().sharedMaterial =
                    new Material(floorShader) { color = new Color(0.72f, 0.55f, 0.34f) };
            crate.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            var crateRb = crate.AddComponent<Rigidbody>();
            crateRb.mass = 5f;
            crateRb.linearDamping = 1.2f;
            crate.AddComponent<Damageable>().Health = 40f;
            crate.AddComponent<Breakable>();

            new GameObject("SZ_DrawingWorld").AddComponent<DrawingWorld>();

            // one of each weapon: slide tablet (right) + rune chamber (left)
            var weapon = SealWeapon.CreatePickup(new Vector3(2.2f, 0f, 0f));
            weapon.transform.SetParent(root, true);
            var chamber = RuneChamberWeapon.CreatePickup(new Vector3(-2.2f, 0f, 0f));
            chamber.transform.SetParent(root, true);

            BuildBeanPlayer(root);

            Selection.activeGameObject = GameObject.Find("SZ_Player");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SpellyZombie] TEST sandbox ready. 1st person: LMB draw (wand only) · Alt precision " +
                      "· E pickup / F drop / 1-2-3 weapon slots · LMB-or-Q slide trigger · R engrave weapon. " +
                      "Tab = 3rd person: 1-9 emotes (frozen till F, X reverts slot) · R body paint · B pose studio. " +
                      "In draw modes: MMB-drag orbits, scroll zooms.");
        }

        /// The bean: CharacterController rig + visible egg body + FAKE ARMS on
        /// shoulder pivots and a head on a neck pivot — all EmoteRig joints,
        /// exactly the ids the real boned model will use later
        /// (shoulder.L / shoulder.R / neck).
        public static void BuildBeanPlayer(Transform root) // PhotoBoothBuilder reuses the whole player rig
        {
            var player = new GameObject("SZ_Player");
            player.transform.position = new Vector3(0f, 1.05f, -5f);
            player.layer = 2; // Ignore Raycast — the pen must not hit our own body
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;

            var beanShader = Shader.Find("Universal Render Pipeline/Lit");
            var skin = new Color(0.93f, 0.87f, 0.72f);

            // the visible bean body (no collider — the CC is the collider)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Bean";
            body.layer = 2;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            body.transform.localScale = new Vector3(0.85f, 0.9f, 0.85f);
            if (beanShader != null)
                body.GetComponent<Renderer>().sharedMaterial = new Material(beanShader) { color = skin };

            // FAKE ARMS + head — poseable stand-ins for the future bones
            var shoulderL = new GameObject("ShoulderPivot.L");
            shoulderL.transform.SetParent(player.transform, false);
            shoulderL.transform.localPosition = new Vector3(-0.45f, 0.45f, 0.05f);
            MakeLimb(shoulderL.transform, "Arm.L", new Vector3(0f, -0.35f, 0f),
                new Vector3(0.13f, 0.7f, 0.13f), skin * 0.95f, beanShader);

            var shoulderR = new GameObject("ShoulderPivot.R");
            shoulderR.transform.SetParent(player.transform, false);
            shoulderR.transform.localPosition = new Vector3(0.45f, 0.45f, 0.05f);
            MakeLimb(shoulderR.transform, "Arm.R", new Vector3(0f, -0.35f, 0f),
                new Vector3(0.13f, 0.7f, 0.13f), skin * 0.95f, beanShader);

            var neck = new GameObject("NeckPivot");
            neck.transform.SetParent(player.transform, false);
            neck.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            MakeLimb(neck.transform, "Head", new Vector3(0f, 0.24f, -0.02f),
                Vector3.one * 0.34f, skin, beanShader);

            var rig = player.AddComponent<EmoteRig>();
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "shoulder.L",
                T = shoulderL.transform,
                GrabHint = GrabHint(shoulderL.transform, "Grab.HandL", new Vector3(0f, -0.68f, 0f))
            });
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "shoulder.R",
                T = shoulderR.transform,
                GrabHint = GrabHint(shoulderR.transform, "Grab.HandR", new Vector3(0f, -0.68f, 0f))
            });
            rig.Joints.Add(new EmoteRig.JointEntry
            {
                Id = "neck",
                T = neck.transform,
                GrabHint = GrabHint(neck.transform, "Grab.Head", new Vector3(0f, 0.26f, 0f))
            });
            player.AddComponent<EmotePlayer>();
            player.AddComponent<WeaponSlots>();          // 1 wand · 2-3 pickups (E/F)
            player.AddComponent<SelfPaint>();            // R = paint your own body
            player.AddComponent<PersistentInkSurface>(); // body ink is forever

            // first-person camera on a pivot; Tab pulls it back to third person
            var camGo = new GameObject("SZ_Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            var controller = player.AddComponent<SimpleFPSController>();
            controller.CameraPivot = camGo.transform;
            var drawer = player.AddComponent<SurfaceDrawer>();
            drawer.Cam = cam;

            // pose studio (the emote editor): B anywhere, or R in third person
            var studioCamGo = new GameObject("SZ_StudioCamera");
            studioCamGo.transform.SetParent(root, false);
            var studioCam = studioCamGo.AddComponent<Camera>();
            studioCam.nearClipPlane = 0.05f;
            studioCamGo.AddComponent<AudioListener>();
            studioCamGo.SetActive(false);
            var studio = player.AddComponent<PoseStudio>();
            studio.StudioCamera = studioCam;
            studio.Target = rig;
        }

        static void MakeLimb(Transform parent, string limbName, Vector3 localPos, Vector3 scale,
            Color color, Shader shader)
        {
            var limb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            limb.name = limbName;
            limb.layer = 2; // pen ignores our own limbs
            limb.transform.SetParent(parent, false);
            limb.transform.localPosition = localPos;
            limb.transform.localScale = scale;
            if (shader != null)
                limb.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
        }

        static Transform GrabHint(Transform parent, string hintName, Vector3 localPos)
        {
            var hint = new GameObject(hintName);
            hint.transform.SetParent(parent, false);
            hint.transform.localPosition = localPos;
            return hint.transform;
        }
    }
}
