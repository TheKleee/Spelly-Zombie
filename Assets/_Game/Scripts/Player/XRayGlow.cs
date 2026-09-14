using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Third-person reveal: when geometry hides the body, a second camera
    /// renders with its near plane just in front of the body, shown in a soft
    /// circle - the occluder is clipped away inside it. Local-only, cosmetic.
    public class XRayGlow : MonoBehaviour
    {
        const float NearGap = 0.12f;   // the cut stops this short of the body's nearest point
        const float MaxRange = 10f;    // window off beyond this camera-to-body distance
        const float HalfWidth = 0.7f;  // minimum world half width of the window
        const float Soft = 0.3f;       // window edge softness
        const int Downscale = 1;       // full resolution, must match the scene
        const float Hold = 0.3f;       // the window outlives the last blocked frame by this
        const float Ease = 0.08f;      // seconds for the window to follow the body
        const float MaxRad = 0.45f;    // viewport half size cap: never most of the screen

        Camera _main, _cam;
        RenderTexture _rt;
        RawImage _img;
        Material _mask;
        GameObject _canvasGo;
        float _hold;
        Vector2 _center;
        float _rx, _ry;
        bool _shown;
        Transform _pivot;
        readonly List<Renderer> _rends = new List<Renderer>();
        float _rendsAt;

        public static void Show(GameObject root)
        {
            var g = root.GetComponent<XRayGlow>();
            if (g == null) g = root.AddComponent<XRayGlow>();
            g.enabled = true;
        }

        public static void Hide(GameObject root)
        {
            var g = root.GetComponent<XRayGlow>();
            if (g != null) g.enabled = false;
        }

        readonly List<Renderer> _hidden = new List<Renderer>();

        void OnEnable()
        {
            if (!Build()) { enabled = false; return; }
            var pilot = GetComponent<SimpleFPSController>();
            _pivot = pilot != null ? pilot.CameraPivot : null;
            _rendsAt = 0f;
            RenderPipelineManager.beginCameraRendering += OnBeginReveal;
            RenderPipelineManager.endCameraRendering += OnEndReveal;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginReveal;
            RenderPipelineManager.endCameraRendering -= OnEndReveal;
            SetActive(false);
        }

        /// The reveal must never expose OTHER players. Only players the main
        /// camera cannot see sit out the reveal render; anyone already
        /// visible keeps rendering normally, walls or not.
        void OnBeginReveal(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _main == null) return;
            Vector3 eye = _main.transform.position;
            foreach (var a in NetAvatar.All)
            {
                if (a == null) continue;
                Bounds b = ShapeShift.FindObjectBounds(a.transform);
                Vector3 mid = b.center;
                Vector3 top = new Vector3(mid.x, b.max.y, mid.z);
                bool hiddenFromView = BlockedFor(eye, a.transform, mid)
                    && BlockedFor(eye, a.transform, top);
                if (!hiddenFromView) continue;
                foreach (var r in a.GetComponentsInChildren<Renderer>())
                    if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
            }
        }

        void OnEndReveal(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam) return;
            foreach (var r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
        }

        bool Build()
        {
            if (_mask != null) return true;
            var shader = Shader.Find("SpellyZombie/XRayCircle");
            if (shader == null)
            {
                Debug.LogError("[SpellyZombie] SZXRayCircle.shader missing or failed to compile.");
                return false;
            }
            _mask = new Material(shader);

            _canvasGo = new GameObject("XRayReveal");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var imgGo = new GameObject("Circle");
            imgGo.transform.SetParent(_canvasGo.transform, false);
            _img = imgGo.AddComponent<RawImage>();
            var rect = _img.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _img.material = _mask;
            _img.raycastTarget = false;

            var camGo = new GameObject("XRayCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            SetActive(false);
            return true;
        }

        void SetActive(bool on)
        {
            if (_canvasGo != null && _canvasGo.activeSelf != on) _canvasGo.SetActive(on);
            if (_cam != null && _cam.enabled != on) _cam.enabled = on;
        }

        void EnsureRT()
        {
            int w = Mathf.Max(160, Screen.width / Downscale);
            int h = Mathf.Max(90, Screen.height / Downscale);
            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                _cam.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 24);
                _rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
                _cam.targetTexture = _rt;
                _img.texture = _rt;
            }
        }

        static bool BlockedFor(Vector3 eye, Transform who, Vector3 at)
        {
            return Physics.Linecast(eye, at, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(who);
        }

        bool Blocked(Vector3 eye, Vector3 at) => BlockedFor(eye, transform, at);

        void LateUpdate()
        {
            if (_main == null)
            {
                _main = Camera.main;
                if (_main == null) { Off(); return; }
            }

            Bounds body = BodyBounds();
            if (body.size.magnitude < 0.05f) { Off(); return; }
            Vector3 mid = body.center;
            Vector3 head = new Vector3(mid.x, body.max.y, mid.z);
            Vector3 eye = _main.transform.position;
            if (Vector3.Distance(eye, mid) > MaxRange) { _hold = 0f; Off(); return; }
            bool occluded = Blocked(eye, mid) || Blocked(eye, head);
            _hold = occluded ? Hold : _hold - Time.deltaTime;
            if (_hold <= 0f) { Off(); return; }

            // the cut runs along the LENS AXIS, like a tube from the camera to
            // the body, and stops just short of the body's NEAREST point. A
            // plane set off the body's centre sliced a lying body open: the
            // inside of the robe filled the window and changed with every bob
            Vector3 fwd = _main.transform.forward;
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            float axialMin = float.MaxValue;
            int seen = 0;
            Vector3 e = body.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mid + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                axialMin = Mathf.Min(axialMin, Vector3.Dot(c - eye, fwd));
                Vector3 vp = _main.WorldToViewportPoint(c);
                if (vp.z <= 0f) continue;
                lo = Vector2.Min(lo, vp);
                hi = Vector2.Max(hi, vp);
                seen++;
            }
            float near = axialMin - NearGap;
            // right on top of the body there is nothing to cut away
            if (seen == 0 || near < _main.nearClipPlane + 0.1f) { Off(); return; }

            SetActive(true);
            EnsureRT();
            _cam.transform.SetPositionAndRotation(eye, _main.transform.rotation);
            _cam.fieldOfView = _main.fieldOfView;
            _cam.aspect = _main.aspect;
            _cam.cullingMask = _main.cullingMask & ~(1 << 5); // never the UI
            _cam.nearClipPlane = near;
            _cam.farClipPlane = _main.farClipPlane;
            // the window must only differ where something was cut away, so
            // empty regions show the same sky the scene shows, never black
            _cam.clearFlags = CameraClearFlags.Skybox;

            // at least a hand's width around the body, never most of the screen
            Vector3 vpMid = _main.WorldToViewportPoint(mid);
            Vector3 vpSide = _main.WorldToViewportPoint(mid + _main.transform.right * HalfWidth);
            float minR = Mathf.Abs(vpSide.x - vpMid.x);
            Vector2 center = (lo + hi) * 0.5f;
            float rx = Mathf.Clamp(Mathf.Max((hi.x - lo.x) * 0.5f * 1.15f, minR), 0.05f, MaxRad);
            float ry = Mathf.Clamp(Mathf.Max((hi.y - lo.y) * 0.5f * 1.15f, minR * _main.aspect), 0.05f, MaxRad);
            // eased, so a ragdoll twitch never shows as a pulse
            if (!_shown) { _center = center; _rx = rx; _ry = ry; _shown = true; }
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Ease);
                _center = Vector2.Lerp(_center, center, k);
                _rx = Mathf.Lerp(_rx, rx, k);
                _ry = Mathf.Lerp(_ry, ry, k);
            }
            _mask.SetVector("_Center", new Vector4(_center.x, _center.y, 0f, 0f));
            _mask.SetFloat("_RadX", _rx);
            _mask.SetFloat("_RadY", _ry);
            _mask.SetFloat("_Soft", Soft);
        }

        void Off()
        {
            SetActive(false);
            _shown = false;
        }

        /// The body's own skin as it lies, plus what it wears; nothing that
        /// rides the camera. As a ghost the camera is away with the spirit,
        /// so the old box (every renderer under the root, skins at their
        /// standing import size) ran from the corpse to wherever the ghost
        /// flew: the window pulsed with every move and swallowed the screen.
        Bounds BodyBounds()
        {
            if (Time.time >= _rendsAt)
            {
                _rendsAt = Time.time + 1f;
                GetComponentsInChildren(false, _rends);
            }
            Transform eyeRig = _main != null ? _main.transform : null;
            bool any = false;
            var b = new Bounds();
            foreach (var r in _rends)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (eyeRig != null && r.transform.IsChildOf(eyeRig)) continue;
                if (_pivot != null && r.transform.IsChildOf(_pivot)) continue;
                Bounds rb = r is SkinnedMeshRenderer smr ? ShapeShift.SkinBounds(smr) : r.bounds;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return any ? b : new Bounds(transform.position, Vector3.zero);
        }

        void OnDestroy()
        {
            if (_canvasGo != null) Destroy(_canvasGo);
            if (_cam != null) Destroy(_cam.gameObject);
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
