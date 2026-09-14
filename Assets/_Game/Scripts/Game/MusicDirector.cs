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
        static float Vol => BaseVolume * AudioOptions.Music;

        static MusicDirector _instance;

        AudioSource _chill, _action;
        float _chillMix, _actionMix;     // the crossfade, before the egg's duck
        float _duck = 1f;                // follows LoadEgg.MusicLevel

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
            _instance._chillMix = Vol;
            if (_instance._chill != null) { _instance._chill.volume = Vol; _instance._chill.PlayScheduled(at); }
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

        /// Action music when anything my team should fear is near - enemy
        /// players, creatures, or their live spells, all judged by the Teams
        /// law. One exception: wizards never hear acolyte players or dormant
        /// runes, so the music can't work as a hider detector.
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
            Team mine = Teams.OfOwner(Grimoire.LocalPlayerId);

            if (mine == Team.Acolyte)
            {
                foreach (var a in NetAvatar.All)
                {
                    if (a == null || a.Downed) continue;
                    if (Sides.Of(NetSync.OwnerIdOf(a.Id)) != Side.Wizard) continue;
                    if ((a.transform.position - at).sqrMagnitude < sq) return true;
                }
            }

            if (Teams.Enemies(mine, Team.Acolyte))
            {
                foreach (var z in Zombie.All)
                    if (z != null && (z.transform.position - at).sqrMagnitude < sq) return true;
                if (NetSync.AnyZombieNear(at, r)) return true;
            }

            foreach (var g in Golem.All)
            {
                if (g == null || !Teams.Enemies(mine, Teams.OfOwner(g.OwnerId))) continue;
                if ((g.transform.position - at).sqrMagnitude < sq) return true;
            }
            if (NetSync.AnyEnemyGolemNear(at, r, mine)) return true;

            // live, unheld enemy motes; a held mote's threat is its holder
            foreach (var m in SpellParticle.Living)
            {
                if (m == null || m.Dead || m.Dormant || m.Holder != null) continue;
                if (!Teams.Enemies(mine, Teams.OfOwner(m.OwnerId))) continue;
                if ((m.transform.position - at).sqrMagnitude < sq) return true;
            }
            if (NetSync.AnyEnemyMoteNear(at, r, mine)) return true;
            return false;
        }

        void Update()
        {
            // hold after the last contact so the mix does not flutter at the range boundary
            if (DangerNear()) _dangerHold = 4f;
            else _dangerHold -= Time.unscaledDeltaTime;
            bool action = _dangerHold > 0f;

            float step = (BaseVolume / FadeSeconds) * Time.unscaledDeltaTime;
            _chillMix = Mathf.MoveTowards(_chillMix, action ? 0f : Vol, step);
            _actionMix = Mathf.MoveTowards(_actionMix, action ? Vol : 0f, step);
            // the egg carries the music: down as the shell forms, up as it breaks apart
            _duck = Mathf.MoveTowards(_duck, LoadEgg.MusicLevel, Time.unscaledDeltaTime * 3f);
            if (_chill != null) _chill.volume = _chillMix * _duck;
            if (_action != null) _action.volume = _actionMix * _duck;

            // pin the silent clip to the other's sample clock so the loops never drift
            if (_chill != null && _action != null
                && _chill.clip.samples == _action.clip.samples)
            {
                if (_actionMix <= 0f && _chillMix > 0f
                    && Mathf.Abs(_action.timeSamples - _chill.timeSamples) > 512)
                    _action.timeSamples = _chill.timeSamples;
                else if (_chillMix <= 0f && _actionMix > 0f
                    && Mathf.Abs(_chill.timeSamples - _action.timeSamples) > 512)
                    _chill.timeSamples = _action.timeSamples;
            }
        }
    }
}
