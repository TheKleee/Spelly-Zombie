using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ONE-SHOT HINTS (Marko: "our players don't know that they can click alt
    /// to use a free-hand tool... hints should tell you about every available
    /// option when it arises - once, when you do it the hint disappears").
    ///
    /// Exactly that, nothing more: a system calls Offer() every frame the
    /// option is genuinely available, Retire() the moment the player uses it.
    /// Retired hints never come back (persisted). ON by default for the demo;
    /// Hints.Enabled is the options switch.
    public static class Hints
    {
        public enum Id
        {
            FreeHand, // hold Alt = free cursor, draw fast
            Erase,    // right-drag rubs ink out — and scoops it back into the wand
            Pages,    // ← → flip the book's pages
            Absorb,   // F learns a rune off the world
        }

        const string OnKey = "sz_hints_on";
        const string DoneKey = "sz_hint_done_";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(OnKey, 1) != 0;   // demo default: ON
            set { PlayerPrefs.SetInt(OnKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static readonly HashSet<Id> _done = new HashSet<Id>();
        static bool _loaded;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (Id id in System.Enum.GetValues(typeof(Id)))
                if (PlayerPrefs.GetInt(DoneKey + id, 0) != 0) _done.Add(id);
        }

        /// Call every frame this option is actually available to the player.
        public static void Offer(Id id)
        {
            Load();
            if (!Enabled || _done.Contains(id)) return;
            HintDraw.Show(Text(id));
        }

        /// The player just did it — the hint is finished, for good.
        public static void Retire(Id id)
        {
            Load();
            if (!_done.Add(id)) return;
            PlayerPrefs.SetInt(DoneKey + id, 1);
            PlayerPrefs.Save();
        }

        /// Options: show every hint again from scratch.
        public static void ResetAll()
        {
            Load();
            foreach (Id id in System.Enum.GetValues(typeof(Id)))
                PlayerPrefs.DeleteKey(DoneKey + id);
            _done.Clear();
            PlayerPrefs.Save();
        }

        static string Text(Id id)
        {
            switch (id)
            {
                case Id.FreeHand: return "hold Alt to free the cursor and draw fast";
                case Id.Erase: return "right-drag erases ink and returns it to your wand";
                case Id.Pages: return "← and → turn the pages";
                case Id.Absorb: return "F absorbs it into your grimoire";
                default: return "";
            }
        }
    }

    /// The hint line itself — one short faded sentence above the key prompt.
    /// Self-spawning; no scene setup.
    public class HintDraw : MonoBehaviour
    {
        static HintDraw _i;
        static string _text;
        static int _frame = -1;

        public static void Show(string text)
        {
            if (_i == null)
            {
                var go = new GameObject("Hint");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<HintDraw>();
            }
            _text = text;
            _frame = Time.frameCount;
        }

        static GUIStyle _style;

        void OnGUI()
        {
            if (Time.frameCount - _frame > 1 || string.IsNullOrEmpty(_text)) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15
                };
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.96f, 0.85f, 0.75f);
            GUI.Label(new Rect(0f, Screen.height - 150f, Screen.width, 24f), _text, _style);
            GUI.color = prev;
        }
    }
}
