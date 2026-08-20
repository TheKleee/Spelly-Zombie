using UnityEngine;

namespace SpellyZombie
{
    public enum EyeMood { Neutral, Scared, Wowed, Mad, Dizzy }

    /// Googly eyes on a spring (the wobble IS the joke); moods restyle pupils, auto-watch makes drawing a distraction; brain overrides decay back to auto.
    public class GooglyEyes : MonoBehaviour
    {
        public EyeMood Mood { get; private set; }
        public Vector3 LookTarget;

        /// Whose "forward" idle gaze follows. Eyes mounted on a BONE inherit
        /// that bone's axes, which rarely point where the body faces - a blob
        /// bone often points straight down. Set this to the body root and the
        /// eyes look where the creature is going. Null = this object's own.
        public Transform Facing;

        Transform Face => Facing != null ? Facing : transform;

        const float Spring = 60f, Damp = 8f;

        Transform _leftEye, _rightEye, _leftPupil, _rightPupil;
        Vector3 _lVel, _rVel, _lPos, _rPos;         // pupil spring state (local)
        Vector3 _lastHeadPos;
        float _moodHold, _scale = 1f, _dizzyPhase;
        float _pupilBase = 0.45f; // the Eyes prefab keeps its own pupil size
        float _pupilDepth = 0.4f;  // ...and its own pupil-forward offset

        /// True when the prefab supplied the eyes - callers must not then
        /// re-position/re-scale them as if they were the code-built pair.
        public bool IsCustom { get; private set; }

        /// A fully wired pair: two eyeballs, two pupils, ready to animate.
        /// False = this component is dead decoration (bake lost the wiring
        /// and self-wire found no usable pair) - callers should rebuild.
        public bool IsAlive => _leftEye != null && _rightEye != null
            && _leftPupil != null && _rightPupil != null;

        Material _pupilMatL, _pupilMatR;   // whatever the pupils wore before a tint
        bool _tinted;

        /// Eyes go WIDE for a moment - the charge tell, and later the
        /// proximity-voice tell (bigger the louder you are). Size is its own
        /// channel: pupil COLOUR carries buffs, so the two never collide.
        public void Swell(float seconds, float amount)
        {
            _swellSpan = Mathf.Max(0.01f, seconds);
            _swellUntil = Time.time + _swellSpan;
            _swellAmount = Mathf.Max(1f, amount);
        }

        float _swellUntil, _swellSpan = 1f, _swellAmount = 1f, _swellShown = 1f;
        Vector3 _restEyeL, _restEyeR;   // the scale they were placed at

        /// 1 = resting, above = wide. Applied on top of the built scale.
        /// ONE swell: open fast, settle back. It used to ride an 18Hz sine,
        /// which read as the eyes flickering rather than going wide.
        float SwellNow()
        {
            float left = _swellUntil - Time.time;
            if (left <= 0f) return 1f;
            float gone = 1f - Mathf.Clamp01(left / _swellSpan);   // 0 at the start
            float k = gone < 0.15f ? gone / 0.15f                  // snap open
                                   : 1f - (gone - 0.15f) / 0.85f;  // then ease shut
            return Mathf.Lerp(1f, _swellAmount, Mathf.Clamp01(k));
        }

        /// Red pupils mark a mind-controlled zombie. Passing null restores
        /// exactly what the pupils wore before.
        public void SetPupilTint(Color? c)
        {
            if (_leftPupil == null || _rightPupil == null) return;
            var lr = _leftPupil.GetComponent<Renderer>();
            var rr = _rightPupil.GetComponent<Renderer>();
            if (lr == null || rr == null) return;

            if (c == null)
            {
                if (!_tinted) return;
                if (_pupilMatL != null) lr.sharedMaterial = _pupilMatL;
                if (_pupilMatR != null) rr.sharedMaterial = _pupilMatR;
                _tinted = false;
                return;
            }

            if (!_tinted)
            {
                _pupilMatL = lr.sharedMaterial;
                _pupilMatR = rr.sharedMaterial;
                _tinted = true;
            }
            var m = MatterFX.Get(c.Value, MoteShade.Opaque);
            lr.sharedMaterial = m;
            rr.sharedMaterial = m;
        }

