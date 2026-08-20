using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Brings local players INTO a scene. Nothing is assumed to be sitting
    /// there already: a scene with no player gets one from the prefab, a scene
    /// that still has one keeps it, and a mode wanting two - split screen,
    /// co-op - just raises LocalCount and gets two the same way.
    /// This is the only reason a scene works without a player in it, and the
    /// only reason a mode can want more than one.
    /// SpawnPlan decides where each of them lands.
    public static class PlayerSpawner
    {
        /// How many local bodies this mode needs. One today.
        public static int LocalCount = 1;

        static int _builtFor = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            _builtFor = -1;
            SceneManager.sceneLoaded += (_, __) => _builtFor = -1;
        }

        /// Ticked every frame; does its work once per scene, or again the
        /// moment a mode changes how many players it wants.
        public static void Tick()
        {
            if (_builtFor == LocalCount) return;
            if (ActiveScene.Name == "Menu") return;   // the menu has no bodies

            int have = 0;
            foreach (var p in SimpleFPSController.All) if (p != null) have++;
            if (have >= LocalCount) { _builtFor = LocalCount; return; }

            var prefab = CollectionManager.Player;
            if (prefab == null) { _builtFor = LocalCount; return; } // the slot said so

            for (int i = have; i < LocalCount; i++)
            {
                var go = Object.Instantiate(prefab);
                go.name = i == 0 ? "Player" : $"Player_{i + 1}";
            }
            _builtFor = LocalCount;
        }
    }
}
