using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Third-person reveal sphere: when geometry hides the body, a second
    /// camera renders the scene with its near plane pushed to just in front
    /// of the body, and that texture shows in a soft circle around them.
    /// The occluder is clipped away inside the circle, so the body AND some
    /// surroundings stay visible. Local player only, purely cosmetic on this
    /// machine, nothing replicates.
    public class XRayGlow : MonoBehaviour
    {
        const float StandOff = 0.45f;  // clip everything up to this close to the body
        const float MaxRange = 10f;    // window off beyond this camera-to-body distance
        const float HalfWidth = 0.7f;  // minimum world half width of the window
        const float Soft = 0.3f;       // window edge softness
        const int Downscale = 1;       // full resolution, must match the scene

        Camera _main, _cam;
        RenderTexture _rt;
        RawImage _img;
        Material _mask;
        GameObject _canvasGo;
        float _hold;

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
                if (_main == null) { SetActive(false); return; }
            }

            // the body's real center, same method the shape scan uses; also
            // fits whatever shape the body currently is, sprawled or worn
            Bounds body = ShapeShift.FindObjectBounds(transform);
            if (body.size.y < 0.05f) { SetActive(false); return; }
            Vector3 mid = body.center;
            Vector3 head = new Vector3(mid.x, body.max.y, mid.z);
            Vector3 feet = new Vector3(mid.x, body.min.y, mid.z);
            Vector3 eye = _main.transform.position;
            if (Vector3.Distance(eye, mid) > MaxRange) { _hold = 0f; SetActive(false); return; }
            bool occluded = Blocked(eye, mid) || Blocked(eye, head);
            _hold = occluded ? 0.15f : _hold - Time.deltaTime;
            bool on = _hold > 0f;
            SetActive(on);
            if (!on) return;

            EnsureRT();
            // the cut runs along the LENS AXIS, like a tube from the camera
            // to the body: straight-line distance misplaces the plane when
            // the body sits at the screen edge
            float axial = Vector3.Dot(mid - eye, _main.transform.forward);
            if (axial < 0.2f) { SetActive(false); return; }
            _cam.transform.SetPositionAndRotation(eye, _main.transform.rotation);
            _cam.fieldOfView = _main.fieldOfView;
            _cam.aspect = _main.aspect;
            _cam.cullingMask = _main.cullingMask & ~(1 << 5); // never the UI
            _cam.nearClipPlane = Mathf.Max(0.05f, axial - StandOff);
            _cam.farClipPlane = _main.farClipPlane;
            // the window must only differ where something was cut away, so
            // empty regions show the same sky the scene shows, never black
            _cam.clearFlags = CameraClearFlags.Skybox;

            Vector3 vpMid = _main.WorldToViewportPoint(mid);
            if (vpMid.z <= 0f) { SetActive(false); return; }
            Vector3 vpHead = _main.WorldToViewportPoint(head + Vector3.up * 0.15f);
            Vector3 vpFeet = _main.WorldToViewportPoint(feet - Vector3.up * 0.2f);
            float side = Mathf.Max(HalfWidth, body.extents.x, body.extents.z);
            Vector3 vpSide = _main.WorldToViewportPoint(mid + _main.transform.right * side);
            Vector2 center = new Vector2((vpHead.x + vpFeet.x) * 0.5f, (vpHead.y + vpFeet.y) * 0.5f);
            float ry = Mathf.Clamp(Mathf.Abs(vpHead.y - vpFeet.y) * 0.5f * 1.25f, 0.05f, 0.75f);
            float rx = Mathf.Clamp(Mathf.Abs(vpSide.x - vpMid.x) * 1.15f, 0.05f, 0.75f);
            _mask.SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
            _mask.SetFloat("_RadX", rx);
            _mask.SetFloat("_RadY", ry);
            _mask.SetFloat("_Soft", Soft);
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