        /// Builds the rig on any transform. headHeight = local Y of eye line,
        /// scale ≈ creature size (1 = human).
        public static GooglyEyes Attach(Transform body, float headHeight, float scale)
        {
            // the prefab first, both names, component ADOPTED, offset/scale composed
            var custom = PrefabVault.Spawn("Eyes", body) ?? PrefabVault.Spawn("GooglyEyes", body);
            if (custom != null)
            {
                custom.name = "GooglyEyes";
                custom.transform.localPosition += new Vector3(0f, headHeight, 0f);
                custom.transform.localScale *= scale;
                var e = Adopt.Component<GooglyEyes>(custom);
                e.IsCustom = true;
                e._scale = scale;
                if (e.WireEyeballs(out var pupil))
                {
                    // pupil depth is styling; the ratio drives dilation
                    e._pupilDepth = pupil.localPosition.z;

                    // never adopt the authored pupil localScale as _pupilBase:
                    // prefab pupils are sized by mesh, not transform. The 0.45
                    // normalises dilation across all eyes.
                }
                else
                    Debug.LogWarning("[SpellyZombie] Eyes prefab has no Eye→Pupil pair. " +
                        "pupils can't react. Name two children starting with 'Eye', " +
                        "each holding a child named 'Pupil'.", custom);
                return e;
            }

            var rig = new GameObject("GooglyEyes");
            rig.transform.SetParent(body, false);
            rig.transform.localPosition = new Vector3(0f, headHeight, 0f);
            var eyes = rig.AddComponent<GooglyEyes>();
            eyes._scale = scale;
            eyes._leftEye = eyes.BuildEye(rig.transform, -0.11f * scale, out eyes._leftPupil);
            eyes._rightEye = eyes.BuildEye(rig.transform, 0.11f * scale, out eyes._rightPupil);
            return eyes;
        }

        Transform BuildEye(Transform rig, float x, out Transform pupil)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            Destroy(eye.GetComponent<Collider>());
            eye.transform.SetParent(rig, false);
            eye.transform.localPosition = new Vector3(x, 0f, 0.24f * _scale);
            eye.transform.localScale = Vector3.one * 0.16f * _scale;
            eye.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(Color.white, MoteShade.Opaque);

            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = "Pupil";
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(eye.transform, false);
            p.transform.localScale = Vector3.one * 0.45f;
            p.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            p.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(new Color(0.05f, 0.05f, 0.05f), MoteShade.Opaque);
            pupil = p.transform;
            return eye.transform;
        }

        /// Baked bodies lose the private wiring — re-wire; baked pupil scale resets to neutral ratio.
        void Awake()
        {
            if (_leftEye != null && _leftPupil != null) return; // Attach wired us
            if (WireEyeballs(out var pupil))
                _pupilDepth = pupil.localPosition.z; // depth survives; ratio resets
        }

