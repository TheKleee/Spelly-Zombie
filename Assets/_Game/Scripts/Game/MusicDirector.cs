using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Two-track soundtrack: chill and action clips play in lockstep and the
    /// mix crossfades between them while danger is near. Clips load from
    /// Resources/Custom/Music_Chill and Music_Action; missing files = silence.
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
            if (chillClip == null && actionClip == null) return;

            var go = new GameObject("SZ_Music");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MusicDirector>();
            _instance._chill = Source(go, chillClip);
            _instance._action = Source(go, actionClip);
            // scheduled start keeps both clips sample-locked
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
            src.spatialBlend = 0f;
            return src;
        }

        float _dangerHold;

        /// Acolytes hear action music when a wizard is near; wizards only for
        /// zombies - acolyte proximity must never leak a hiding spot.
        bool DangerNear()
        {
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
            // hold after the last contact so the mix does not flutter at the range boundary
            if (DangerNear()) _dangerHold = 4f;
            else _dangerHold -= Time.unscaledDeltaTime;
            bool action = _dangerHold > 0f;

            float step = (BaseVolume / FadeSeconds) * Time.unscaledDeltaTime;
            if (_chill != null)
                _chill.volume = Mathf.MoveTowards(_chill.volume, action ? 0f : BaseVolume, step);
            if (_action != null)
                _action.volume = Mathf.MoveTowards(_action.volume, action ? BaseVolume : 0f, step);

            // pin the silent clip to the other's sample clock so the loops never drift
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
