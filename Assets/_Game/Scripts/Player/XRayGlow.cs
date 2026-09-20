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
        const float MinRange = 2.5f;   // right on top of the body there is nothing to find
        const float Wake = 0.25f;      // blocked this long before the window opens
        const float Dwell = 0.6f;      // and it may not turn over again for this long
        const float TubeRadius = 1.3f; // ★ HIS CALL: a cylinder this wide runs from the
                                       // camera to the body and whatever stands in it is
                                       // cut away, so the wizard and the room show, never
                                       // a wall in the face. The window IS that tube seen
                                       // from the camera, so it holds still instead of
                                       // breathing with the body's own size.
        const float Soft = 0.3f;       // window edge softness
        const int Downscale = 1;       // full resolution, must match the scene
        const float Hold = 0.3f;       // the window outlives the last blocked frame by this
        const float Ease = 0.08f;      // seconds for the window to follow the body
        const float MaxRad = 1f;       // the tube may take the whole screen when the cut is at the lens
        const float Huge = 30f;        // a collider this big (the ground, a cliff) is only sliced, never left out

        Camera _main, _cam;
        RenderTexture _rt;
        RawImage _img;
        Material _mask;
        GameObject _canvasGo;
        float _hold, _blocked, _flipAt;
        bool _on;
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
        readonly List<Renderer> _inTube = new List<Renderer>();
        readonly Dictionary<Collider, Renderer[]> _rendsOf = new Dictionary<Collider, Renderer[]>();

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
            // what stands in the tube sits out the window's picture
            foreach (var r in _inTube)
                if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
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

        static readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Vector3[] _probes = new Vector3[6];   // the tube's middle, its top, and its four sides

        /// Just past the back of the thing nearest the body on the line from
        /// the eye to this point, along the lens; 0 when nothing is in the way.
        float OccluderBack(Vector3 eye, Vector3 at, Vector3 fwd)
        {
            Vector3 d = eye - at;
            float len = d.magnitude;
            if (len < 0.01f) return 0f;
            int n = Physics.RaycastNonAlloc(at, d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue, back = 0f;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= nearest) continue;
                nearest = h.distance;
                back = Vector3.Dot(h.point - eye, fwd) + 0.02f;
            }
            return back;
        }

        /// Just past the front face of the last collider between the eye and
        /// this point, along the lens; 0 when nothing is in the way. Anything
        /// whose front lies beyond the body's nearest point is in front of the
        /// body's visible part, not hiding it, and is left alone.
        float LastOccluderFront(Vector3 eye, Vector3 at, Vector3 fwd, float bodyNearest)
        {
            Vector3 d = at - eye;
            float len = d.magnitude;
            if (len < 0.01f) return 0f;
            int n = Physics.RaycastNonAlloc(eye, d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore);
            float best = 0f;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                float front = Vector3.Dot(h.point - eye, fwd);
                if (front < bodyNearest) best = Mathf.Max(best, front + 0.02f);
            }
            return best;
        }

        /// ★ EVERYTHING IN THE TUBE (his call): whatever stands between the lens
        /// and the body is left out of the window's picture outright, however
        /// close to the body it stands. The cut alone may never pass the body's
        /// own front, so a wall flush against the wizard stayed in the way.
        /// Rays run both ways, so a wall the camera stands in is found too.
        /// Floors and huge things are left to the cut, or the ground under the
        /// wizard would vanish with them.
        void GatherTube(Vector3 eye)
        {
            _inTube.Clear();
            for (int p = 0; p < _probes.Length; p++)
            {
                Vector3 d = _probes[p] - eye;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                Gather(Physics.RaycastNonAlloc(eye, d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore));
                Gather(Physics.RaycastNonAlloc(_probes[p], -d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore));
            }
            if (_rendsOf.Count > 256) _rendsOf.Clear(); // colliders come and go
        }

        void Gather(int n)
        {
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var col = h.collider;
                if (col == null || h.transform.IsChildOf(transform)) continue;
                if (Mathf.Abs(h.normal.y) > 0.7f) continue; // a floor or a ceiling, not a wall
                if (col is TerrainCollider || col.bounds.size.magnitude > Huge) continue;
                if (!_rendsOf.TryGetValue(col, out var rends))
                {
                    rends = col.GetComponentsInChildren<Renderer>(false);
                    _rendsOf[col] = rends;
                }
                foreach (var r in rends)
                    if (r != null && !_inTube.Contains(r)) _inTube.Add(r);
            }
        }

        void LateUpdate()
        {
            if (EndingShot.Playing) { Off(); return; } // the ending camera has the screen
            if (GhostState.LocalIsGhost) { Off(); return; } // never while a ghost, whoever switched it on
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
            float span = Vector3.Distance(eye, mid);
            bool inReach = span <= MaxRange && span >= MinRange;

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

            // ★ THE WHOLE TUBE IS SWEPT, not just the line to the body: the
            // sides of the cylinder are probed too, so a wall the camera sits
            // in goes as readily as one straight in front of the wizard. The
            // cut always lands past the front face of the LAST thing in the
            // way and never into the body's own visible front.
            float ring = TubeRadius * 0.7f;
            Vector3 right = _main.transform.right, up = _main.transform.up;
            _probes[0] = mid;
            _probes[1] = head;
            _probes[2] = mid + right * ring;
            _probes[3] = mid - right * ring;
            _probes[4] = mid + up * ring;
            _probes[5] = mid - up * ring;
            float cut = 0f;
            if (inReach)
                for (int i = 0; i < _probes.Length; i++)
                {
                    cut = Mathf.Max(cut, LastOccluderFront(eye, _probes[i], fwd, axialMin));
                    cut = Mathf.Max(cut, OccluderBack(eye, _probes[i], fwd));
                }

            // NEVER A STROBE: the window opens only after the tube has held
            // something for a moment, never right on top of the body - a ghost
            // hovers at its own corpse, where the cut had nothing to cut and
            // flicked on and off every frame - and once it turns over it holds.
            bool occluded = inReach && (cut > 0f || Blocked(eye, mid) || Blocked(eye, head));
            _blocked = occluded ? _blocked + Time.deltaTime : 0f;
            _hold = occluded ? Hold : _hold - Time.deltaTime;
            bool want = occluded ? _blocked >= Wake : _hold > 0f;
            if (want != _on && Time.unscaledTime >= _flipAt)
            {
                _on = want;
                _flipAt = Time.unscaledTime + Dwell;
            }
            if (!_on) { Off(); return; }
            GatherTube(eye);

            // the cut clears what stands in the tube and stops there: running it
            // to the body cut the ground in front of a far corpse away too
            float near = cut > 0f ? Mathf.Min(cut, axialMin) : axialMin - NearGap;
            // with nothing to cut away the window renders the plain view and
            // reads as nothing at all, which is what it should do rather than
            // blink out and back
            near = Mathf.Max(near, _main.nearClipPlane + 0.1f);

            SetActive(true);
            EnsureRT();
            _cam.transform.SetPositionAndRotation(eye, _main.transform.rotation);
            _cam.fieldOfView = _main.fieldOfView;
            _cam.aspect = _main.aspect;
            _cam.cullingMask = _main.cullingMask & ~(1 << 5); // never the UI
            _cam.nearClipPlane = near;
            _cam.farClipPlane = _main.farClipPlane;
            // the window must only differ where something was cut away: what
            // the reveal leaves empty is see-through (the circle shader reads
            // its alpha), so the main view shows there, never a hole of sky
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f, 0f);

            // ★ THE TUBE'S OWN WIDTH WHERE IT IS CUT. A cylinder is fat on the
            // screen close to the lens and slim far away, so the window is
            // measured at the CUT: a wall the camera stands in clears the whole
            // screen, the way a ghost sees one from inside, while a wall out at
            // the wizard clears only the run of tube around him - never the map.
            Vector3 atCut = eye + fwd * Mathf.Max(near, 0.05f);
            Vector3 vpCut = _main.WorldToViewportPoint(atCut);
            Vector3 vpCutSide = _main.WorldToViewportPoint(atCut + _main.transform.right * TubeRadius);
            float minR = Mathf.Abs(vpCutSide.x - vpCut.x);
            Vector2 center = (lo + hi) * 0.5f;
            float rx = Mathf.Clamp(Mathf.Max((hi.x - lo.x) * 0.5f * 1.15f, minR), 0.05f, MaxRad);
            float ry = Mathf.Clamp(Mathf.Max((hi.y - lo.y) * 0.5f * 1.15f, minR * _main.aspect), 0.05f, MaxRad);
            // eased, so a ragdoll twitch never shows as a pulse; a body gone
            // off the edge of the screen keeps the circle it had
            if (seen == 0) { }
            else if (!_shown) { _center = center; _rx = rx; _ry = ry; _shown = true; }
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
            _inTube.Clear();
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
