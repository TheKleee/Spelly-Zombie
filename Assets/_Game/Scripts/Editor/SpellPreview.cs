using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ A PREVIEW PANE INSIDE A WINDOW. The thing you are making, rendered
    /// right there - not a detour to the scene view. Orbit with drag, zoom with
    /// the wheel, and the shown object can be re-coloured live so sliders are
    /// seen, not imagined.
    ///
    /// Bones in the shown object are exposed as handles drawn over the pane,
    /// so a body can be posed where it is authored.
    public class SpellPreview
    {
        PreviewRenderUtility _pr;
        GameObject _shown;
        Vector2 _orbit = new Vector2(25f, -20f);
        float _zoom = 3.2f;
        Transform[] _bones = new Transform[0];
        int _grabbed = -1;
        MaterialPropertyBlock _block;

        static readonly int StateID = Shader.PropertyToID("_StateT");
        static readonly int ColorID = Shader.PropertyToID("_BaseColor");

        public GameObject Shown => _shown;

        /// Put this prefab in the pane. The previous one is thrown away.
        public void Show(GameObject prefab)
        {
            Clear();
            if (prefab == null) return;
            Ensure();
            _shown = Object.Instantiate(prefab);
            _shown.hideFlags = HideFlags.HideAndDontSave;
            _pr.AddSingleGO(_shown);
            foreach (var col in _shown.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in _shown.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("D_")) list.Add(t);
            _bones = list.ToArray();

            // ★ A PARTICLE EFFECT DOES NOT RUN ON ITS OWN IN A PREVIEW. Nothing
            // ticks it, so a fire or a poison cloud showed an empty grid. They
            // are stepped by hand every repaint, from the pane's own clock -
            // which is also what makes them loop instead of playing once.
            // ★ ONE PATH. The golem renders its material changes correctly and
            // it goes through StateView; the bare blob went through a hand
            // written property-block loop that should have been identical and
            // visibly was not. Rather than keep hunting the difference, the
            // hand-written path is GONE: anything shown here gets a StateView
            // and is driven exactly the way the working body is.
            if (_shown.GetComponentInChildren<StateView>(true) == null)
                _shown.AddComponent<StateView>();

            // WHAT IS THE PANE ACTUALLY SHOWING - printed once per Show so the
            // truth is on screen instead of guessed at: which prefab, at what
            // scale, which mesh, how many bones.
            var smr = _shown.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[SpellyZombie] preview shows '{prefab.name}' | root scale {_shown.transform.localScale} | " +
                (smr != null
                    ? $"mesh '{smr.sharedMesh?.name}' on '{smr.name}' scale {smr.transform.lossyScale}, " +
                      $"rootBone {(smr.rootBone != null ? smr.rootBone.name : "NONE")}, bounds {smr.localBounds.extents}"
                    : "no skinned mesh") +
                $" | D_ bones {CountBones(_shown)}");

            _fx = _shown.GetComponentsInChildren<ParticleSystem>(true);
            _fxClock = 0f;
            foreach (var ps in _fx)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.useUnscaledTime = true;
            }
            _lastTick = EditorApplication.timeSinceStartup;
        }

        static int CountBones(GameObject go)
        {
            int n = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("D_")) n++;
            return n;
        }

        ParticleSystem[] _fx = new ParticleSystem[0];
        float _fxClock;
        double _lastTick;

        /// Step the effects by however long it has been since the last paint,
        /// and wrap at the longest loop so a one-shot burst keeps coming back.
        void TickFx()
        {
            if (_fx.Length == 0) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - _lastTick), 0f, 0.1f);
            _lastTick = now;

            float longest = 0f;
            foreach (var ps in _fx) if (ps != null) longest = Mathf.Max(longest, ps.main.duration + ps.main.startLifetime.constantMax);
            _fxClock += dt;
            bool wrap = longest > 0f && _fxClock > longest + 0.5f;
            if (wrap) _fxClock = 0f;

            foreach (var ps in _fx)
            {
                if (ps == null) continue;
                if (wrap) ps.Simulate(0f, false, true, false);
                ps.Simulate(dt, false, false, false);
            }
        }

        /// ★ WEAR A SAVED POSE. Bones match by name, so a pose asset only has
        /// to be the same rig - anything it does not mention stays put. Without
        /// this the pane always showed the base blob, and a shape you had saved
        /// could never be seen again.
        public void ApplyPose(GameObject pose)
        {
            if (_shown == null || pose == null) return;
            var want = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in pose.GetComponentsInChildren<Transform>(true))
                if (!want.ContainsKey(t.name)) want[t.name] = t;
            foreach (var b in _bones)
            {
                if (b == null || !want.TryGetValue(b.name, out var src)) continue;
                b.localPosition = src.localPosition;
                b.localRotation = src.localRotation;
                b.localScale = src.localScale;
            }
        }

        /// The same, from book data - shapes are data now, not prefabs.
        public void ApplyPose(ShapeDef pose)
        {
            if (_shown == null || pose == null) return;
            var want = new System.Collections.Generic.Dictionary<string, BonePose>();
            foreach (var b in pose.Bones)
                if (!string.IsNullOrEmpty(b.Bone) && !want.ContainsKey(b.Bone)) want[b.Bone] = b;
            foreach (var b in _bones)
            {
                if (b == null || !want.TryGetValue(b.name, out var src)) continue;
                b.localPosition = src.P;
                b.localRotation = src.R;
                b.localScale = src.S;
            }
        }

        /// Colour, state and the material sliders, pushed into whatever the
        /// shown thing is wearing. The sliders matter as much as the colour:
        /// a tornado IS its swirl, and a preview that only showed the colour
        /// left an author posing a funnel they could never see spin.
        public void Tint(Color c, float state01, SpellTable.Look skin = null)
        {
            if (_shown == null) return;
            var view = _shown.GetComponentInChildren<StateView>(true);
            if (view == null) return;   // Show() guarantees one; belt and braces
            view.Tint = c;
            view.DriveTint = true;
            view.StateT = state01;
            view.Look = skin ?? Quiet;
            view.PushNow();
        }

        static readonly SpellTable.Look Quiet = new SpellTable.Look();

        void Put(string id, float v) => _block.SetFloat(id, Mathf.Max(0f, v));

        /// Draw the pane and handle its input. Returns true if a bone moved.
        public bool Draw(Rect rect, bool posable)
        {
            Ensure();
            // the material animates on time, and an editor pane only repaints
            // when asked - so ask, or a tornado stands perfectly still
            if (_shown != null && Event.current.type == EventType.Repaint)
                EditorApplication.delayCall += RequestRepaint;
            bool moved = false;
            var e = Event.current;

            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    _zoom = Mathf.Clamp(_zoom + e.delta.y * 0.15f, 0.8f, 12f);
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button == 0 && posable)
                {
                    _grabbed = NearestBone(new Rect(0f, 0f, rect.width, rect.height),
                                           e.mousePosition - rect.position);
                    if (_grabbed >= 0) e.Use();
                }
                else if (e.type == EventType.MouseDrag)
                {
                    if (_grabbed >= 0 && e.button == 0)
                    {
                        DragBone(_grabbed, new Rect(0f, 0f, rect.width, rect.height), e.delta);
                        moved = true;
                        e.Use();
                    }
                    else if (e.button == 1 || (e.button == 0 && _grabbed < 0))
                    {
                        _orbit += new Vector2(e.delta.x, -e.delta.y) * 0.6f;
                        e.Use();
                    }
                }
            }
            if (e.type == EventType.MouseUp) _grabbed = -1;

            if (e.type == EventType.Repaint)
            {
                TickFx();
                _pr.BeginPreview(rect, GUIStyle.none);
                Vector3 centre = _shown != null ? Bounds().center : Vector3.zero;
                var rot = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
                _pr.camera.transform.position = centre + rot * new Vector3(0f, 0f, -_zoom);
                _pr.camera.transform.LookAt(centre);
                _pr.camera.nearClipPlane = 0.05f;
                _pr.camera.farClipPlane = 60f;
                _pr.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                _pr.lights[0].intensity = 1.4f;
                _pr.ambientColor = new Color(0.35f, 0.35f, 0.4f);
                _pr.camera.Render();
                var tex = _pr.EndPreview();
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);

                // ★ WHICH WAY IS UP. A blob in a void has no orientation, so
                // a ground grid sits under it and a small axis sits in the
                // corner - the same two cues the scene view gives you.
                // Clipped to the pane: the grid lines are drawn in window
                // space and were spilling out past the bottom of it.
                GUI.BeginClip(rect);
                var local = new Rect(0f, 0f, rect.width, rect.height);
                DrawGround(local);
                DrawAxisGizmo(local);
                if (posable) DrawBoneHandles(local);
                GUI.EndClip();
            }

            if (_shown == null && e.type == EventType.Repaint)
                EditorGUI.DropShadowLabel(rect, "nothing to show yet");
            return moved;
        }

        /// A grid on the floor beneath the body, drawn in the pane's own 2D
        /// space from projected world points - so it foreshortens with the
        /// orbit and reads as a floor rather than a backdrop.
        void DrawGround(Rect rect)
        {
            float floor = _shown != null ? Bounds().min.y - 0.02f : 0f;
            const int half = 4; const float step = 0.5f;
            var faint = new Color(1f, 1f, 1f, 0.10f);
            var axis = new Color(1f, 1f, 1f, 0.25f);

            Handles.BeginGUI();
            for (int i = -half; i <= half; i++)
            {
                float k = i * step;
                Handles.color = i == 0 ? axis : faint;
                Line(rect, new Vector3(k, floor, -half * step), new Vector3(k, floor, half * step));
                Line(rect, new Vector3(-half * step, floor, k), new Vector3(half * step, floor, k));
            }
            Handles.EndGUI();
        }

        /// X red, Y green, Z blue, in the bottom-left corner - oriented like the
        /// view is, so tilting the orbit tilts the gizmo.
        void DrawAxisGizmo(Rect rect)
        {
            var cam = _pr.camera.transform;
            Vector2 o = new Vector2(rect.x + 34f, rect.yMax - 34f);
            const float len = 22f;

            Handles.BeginGUI();
            Arm(o, cam, Vector3.right, len, Color.red, "X");
            Arm(o, cam, Vector3.up, len, Color.green, "Y");
            Arm(o, cam, Vector3.forward, len, new Color(0.3f, 0.5f, 1f), "Z");
            Handles.EndGUI();
        }

        static void Arm(Vector2 o, Transform cam, Vector3 world, float len, Color c, string label)
        {
            // the world axis as the camera sees it, flattened into the pane
            Vector3 v = cam.InverseTransformDirection(world);
            Vector2 d = new Vector2(v.x, -v.y) * len;
            Handles.color = c;
            Handles.DrawAAPolyLine(2.5f, o, o + d);
            var st = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = c } };
            GUI.Label(new Rect(o.x + d.x - 6f, o.y + d.y - 8f, 16f, 16f), label, st);
        }

        void Line(Rect rect, Vector3 a, Vector3 b)
        {
            var cam = _pr.camera;
            var pa = cam.WorldToViewportPoint(a);
            var pb = cam.WorldToViewportPoint(b);
            if (pa.z <= 0f || pb.z <= 0f) return;   // behind the camera
            Handles.DrawLine(
                new Vector3(rect.x + pa.x * rect.width, rect.y + (1f - pa.y) * rect.height),
                new Vector3(rect.x + pb.x * rect.width, rect.y + (1f - pb.y) * rect.height));
        }

        // ------------------------------------------------------------- bones
        void DrawBoneHandles(Rect rect)
        {
            Handles.BeginGUI();
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                Vector2 p = ToPane(rect, _bones[i].position);
                if (!rect.Contains(p)) continue;
                var c = i == _grabbed ? Color.yellow : BoneColor(_bones[i].name);
                EditorGUI.DrawRect(new Rect(p.x - 5f, p.y - 5f, 10f, 10f), c);
                // a thin dark edge so a pale one still reads against the body
                Handles.color = new Color(0f, 0f, 0f, 0.6f);
                Handles.DrawSolidRectangleWithOutline(
                    new Rect(p.x - 5f, p.y - 5f, 10f, 10f), Color.clear, Handles.color);
            }
            Handles.EndGUI();
        }

        /// ★ WHICH BONE IS WHICH. The rig names them by the direction they
        /// push - D_Up, D_Dn, D_Xp, D_Xn, D_Yp, D_Yn - so the colour comes off
        /// the name, the same way the scene gizmo colours its arrows: the
        /// positive end wears the full axis colour, the negative end a pale
        /// version of it, and anything unnamed is grey.
        ///
        /// Without this every bone was the same green, and dragging the wrong
        /// one quietly turned a funnel inside out.
        static Color BoneColor(string name)
        {
            Color Pale(Color c) => Color.Lerp(c, Color.white, 0.55f);
            var up = new Color(0.35f, 1f, 0.35f);
            var right = new Color(1f, 0.35f, 0.35f);
            var fwd = new Color(0.4f, 0.55f, 1f);

            switch (name)
            {
                case "D_Up": return up;          // natural up
                case "D_Dn": return Pale(up);    // its opposite
                case "D_Xp": return right;       // natural right
                case "D_Xn": return Pale(right);
                case "D_Yp": return fwd;         // natural forward (the rig's Y is the blob's depth)
                case "D_Yn": return Pale(fwd);
                default:     return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        int NearestBone(Rect rect, Vector2 mouse)
        {
            int best = -1; float bd = 14f * 14f;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                float d = (ToPane(rect, _bones[i].position) - mouse).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void DragBone(int i, Rect rect, Vector2 delta)
        {
            var cam = _pr.camera;
            var t = _bones[i];
            // move in the camera's own plane, scaled by how far the bone is
            float depth = Vector3.Dot(t.position - cam.transform.position, cam.transform.forward);
            float perPixel = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / rect.height;
            Vector3 move = (cam.transform.right * delta.x - cam.transform.up * delta.y) * perPixel;
            Undo.RecordObject(t, "Pose bone");
            t.position += move;
        }

        Vector2 ToPane(Rect rect, Vector3 world)
        {
            var v = _pr.camera.WorldToViewportPoint(world);
            return new Vector2(rect.x + v.x * rect.width, rect.y + (1f - v.y) * rect.height);
        }

        Bounds Bounds()
        {
            var rends = _shown.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(_shown.transform.position, Vector3.one);
            var b = new Bounds(_shown.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in rends)
            {
                // a particle renderer with no particles reports a zero box at
                // the origin and drags the frame off the effect
                if (r is ParticleSystemRenderer && r.bounds.size.sqrMagnitude < 0.0001f) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(_shown.transform.position, Vector3.one * 1.5f);
            return b;
        }

        public System.Action OnNeedsRepaint;
        void RequestRepaint() => OnNeedsRepaint?.Invoke();

        // ----------------------------------------------------------- lifetime
        void Ensure()
        {
            if (_pr != null) return;
            _pr = new PreviewRenderUtility();
            _pr.camera.fieldOfView = 30f;
            _pr.camera.clearFlags = CameraClearFlags.SolidColor;
            _pr.camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
        }

        public void Clear()
        {
            if (_shown != null) Object.DestroyImmediate(_shown);
            _shown = null;
            _bones = new Transform[0];
            _fx = new ParticleSystem[0];
        }

        public void Dispose()
        {
            Clear();
            _pr?.Cleanup();
            _pr = null;
        }
    }
}
