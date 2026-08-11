using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// THE INTERACT BADGE (Marko Aug 10, with the reference shots: "there
    /// really needs a popup like in normal games... Right now you have no
    /// idea what can be absorbed as it has no indicator whatsoever...
    /// interactable should pop up above those things when we aim at them").
    ///
    /// One badge, one law: aim at an interactable and its KEY floats above
    /// it — the right key per thing, because different controls own
    /// different verbs:
    ///   F — absorb an AbsorbSource (wizard) · scan a Scannable (acolyte)
    ///   E — grab a spell particle or your own floating spell matter
    /// This is also his ruling that replaced the grab auto-aim: the badge IS
    /// the aim; no badge, no fuzzy cone pick.
    public class AimBadge : MonoBehaviour
    {
        /// What the crosshair is on right now — interactables check this
        /// before accepting their key.
        public static Component Aimed { get; private set; }

        /// The acolyte's scan target under the aim (anything LIFT-ABLE,
        /// Marko's simplification) — ShapeShift reads this on F.
        public static Transform ScanTarget { get; private set; }

        const float Reach = 4.5f;

        RectTransform _ui;
        Text _letter;
        Image _back;
        static readonly Color CalmTint = Color.white;
        static readonly Color DangerTint = new Color(0.9f, 0.34f, 0.28f, 0.95f); // E = THROW now

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("AimBadge");
            DontDestroyOnLoad(go);
            go.AddComponent<AimBadge>();
        }

        void Build()
        {
            UIKit.Retire(_ui);
            _ui = UIKit.Group(UIKit.Root, "AimBadge");
            _ui.sizeDelta = new Vector2(44f, 44f);
            var skin = UISkin.I;
            _back = UIKit.Panel(_ui, skin != null ? skin.RoundBrown : null,
                skin != null ? Color.white : new Color(0.95f, 0.93f, 0.85f, 0.9f));
            UIKit.Stretch((RectTransform)_back.transform);
            _letter = UIKit.Label(_ui, "E", 20, new Color(0.15f, 0.1f, 0.2f), TextAnchor.MiddleCenter, true);
            UIKit.Stretch((RectTransform)_letter.transform);
        }

        Vector3 _anchor;
        bool _show;

        void LateUpdate()
        {
            Aimed = null;
            ScanTarget = null;
            _show = false;
            bool danger = false;
            var cam = Camera.main;
            bool uiBusy = GameMenu.IsOpen || PoseStudio.IsOpen || LobbyStand.PanelOpen;

            if (cam != null && !uiBusy && HandGrab.LocalHolding)
            {
                // CARRYING (Marko Aug 10): the E badge STAYS but wears DANGER
                // RED — E is a throw now, and red says so. F is the calm out,
                // spelled out below so nobody has to lift twice to learn it.
                var held = HandGrab.LocalHeldBody != null ? HandGrab.LocalHeldBody.transform
                    : HandGrab.LocalHeldMote != null ? HandGrab.LocalHeldMote.transform : null;
                if (held != null)
                {
                    _anchor = held.position + Vector3.up * 0.55f;
                    _show = true;
                    danger = true;
                    if (_ui == null) Build();
                    if (_letter != null && _letter.text != "E") _letter.text = "E";
                    UIPrompt.Show("F", "put it down. E throws it");
                }
            }
            else if (cam != null && !uiBusy
                && Physics.Raycast(cam.transform.position, cam.transform.forward,
                    out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                Resolve(hit);

            bool show = _show && (Aimed != null || danger);
            if (_ui == null && show) Build();
            if (_ui == null) return;
            if (_ui.gameObject.activeSelf != show) _ui.gameObject.SetActive(show);
            if (!show) return;

            if (_back != null) _back.color = danger ? DangerTint : CalmTint;
            Vector3 sp = cam.WorldToScreenPoint(_anchor);
            if (sp.z <= 0f) { _ui.gameObject.SetActive(false); return; }
            _ui.position = sp;
        }

        /// The right key per thing — nothing fuzzy, only what the ray hit.
        void Resolve(RaycastHit hit)
        {
            int me = Grimoire.LocalPlayerId;
            bool acolyte = Sides.Of(me) == Side.Acolyte;

            var absorb = hit.collider.GetComponentInParent<AbsorbSource>();
            if (!acolyte && absorb != null && absorb.NextFor(me) != RuneType.None)
            {
                Point(absorb, absorb.transform, hit, "F");
                return;
            }

            // THE ACOLYTE SCANS ANYTHING LIFT-ABLE (Marko Aug 10: "to keep
            // things simple") — the same size law the grab lives by, no
            // marker component required. Not people, not creatures, not the
            // pot itself.
            // THE SCAN BADGE ONLY WHEN THE SCAN CAN RUN (his bug, Aug 10: "the
            // F indicator is always popping up even when your page isn't
            // open") — with the book shut it falls through to the lift badge
            // below, which is why E was never appearing.
            if (acolyte && GrimoirePages.BookOpen && GrimoirePages.ScanPageOpen)
            {
                bool person = hit.collider.GetComponentInParent<SimpleFPSController>() != null
                    || hit.collider.GetComponentInParent<Creature>() != null
                    || ZombieOwner.From(hit.collider) != null   // the dressed skin resolves to nothing above
                    || hit.collider.GetComponentInParent<CauldronEconomy>() != null;
                if (!person && hit.collider.GetComponentInParent<SpellParticle>() == null)
                {
                    var s = hit.collider.bounds.size;
                    float dim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    if (dim > 0.05f && dim <= DrawingConfig.LiftMaxDimension
                        && hit.collider.GetComponentInParent<Renderer>() != null)
                    {
                        var rb = hit.collider.attachedRigidbody;
                        var root = rb != null ? rb.transform : hit.collider.transform;
                        ScanTarget = root;
                        Point(hit.collider, root, hit, "F");
                        return;
                    }
                }
            }

            var mote = hit.collider.GetComponentInParent<SpellParticle>();
            if (mote != null && !mote.Dead)
            {
                Point(mote, mote.transform, hit, "E");
                return;
            }

            var spellMatter = hit.collider.GetComponentInParent<MatterStrike>();
            if (spellMatter != null && spellMatter.SpellForm && spellMatter.OwnerId == me)
            {
                Point(spellMatter, spellMatter.transform, hit, "E");
                return;
            }

            // DRAWN-ON = LIFTABLE, AND IT SAYS SO (Marko Aug 10: "I don't get
            // an indicator that I can grab something if I draw on it... which
            // should popup the moment I can actually lift it. That'd feel
            // natural"). The badge appears at the exact moment your ink
            // authority lifts the thing free — the same law TryGrab enforces,
            // asked ahead of time instead of after the keypress.
            // ROOTED THINGS COUNT TOO (Marko Aug 10: a freshly inked bench that
            // was NEVER lifted showed no badge) — but the badge must ask the
            // GRAB'S OWN QUESTION, not a lookalike. This used to test "any
            // InkMark under the collider vs prop mass", which knew nothing about
            // the world-scale refusal or anchor hold, so aiming at inked ground
            // promised an E the keypress then refused ("The E pops up even on
            // things I can't interact with which is a clear bug"). One law, one
            // implementation, asked ahead of time instead of after.
            if (!HandGrab.LocalHolding && HandGrab.CanAcquire(hit.collider, me))
            {
                var rb = hit.collider.attachedRigidbody;
                var root = rb != null ? rb.transform : hit.collider.transform;
                Point(rb != null ? (Component)rb : hit.collider, root, hit, "E");
            }
        }

        /// The badge sits NEAR THE AIM, not over the object's roof (Marko
        /// Aug 10: "some objects are massive so we can't see that... it
        /// should be above the aim near the object") — the hit point plus a
        /// hand's width of lift, wherever on the thing you're looking.
        void Point(Component what, Transform over, RaycastHit hit, string key)
        {
            Aimed = what;
            _anchor = hit.point + Vector3.up * 0.35f;
            _show = true;
            if (_ui == null) Build();
            if (_letter != null && _letter.text != key) _letter.text = key;
        }
    }
}
