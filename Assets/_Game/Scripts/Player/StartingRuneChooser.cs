using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Match start: you're offered THREE random rune families and pick ONE —
    /// that's your primary rune for the whole match (Marko's rule). Everything
    /// else is out there — carried by zombies, dropped when they die.
    /// Bootstraps itself; shows until the local player owns at least one card.
    public class StartingRuneChooser : MonoBehaviour
    {
        static readonly RuneCardType[] AllCards =
            (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));

        /// The pick — the cape's back icon reads this later.
        public static RuneCardType ChosenCard { get; private set; }
        public static bool HasChosen { get; private set; }

        RuneCardType[] _offers;

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
            if (!NeedsChoice || PoseStudio.IsOpen)
            {
                _offers = null; // fresh roll next time a choice is needed
                return;
            }
            if (_offers == null) Roll();

            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < _offers.Length; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame)
                {
                    Grimoire.Unlock(Grimoire.LocalPlayerId, _offers[i]);
                    ChosenCard = _offers[i];
                    HasChosen = true;
                    Debug.Log($"[SpellyZombie] Primary rune chosen: {_offers[i]} — collect the rest from zombies.");
                }
            }
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
        }

        void OnGUI()
        {
            if (!NeedsChoice || _offers == null) return;
            float w = 460f, h = 40f + _offers.Length * 22f;
            var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.22f, w, h);
            GUI.Box(r, "CHOOSE YOUR PRIMARY RUNE — one of these three (press the number)");
            for (int i = 0; i < _offers.Length; i++)
                GUI.Label(new Rect(r.x + 14f, r.y + 26f + i * 22f, w - 28f, 20f),
                    $"[{i + 1}] {RuneLibrary.CardDescription(_offers[i])}");
        }
    }
}
