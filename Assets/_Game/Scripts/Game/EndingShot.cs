using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ WHY THEY WON, SHOWN (his call, panned like Dota's end camera). When a
    /// match ends the camera flies from the player's eye to the cause - the dry
    /// pot, the green pot, a fallen body of the side that went down, where the
    /// boss fell - circles it with one short line under it, then flies back
    /// before the trip home, so the egg still closes around the player's own
    /// view. Every machine flies its own from the ending and the spot the host sent.
    public class EndingShot : MonoBehaviour
    {
        const float Hold = 3.5f;                           // seconds at the cause
        const float Back = 4.5f, Up = 2.4f, Look = 0.7f;   // where the camera settles and what it aims at
        const float Circle = 14f;                          // degrees per second around the cause
        const float Skip = 0.8f;                           // the cause's own shell never stops the camera
        const float PanSpeed = 25f;                        // metres a second a flight covers...
        const float PanMin = 0.8f, PanMax = 1.6f;          // ...in no less and no more than this (both ways fit the 7 s end)
        const float Arc = 0.3f, ArcMax = 15f;              // a flight peaks this share of its length over the roofs
        const float Turn = 0.4f;                           // share of a flight spent turning to or from the cause

        /// The ending camera has the screen (the see-through window stays off).
        public static bool Playing { get; private set; }

        Camera _cam;
        RectTransform _caption;
        Vector3 _at, _fromPos;
        Quaternion _fromRot;
        float _yaw, _t, _pan;
        bool _wasOver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~EndingShot");
            DontDestroyOnLoad(go);
            go.AddComponent<EndingShot>();
        }

        void LateUpdate()
        {
            bool over = RoundDirector.Ended(out _, out var ending, out var at);
            if (over && !_wasOver) Begin(ending, at);
            _wasOver = over;
            if (!Playing) return;
            // a host who skips the end starts the trip at once: the egg forms around the player's camera
            if (!over || _cam == null || LoadEgg.Leaving) { Stop(); return; }
            _t += Time.unscaledDeltaTime;
            if (_t >= _pan * 2f + Hold) { Stop(); return; }
            bool words = _t >= _pan && _t < _pan + Hold;
            if (_caption != null && _caption.gameObject.activeSelf != words) _caption.gameObject.SetActive(words);
            Frame();
        }

        void Begin(Achievements.Ending ending, Vector3 at)
        {
            string words = Words(ending);
            var main = Camera.main;
            if (words == null || at == Vector3.zero || main == null || ActiveScene.Name == "Lobby") return;
            _at = at;
            _t = 0f;
            _yaw = main.transform.eulerAngles.y;
            _fromPos = main.transform.position;
            _fromRot = main.transform.rotation;
            _pan = Mathf.Clamp(Vector3.Distance(_fromPos, at) / PanSpeed, PanMin, PanMax);

            // a camera of its own over the player's, so nothing that drives the
            // player's camera has to be talked out of it
            _cam = new GameObject("EndingShotCam").AddComponent<Camera>();
            _cam.CopyFrom(main);
            _cam.depth = main.depth + 10f;
            _cam.targetTexture = null;
            _cam.GetUniversalAdditionalCameraData().renderPostProcessing =
                main.GetUniversalAdditionalCameraData().renderPostProcessing;
            _cam.transform.SetPositionAndRotation(_fromPos, _fromRot);
            Playing = true;

            // the line shows while the camera is at the cause
            _caption = UIKit.Group(UIKit.Root, "EndingCaption");
            _caption.SetAsLastSibling();
            UIKit.Place(_caption, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1400f, 70f));
            var text = UIKit.Label(_caption, words, 36, Color.white, TextAnchor.MiddleCenter, true);
            UIKit.Stretch(text.rectTransform);
            var edge = text.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.85f);
            edge.effectDistance = new Vector2(2f, -2f);
            _caption.gameObject.SetActive(false);
        }

        /// Out along an arc, round the cause slowly, never from behind a wall, then home.
        void Frame()
        {
            _yaw += Circle * Time.unscaledDeltaTime;
            Vector3 aim = _at + Vector3.up * Look;
            Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.back;
            Vector3 there = _at + Vector3.up * Up + dir * Back;
            Vector3 d = there - aim;
            float len = d.magnitude;
            Vector3 n = d / Mathf.Max(0.001f, len);
            if (len > Skip && Physics.Raycast(aim + n * Skip, n, out var hit, len - Skip,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                there = hit.point - n * 0.3f;

            Vector3 pos = there;
            Quaternion rot;
            float home = _t - _pan - Hold; // time into the flight back
            if (_t < _pan)
            {
                // out: it turns to the cause first, so everyone sees which way it lies
                pos = Arced(_fromPos, there, Mathf.SmoothStep(0f, 1f, _t / _pan));
                rot = Quaternion.Slerp(_fromRot, Facing(aim - pos), Mathf.SmoothStep(0f, 1f, _t / (_pan * Turn)));
            }
            else if (home > 0f)
            {
                // back: it lands exactly on the player's camera, wherever that went meanwhile
                var main = Camera.main;
                if (main == null) { Stop(); return; }
                float k = home / _pan;
                pos = Arced(there, main.transform.position, Mathf.SmoothStep(0f, 1f, k));
                rot = Quaternion.Slerp(Facing(aim - pos), main.transform.rotation,
                    Mathf.SmoothStep(0f, 1f, (k - (1f - Turn)) / Turn));
            }
            else rot = Facing(aim - pos);
            _cam.transform.SetPositionAndRotation(pos, rot);
        }

        /// A point of a flight: eased both ends, arcing over the houses between.
        static Vector3 Arced(Vector3 from, Vector3 to, float k)
        {
            float rise = Mathf.Min(ArcMax, Vector3.Distance(from, to) * Arc);
            Vector3 mid = (from + to) * 0.5f + Vector3.up * (2f * rise); // the curve peaks at half its handle
            return Vector3.Lerp(Vector3.Lerp(from, mid, k), Vector3.Lerp(mid, to, k), k);
        }

        Quaternion Facing(Vector3 d) =>
            d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d, Vector3.up) : _cam.transform.rotation;

        void Stop()
        {
            Playing = false;
            if (_cam != null) Destroy(_cam.gameObject);
            if (_caption != null) Destroy(_caption.gameObject);
            _cam = null;
            _caption = null;
        }

        static string Words(Achievements.Ending ending)
        {
            switch (ending)
            {
                case Achievements.Ending.PotDry: return Loc.T("end.potdry");
                case Achievements.Ending.NoWizards: return Loc.T("end.nowizards");
                case Achievements.Ending.Sweep: return Loc.T("end.sweep");
                case Achievements.Ending.GreenBell: return Loc.T("end.greenbell");
                case Achievements.Ending.CleanBell: return Loc.T("end.cleanbell");
                case Achievements.Ending.BossDown: return Loc.T("end.bossdown");
                case Achievements.Ending.EveryoneDown: return Loc.T("end.everyonedown");
                case Achievements.Ending.TimeUp: return Loc.T("end.timeup");
                default: return null;
            }
        }
    }
}
