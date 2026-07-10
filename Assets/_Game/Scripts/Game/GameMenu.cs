using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// ESC = pause. Freezes time, frees the cursor, offers Resume / Restart /
    /// Options (sensitivity + volume, persisted) / Wishlist / Quit. The demo's
    /// entire menu system — deliberately OnGUI so it needs zero scene setup.
    public class GameMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        const string WishlistUrl = "https://store.steampowered.com/"; // real page URL once it exists

        bool _options;
        float _sens, _volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("GameMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMenu>();
        }

        void Awake()
        {
            _sens = PlayerPrefs.GetFloat("sz_look_sens", 0.12f);
            _volume = PlayerPrefs.GetFloat("sz_volume", 1f);
            AudioListener.volume = _volume;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || PoseStudio.IsOpen) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }
        }

        void Open()
        {
            IsOpen = true;
            _options = false;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Close()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            PlayerPrefs.SetFloat("sz_look_sens", _sens);
            PlayerPrefs.SetFloat("sz_volume", _volume);
            PlayerPrefs.Save();
            // cursor re-locks on the next left-click (controller's existing rule)
        }

        void OnGUI()
        {
            if (!IsOpen) return;

            // dim the world
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = 300f, x = (Screen.width - w) / 2f, y = Screen.height * 0.25f;

            var title = new GUIStyle(GUI.skin.label)
            { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, y - 70f, Screen.width, 50f), "SPELLY ZOMBIE", title);

            if (_options)
            {
                GUI.Label(new Rect(x, y, w, 22f), $"Look sensitivity: {_sens:0.00}");
                float newSens = GUI.HorizontalSlider(new Rect(x, y + 24f, w, 20f), _sens, 0.02f, 0.4f);
                if (!Mathf.Approximately(newSens, _sens))
                {
                    _sens = newSens;
                    foreach (var p in SimpleFPSController.All)
                        if (p != null) p.LookSensitivity = _sens;
                }

                GUI.Label(new Rect(x, y + 56f, w, 22f), $"Volume: {_volume * 100f:0}%");
                float newVol = GUI.HorizontalSlider(new Rect(x, y + 80f, w, 20f), _volume, 0f, 1f);
                if (!Mathf.Approximately(newVol, _volume))
                {
                    _volume = newVol;
                    AudioListener.volume = _volume;
                }

                if (GUI.Button(new Rect(x, y + 116f, w, 34f), "Back")) _options = false;
                return;
            }

            if (GUI.Button(new Rect(x, y, w, 38f), "Resume")) Close();
            if (GUI.Button(new Rect(x, y + 46f, w, 38f), "Restart run"))
            {
                Close();
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            if (GUI.Button(new Rect(x, y + 92f, w, 38f), "Options")) _options = true;
            if (GUI.Button(new Rect(x, y + 138f, w, 38f), "♥ Wishlist on Steam"))
                Application.OpenURL(WishlistUrl);
            if (GUI.Button(new Rect(x, y + 184f, w, 38f), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
