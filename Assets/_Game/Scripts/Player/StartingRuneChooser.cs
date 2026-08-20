using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Match start: three random rune families offered, pick one as your
    /// primary. Bootstraps itself; shows until the local player owns a card.
    public class StartingRuneChooser : MonoBehaviour
    {
        static readonly RuneCardType[] AllCards =
            (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));

        /// The pick - the cape's back icon reads this later.
        public static RuneCardType ChosenCard { get; private set; }
        public static bool HasChosen { get; private set; }

        RuneCardType[] _offers;
        float _pollAt; // idle poll gate - this bootstrap lives in EVERY scene forever

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("StartingRuneChooser");
            go.AddComponent<StartingRuneChooser>();
            DontDestroyOnLoad(go);
        }

        bool NeedsChoice =>
            RuneLibrary.RestrictedArena && // the pick happens IN THE GAME - the lobby is free practice
            !RuneLibrary.AllRunesUnlockedForTesting &&
            Grimoire.LocalPlayerId != 0 &&
            !Grimoire.HasAny(Grimoire.LocalPlayerId);

        void Update()
        {
            // while the UI is DOWN, poll NeedsChoice (Grimoire lookups) at
            // 2 Hz instead of every frame in every scene; a shown chooser
            // still runs per-frame so keys and teardown stay instant
            if (_offers == null)
            {
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.5f;
            }

            if (!NeedsChoice || PoseStudio.IsOpen)
            {
                _offers = null; // fresh roll next time a choice is needed
                if (_ui != null) { Destroy(_ui.gameObject); _ui = null; }
                return;
            }
            if (_offers == null)
            {
                Roll();
                BuildUI();
            }

            var kb = Keyboard.current;
            if (kb == null || UIKit.Typing) return;
            for (int i = 0; i < _offers.Length; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame) Pick(i);
            }
        }

        void Pick(int i)
        {
            Grimoire.Unlock(Grimoire.LocalPlayerId, _offers[i]);
            ChosenCard = _offers[i];
            HasChosen = true;
            Debug.Log($"[SpellyZombie] Primary rune chosen: {_offers[i]}. Collect the rest from zombies.");
        }

        /// Three distinct random families out of the six.
        void Roll()
        {
            var pool = (RuneCardType[])AllCards.Clone();
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            _offers = new[] { pool[0], pool[1], pool[2] };

            // at least one offer must be able to fight alone: Heat, Direction
            // or Luminance
            var teeth = new[] { RuneCardType.Heat, RuneCardType.Direction, RuneCardType.Luminance };
            bool hasTeeth = System.Array.IndexOf(teeth, _offers[0]) >= 0
                || System.Array.IndexOf(teeth, _offers[1]) >= 0
                || System.Array.IndexOf(teeth, _offers[2]) >= 0;
            if (!hasTeeth)
                _offers[Random.Range(0, _offers.Length)] = teeth[Random.Range(0, teeth.Length)];
        }

        RectTransform _ui;

        void BuildUI()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "RuneChooser");
            UIKit.Place(_ui, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(880f, 200f));

            var header = UIKit.Panel(_ui, skin != null ? skin.BannerCurtain : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.6f));
            UIKit.Place((RectTransform)header.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(720f, 70f));
            var headText = UIKit.Label((RectTransform)header.transform,
                "CHOOSE ONE PRIMARY RUNE", 18, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Stretch((RectTransform)headText.transform);

            float w = 272f, gap = 16f;
            float x0 = -(_offers.Length - 1) * (w + gap) * 0.5f;
            for (int i = 0; i < _offers.Length; i++)
            {
                int pick = i;
                var card = UIKit.Group(_ui, "Rune" + i);
                UIKit.Place(card, new Vector2(0.5f, 0f), new Vector2(x0 + i * (w + gap), 44f), new Vector2(w, 104f));
                var back = UIKit.Panel(card, skin != null ? skin.PanelBrown : null,
                    skin != null ? Color.white : new Color(0.25f, 0.2f, 0.14f, 0.95f));
                UIKit.Stretch((RectTransform)back.transform);

                var cap = UIKit.Keycap(card, (i + 1).ToString(), 28f);
                UIKit.Place(cap, new Vector2(0f, 1f), new Vector2(10f, -10f), cap.sizeDelta);

                var name = UIKit.Label(card, _offers[i].ToString().ToUpper(), 18, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(52f, -24f), new Vector2(w - 60f, 24f));

                var desc = UIKit.Label(card, RuneLibrary.CardDescription(_offers[i]), 14,
                    new Color(0.25f, 0.19f, 0.12f), TextAnchor.UpperLeft);

                // baked SZ_UI cards keep their baked labels - write this run's
                // offers explicitly
                name.text = _offers[i].ToString().ToUpper();
                desc.text = RuneLibrary.CardDescription(_offers[i]);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                UIKit.Place((RectTransform)desc.transform, new Vector2(0f, 1f), new Vector2(14f, -46f), new Vector2(w - 28f, 52f));

                // get-or-add: an adopted card already carries its Button, and
                // AddComponent of a duplicate returns null
                var btn = card.gameObject.GetComponent<UnityEngine.UI.Button>();
                if (btn == null) btn = card.gameObject.AddComponent<UnityEngine.UI.Button>();
                btn.targetGraphic = back;
                back.raycastTarget = true;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Pick(pick));
            }
        }
    }
}
