using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Run start: you know ONE rune family, and it's your choice. Everything
    /// else is out there — carried by zombies, dropped when they die. Bootstraps
    /// itself; shows until the local player owns at least one card.
    public class StartingRuneChooser : MonoBehaviour
    {
        static readonly RuneCardType[] Cards =
            (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("StartingRuneChooser");
            go.AddComponent<StartingRuneChooser>();
            DontDestroyOnLoad(go);
        }

        bool NeedsChoice =>
            !RuneLibrary.AllRunesUnlockedForTesting &&
            Grimoire.LocalPlayerId != 0 &&
            !Grimoire.HasAny(Grimoire.LocalPlayerId);

        void Update()
        {
            if (!NeedsChoice || PoseStudio.IsOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < Cards.Length && i < 9; i++)
            {
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                {
                    Grimoire.Unlock(Grimoire.LocalPlayerId, Cards[i]);
                    Debug.Log($"[SpellyZombie] Starting rune chosen: {Cards[i]} — collect the rest from zombies.");
                }
            }
        }

        void OnGUI()
        {
            if (!NeedsChoice) return;
            float w = 460f, h = 40f + Cards.Length * 22f;
            var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.22f, w, h);
            GUI.Box(r, "CHOOSE YOUR STARTING RUNE (press the number)");
            for (int i = 0; i < Cards.Length; i++)
                GUI.Label(new Rect(r.x + 14f, r.y + 26f + i * 22f, w - 28f, 20f),
                    $"[{i + 1}] {RuneLibrary.CardDescription(Cards[i])}");
        }
    }
}
