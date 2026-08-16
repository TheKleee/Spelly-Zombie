using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Put this on a Text or TMP_Text authored in a prefab and give it a key.
    /// The words then come from the language files instead of what was typed,
    /// and re-read themselves when the language changes.
    ///
    /// UIKit adopts prefab UI but keeps its captions, so this is how authored
    /// text gets translated. Works on both legacy Text and TextMeshPro.
    [DisallowMultipleComponent]
    public class LocText : MonoBehaviour
    {
        [Tooltip("Key from Loc, e.g. menu.resume or opt.language. Empty does nothing.")]
        public string Key;

        [Tooltip("Optional: numbers/names to substitute for {0}, {1} in the string.")]
        public string[] Args;

        Text _legacy;
        TMPro.TMP_Text _tmp;

        void Awake()
        {
            _legacy = GetComponent<Text>();
            _tmp = GetComponent<TMPro.TMP_Text>();
        }

        void OnEnable()
        {
            Loc.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Loc.Changed -= Refresh;

        public void Refresh()
        {
            if (string.IsNullOrEmpty(Key))
            {
                Debug.LogError("[SpellyZombie] LocText on '" + name + "' has no Key. "
                    + "Set one (e.g. menu.resume) or remove the component.", this);
                enabled = false;
                return;
            }

            string s = Args != null && Args.Length > 0 ? Loc.F(Key, Args) : Loc.T(Key);
            if (_legacy != null) _legacy.text = s;
            if (_tmp != null) _tmp.text = s;
        }
    }
}
