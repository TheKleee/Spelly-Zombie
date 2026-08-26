using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SpellyZombie
{
    /// Plays one authored move clip on a body's Animator, then hands the body
    /// back to its controller. Zombies and golems share it.
    public class OneShotClip : MonoBehaviour
    {
        PlayableGraph _graph;
        Animator _anim;
        float _end;

        /// False when the body cannot perform it - no clip, or no Animator
        /// anywhere on it, which is said out loud instead of shrugged off.
        public static bool Play(GameObject body, AnimationClip clip)
        {
            if (body == null || clip == null) return false;
            var anim = body.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                Debug.LogWarning($"[SpellyZombie] '{body.name}' links move animation " +
                    $"'{clip.name}' but has no Animator - add one to its prefab.");
                return false;
            }
            var one = body.GetComponent<OneShotClip>();
            if (one == null) one = body.AddComponent<OneShotClip>();
            one.Begin(anim, clip);
            return true;
        }

        void Begin(Animator anim, AnimationClip clip)
        {
            Stop();
            _anim = anim;
            AnimationPlayableUtilities.PlayClip(anim, clip, out _graph);
            _end = Time.time + Mathf.Max(0.05f, clip.length);
        }

        void Update()
        {
            if (_graph.IsValid() && Time.time >= _end) Stop();
        }

        void Stop()
        {
            if (!_graph.IsValid()) return;
            _graph.Destroy();
            if (_anim != null && _anim.isActiveAndEnabled && _anim.runtimeAnimatorController != null)
                _anim.Rebind();
        }

        void OnDestroy() => Stop();
    }
}
