using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// the TWO-SONG SOUNDTRACK (the Song Maker pair: "one for action,
    /// one for chilling - almost the same"): that similarity is exactly what
    /// vertical layering wants. Both tracks play in lockstep from the same
    /// moment, and the mix CROSSFADES between them - chill in the lobby,
    /// menus and calm stretches; action while danger stands near. The
    /// transition is a fade, not a track change, so the music never stumbles.
    ///
    /// the SHELF, as always: drop the WAVs at
    ///   Resources/Custom/Music_Chill
    ///   Resources/Custom/Music_Action
    /// (loop-clean if the song ends on the bar - Song Maker exports do).
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
            // both start on the SAME DSP tick - phase-locked twins, faded not
            // swapped; scheduled start makes the lock sample-exact
            double at = AudioSettings.dspTime + 0.1;
            if (_instance._chill != null) { _instance._chill.volume = BaseVolume; _instance._chill.PlayScheduled(at); }
            if (_instance._action != null) { _instance._action.volume = 0f; _instance._action.PlayScheduled(at); }
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

        float _dangerHold;

        /// Action means DANGER NEAR, per side: an acolyte hears it when a
        /// wizard closes in; a wizard hears it for zombies - never for
        /// acolytes, whose hiding spot the music must not leak.
        bool DangerNear()
        {
            // the lobby counts too: it is the sandbox where the rule is
            // learned and tested, only the menu stays quiet
            if (ActiveScene.Name == "Menu") return false;
            SimpleFPSController me = null;
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) { me = p; break; }
            if (me == null || me.IsDead) return false;

            Vector3 at = me.transform.position;
            float r = DrawingConfig.MusicDangerRange;
            float sq = r * r;

            if (Sides.LocalIsAcolyte)
            {
                foreach (var a in NetAvatar.All)
                {
                    if (a == null || a.Downed) continue;
                    if (Sides.Of(NetSync.OwnerIdOf(a.Id)) != Side.Wizard) continue;
                    if ((a.transform.position - at).sqrMagnitude < sq) return true;
                }
                return false;
            }

            foreach (var z in Zombie.All)
                if (z != null && (z.transform.position - at).sqrMagnitude < sq) return true;
            return NetSync.AnyZombieNear(at, r);
        }

        void Update()
        {
            // a short hold after the last contact, so the mix never flutters
            // at the range boundary
            if (DangerNear()) _dangerHold = 4f;
            else _dangerHold -= Time.unscaledDeltaTime;
            bool action = _dangerHold > 0f;

            float step = (BaseVolume / FadeSeconds) * Time.unscaledDeltaTime;
            if (_chill != null)
                _chill.volume = Mathf.MoveTowards(_chill.volume, action ? 0f : BaseVolume, step);
            if (_action != null)
                _action.volume = Mathf.MoveTowards(_action.volume, action ? BaseVolume : 0f, step);

            // twin clips: while one lies fully silent, pin it to the other's
            // sample clock, so the loops can never drift apart over a session
            if (_chill != null && _action != null
                && _chill.clip.samples == _action.clip.samples)
            {
                if (_action.volume <= 0f && _chill.volume > 0f
                    && Mathf.Abs(_action.timeSamples - _chill.timeSamples) > 512)
                    _action.timeSamples = _chill.timeSamples;
                else if (_chill.volume <= 0f && _action.volume > 0f
                    && Mathf.Abs(_chill.timeSamples - _action.timeSamples) > 512)
                    _chill.timeSamples = _action.timeSamples;
            }
        }
    }
}
