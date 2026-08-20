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

        static readonly int StateID = Shader.PropertyToID("_StateT");
        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

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
            _rends = GetComponentsInChildren<Renderer>(true);
            _anim = GetComponentInChildren<Animator>();
            _hasAnimParam = false;
            if (_anim != null)
                foreach (var p in _anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Float && p.name == "StateT")
                    { _hasAnimParam = true; break; }
            _messaged = GetComponentInChildren<MonoBehaviour>() != null;
            _pushed = -1f;
        }

        void LateUpdate()
        {
            bool stateMoved = !Mathf.Approximately(_pushed, StateT);
            bool tintMoved = DriveTint && _pushedTint != Tint;
            if (!stateMoved && !tintMoved) return;
            _pushed = StateT;
            _pushedTint = Tint;

            if (_rends != null)
                foreach (var r in _rends)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(StateID, StateT);
                    if (DriveTint) _mpb.SetColor(BaseColorID, Tint);
                    r.SetPropertyBlock(_mpb);
                }

            if (_hasAnimParam) _anim.SetFloat("StateT", StateT);
            if (_messaged) SendMessage("OnStateT", StateT, SendMessageOptions.DontRequireReceiver);
        }
    }
}
