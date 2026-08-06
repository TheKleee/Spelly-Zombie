using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// MARKO'S TWO-SONG SOUNDTRACK (his Song Maker pair: "one for action,
    /// one for chilling - almost the same"): that similarity is exactly what
    /// vertical layering wants. Both tracks play in lockstep from the same
    /// moment, and the mix CROSSFADES between them — chill during prep,
    /// lobby and menus; action while a wave is actually running. The
    /// transition is a fade, not a track change, so the music never stumbles.
    ///
    /// HIS SHELF, as always: drop the WAVs at
    ///   Resources/Custom/Music_Chill
    ///   Resources/Custom/Music_Action
    /// (loop-clean if the song ends on the bar — Song Maker exports do).
    /// Missing files = silence, nothing breaks. Replace anytime.
    public class MusicDirector : MonoBehaviour
    {
        const float BaseVolume = 0.5f;   // music sits under the SFX
        const float FadeSeconds = 1.8f;

        static MusicDirector _instance;

        AudioSource _chill, _action;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var chillClip = Resources.Load<AudioClip>("Custom/Music_Chill");
            var actionClip = Resources.Load<AudioClip>("Custom/Music_Action");
            if (chillClip == null && actionClip == null) return; // no soundtrack yet

            var go = new GameObject("SZ_Music");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MusicDirector>();
            _instance._chill = Source(go, chillClip);
            _instance._action = Source(go, actionClip);
            // both start NOW, together — phase-locked twins, faded not swapped
            if (_instance._chill != null) { _instance._chill.volume = BaseVolume; _instance._chill.Play(); }
            if (_instance._action != null) { _instance._action.volume = 0f; _instance._action.Play(); }
        }

        static AudioSource Source(GameObject holder, AudioClip clip)
        {
            if (clip == null) return null;
            var src = holder.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f; // the soundtrack has no position
            return src;
        }

        void Update()
        {
            bool action = RoundDirector.WaveActive;
            float step = (BaseVolume / FadeSeconds) * Time.unscaledDeltaTime;
            if (_chill != null)
                _chill.volume = Mathf.MoveTowards(_chill.volume, action ? 0f : BaseVolume, step);
            if (_action != null)
                _action.volume = Mathf.MoveTowards(_action.volume, action ? BaseVolume : 0f, step);
        }
    }
}
