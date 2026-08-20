using UnityEngine;

namespace SpellyZombie
{
    /// The wand is the mana bar: a visible reservoir drains and refills, no
    /// HUD pool. Contract: name the liquid mesh "Ink" with its pivot at the
    /// reservoir bottom - local Y scales with the ink fraction. No "Ink"
    /// child = a placeholder vial grows on the wand instead.
    public class WandInk : MonoBehaviour
    {
        Transform _ink;
        Vector3 _fullScale;
        PlayerInk _pool;

        // the whole wand also scales from the grip with the ink fraction;
        // WandFX plays while the size is changing
        Vector3 _wandScale0;
        WandTipFlow _flow;   // the three motes at the tip: out = draining, in = filling
        float _lastF = -1f;
        WandState _state;
        float _factor = 1f;
        GameObject _fx;   // the shrink/grow effect: a child named "WandFX",
                          // always present, toggled instead of spawned

        void Start()
        {
            // one scan for both contract children - "Ink" and "WandFX"
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (_ink == null && t.name == "Ink") _ink = t;
                else if (_fx == null && t.name == "WandFX") _fx = t.gameObject;
            }
            if (_ink == null) BuildVial();
            if (_ink != null) _fullScale = _ink.localScale;
            _wandScale0 = transform.localScale; // the authored size is 100%
            _state = GetComponentInParent<WandState>();
        }

        /// Placeholder: a slim ink column strapped to the wand so the
        /// mechanic reads today - the art replaces it wholesale.
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
            // the wand matches the ink color
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

            // the tip motes report flow direction
            if (_flow == null) _flow = GetComponent<WandTipFlow>()
                ?? gameObject.AddComponent<WandTipFlow>();
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            _flow.Report((f - _lastF) / dt);
            _lastF = f;

            // the wand body follows the ink - wandless melts it to nothing,
            // a refill FORMS it back (the same motion, reversed)
            float target = _state != null && !_state.HasWand
                ? 0f
                : Mathf.Lerp(0.4f, 1f, f);
            _factor = Mathf.MoveTowards(_factor, target,
                Time.deltaTime * DrawingConfig.WandResizeSpeed);
            transform.localScale = _wandScale0 * Mathf.Max(0.001f, _factor);

            // the "WandFX" child is on while the size is moving, off settled
            if (_fx != null)
            {
                bool changing = Mathf.Abs(_factor - target) > 0.0005f;
                if (_fx.activeSelf != changing) _fx.SetActive(changing);
            }
        }
    }
}
