using FishNet;
using UnityEngine;

namespace SpellyZombie
{
    /// Connection front door (Phase B1). Uses whatever transport the scene's
    /// NetworkManager has active — Tugboat for localhost/LAN testing now,
    /// FishySteamworks once we flip to Steam lobbies. Pure OnGUI, zero scene
    /// dependencies beyond the NetworkManager the user already created.
    public class NetGame : MonoBehaviour
    {
        public static bool Connected =>
            InstanceFinder.NetworkManager != null &&
            (InstanceFinder.ServerManager.Started || InstanceFinder.ClientManager.Started);

        public static bool IsHost =>
            InstanceFinder.NetworkManager != null && InstanceFinder.ServerManager.Started;

        string _address = "127.0.0.1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("NetGame");
            DontDestroyOnLoad(go);
            go.AddComponent<NetGame>();
            go.AddComponent<NetSync>();
        }

        void OnGUI()
        {
            if (InstanceFinder.NetworkManager == null) return; // no NetworkManager in scene
            if (GameMenu.IsOpen || PoseStudio.IsOpen) return;

            float x = Screen.width - 232f, y = 10f;

            if (!Connected)
            {
                GUI.Box(new Rect(x - 8f, y - 4f, 232f, 96f), "CO-OP (LAN test)");
                if (GUI.Button(new Rect(x, y + 20f, 100f, 26f), "HOST"))
                {
                    InstanceFinder.ServerManager.StartConnection();
                    InstanceFinder.ClientManager.StartConnection();
                }
                if (GUI.Button(new Rect(x + 108f, y + 20f, 100f, 26f), "JOIN"))
                    InstanceFinder.ClientManager.StartConnection(_address);
                _address = GUI.TextField(new Rect(x, y + 52f, 208f, 24f), _address);
            }
            else
            {
                string role = IsHost ? "HOSTING" : "CONNECTED";
                GUI.Label(new Rect(x, y, 220f, 20f), $"● {role} — {NetSync.RemoteCount + 1} player(s)");
            }
        }
    }
}
