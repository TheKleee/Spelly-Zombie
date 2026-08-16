using UnityEngine;

namespace SpellyZombie
{
    /// Points at the rigged character (the Mixamo T-pose FBX). Wired once
    /// by "Spelly Zombie/Build Character Rig"; loaded from Resources at
    /// runtime. No model wired = every player keeps the graybox bean.
    public class CharacterLibrary : ScriptableObject
    {
        [Tooltip("The Mixamo-rigged T-pose FBX (SZ_Body)")]
        public GameObject BodyModel;

        [Tooltip("Locomotion controller built by the wizard (idle/walk/run + crouch)")]
        public RuntimeAnimatorController AnimController;

        [Tooltip("Zombie animation controller (same body, undead moves)")]
        public RuntimeAnimatorController ZombieController;

        static CharacterLibrary _loaded;
        static bool _tried;

        static CharacterLibrary Loaded
        {
            get
            {
                if (_loaded == null && !_tried)
                {
                    _tried = true;
                    _loaded = Resources.Load<CharacterLibrary>("CharacterLibrary");
                }
                return _loaded;
            }
        }

        public static GameObject Model => Loaded != null ? Loaded.BodyModel : null;
        public static RuntimeAnimatorController Anim => Loaded != null ? Loaded.AnimController : null;
        public static RuntimeAnimatorController ZombieAnim => Loaded != null ? Loaded.ZombieController : null;
    }
}
