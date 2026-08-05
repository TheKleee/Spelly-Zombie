using UnityEngine;

namespace SpellyZombie
{
    /// THE WAND IS THE MANA BAR (Marko's design): a visible ink reservoir on
    /// the wand drains as you draw and refills as kills award ink — no HUD
    /// pool, you read your ink off the tool in your hand.
    ///
    /// MARKO'S BLENDER WAND CONTRACT: inside Weapon_Wand, name the liquid
    /// mesh "Ink" and put its PIVOT AT THE BOTTOM of the reservoir — this
    /// component scales its local Y by the ink fraction, so the level sinks
    /// toward the pivot as you spend. Glass/container is your own geometry,
    /// any shape (vial, swirl tube, bulb — the sketch's quill works too).
    /// No "Ink" child = a placeholder vial grows on the wand instead.
    public class WandInk : MonoBehaviour
    {
        Transform _ink;
        Vector3 _fullScale;
        PlayerInk _pool;

        // THE WAND ITSELF SHRINKS (Marko: the wand IS ink — "always visibly
        // decaying", and "the wand disintegrating effect should exist as it's
        // getting smaller or increasing, but opposite, as if it's formed").
        // The whole wand scales from the grip with the ink fraction; while
        // the size is actually CHANGING, little motes flake off (drain) or
        // gather in (reform) — the same effect both ways, played opposite.
        Vector3 _wandScale0;
        WandState _state;
        float _factor = 1f;
        GameObject _fx;   // HIS shrink/grow effect: a child named "WandFX",
                          // always present, toggled instead of spawned

        void Start()
        {
            // ONE scan for both contract children — "Ink" and "WandFX" (the
            // per-LateUpdate WandFX search was an array alloc every frame)
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (_ink == null && t.name == "Ink") _ink = t;
                else if (_fx == null && t.name == "WandFX") _fx = t.gameObject;
            }
            if (_ink == null) BuildVial();
            if (_ink != null) _fullScale = _ink.localScale;
            _wandScale0 = transform.localScale; // his authored size is 100%
            _state = GetComponentInParent<WandState>();
        }

        /// Placeholder: a slim ink column strapped to the wand so the
        /// mechanic reads today — his art replaces it wholesale.
        void BuildVial()
        {
            var pivot = new GameObject("Ink"); // bottom pivot, same contract
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = new Vector3(0.014f, -0.01f, 0.045f);
            pivot.transform.localRotation = Quaternion.Euler(70f, 0f, 0f); // lie along the wand
            pivot.layer = gameObject.layer;
            _ink = pivot.transform;

            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "InkColumn";
            Destroy(column.GetComponent<Collider>());
            column.transform.SetParent(pivot.transform, false);
            // cylinder pivot is its center: offset by half height so the
            // PARENT's pivot sits at the bottom and Y-scaling drains downward
            column.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            column.transform.localScale = new Vector3(0.008f, 0.035f, 0.008f);
            column.layer = gameObject.layer;
            // THE WAND IS INK, SO IT IS BLACK (Marko: "the wand needs to be the
            // same color as the ink => both black, not brown and blue")
            column.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(DrawingConfig.InkColor, MoteShade.Opaque);
        }

        void LateUpdate()
        {
            if (_ink == null) return;
            if (_pool == null)
            {
                _pool = GetComponentInParent<PlayerInk>();
                if (_pool == null) return;
            }
            float f = Mathf.Clamp01(_pool.Fraction);
            var s = _fullScale;
            s.y *= Mathf.Max(f, 0.03f); // a dry wand keeps a visible dreg
            _ink.localScale = s;

            // the wand body follows the ink — wandless melts it to nothing,
            // a refill FORMS it back (the same motion, reversed)
            float target = _state != null && !_state.HasWand
                ? 0f
                : Mathf.Lerp(0.4f, 1f, f);
            _factor = Mathf.MoveTowards(_factor, target,
                Time.deltaTime * DrawingConfig.WandResizeSpeed);
            transform.localScale = _wandScale0 * Mathf.Max(0.001f, _factor);

            // NO SPAWNED FLAKES (Marko: "remove this laggy particle from wand
            // degradation") — his hook: a "WandFX" child (found once in Start)
            // is on while the size is really moving, off when it settles.
            if (_fx != null)
            {
                bool changing = Mathf.Abs(_factor - target) > 0.0005f;
                if (_fx.activeSelf != changing) _fx.SetActive(changing);
            }
        }
    }
}
