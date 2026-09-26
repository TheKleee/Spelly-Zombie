using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Whether a thing was born with its scene. A scene is LOADING from the moment its objects
    /// wake until Unity reports it loaded (sceneLoaded fires after every Awake and OnEnable of
    /// the scene, before any Start), so an Element that wakes while its scene is still loading
    /// was authored in it, and one that wakes later was spawned at play. The clock cannot tell
    /// the two apart: in a build that reaches the lobby from the main menu, Time.timeSinceLevelLoad
    /// still reads the menu's minutes during the lobby's own Awakes.
    public static class SceneBirth
    {
        static readonly HashSet<int> _settled = new HashSet<int>();
        static bool _hooked;

        /// True while `scene` is still loading: an Awake now is a load-time birth.
        public static bool Loading(Scene scene)
        {
            if (!scene.IsValid()) return false;
            if (!_hooked) Hook(scene);
            return !_settled.Contains(scene.handle);
        }

        static void Hook(Scene loading)
        {
            _hooked = true;
            // every scene already up when the first question is asked has settled, except the
            // one asking from inside its own load
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.handle != loading.handle) _settled.Add(s.handle);
            }
            SceneManager.sceneLoaded += (s, mode) => _settled.Add(s.handle);
            SceneManager.sceneUnloaded += s => _settled.Remove(s.handle);
        }
    }
}
