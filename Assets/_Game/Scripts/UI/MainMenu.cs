using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The main menu, per the design sketch (layout copied from Meccha Chameleon):
    /// game name top-left, server buttons on the left, your poseable character on
    /// a pedestal right-of-center (PoseStudio in AlwaysOpen mode handles that).
    ///
    /// Create Server starts a local match until EOS lands; Find Server is the
    /// future server browser. Poses are saved HERE — drawing on your character
    /// happens pre-match in the lobby, never in the menu.
    public class MainMenu : MonoBehaviour
    {
        bool _settingsOpen;
        float _sensitivity;
        string _status = "";

        void Awake()
        {
            _sensitivity = PlayerPrefs.GetFloat("sz_look_sens", 0.12f);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnGUI()
        {
            GUI.Label(new Rect(40, 30, 700, 110), "Spelly Zombie", Title());

            float y = Screen.height * 0.42f;
            if (Button(ref y, "Create Server"))
            {
                SceneManager.LoadScene("Game");
            }
            if (Button(ref y, "Find Server"))
            {
                _status = "The server browser arrives with Epic Online Services. Create Server starts a match right now.";
            }
            if (Button(ref y, "Settings"))
            {
                _settingsOpen = !_settingsOpen;
            }
            if (Button(ref y, "Quit Game"))
            {
                Quit();
            }

            if (!string.IsNullOrEmpty(_status))
                GUI.Label(new Rect(40, y + 10, 300, 100), _status, Wrap());

            if (_settingsOpen)
                SettingsPanel();
        }

        bool Button(ref float y, string label)
        {
            var r = new Rect(40, y, 230, 46);
            y += 56;
            return GUI.Button(r, label, Btn());
        }

        void SettingsPanel()
        {
            var r = new Rect(300, Screen.height * 0.42f, 300, 120);
            GUI.Box(r, "Settings");
            GUI.Label(new Rect(r.x + 15, r.y + 30, 200, 24), $"Mouse sensitivity: {_sensitivity:0.00}");
            float ns = GUI.HorizontalSlider(new Rect(r.x + 15, r.y + 58, 270, 20), _sensitivity, 0.03f, 0.30f);
            if (!Mathf.Approximately(ns, _sensitivity))
            {
                _sensitivity = ns;
                PlayerPrefs.SetFloat("sz_look_sens", _sensitivity);
            }
            if (GUI.Button(new Rect(r.x + 15, r.y + 84, 270, 26), "Close"))
                _settingsOpen = false;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static GUIStyle _title, _btn, _wrap;
        static GUIStyle Title()
        {
            if (_title == null)
                _title = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold };
            return _title;
        }
        static GUIStyle Btn()
        {
            if (_btn == null)
                _btn = new GUIStyle(GUI.skin.button) { fontSize = 19 };
            return _btn;
        }
        static GUIStyle Wrap()
        {
            if (_wrap == null)
                _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            return _wrap;
        }
    }
}
