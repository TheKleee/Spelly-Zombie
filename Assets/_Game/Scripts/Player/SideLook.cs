using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// WHAT CHANGING SIDES LOOKS LIKE. `Sides` records which team you are on;
    /// this is the only thing that makes it visible.
    ///
    /// Marko's calls, in order of how much they matter:
    ///   - THE WAND IS THE IDENTITY SIGNAL. Green means corrupt, and it is the
    ///     one indicator that can also show a HALF-turned wizard later, which a
    ///     model swap never could.
    ///   - Same model, different colour. Not a different body and NOT a smaller
    ///     one: changing size mid-round would move the collider and the camera
    ///     height at the exact moment somebody converts.
    ///   - Nothing is created or destroyed, so switching cannot hitch.
    ///
    /// Tints through a MaterialPropertyBlock rather than touching `.material`,
    /// which would instantiate a copy per player and quietly break batching.
    /// Reverting is therefore exact: clear the block and the original look is
    /// back, whatever his art was.
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

        /// Cache the body's renderers once. THE ROBE IS NOT EVERYTHING YOU CARRY:
        /// the wand is the signal (tinted fully, above) and the grimoire is his
        /// art, so neither is robe.
        ///
        /// AXIOM (Marko Aug 7, "why is grimoire from acolyte green? It looks
        /// strange"): this used to exclude them by SOCKET LOOKUP, which silently
        /// stopped working on a baked player. Runtime-built props did not exist
        /// yet the first time this ran, so they were never collected. A BAKED
        /// prefab carries its book and wand from frame one, but the sockets do
        /// not resolve until the rig's first LateUpdate — so the lookup was
        /// still null here and both got cached as body. The wand suffered too:
        /// the robe pass ran after the wand pass and diluted its full green
        /// down to a 45% mix.
        ///
        /// Testing BY NAME instead is immune to when the rig finishes, and the
        /// names are already a hard contract (CharacterRig and SideLook both do
        /// an exact-name Find for "Wand" and "Grimoire").
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

        /// Held or worn by another system — not skin, not robe.
        bool IsCarried(Transform t)
        {
            for (var walk = t; walk != null && walk != transform; walk = walk.parent)
            {
                string n = walk.name;
                if (n == "Wand" || n == "Grimoire" || n == "Shapes") return true;
            }
            return false;
        }

        /// `colour == null` clears the override and puts his art back exactly.
        readonly System.Collections.Generic.HashSet<Renderer> _mine =
            new System.Collections.Generic.HashSet<Renderer>();

        void Tint(Renderer r, Color? colour, float strength)
        {
            if (r == null) return;

            // ONLY UNDO WHAT WE DID. This used to clear the property block on
            // every body renderer whenever you were a wizard, which wiped
            // colours other systems had set through a block. That is how the
            // hat lost its colour: nothing set it grey, something stopped
            // setting it coloured.
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
