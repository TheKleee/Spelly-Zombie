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
        bool _wandDone, _sizeDone;
        Side _want;

        void Update()
        {
            if (_wandDone && _sizeDone) return;
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

            // ---- the size: on a client this body never beats, so ask ----
            var el = GetComponent<Element>();
            if (el != null) { el.RefreshLook(); _sizeDone = true; }
        }

        /// Re-collected every Apply: the model and costume build over a few
        /// frames, and a list cached before they existed tinted the old
        /// placeholders forever while the real robe stayed pale.
        void CollectBody()
        {
            _bodyRends.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (IsCarried(r.transform)) continue;
                _bodyRends.Add(r);
            }
        }

        /// Held or worn by another system - not skin, not robe.
        bool IsCarried(Transform t) => IsCarried(t, transform);

        static bool IsCarried(Transform t, Transform root)
        {
            for (var walk = t; walk != null && walk != root; walk = walk.parent)
            {
                string n = walk.name;
                if (n == "Wand" || n == "Grimoire" || n == "Shapes") return true;
            }
            return false;
        }

        /// The hat keeps its pillar colour on both sides; an acolyte's takes
        /// the same hint as the robe.
        public Color HatShade(Color hat) => HatShade(hat, _want == Side.Acolyte, BodyTint);

        public static Color HatShade(Color hat, bool acolyte, float bodyTint = 0.45f) => acolyte
            ? Color.Lerp(hat, DrawingConfig.CorruptInkColor, Mathf.Clamp01(bodyTint)) : hat;

        /// `colour == null` clears the override and puts the art back exactly.
        readonly System.Collections.Generic.HashSet<Renderer> _mine =
            new System.Collections.Generic.HashSet<Renderer>();

        void Tint(Renderer r, Color? colour, float strength)
            => Tint(r, colour, strength, _block, _mine, HatColor.Worn(), _want == Side.Acolyte, BodyTint);

        /// The same paint on a puppet: wand fully green for an acolyte, the
        /// robe a hint, the hat its owner's pillar colour. `skip` = a worn
        /// disguise that keeps its own look.
        public static void Paint(Transform root, Transform wand, bool acolyte, Color? hat,
            MaterialPropertyBlock block, System.Collections.Generic.HashSet<Renderer> mine,
            Transform skip = null, float bodyTint = 0.45f)
        {
            Color? paint = acolyte ? DrawingConfig.CorruptInkColor : (Color?)null;
            if (wand != null)
                foreach (var r in wand.GetComponentsInChildren<Renderer>(true))
                    Tint(r, paint, 1f, block, mine, hat, acolyte, bodyTint);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsCarried(r.transform, root)) continue;
                if (skip != null && r.transform.IsChildOf(skip)) continue;
                Tint(r, paint, bodyTint, block, mine, hat, acolyte, bodyTint);
            }
        }

        static void Tint(Renderer r, Color? colour, float strength, MaterialPropertyBlock block,
            System.Collections.Generic.HashSet<Renderer> mine, Color? hatColour, bool acolyte, float bodyTint)
        {
            if (r == null) return;

            // a painted hat is never put back to its bare material: clearing
            // or tinting it starts from the pillar colour
            if (HatColor.IsHat(r.transform) && hatColour is Color hat)
            {
                r.GetPropertyBlock(block);
                Color c = HatShade(hat, acolyte, bodyTint);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
                mine.Add(r);
                return;
            }

            // only clear blocks this component set - clearing every renderer
            // wipes colours other systems set through blocks
            if (colour == null)
            {
                if (mine.Remove(r)) r.SetPropertyBlock(null);
                return;
            }
            mine.Add(r);

            r.GetPropertyBlock(block);
            Color baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                ? r.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
            Color mixed = Color.Lerp(baseCol, colour.Value, Mathf.Clamp01(strength));
            block.SetColor(BaseColorId, mixed);
            block.SetColor(ColorId, mixed);   // built-in shaders use _Color
            r.SetPropertyBlock(block);
        }
    }
}
