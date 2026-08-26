using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One state channel for everything that can be solid, liquid or gas -
    /// players, zombies, golems, particles. Holds the value and pushes it to
    /// whatever the art understands, in this order, all optional:
    ///   material "_StateT" (SZ_StateMatter) · Animator float "StateT" ·
    ///   an OnStateT(float) message on the object.
    /// Never replaces a material: the push goes through a property block, so
    /// authored shaders and maps survive. Tint rides the same channel.
    [DisallowMultipleComponent]
    public class StateView : MonoBehaviour
    {
        public const float Solid = 1f, Liquid = 0.5f, Gas = 0.1f;

        [Tooltip("1 solid · 0.5 liquid · 0.1 gas. Spells and biomes drive this.")]
        [Range(0f, 1f)] public float StateT = Solid;

        [Tooltip("Body colour, e.g. the fusion of a creature's inherited stats. Alpha unused - the shader owns per-state alpha.")]
        public Color Tint = Color.white;
        public bool DriveTint;

        /// ★ HOW THE BODY MOVES. The same sliders a spell has - wobble, swirl,
        /// bubbles, break-up, rim - on a zombie or a golem, pushed through the
        /// same filter that already keeps the eyes out. A zombie summoned by a
        /// swirling spell swirls; nothing else had to learn what a zombie is.
        public SpellTable.Look Look;
        SpellTable.Look _pushedLook;

        static readonly int StateID = Shader.PropertyToID("_StateT");
        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

        float _pushedFade = 1f;

        Renderer[] _rends;
        Animator _anim;
        MaterialPropertyBlock _mpb;
        bool _hasAnimParam, _messaged;
        float _pushed = -1f;
        Color _pushedTint;

        public static float Of(MatterPhase p) =>
            p == MatterPhase.Solid ? Solid : p == MatterPhase.Liquid ? Liquid : Gas;

        public void Set(MatterPhase p) => StateT = Of(p);

        public MatterPhase Phase =>
            StateT > 0.75f ? MatterPhase.Solid
            : StateT > 0.3f ? MatterPhase.Liquid
            : MatterPhase.Gas;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            Rescan();
        }

        /// Call after the art changes underneath (a dressed body, a new skin).
        public void Rescan()
        {
            // THE EYES ARE THEIR OWN CHANNEL. Body colour says what a thing is
            // made of; pupils say who is driving it and what buff it carries.
            // Tinting the eyes with the body erases the face and the tell.
            var all = GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>(all.Length);
            foreach (var r in all)
                if (r != null && r.GetComponentInParent<GooglyEyes>() == null) keep.Add(r);
            _rends = keep.ToArray();
            _anim = GetComponentInChildren<Animator>();
            _hasAnimParam = false;
            if (_anim != null)
                foreach (var p in _anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Float && p.name == "StateT")
                    { _hasAnimParam = true; break; }
            _messaged = GetComponentInChildren<MonoBehaviour>() != null;
            _pushed = -1f;
        }

        /// HOW VISIBLE THIS BODY IS, 1 = solid, 0 = gone. Its own channel, so a
        /// spell can fade something without pretending it changed state - and
        /// it works the same on a player, a zombie, a golem or a crate.
        /// The shader multiplies it into the alpha its state already gives.
        public float Visibility { get; private set; } = 1f;

        float _fadeUntil, _fadeTo = 1f;

        /// Drop any fade in flight - a woken ghost turns fully real at once.
        public void ClearFade() { _fadeUntil = 0f; _fadeTo = 1f; }

        /// Fade to `visible` (0..1) for this long, then come back. The strongest
        /// fade in flight wins, so two spells cannot brighten each other.
        public void Fade(float visible, float seconds)
        {
            visible = Mathf.Clamp01(visible);
            if (Time.time < _fadeUntil) visible = Mathf.Min(visible, _fadeTo);
            _fadeTo = visible;
            _fadeUntil = Time.time + Mathf.Max(0.01f, seconds);
        }

        /// ★ PUSH EVERYTHING NOW. The editor preview calls this directly, so
        /// there is ONE writer to the body's material: this component. Before,
        /// the preview wrote colour into the property block and StateView's
        /// own pass overwrote it with white - two writers, and the one on the
        /// prefab won. That is why the golem stayed grey and the demon never
        /// turned hot.
        public void PushNow()
        {
            // Awake does not run on an editor preview instance, so make what
            // Awake would have made
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (_rends == null) Rescan();
            _pushed = -1f;              // force the pass to write
            _pushedLook = null;         // and re-read the sliders even on the same object
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            float want = Time.time < _fadeUntil ? _fadeTo : 1f;
            if (!Mathf.Approximately(want, Visibility))
                Visibility = Mathf.MoveTowards(Visibility, want, Time.deltaTime * 2.5f);

            bool stateMoved = !Mathf.Approximately(_pushed, StateT);
            bool tintMoved = DriveTint && _pushedTint != Tint;
            bool fadeMoved = !Mathf.Approximately(_pushedFade, Visibility);
            bool lookMoved = !ReferenceEquals(Look, _pushedLook);
            if (!stateMoved && !tintMoved && !fadeMoved && !lookMoved) return;
            _pushed = StateT;
            _pushedTint = Tint;
            _pushedFade = Visibility;
            _pushedLook = Look;

            if (_rends != null)
                foreach (var r in _rends)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(StateID, StateT);
                    // alpha rides the tint, so a faded body needs the colour
                    // pushed even when nothing is driving the tint itself
                    var c = DriveTint ? Tint : Color.white;
                    c.a = Visibility;
                    _mpb.SetColor(BaseColorID, c);
                    if (Look != null)
                    {
                        _mpb.SetFloat("_Wobble", Look.Wobble);       _mpb.SetFloat("_WobbleSpeed", Look.WobbleSpeed);
                        _mpb.SetFloat("_Swirl", Look.Swirl);         _mpb.SetFloat("_SwirlSpeed", Look.SwirlSpeed);
                        _mpb.SetFloat("_Turbulence", Look.Turbulence);
                        _mpb.SetFloat("_Bubbles", Look.Bubbles);     _mpb.SetFloat("_BubbleScale", Look.BubbleSize);
                        _mpb.SetFloat("_BubbleRise", Look.BubbleRise);
                        _mpb.SetFloat("_Holes", Look.Holes);         _mpb.SetFloat("_HoleScale", Look.HoleSize);
                        _mpb.SetFloat("_Rim", Look.Rim);
                    }
                    r.SetPropertyBlock(_mpb);
                }

            if (_hasAnimParam) _anim.SetFloat("StateT", StateT);
            if (_messaged) SendMessage("OnStateT", StateT, SendMessageOptions.DontRequireReceiver);
        }
    }
}
