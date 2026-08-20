using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Makes a player's side visible: the wand goes fully corrupt green for
    /// acolytes, the body takes only a hint. Same model and size both sides.
    /// Tints via MaterialPropertyBlock (no material copies), so reverting is
    /// exact.
    public class SideLook : MonoBehaviour
    {
        [Tooltip("How strongly an acolyte's robe takes the corrupt green. The WAND goes " +
                 "fully green regardless; the body is only a hint, so keep this low.")]
        [Range(0f, 1f)] public float BodyTint = 0.45f;

        SimpleFPSController _pilot;
        CharacterRig _rig;
        MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<Renderer> _bodyRends = new List<Renderer>();

        int OwnerId => Grimoire.LocalPlayerId;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _rig = GetComponentInChildren<CharacterRig>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            Sides.Changed += OnSideChanged;
            Apply(Sides.Of(OwnerId));
        }

        void OnDisable() => Sides.Changed -= OnSideChanged;

        void OnSideChanged(int owner, Side side)
        {
            if (owner != OwnerId) return;
            Apply(side);
        }

        // The rig builds the wand at runtime, so it may not exist the first time
        // this runs. Retry on a slow beat until we have actually dressed it once.
        float _retry;
        bool _wandDone;
        Side _want;

        void Update()
        {
            if (_wandDone) return;
            _retry -= Time.deltaTime;
            if (_retry > 0f) return;
            _retry = 0.5f;
            Apply(_want);
        }

        void Apply(Side side)
        {
            _want = side;
            bool corrupt = side == Side.Acolyte;
            Color? paint = corrupt ? DrawingConfig.CorruptInkColor : (Color?)null;

            // ---- the wand: the signal that matters ----
            Transform grip = _rig != null ? _rig.GripSocketR : null;
            Transform wand = grip != null ? grip.Find("Wand") : null;
            if (wand != null)
            {
                foreach (var r in wand.GetComponentsInChildren<Renderer>(true))
                    Tint(r, paint, 1f);
                _wandDone = true;
            }

            // ---- the robe: a hint, not the signal ----
            CollectBody();
            foreach (var r in _bodyRends) Tint(r, paint, BodyTint);
        }

        /// Cache the body's renderers once, excluding carried pieces (wand,
        /// grimoire) BY NAME - socket lookups may not have resolved yet when
        /// this first runs.
        void CollectBody()
        {
            if (_bodyRends.Count > 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (IsCarried(r.transform)) continue;
                _bodyRends.Add(r);
            }
        }

        /// Held or worn by another system - not skin, not robe.
        bool IsCarried(Transform t)
        {
            for (var walk = t; walk != null && walk != transform; walk = walk.parent)
            {
                string n = walk.name;
                if (n == "Wand" || n == "Grimoire" || n == "Shapes") return true;
            }
            return false;
        }

        /// `colour == null` clears the override and puts the art back exactly.
        readonly System.Collections.Generic.HashSet<Renderer> _mine =
            new System.Collections.Generic.HashSet<Renderer>();

        void Tint(Renderer r, Color? colour, float strength)
        {
            if (r == null) return;

            // only clear blocks this component set - clearing every renderer
            // wipes colours other systems set through blocks
            if (colour == null)
            {
                if (_mine.Remove(r)) r.SetPropertyBlock(null);
                return;
            }
            _mine.Add(r);

            r.GetPropertyBlock(_block);
            Color baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                ? r.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
            Color mixed = Color.Lerp(baseCol, colour.Value, Mathf.Clamp01(strength));
            _block.SetColor(BaseColorId, mixed);
            _block.SetColor(ColorId, mixed);   // built-in shaders use _Color
            r.SetPropertyBlock(_block);
        }
    }
}