        /// Find two eyeballs: children named "Eye*" each holding a "*Pupil*"
        /// child. Returns the left pupil for proportion reading.
        bool WireEyeballs(out Transform leftPupil)
        {
            leftPupil = null;
            Transform FindPupil(Transform eye)
            {
                foreach (var c in eye.GetComponentsInChildren<Transform>(true))
                    if (c != eye && c.name.Contains("Pupil")) return c;
                return null;
            }
            var balls = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform || !t.name.StartsWith("Eye") || t.name.Contains("Pupil")
                    || t.name == "GooglyEyes" || t.name == "Eyes" || FindPupil(t) == null)
                    continue;
                // a container ("Eyes" group) and its eyeball both match — the
                // OUTERMOST wins per branch, nested repeats are the same eye
                bool nested = false;
                foreach (var b in balls)
                    if (t.IsChildOf(b)) { nested = true; break; }
                if (!nested) balls.Add(t);
            }
            if (balls.Count < 2) return false;
            _leftEye = balls[0];
            _rightEye = balls[1];
            _leftPupil = FindPupil(balls[0]);
            _rightPupil = FindPupil(balls[1]);
            leftPupil = _leftPupil;
            return true;
        }

        /// Brains call this to hold a mood for a while (auto-mood resumes after).
        public void SetMood(EyeMood mood, float seconds)
        {
            Mood = mood;
            _moodHold = seconds;
        }

        /// Local first-person players hide their own eyes (they'd float in view);
        /// the rig keeps simulating so remote players/clips still see them.
        public void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = visible;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // unwired custom eyes are decoration only
            if (_leftEye == null || _rightEye == null
                || _leftPupil == null || _rightPupil == null) return;

            // Wide eyes: the charge tell now, voice volume later. MULTIPLIES
            // whatever the eyes were built or authored at - it must never
            // recompute a size, or a prefab's own eyes get moved and resized
            // out from under whoever placed them.
            float swell = SwellNow();
            if (!Mathf.Approximately(swell, _swellShown))
            {
                _swellShown = swell;
                if (_restEyeL == Vector3.zero)
                {
                    _restEyeL = _leftEye.localScale;
                    _restEyeR = _rightEye.localScale;
                }
                _leftEye.localScale = _restEyeL * swell;
                _rightEye.localScale = _restEyeR * swell;
            }

            // ---- auto behavior unless a brain is holding a mood ----
            if (_moodHold > 0f) _moodHold -= dt;
            else AutoWatch();

            // ---- pupil target: where inside the eye should the pupil sit ----
            float dilate = Mood == EyeMood.Wowed ? 1.8f :
                           Mood == EyeMood.Scared ? 0.45f :
                           Mood == EyeMood.Mad ? 0.7f : 1f;

            // head jiggle: pupils get kicked opposite to head movement
            Vector3 headDelta = transform.position - _lastHeadPos;
            _lastHeadPos = transform.position;
            Vector3 kick = -transform.InverseTransformVector(headDelta) * 14f;

            TickPupil(_leftEye, _leftPupil, ref _lPos, ref _lVel, kick, dilate, dt);
            TickPupil(_rightEye, _rightPupil, ref _rPos, ref _rVel, kick, dilate, dt);
        }

        void AutoWatch()
        {
            // priority: nearby loud event (scared/wowed) > fresh ink > forward
            if (WorldEvents.TryGetLoudest(3f, out var evt))
            {
                LookTarget = evt.Pos;
                float dist = Vector3.Distance(transform.position, evt.Pos);
                Mood = evt.Intensity >= 2f
                    ? (dist < 8f ? EyeMood.Scared : EyeMood.Wowed)
                    : EyeMood.Neutral;
            }
            else if (WorldEvents.InkIsFresh)
            {
                LookTarget = WorldEvents.LatestInkPos;
                Mood = EyeMood.Neutral;
            }
            else
            {
                LookTarget = transform.position + Face.forward * 5f;
                Mood = EyeMood.Neutral;
            }
        }

        void TickPupil(Transform eye, Transform pupil, ref Vector3 pos, ref Vector3 vel,
            Vector3 kick, float dilate, float dt)
        {
            // where the pupil wants to be: toward the look target, clamped to lens
            Vector3 dirLocal = eye.InverseTransformDirection((LookTarget - eye.position).normalized);
            Vector3 target = new Vector3(dirLocal.x, dirLocal.y, 0f) * 0.32f;

            if (Mood == EyeMood.Dizzy) // pupils orbit (stunned)
            {
                _dizzyPhase += dt * 9f;
                target = new Vector3(Mathf.Cos(_dizzyPhase), Mathf.Sin(_dizzyPhase), 0f) * 0.3f;
            }
            else if (Mood == EyeMood.Scared) // trembling
                target += (Vector3)(Random.insideUnitCircle * 0.06f);

            // spring-damper with the head-motion kick = the googly wobble
            vel += (target - pos) * Spring * dt + kick;
            vel *= Mathf.Max(0f, 1f - Damp * dt);
            pos += vel * dt;
            pos = Vector3.ClampMagnitude(pos, 0.34f);

            pupil.localPosition = new Vector3(pos.x, pos.y, _pupilDepth);
            pupil.localScale = Vector3.one * Mathf.MoveTowards(pupil.localScale.x, _pupilBase * dilate, dt * 2.5f);
        }
    }
}
