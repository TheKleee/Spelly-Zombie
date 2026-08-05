using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpellyZombie
{
    /// One-click wiring of Marko's Mixamo-rigged character: finds SZ_Body.fbx,
    /// enforces sane import settings, retargets every Mixamo animation clip to
    /// its avatar (looping on), builds the locomotion AnimatorController
    /// (idle/walk/run blend by Speed + separate Crouch, IK pass on for weapon
    /// holding) and points CharacterLibrary at all of it.
    public static class CharacterWizard
    {
        const string LibPath = "Assets/_Game/Resources/CharacterLibrary.asset";
        const string CtrlPath = "Assets/_Game/Art/SZ_Wizard.controller";

        [MenuItem("Spelly Zombie/Build Character Rig")]
        public static void Build()
        {
            string fbxPath = null;
            foreach (var guid in AssetDatabase.FindAssets("SZ_Body t:Model", new[] { "Assets/_Game" }))
            {
                fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                break;
            }
            if (fbxPath == null)
            {
                Debug.LogError("[SpellyZombie] SZ_Body.fbx not found under Assets/_Game — import the Mixamo T-pose first.");
                return;
            }

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null)
            {
                // THE BODY'S RIG BELONGS TO MARKO (his ruling after the wizard
                // "ruined the character again"): force-converting to Humanoid
                // let Unity's AUTOMATIC bone-mapping + T-pose enforcement
                // reconfigure his edited skeleton — that automatic mangling
                // was the ruin. The wizard now NEVER touches rig settings.
                // Not Humanoid yet? Stop and say exactly what to do by hand,
                // where he can SEE the mapping.
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    Debug.LogError(
                        "[SpellyZombie] SZ_Body is not set up as Humanoid — and the wizard will NOT " +
                        "convert it (automatic conversion is what kept ruining the character). " +
                        "One-time manual setup: select SZ_Body.fbx → Inspector → Rig tab → " +
                        "Animation Type: Humanoid → Apply. Then click Configure… and CHECK the bone " +
                        "mapping (especially the shoulders you edited) before pressing Done. " +
                        "Re-run Build Character Rig afterwards — it only READS the rig from now on.");
                    return;
                }
                bool dirty = false;
                if (importer.importCameras || importer.importLights)
                {
                    importer.importCameras = false;
                    importer.importLights = false;
                    dirty = true;
                }
                if (!importer.isReadable)
                {
                    // body paint bakes the skinned mesh into a collider shell —
                    // BakeMesh needs CPU-readable vertices or it throws.
                    // (Mesh-access flag only; the rig is never modified.)
                    importer.isReadable = true;
                    dirty = true;
                }
                if (dirty) importer.SaveAndReimport();
            }

            // the body's avatar — every clip retargets to THIS skeleton
            Avatar avatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is Avatar av) { avatar = av; break; }

            // ---- gather + configure the PLAYER animation FBXs. Precise slot
            // matching: "Left Strafe Walking" must never claim the walk slot
            // (that's what made walking play a strafe). Zombie folder excluded.
            var picks = new System.Collections.Generic.Dictionary<string, string>();
            void Consider(string slot, string path)
            {
                if (!picks.TryGetValue(slot, out var cur) || path.Length < cur.Length)
                    picks[slot] = path; // shortest name beats "(2)" duplicates
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == fbxPath || path.Contains("/Zombie/")) continue;
                string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                if (file.Contains("crouch"))
                {
                    if (file.Contains("left")) Consider("crouchL", path);
                    else if (file.Contains("right")) Consider("crouchR", path);
                    else if (file.Contains("back")) Consider("crouchBack", path);
                    else if (file.Contains("idle")) Consider("crouchIdle", path);
                    else Consider("crouch", path);
                }
                else if (file.Contains("jump"))
                {
                    // ORDER MATTERS: "JumpRun" also contains "run" — without
                    // this early branch it would eat the run LOCOMOTION slot
                    Consider(file.Contains("run") ? "jumpRun" : "jump", path);
                }
                else if (file.Contains("strafe"))
                {
                    bool walking = file.Contains("walking");
                    if (file.Contains("left")) Consider(walking ? "strafeWalkL" : "strafeRunL", path);
                    else if (file.Contains("right")) Consider(walking ? "strafeWalkR" : "strafeRunR", path);
                }
                else if (file.Contains("backward"))
                    Consider(file.Contains("run") ? "runBack" : "walkBack", path);
                else if (file.Contains("run")) Consider("run", path);
                else if (file.Contains("idle")) Consider("idle", path);
                else if (file.Contains("walk")) Consider("walk", path);
            }

            var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (var pair in picks)
            {
                ConfigureClipFbx(pair.Value, avatar);
                var clip = LoadClip(pair.Value);
                if (clip != null) clips[pair.Key] = clip;
            }

            RuntimeAnimatorController ctrl = clips.Count > 0 ? BuildController(clips) : null;
            var zombieCtrl = BuildZombieSet(avatar);

            // ---- costume pool: Marko drops prefabs into Prefabs/Costume,
            // named "<Socket>_<Name>" (players) or "Z<Socket>_<Name>" (zombies)
            System.IO.Directory.CreateDirectory("Assets/_Game/Prefabs/Costume");
            System.IO.Directory.CreateDirectory("Assets/_Game/Prefabs/Weapons");
            System.IO.Directory.CreateDirectory("Assets/_Game/Resources");
            const string costumePath = "Assets/_Game/Resources/CostumeLibrary.asset";
            var costumes = AssetDatabase.LoadAssetAtPath<CostumeLibrary>(costumePath);
            if (costumes == null)
            {
                costumes = ScriptableObject.CreateInstance<CostumeLibrary>();
                AssetDatabase.CreateAsset(costumes, costumePath);
            }
            costumes.Pieces.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab",
                new[] { "Assets/_Game/Prefabs/Costume", "Assets/_Game/Prefabs/Weapons" }))
            {
                var piece = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (piece != null) costumes.Pieces.Add(piece);
            }
            EditorUtility.SetDirty(costumes);

            var lib = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(lib, LibPath);
            }
            lib.BodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            // AXIOM: a builder returning null must NEVER blank a controller he
            // already has wired (a missing clip used to unwire the whole rig)
            if (ctrl != null) lib.AnimController = ctrl;
            if (zombieCtrl != null) lib.ZombieController = zombieCtrl;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Character wired: {fbxPath} — player clips: " +
                      string.Join(", ", clips.Keys) +
                      $" · zombie set {(zombieCtrl != null ? "✓" : "–")}" +
                      $" · costume pieces {costumes.Pieces.Count} (drop prefabs in Prefabs/Costume, " +
                      "named Hat_x / Cape_x / ZHat_x…). Placeholder team hat+cloak until then.");
        }

        static void ConfigureClipFbx(string path, Avatar avatar)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return;
            bool dirty = false;
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                dirty = true;
            }
            if (avatar != null && imp.sourceAvatar != avatar)
            {
                imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                imp.sourceAvatar = avatar;
                dirty = true;
            }
            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
            foreach (var c in clips)
                if (!c.loopTime)
                {
                    c.loopTime = true; // Mixamo ships loops with looping OFF
                    dirty = true;
                }
            if (dirty)
            {
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
        }

        static AnimationClip LoadClip(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        /// The undead wardrobe: retarget everything in Art/Zombie to the shared
        /// avatar and build the zombie controller — locomotion blend (their
        /// speeds are slower) + Attack / Hit / Scream one-shot triggers.
        /// Duplicate clips ("(2)", "(3)") lose to the shortest filename.
        static RuntimeAnimatorController BuildZombieSet(Avatar avatar)
        {
            const string zombieDir = "Assets/_Game/Art/Zombie";
            if (!AssetDatabase.IsValidFolder(zombieDir)) return null;

            var picks = new System.Collections.Generic.Dictionary<string, string>(); // slot -> path
            void Consider(string slot, string path)
            {
                if (!picks.TryGetValue(slot, out var cur) ||
                    path.Length < cur.Length) picks[slot] = path; // shortest name wins
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { zombieDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (file.Contains("scratch")) Consider("fidget", path);
                else if (file.Contains("idle")) Consider("idle", path);
                else if (file.Contains("running")) Consider("run", path);
                else if (file.Contains("walking")) Consider("walk", path);
                else if (file.Contains("attack")) Consider("attack", path);
                else if (file.Contains("punching")) Consider("punch", path);
                else if (file.Contains("kicking")) Consider("kick", path);
                else if (file.Contains("headbutt")) Consider("headbutt", path);
                else if (file.Contains("reaction hit")) Consider("hit", path);
                else if (file.Contains("agonizing")) Consider("scream", path);
                else if (file.Contains("stand up")) Consider("standup", path);
            }
            if (picks.Count == 0) return null;

            AnimationClip Clip(string slot)
            {
                if (!picks.TryGetValue(slot, out var path)) return null;
                ConfigureClipFbx(path, avatar);
                return LoadClip(path);
            }
            var idle = Clip("idle");
            var walk = Clip("walk");
            var run = Clip("run");
            var attack = Clip("attack");
            var hit = Clip("hit");
            var scream = Clip("scream");
            if (idle == null && walk == null) return null;

            const string path2 = "Assets/_Game/Art/SZ_ZombieAnim.controller";
            var ctrl = OpenForBuild(path2);
            if (ctrl == null) return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path2);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Variant", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Scream", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("StandUp", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Fidget", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;

            var tree = new BlendTree
            {
                name = "Shamble",
                blendParameter = "Speed",
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 1.4f);   // shamble speed
            if (run != null) tree.AddChild(run, 4.5f);     // runner / charger sprint
            var loco = sm.AddState("Shamble");
            loco.motion = tree;
            sm.defaultState = loco;

            void OneShot(string stateName, Motion motion, string trigger, float exitTime = 0.9f)
            {
                if (motion == null) return;
                var st = sm.AddState(stateName);
                st.motion = motion;
                var enter = sm.AddAnyStateTransition(st);
                enter.hasExitTime = false;
                enter.duration = 0.08f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                var exit = st.AddTransition(loco);
                exit.hasExitTime = true;
                exit.exitTime = exitTime;
                exit.duration = 0.15f;
            }

            // attacks come in flavors — ZombieDress rolls "Variant" per swipe
            Motion attackMotion = attack;
            var punch = Clip("punch");
            var kick = Clip("kick");
            var headbutt = Clip("headbutt");
            if (punch != null || kick != null || headbutt != null)
            {
                var variants = new BlendTree
                {
                    name = "AttackVariants",
                    blendParameter = "Variant",
                    blendType = BlendTreeType.Simple1D,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(variants, ctrl);
                float slot = 0f;
                foreach (var clip in new[] { attack, punch, kick, headbutt })
                    if (clip != null) variants.AddChild(clip, slot++);
                attackMotion = variants;
            }

            OneShot("Attack", attackMotion, "Attack");
            OneShot("Hit", hit, "Hit");
            OneShot("Scream", scream, "Scream");
            OneShot("StandUp", Clip("standup"), "StandUp", 0.95f);
            OneShot("Fidget", Clip("fidget"), "Fidget");
            return ctrl;
        }

        /// AXIOM (Marko Jul 25): the Animator window is the ONLY authoring
        /// surface for animation feel, and rebuilding used to DeleteAsset it
        /// "clean every run" — so every hand-tuned transition, blend threshold,
        /// extra layer, avatar mask and StateMachineBehaviour died the next
        /// time he clicked Build (which he must do whenever he adds a clip).
        /// Now: if the controller carries HIS edits it is KEPT untouched;
        /// otherwise it is backed up before regenerating. Returns null when the
        /// caller should keep the existing asset as-is.
        static AnimatorController OpenForBuild(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null && HasYourEdits(existing))
            {
                Debug.LogWarning($"[SpellyZombie] {path} has YOUR edits (extra layer / avatar mask / " +
                    "StateMachineBehaviour / hand-added state) — KEPT AS-IS, nothing regenerated. " +
                    "Delete the file by hand if you want a fresh build.", existing);
                return null;
            }
            if (existing != null)
                AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(
                    path.Replace(".controller", $".bak-{System.DateTime.Now:yyyyMMdd-HHmm}.controller")));
            AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        /// Conservative "did a human touch this?" test — any of these means the
        /// generator never made it, so it must not be thrown away.
        static bool HasYourEdits(AnimatorController c)
        {
            if (c.layers.Length > 1) return true;
            foreach (var l in c.layers)
            {
                if (l.avatarMask != null) return true;
                if (l.stateMachine == null) continue;
                foreach (var s in l.stateMachine.states)
                    if (s.state != null && s.state.behaviours != null && s.state.behaviours.Length > 0) return true;
            }
            return false;
        }

        /// Locomotion: 2D freeform-directional blend on (MoveX, MoveZ) — local
        /// velocity in m/s — so forward/backward/strafes each play their own
        /// clip at both walk (4.5) and sprint (7) radii. Crouch is a separate
        /// state with its own directional blend. IK pass on, so HandIK can
        /// hold the weapon with the upper body while the legs keep moving.
        static RuntimeAnimatorController BuildController(
            System.Collections.Generic.Dictionary<string, AnimationClip> clips)
        {
            AnimationClip C(string key) => clips.TryGetValue(key, out var c) ? c : null;

            var ctrl = OpenForBuild(CtrlPath);
            if (ctrl == null) return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Crouched", AnimatorControllerParameterType.Bool);

            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;

            var sm = ctrl.layers[0].stateMachine;

            BlendTree Directional(string treeName)
            {
                var t = new BlendTree
                {
                    name = treeName,
                    blendParameter = "MoveX",
                    blendParameterY = "MoveZ",
                    blendType = BlendTreeType.FreeformDirectional2D,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(t, ctrl);
                return t;
            }
            void Child(BlendTree t, AnimationClip clip, float x, float z)
            {
                if (clip != null) t.AddChild(clip, new Vector2(x, z));
            }

            // RINGS MATCH CLIP CONTENT, not controller speeds (the glide fix):
            // an in-place walk cycle covers ~1.4 m/s of ground, so it belongs
            // at the SLOW ring; normal MoveSpeed (4.5) must land on the RUN
            // clip whose natural pace nearly matches it. With walk parked at
            // 4.5 the legs strolled while the world flew past at 3× —
            // "gliding". Sprint (7) clamps to the run ring; stride-sync in
            // CharacterRig speeds the cycle the rest of the way.
            var loco = Directional("Locomotion");
            Child(loco, C("idle"), 0f, 0f);
            Child(loco, C("walk"), 0f, 2f);
            Child(loco, C("walkBack"), 0f, -2f);
            Child(loco, C("strafeWalkL"), -2f, 0f);
            Child(loco, C("strafeWalkR"), 2f, 0f);
            Child(loco, C("run"), 0f, 4.5f);
            Child(loco, C("runBack"), 0f, -4.5f);
            Child(loco, C("strafeRunL"), -4.5f, 0f);
            Child(loco, C("strafeRunR"), 4.5f, 0f);
            var stand = sm.AddState("Locomotion");
            stand.motion = loco;
            sm.defaultState = stand;

            AnimatorState duck = null;
            if (C("crouch") != null)
            {
                var duckTree = Directional("CrouchMove");
                // no crouch-idle clip yet: standing idle holds the center
                Child(duckTree, C("crouchIdle") != null ? C("crouchIdle") : C("idle"), 0f, 0f);
                Child(duckTree, C("crouch"), 0f, 2.25f); // crouch speed = half walk
                Child(duckTree, C("crouchBack"), 0f, -2.25f);
                Child(duckTree, C("crouchL"), -2.25f, 0f);
                Child(duckTree, C("crouchR"), 2.25f, 0f);
                duck = sm.AddState("Crouch");
                duck.motion = duckTree;
                var down = stand.AddTransition(duck);
                down.hasExitTime = false;
                down.duration = 0.15f;
                down.AddCondition(AnimatorConditionMode.If, 0f, "Crouched");
                var up = duck.AddTransition(stand);
                up.hasExitTime = false;
                up.duration = 0.15f;
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouched");
            }

            // ---- airborne (Marko's new clips): "Jumping" in place at rest,
            // "JumpRun" the leap once there's real ground speed ----
            var jumpClip = C("jump");
            var jumpRunClip = C("jumpRun");
            if (jumpClip != null || jumpRunClip != null)
            {
                ctrl.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
                ctrl.AddParameter("AirSpeed", AnimatorControllerParameterType.Float);

                Motion airMotion;
                if (jumpClip != null && jumpRunClip != null)
                {
                    var airTree = new BlendTree
                    {
                        name = "Air",
                        blendParameter = "AirSpeed",
                        blendType = BlendTreeType.Simple1D,
                        useAutomaticThresholds = false,
                        hideFlags = HideFlags.HideInHierarchy
                    };
                    AssetDatabase.AddObjectToAsset(airTree, ctrl);
                    airTree.AddChild(jumpClip, 0f);
                    airTree.AddChild(jumpRunClip, 3f); // by jog speed the leap owns the air
                    airMotion = airTree;
                }
                else airMotion = jumpClip != null ? (Motion)jumpClip : jumpRunClip;

                var air = sm.AddState("Air");
                air.motion = airMotion;

                var launch = stand.AddTransition(air);
                launch.hasExitTime = false;
                launch.duration = 0.06f; // leaving the ground is INSTANT news
                launch.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
                var land = air.AddTransition(stand);
                land.hasExitTime = false;
                land.duration = 0.16f; // landing eases back into the legs
                land.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
                if (duck != null)
                {
                    var duckLaunch = duck.AddTransition(air);
                    duckLaunch.hasExitTime = false;
                    duckLaunch.duration = 0.06f;
                    duckLaunch.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
                    // landing always goes through Locomotion; if still crouched
                    // the Crouched bool re-enters Crouch one blend later
                }
            }
            return ctrl;
        }
    }
}
