using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Floating key badge over the aimed interactable. F = absorb (wizard) or
    /// scan (acolyte); E = grab. The badge is the aim: no badge, no cone pick.
    public class AimBadge : MonoBehaviour
    {
        /// What the crosshair is on right now - interactables check this
        /// before accepting their key.
        public static Component Aimed { get; private set; }

        /// The acolyte's scan target under the aim - ShapeShift reads this on F.
        public static Transform ScanTarget { get; private set; }

        /// True while the F badge offers a scan; ModeGuide and the grimoire
        /// close-line stand down while this is up.
        public static bool ScanOfferLive { get; private set; }

        static Vector3 _externalAt;
        static string _externalKey;
        static int _externalFrame = -1;

        /// A system beyond the crosshair scan asks for the floating key at a
        /// world point this frame (used by the declare flow).
        public static void OfferAt(Vector3 at, string key)
        {
            _externalAt = at;
            _externalKey = key;
            _externalFrame = Time.frameCount;
        }

        const float Reach = 4.5f;

        RectTransform _ui;
        Text _letter;
        Image _back;
        static readonly Color CalmTint = Color.white;
        static readonly Color DangerTint = new Color(0.9f, 0.34f, 0.28f, 0.95f); // shown while E means throw

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
            _ui = UIKit.KeyBadge(UIKit.FloatRoot, "AimBadge", "E", out _letter, out _back);

            // caption under the badge, shown only during scan offers
            _caption = UIKit.Label(_ui, "", 15, new Color(0.98f, 0.94f, 0.82f), TextAnchor.UpperCenter, true);
            var cr = (RectTransform)_caption.transform;
            UIKit.Place(cr, new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(280f, 40f));
            cr.pivot = new Vector2(0.5f, 1f); // hangs below the circle
            _caption.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        Text _caption;

        Vector3 _anchor;
        bool _show;
        Component _stableCand;  // anti-flicker debounce
        float _stableSince;

        void LateUpdate()
        {
            Aimed = null;
            ScanTarget = null;
            ScanOfferLive = false;
            if (_caption != null) _caption.text = "";
            _show = false;
            bool danger = false;
            var cam = Camera.main;
            bool uiBusy = GameMenu.IsOpen || PoseStudio.IsOpen || LobbyStand.PanelOpen;

            if (cam != null && !uiBusy && HandGrab.LocalHolding)
            {
                // while carrying: the E badge on the held object turns danger
                // red (E = throw); F = put down
                var held = HandGrab.LocalHeldBody != null ? HandGrab.LocalHeldBody.transform
                    : HandGrab.LocalHeldMote != null ? HandGrab.LocalHeldMote.transform : null;
                if (held != null)
                {
                    _anchor = held.position + Vector3.up * 0.55f;
                    _show = true;
                    danger = true;
                    if (_ui == null) Build();
                    if (_letter != null && _letter.text != "E") _letter.text = "E";
                    UIPrompt.Offer("F", Loc.T("carry.down"));
                }
            }
            else if (cam != null && !uiBusy
                && Physics.Raycast(cam.transform.position, cam.transform.forward,
                    out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                Resolve(hit);

            // external offers apply only when the crosshair scan found nothing
            bool external = !_show && _externalFrame >= Time.frameCount - 1;
            if (external)
            {
                _anchor = _externalAt + Vector3.up * 0.12f;
                _show = true;
                if (_ui == null) Build();
                if (_letter != null && _letter.text != _externalKey) _letter.text = _externalKey;
            }

            // 0.1s of stable aim required before the badge shows;
            // carrying-danger and external offers skip the debounce.
            if (Aimed != _stableCand)
            {
                _stableCand = Aimed;
                _stableSince = Time.time;
            }
            bool stable = Aimed != null && Time.time - _stableSince >= 0.1f;
            bool show = _show && (stable || danger || external);
            if (_ui == null && show) Build();
            if (_ui == null) return;
            if (_ui.gameObject.activeSelf != show) _ui.gameObject.SetActive(show);
            if (!show) return;

            if (_back != null) _back.color = danger ? DangerTint : CalmTint;
            Vector3 sp = cam.WorldToScreenPoint(_anchor);
            if (sp.z <= 0f) { _ui.gameObject.SetActive(false); return; }
            _ui.position = sp;
        }

        /// Picks the key for what the ray hit.
        void Resolve(RaycastHit hit)
        {
            int me = Grimoire.LocalPlayerId;
            bool acolyte = Sides.Of(me) == Side.Acolyte;

            // pillar badges need the crosshair on them within their own Range
            if (ActiveScene.Name == "Lobby")
            {
                var side = hit.collider.GetComponentInParent<SidePillar>();
                if (side != null && hit.distance <= side.Range)
                {
                    Point(side, side.transform, hit, "E");
                    return;
                }
                var hat = hit.collider.GetComponentInParent<HatPillar>();
                if (hat != null && hit.distance <= hat.Range)
                {
                    Point(hat, hat.transform, hit, "E");
                    return;
                }
            }

            var absorb = hit.collider.GetComponentInParent<AbsorbSource>();
            if (!acolyte && absorb != null && absorb.NextFor(me) != RuneType.None)
            {
                Point(absorb, absorb.transform, hit, "F");
                return;
            }

            // Scan offer: the action's own rule (ShapeShift.CanScan). Offered
            // only while the scan page is open; else falls through to lift.
            if (acolyte && GrimoirePages.BookOpen && GrimoirePages.ScanPageOpen)
            {
                if (ShapeShift.CanScan(hit.collider, out var scanRoot))
                {
                    ScanTarget = scanRoot;
                    Point(hit.collider, scanRoot, hit, "F");
                    ScanOfferLive = true;
                    if (_caption != null) _caption.text = Loc.T("scan.aim");
                    return;
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

            // asks the grab's own check (HandGrab.CanAcquire) so the badge
            // matches what TryGrab will accept
            if (!HandGrab.LocalHolding && HandGrab.CanAcquire(hit.collider, me))
            {
                var rb = hit.collider.attachedRigidbody;
                var root = rb != null ? rb.transform : hit.collider.transform;
                Point(rb != null ? (Component)rb : hit.collider, root, hit, "E");
            }
        }

        /// Anchors the badge at the hit point plus a small lift - near the
        /// aim, not over the object's top.
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
