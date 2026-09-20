using UnityEngine;

namespace SpellyZombie
{
    /// When the physics steps themselves make a frame long, catching up on them makes the
    /// next frame longer still: Unity allows 16 steps a frame, and the army scenes ended at
    /// 1 to 3 fps that way. While physics is what the frame is spent on, the catch-up is
    /// capped, so the world runs a little slow instead of stopping. A frame that is slow for
    /// any other reason (a weak graphics card) keeps the project's own setting, and so does
    /// its clock.
    [DefaultExecutionOrder(-10000)]
    class PhysicsGovernor : MonoBehaviour
    {
        const float CappedStep = 0.05f;    // at most two or three physics steps a frame while it holds
        const float BusyShare = 0.5f;      // physics took this much of the frame
        const int BusyFrames = 3;          // in a row, before the cap goes on
        const float CalmFrame = 0.025f;    // frames this short again...
        const float CalmSeconds = 1f;      // ...for this long, and the cap comes off

        float _projectStep;
        double _fixedFrom, _lastUpdate;
        int _steps, _busy;
        float _calm;
        bool _capped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("PhysicsGovernor");
            DontDestroyOnLoad(go);
            go.AddComponent<PhysicsGovernor>();
        }

        void Awake()
        {
            _projectStep = Time.maximumDeltaTime;
            _lastUpdate = Time.realtimeSinceStartupAsDouble;
        }

        // first in line (execution order): the physics part of the frame starts here
        void FixedUpdate()
        {
            if (_steps++ == 0) _fixedFrom = Time.realtimeSinceStartupAsDouble;
        }

        // and first in line again: everything between the two was steps and their scripts
        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float frame = (float)(now - _lastUpdate);
            float physics = _steps > 0 ? (float)(now - _fixedFrom) : 0f;
            _lastUpdate = now;
            bool busy = _steps >= 2 && physics > frame * BusyShare && frame > CalmFrame;
            _steps = 0;

            if (!_capped)
            {
                _busy = busy ? _busy + 1 : 0;
                if (_busy < BusyFrames) return;
                _capped = true;
                _calm = 0f;
                Time.maximumDeltaTime = CappedStep;
                Debug.Log("[SpellyZombie] physics is the whole frame: catching up is capped, the world runs a little slow for now");
                return;
            }
            _calm = frame < CalmFrame ? _calm + frame : 0f;
            if (_calm < CalmSeconds) return;
            _capped = false;
            _busy = 0;
            Time.maximumDeltaTime = _projectStep;
            Debug.Log("[SpellyZombie] physics caught up: the world is back to full speed");
        }

        void OnDestroy()
        {
            if (_capped) Time.maximumDeltaTime = _projectStep;
        }
    }
}
