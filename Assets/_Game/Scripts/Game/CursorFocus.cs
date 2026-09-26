using UnityEngine;

namespace SpellyZombie
{
    /// Windows lets the cursor go when the game loses focus (alt-tab), while
    /// Unity can still report it locked. The click-to-relock rule only runs
    /// when the cursor reads free, so the mouse stayed dead until Escape opened
    /// and closed the menu, which sets the lock again. This does the same the
    /// moment the window is back, but only a lock the game still holds: one it
    /// let go of while the window was away (it travelled to the main menu)
    /// came back as a dead mouse in the menu.
    public class CursorFocus : MonoBehaviour
    {
        int _relockFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SZ_CursorFocus");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorFocus>();
        }

        void LateUpdate()
        {
            if (_relockFrame < 0 || Time.frameCount < _relockFrame) return;
            _relockFrame = -1;
            if (Cursor.lockState != CursorLockMode.Locked) return; // the game let go of it meanwhile
            // setting it again is what makes Windows capture the cursor again
            Cursor.lockState = CursorLockMode.None;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnApplicationFocus(bool focused)
        {
            // a frame later: the window has to be in front before the capture holds
            if (focused && Cursor.lockState == CursorLockMode.Locked) _relockFrame = Time.frameCount + 1;
        }
    }
}
