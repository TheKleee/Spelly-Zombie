using UnityEngine;

namespace SpellyZombie
{
    public enum EyeMood { Neutral, Scared, Wowed, Mad, Dizzy }

    /// Googly eyes: two white spheres with black pupils that physically lag
    /// behind head motion on a spring (the wobble IS the joke). Pupils track a
    /// look target, and the mood changes what they do:
    ///   Scared → pinprick pupils + trembling      (danger close)
    ///   Wowed  → dilated saucers                  (big things far away)
    ///   Mad    → small fast-locked pupils         (zombie beef)
    ///   Dizzy  → pupils orbit in circles          (charger ate a wall)
    /// Left alone, they auto-watch the world: fresh ink, spells, explosions —
    /// which means you can DISTRACT things by drawing. Brains can override the
    /// look target and mood; overrides decay back to auto.
    public class GooglyEyes : MonoBehaviour
    {
        public EyeMood Mood { get; private set; }
        public Vector3 LookTarget;

        const float Spring = 60f, Damp = 8f;

        Transform _leftEye, _rightEye, _leftPupil, _rightPupil;
        Vector3 _lVel, _rVel, _lPos, _rPos;         // pupil spring state (local)
        Vector3 _lastHeadPos;
        float _moodHold, _scale = 1f, _dizzyPhase;
        float _pupilBase = 0.45f;  // Marko's Eyes prefab keeps its own pupil size
        float _pupilDepth = 0.4f;  // ...and its own pupil-forward offset

        /// True when HIS prefab supplied the eyes — callers must not then
        /// re-position/re-scale them as if they were the code-built pair.
        public bool IsCustom { get; private set; }

        /// A fully wired pair: two eyeballs, two pupils, ready to animate.
        /// False = this component is dead decoration (bake lost the wiring
        /// and self-wire found no usable pair) — callers should rebuild.
        public bool IsAlive => _leftEye != null && _rightEye != null
            && _leftPupil != null && _rightPupil != null;

        /// Builds the rig on any transform. headHeight = local Y of eye line,
        /// scale ≈ creature size (1 = human).
        public static GooglyEyes Attach(Transform body, float headHeight, float scale)
        {
            // MARKO'S PREFAB FIRST (Resources/Custom/Eyes): children named
            // "Eye" — each with a child "Pupil" — get the full googly
            // behavior; anything else in the prefab just rides the head.
            // AXIOM (Marko Jul 25): his Resources/Custom/GooglyEyes.prefab had
            // NEVER loaded — the hook only asked for "Eyes". Both names work
            // now, his prefab's own GooglyEyes component is ADOPTED (adding a
            // second made a dead copy that SetMood then drove), and his
            // authored offset/scale are composed with, not overwritten.
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
                    // pupil DEPTH is styling (his); pupil RATIO is behavior —
                    // dilation animates it every frame, and prefabs that came
                    // from bakes carry a frozen mid-dilation scale that made
                    // eyes permanently saucer-sized. Neutral ratio for all.
                    e._pupilDepth = pupil.localPosition.z;
                }
                else
                    Debug.LogWarning("[SpellyZombie] Eyes prefab has no Eye→Pupil pair — " +
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

        /// A BAKED body carries this component with its private wiring LOST
        /// (Marko's bug: "eyes always too large instead of reacting") — the
        /// pupils sat frozen at whatever dilation the bake caught. Re-find the
        /// pair so they live again. Baked pupil scale is a frozen animation
        /// instant, NOT authoring — it returns to the neutral ratio.
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

        /// First-person trick: the eyes vanish from YOUR camera but keep
        /// casting shadows — no giant orbs in your face, no eyeless shadow.
        public void SetShadowsOnly(bool hidden)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = hidden
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // a custom Eyes prefab without the Eye/Pupil naming contract is
            // pure decoration — it rides the head, nothing to animate
            if (_leftEye == null || _rightEye == null
                || _leftPupil == null || _rightPupil == null) return;

            // ---- auto behavior unless a brain is holding a mood ----
            if (_moodHold > 0f) _moodHold -= dt;
            else AutoWatch();

            // ---- pupil target: where inside the eye should the pupil sit ----
            float dilate = Mood == EyeMood.Wowed ? 1.8f :
                           Mood == EyeMood.Scared ? 0.45f :
                           Mood == EyeMood.Mad ? 0.7f : 1f;

            // head jiggle: pupils get kicked opposite to head movement (googly!)
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
                LookTarget = transform.position + transform.forward * 5f;
                Mood = EyeMood.Neutral;
            }
        }

        void TickPupil(Transform eye, Transform pupil, ref Vector3 pos, ref Vector3 vel,
            Vector3 kick, float dilate, float dt)
        {
            // where the pupil wants to be: toward the look target, clamped to lens
            Vector3 dirLocal = eye.InverseTransformDirection((LookTarget - eye.position).normalized);
            Vector3 target = new Vector3(dirLocal.x, dirLocal.y, 0f) * 0.32f;

            if (Mood == EyeMood.Dizzy) // pupils orbit — universal cartoon for "stunned"
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
