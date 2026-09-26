using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Third-person reveal: a soft circle around the body where whatever stands between the
    /// camera and the body turns transparent. A second camera renders the view with its near
    /// plane past what stands in a tube from the camera to the body, past the body's own front
    /// when a wall touches it; a third camera draws only the body, whole, so it is never cut.
    /// The circle lays both over the main view, which is never changed. Local-only, cosmetic.
    public class XRayGlow : MonoBehaviour
    {
        const float NearGap = 0.12f;   // with nothing to cut, the plane stops this short of the body
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
        const float MaxRad = 0.6f;     // a circle around the body, never the whole screen
        const float Huge = 30f;        // a collider this big (the ground, a cliff) is only sliced, never left out
        const float SeeThrough = 0.75f; // inside the circle what stands in front shows at a quarter (1 = gone)
        const int BodyLayer = 24;      // the body's own picture: a layer nothing else uses
        const float Past = 0.4f;       // how far past the body the rays reach for the back of a wall it touches
        const float CornerReach = 0.75f; // another player is looked for this far toward each corner of its shape

        Camera _main, _cam, _bodyCam, _othersCam;
        RenderTexture _rt, _bodyRt, _othersRt;
        readonly List<NetAvatar> _seen = new List<NetAvatar>(), _unseen = new List<NetAvatar>();
        bool _othersOn;
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
        readonly List<Renderer> _bodyMoved = new List<Renderer>();
        readonly List<int> _bodyWas = new List<int>();
        readonly List<Renderer> _bodyNow = new List<Renderer>();

        void ToBodyLayer(Renderer r)
        {
            _bodyMoved.Add(r);
            _bodyWas.Add(r.gameObject.layer);
            r.gameObject.layer = BodyLayer;
        }

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
            PutBodyBack();
            Off();
        }

        /// The reveal must never expose OTHER players. Only players the main
        /// camera cannot see sit out the reveal render; anyone already
        /// visible keeps rendering normally, walls or not, and what of them
        /// stands nearer than the cut gets a picture of its own.
        void OnBeginReveal(ScriptableRenderContext ctx, Camera cam)
        {
            if (_main == null) return;
            if (cam == _othersCam)
            {
                foreach (var a in _seen)
                {
                    if (a == null) continue;
                    a.GetComponentsInChildren(false, _bodyNow);
                    foreach (var r in _bodyNow)
                        if (Drawn(r)) ToBodyLayer(r);
                }
                return;
            }
            if (cam == _bodyCam)
            {
                // the body alone, on its own layer for this one picture: as it is this frame, ink
                // ribbons made a moment ago included, and the lines of the ink drawn on it
                Transform eyeRig = _main.transform;
                GetComponentsInChildren(false, _bodyNow);
                foreach (var r in _bodyNow)
                    if (IsBody(r, eyeRig)) ToBodyLayer(r);
                var world = DrawingWorld.Instance;
                if (world != null)
                    foreach (var s in world.Strokes)
                    {
                        if (s == null || s.Surface == null || s.LineObject == null || !s.Surface.IsChildOf(transform)) continue;
                        foreach (var r in s.LineObject.GetComponentsInChildren<Renderer>(false))
                            if (r != null && r.enabled) ToBodyLayer(r);
                    }
                return;
            }
            if (cam != _cam) return;
            // what stands on the line to the body sits out the window's picture
            foreach (var r in _inTube)
                if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
            foreach (var a in _unseen)
            {
                if (a == null) continue;
                foreach (var r in a.GetComponentsInChildren<Renderer>())
                    if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
            }
        }

        /// Every other player, seen or hidden from the eye. Seen = any of its shape shows: its
        /// middle, its top, or a point toward any corner. Two points were not enough: an acolyte
        /// hiding as a rock has both behind the ground or the rock it copied while most of it shows.
        void SortPlayers(Vector3 eye)
        {
            _seen.Clear();
            _unseen.Clear();
            foreach (var a in NetAvatar.All)
            {
                if (a == null) continue;
                (Seen(eye, a.transform, ShapeShift.FindObjectBounds(a.transform)) ? _seen : _unseen).Add(a);
            }
        }

        static bool Seen(Vector3 eye, Transform who, Bounds b)
        {
            Vector3 c = b.center;
            if (!BlockedFor(eye, who, c) || !BlockedFor(eye, who, new Vector3(c.x, b.max.y, c.z))) return true;
            Vector3 e = b.extents * CornerReach;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                if (!BlockedFor(eye, who, p)) return true;
            }
            return false;
        }

        static bool Drawn(Renderer r) =>
            r != null && r.enabled && r.gameObject.activeInHierarchy
            && !(r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer);

        void OnEndReveal(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == _bodyCam || cam == _othersCam) { PutBodyBack(); return; }
            if (cam != _cam) return;
            foreach (var r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
        }

        /// Every part the body picture borrowed goes back to its own layer (newest first, so a
        /// part listed twice ends on the layer it started with).
        void PutBodyBack()
        {
            for (int i = _bodyMoved.Count - 1; i >= 0; i--)
                if (_bodyMoved[i] != null) _bodyMoved[i].gameObject.layer = _bodyWas[i];
            _bodyMoved.Clear();
            _bodyWas.Clear();
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

            // ★ HIS CALL: the camera always renders the player. The body gets a picture of its own,
            // cleared to nothing around it, and the circle lays it over the cut view whole
            var bodyGo = new GameObject("XRayBodyCam");
            _bodyCam = bodyGo.AddComponent<Camera>();
            _bodyCam.enabled = false;
            _bodyCam.clearFlags = CameraClearFlags.SolidColor;
            _bodyCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _bodyCam.cullingMask = 1 << BodyLayer;
            var bodyData = _bodyCam.GetUniversalAdditionalCameraData();
            bodyData.renderPostProcessing = false;
            bodyData.renderShadows = false;

            // the other players the main camera sees: only what of them the cut's near plane would
            // take away, drawn whole and laid over the cut view, so a player in plain sight never fades
            var othersGo = new GameObject("XRayOthersCam");
            _othersCam = othersGo.AddComponent<Camera>();
            _othersCam.enabled = false;
            _othersCam.clearFlags = CameraClearFlags.SolidColor;
            _othersCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _othersCam.cullingMask = 1 << BodyLayer;
            var othersData = _othersCam.GetUniversalAdditionalCameraData();
            othersData.renderPostProcessing = false;
            othersData.renderShadows = false;

            SetActive(false);
            return true;
        }

        void SetActive(bool on)
        {
            if (_canvasGo != null && _canvasGo.activeSelf != on) _canvasGo.SetActive(on);
            if (_cam != null && _cam.enabled != on) _cam.enabled = on;
            if (_bodyCam != null && _bodyCam.enabled != on) _bodyCam.enabled = on;
            bool others = on && _othersOn;
            if (_othersCam != null && _othersCam.enabled != others) _othersCam.enabled = others;
            if (_mask != null) _mask.SetFloat("_OthersOn", others ? 1f : 0f);
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
                _bodyCam.targetTexture = null;
                _bodyRt.Release();
                Destroy(_bodyRt);
                _bodyRt = null;
                _othersCam.targetTexture = null;
                _othersRt.Release();
                Destroy(_othersRt);
                _othersRt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 24);
                _rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
                _cam.targetTexture = _rt;
                _img.texture = _rt;
                _bodyRt = new RenderTexture(w, h, 24);
                _bodyRt.antiAliasing = _rt.antiAliasing;
                _bodyCam.targetTexture = _bodyRt;
                _mask.SetTexture("_BodyTex", _bodyRt);
                _othersRt = new RenderTexture(w, h, 24);
                _othersRt.antiAliasing = _rt.antiAliasing;
                _othersCam.targetTexture = _othersRt;
                _mask.SetTexture("_OthersTex", _othersRt);
            }
        }

        static bool BlockedFor(Vector3 eye, Transform who, Vector3 at)
        {
            return Physics.Linecast(eye, at, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(who);
        }

        /// The ground or a floor under the head: it never hides the body from a camera above it.
        static bool FloorUnder(RaycastHit h, float headY) =>
            Mathf.Abs(h.normal.y) > 0.7f && h.point.y < headY + 0.1f;

        /// What is only ever sliced by the near plane, never left out of the picture: the ground and
        /// floors under the head, terrain, anything huge.
        static bool Hard(RaycastHit h, float headY) =>
            h.collider is TerrainCollider || h.collider.bounds.size.magnitude > Huge || FloorUnder(h, headY);

        readonly Dictionary<Collider, bool> _livingOf = new Dictionary<Collider, bool>();
        int _era;

        /// A hit that hides the view: not our own body, not the floor under the head, and not a
        /// player, creature or spell (those are never cut away in front of you).
        bool Hides(RaycastHit h, float headY)
        {
            if (h.transform.IsChildOf(transform) || FloorUnder(h, headY)) return false;
            // the grimoire hanging at the paint easel is his own tool, not a wall (the pen skips it too)
            if (SelfPaint.FloatingBook != null && h.transform.IsChildOf(SelfPaint.FloatingBook)) return false;
            if (_era != LivingObject.Era) { _era = LivingObject.Era; _livingOf.Clear(); } // objects came alive or went back to rest
            if (!_livingOf.TryGetValue(h.collider, out bool living))
            {
                if (_livingOf.Count > 512) _livingOf.Clear(); // colliders come and go
                _livingOf[h.collider] = living = Living(h.collider.transform);
            }
            return !living;
        }

        /// Players, creatures and spells are never cleared away: scenery and props only.
        static bool Living(Transform t) =>
            t.GetComponentInParent<SimpleFPSController>() != null || t.GetComponentInParent<NetAvatar>() != null
            || t.GetComponentInParent<Creature>() != null || t.GetComponentInParent<SpellParticle>() != null
            || t.GetComponentInParent<NetMoteProxy>() != null
            || t.GetComponentInParent<Golem>() != null || t.GetComponentInParent<NetGolemProxy>() != null; // a living object too

        /// A part of this body the body picture draws: what it wears, never what rides the camera.
        bool IsBody(Renderer r, Transform eyeRig)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) return false;
            if (eyeRig != null && r.transform.IsChildOf(eyeRig)) return false;
            return _pivot == null || !r.transform.IsChildOf(_pivot);
        }

        /// The renderers a collider stands for: its own subtree, else the piece it sits under (a
        /// kit wall keeps its collider on a child). A whole house or forest is never one thing.
        static Renderer[] RenderersOf(Collider col)
        {
            var rends = col.GetComponentsInChildren<Renderer>(false);
            var t = col.transform.parent;
            for (int up = 0; up < 2 && rends.Length == 0 && t != null; up++, t = t.parent)
                if (t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)
                    rends = t.GetComponentsInChildren<Renderer>(false);
            return rends.Length > 24 ? System.Array.Empty<Renderer>() : rends;
        }

        static readonly RaycastHit[] _hits = new RaycastHit[16];
        static readonly RaycastHit[] _backHits = new RaycastHit[16];
        readonly Dictionary<Collider, float> _entry = new Dictionary<Collider, float>();
        readonly Vector3[] _probes = new Vector3[6];   // the tube's middle, its top, and its four sides

        /// How deep along the lens the cut must reach on the line from the eye through this point:
        /// just past the far face of everything on it that starts in front of the body (bodyNear),
        /// never beyond `cap`; 0 when nothing does. The rays run from the eye to a point `reach`
        /// past it and back again, never from inside the body: a ray that starts inside a collider
        /// never sees it, and a body leaning on a wall has its middle in the wall - that was the
        /// circle that never opened there.
        float CutAlong(Vector3 eye, Vector3 at, Vector3 fwd, float bodyNear, float reach, float cap, float headY)
        {
            Vector3 d = at - eye;
            float len = d.magnitude;
            if (len < 0.01f) return 0f;
            Vector3 dir = d / len;
            float span = len + reach;
            _entry.Clear();
            int n = Physics.RaycastNonAlloc(eye, dir, _hits, span, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (!Hides(h, headY)) continue;
                float depth = Vector3.Dot(h.point - eye, fwd);
                if (!_entry.TryGetValue(h.collider, out float was) || depth < was) _entry[h.collider] = depth;
            }
            float cut = 0f;
            int m = Physics.RaycastNonAlloc(eye + dir * span, -dir, _backHits, span, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
            {
                var h = _backHits[i];
                if (!Hides(h, headY)) continue;
                float back = Vector3.Dot(h.point - eye, fwd);
                // in front of the body: it starts there, or the eye itself stands inside it
                bool entered = _entry.TryGetValue(h.collider, out float front);
                if (entered ? front >= bodyNear : back >= bodyNear) continue;
                cut = Mathf.Max(cut, back + 0.02f);
                _entry.Remove(h.collider);
            }
            // what the far end stands in (or a one-sided face) shows only its front: cut just past it
            foreach (var kv in _entry)
                if (kv.Value < bodyNear) cut = Mathf.Max(cut, kv.Value + 0.02f);
            return Mathf.Min(cut, cap);
        }

        /// What stands on the line from the lens to the body (its middle and its head) is left out
        /// of the window's picture outright, however close to the body it stands. Rays run both
        /// ways, so a wall the camera stands in is found too. A wall only beside him is not on that
        /// line and is just sliced by the cut. Floors and huge things are left to the cut, or the
        /// ground under the wizard would vanish with them.
        void GatherTube(Vector3 eye, float headY)
        {
            _inTube.Clear();
            for (int p = 0; p < 2; p++)
            {
                Vector3 d = _probes[p] - eye;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                Gather(Physics.RaycastNonAlloc(eye, d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore), headY);
                Gather(Physics.RaycastNonAlloc(_probes[p], -d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore), headY);
            }
            if (_rendsOf.Count > 256) _rendsOf.Clear(); // colliders come and go
        }

        void Gather(int n, float headY)
        {
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var col = h.collider;
                if (col == null || h.transform.IsChildOf(transform)) continue;
                if (Hard(h, headY) || Living(col.transform)) continue;
                if (!_rendsOf.TryGetValue(col, out var rends))
                {
                    rends = RenderersOf(col);
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
            if (LoadEgg.Leaving) { Off(); return; } // the travel egg has the screen
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

            // the cut runs along the LENS AXIS, like a tube from the camera to the body
            Vector3 fwd = _main.transform.forward;
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            float axialMin = float.MaxValue, axialMax = float.MinValue;
            int seen = 0;
            Vector3 e = body.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mid + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                float depth = Vector3.Dot(c - eye, fwd);
                axialMin = Mathf.Min(axialMin, depth);
                axialMax = Mathf.Max(axialMax, depth);
                Vector3 vp = _main.WorldToViewportPoint(c);
                if (vp.z <= 0f) continue;
                lo = Vector2.Min(lo, vp);
                hi = Vector2.Max(hi, vp);
                seen++;
            }

            // ★ THE WHOLE TUBE IS SWEPT: its middle, its top and its four sides. On the lines to the
            // body itself the cut may go past the body's front to the back of a wall it touches (his
            // call: the body has its own picture, so nothing of it is lost); beside it the cut stops
            // at the body's front, so a wall merely next to him is only sliced
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
            {
                float depthSpan = axialMax - axialMin;
                for (int i = 0; i < _probes.Length; i++)
                    cut = Mathf.Max(cut, i < 2
                        ? CutAlong(eye, _probes[i], fwd, axialMin, depthSpan + Past, axialMax, head.y)
                        : CutAlong(eye, _probes[i], fwd, axialMin, Past, axialMin, head.y));
            }

            // NEVER A STROBE: the window opens only after the tube has held
            // something for a moment, never right on top of the body - a ghost
            // hovers at its own corpse, where the cut had nothing to cut and
            // flicked on and off every frame - and once it turns over it holds.
            bool occluded = inReach && cut > 0f;
            _blocked = occluded ? _blocked + Time.deltaTime : 0f;
            _hold = occluded ? Hold : _hold - Time.deltaTime;
            bool want = occluded ? _blocked >= Wake : _hold > 0f;
            if (want != _on && Time.unscaledTime >= _flipAt)
            {
                _on = want;
                _flipAt = Time.unscaledTime + Dwell;
            }
            if (!_on) { Off(); return; }
            GatherTube(eye, head.y);

            float near = cut > 0f ? cut : axialMin - NearGap;
            // with nothing to cut away the window renders the plain view and
            // reads as nothing at all, which is what it should do rather than
            // blink out and back
            near = Mathf.Max(near, _main.nearClipPlane + 0.1f);

            SortPlayers(eye);
            _othersOn = _seen.Count > 0;
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
            // the body's own picture: the same lens, the plain near plane, nothing cut
            _bodyCam.transform.SetPositionAndRotation(eye, _main.transform.rotation);
            _bodyCam.fieldOfView = _main.fieldOfView;
            _bodyCam.aspect = _main.aspect;
            _bodyCam.nearClipPlane = _main.nearClipPlane;
            _bodyCam.farClipPlane = _main.farClipPlane;
            // the seen players up to the cut, a hair past it so no seam opens at the plane
            _othersCam.transform.SetPositionAndRotation(eye, _main.transform.rotation);
            _othersCam.fieldOfView = _main.fieldOfView;
            _othersCam.aspect = _main.aspect;
            _othersCam.nearClipPlane = _main.nearClipPlane;
            _othersCam.farClipPlane = Mathf.Max(near + 0.02f, _main.nearClipPlane + 0.05f);

            // ★ A CIRCLE AROUND THE WIZARD (his call): the tube's own width at the BODY, so the
            // window is the same ring around him wherever the cut lands, and the walls outside it
            // stay drawn (a door beside him still shows)
            Vector3 atBody = eye + fwd * Mathf.Max(Vector3.Dot(mid - eye, fwd), 0.05f);
            Vector3 vpBody = _main.WorldToViewportPoint(atBody);
            Vector3 vpBodySide = _main.WorldToViewportPoint(atBody + _main.transform.right * TubeRadius);
            float minR = Mathf.Abs(vpBodySide.x - vpBody.x);
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
            _mask.SetFloat("_Opacity", SeeThrough);
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
                if (!IsBody(r, eyeRig)) continue;
                Bounds rb = r is SkinnedMeshRenderer smr ? ShapeShift.SkinBounds(smr) : r.bounds;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return any ? b : new Bounds(transform.position, Vector3.zero);
        }

        void OnDestroy()
        {
            if (_canvasGo != null) Destroy(_canvasGo);
            if (_cam != null) Destroy(_cam.gameObject);
            if (_bodyCam != null) Destroy(_bodyCam.gameObject);
            if (_othersCam != null) Destroy(_othersCam.gameObject);
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
            if (_bodyRt != null)
            {
                _bodyRt.Release();
                Destroy(_bodyRt);
            }
            if (_othersRt != null)
            {
                _othersRt.Release();
                Destroy(_othersRt);
            }
        }
    }
}
