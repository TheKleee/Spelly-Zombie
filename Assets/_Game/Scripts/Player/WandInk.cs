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

        void Start()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == "Ink") { _ink = t; break; }
            if (_ink == null) BuildVial();
            if (_ink != null) _fullScale = _ink.localScale;
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
            column.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.16f, 0.2f, 0.55f), MoteShade.Opaque); // wet ink blue
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
        }
    }
}
