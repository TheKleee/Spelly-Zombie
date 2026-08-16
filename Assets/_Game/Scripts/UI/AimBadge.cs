using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// THE INTERACT BADGE (, with the reference shots: "there
    /// really needs a popup like in normal games... Right now you have no
    /// idea what can be absorbed as it has no indicator whatsoever...
    /// interactable should pop up above those things when we aim at them").
    ///
    /// One badge, one law: aim at an interactable and its KEY floats above
    /// it - the right key per thing, because different controls own
    /// different verbs:
    ///   F - absorb an AbsorbSource (wizard) · scan a Scannable (acolyte)
    ///   E - grab a spell particle or your own floating spell matter
    /// This is also the ruling that replaced the grab auto-aim: the badge IS
    /// the aim; no badge, no fuzzy cone pick.
    public class AimBadge : MonoBehaviour
    {
        /// What the crosshair is on right now - interactables check this
        /// before accepting their key.
        public static Component Aimed { get; private set; }

        /// The acolyte's scan target under the aim (anything LIFT-ABLE,
        /// the simplification) - ShapeShift reads this on F.
        public static Transform ScanTarget { get; private set; }

        /// True while the flying F is offering a SCAN - that moment owns the
        /// screen alone ("you don't need any other text there
        /// while that text is there"): ModeGuide and the grimoire close-line
        /// both stand down while this is up.
        public static bool ScanOfferLive { get; private set; }

        static Vector3 _externalAt;
        static string _externalKey;
        static int _externalFrame = -1;

        /// A system beyond the crosshair scan asks for the floating key at a
        /// world point this frame (the declare flow: highlighted ink + F,
        /// "users will understand instantly" — , no words needed).
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
            _ui = UIKit.KeyBadge(UIKit.FloatRoot, "AimBadge", "E", out _letter, out _back);

            // the one line under the flying F, spoken only at the scan moment
            // ("you can have a text below saying to scan something...
            // that text should only appear at that specific time")
            _caption = UIKit.Label(_ui, "", 15, new Color(0.98f, 0.94f, 0.82f), TextAnchor.UpperCenter, true);
            var cr = (RectTransform)_caption.transform;
            UIKit.Place(cr, new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(280f, 40f));
            cr.pivot = new Vector2(0.5f, 1f); // hangs below the circle
            _caption.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        Text _caption;

        Vector3 _anchor;
        bool _show;
        Component _stableCand;  // the anti-flicker debounce (falling drips law)
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
                // CARRYING : the E badge STAYS but wears DANGER
                // RED - E is a throw now, and red says so. F is the calm out,
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
                    // the red badge on the object IS the throw prompt; only
                    // the way OUT of carrying needs saying
                    UIPrompt.Offer("F", Loc.T("carry.down"));
                }
            }
            else if (cam != null && !uiBusy
                && Physics.Raycast(cam.transform.position, cam.transform.forward,
                    out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                Resolve(hit);

            // an external offer fills the badge only when the crosshair scan
            // found nothing of its own (declare fires exactly then anyway)
            bool external = !_show && _externalFrame >= Time.frameCount - 1;
            if (external)
            {
                _anchor = _externalAt + Vector3.up * 0.12f;
                _show = true;
                if (_ui == null) Build();
                if (_letter != null && _letter.text != _externalKey) _letter.text = _externalKey;
            }

            // A TARGET MUST HOLD THE AIM A BEAT BEFORE THE BADGE SPEAKS
            // ("this E is constantly flickering over the pot" — the
            // pot's falling ink drips are real grabbable particles, and each
            // one crossing the crosshair flashed the badge for a frame). A
            // tenth of a second of stability filters every strobe source at
            // once; carrying-danger and external offers are deliberate acts
            // and skip the wait.
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

        /// The right key per thing - nothing fuzzy, only what the ray hit.
        void Resolve(RaycastHit hit)
        {
            int me = Grimoire.LocalPlayerId;
            bool acolyte = Sides.Of(me) == Side.Acolyte;

            // lobby pillars are AIMED, never walked into: the badge appears
            // only with the crosshair on them, inside their own range
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

            // THE ACOLYTE SCANS ANYTHING LIFT-ABLE ("to keep
            // things simple") — the same size law the grab lives by, no
            // marker component required. Not people, not creatures, not the
            // pot itself.
            // THE SCAN BADGE ONLY WHEN THE SCAN CAN RUN (the bug, Aug 10: "the
            // F indicator is always popping up even when your page isn't
            // open") — with the book shut it falls through to the lift badge
            // below, which is why E was never appearing.
            if (acolyte && GrimoirePages.BookOpen && GrimoirePages.ScanPageOpen)
            {
                bool person = hit.collider.GetComponentInParent<SimpleFPSController>() != null
                    || hit.collider.GetComponentInParent<Creature>() != null
                    || ZombieOwner.From(hit.collider) != null;  // the dressed skin resolves to nothing above
                    // THE POT IS A COSTUME TOO ("Acolytes should be
                // allowed to turn into the cauldron as well") — the boldest
                // bluff in the game, exempt from the size law
                var potHit = hit.collider.GetComponentInParent<CauldronEconomy>();
                if (!person && hit.collider.GetComponentInParent<SpellParticle>() == null)
                {
                    var s = hit.collider.bounds.size;
                    float dim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    bool sized = dim > 0.05f && dim <= DrawingConfig.LiftMaxDimension;
                    if ((sized || potHit != null)
                        && hit.collider.GetComponentInParent<Renderer>() != null)
                    {
                        var rb = hit.collider.attachedRigidbody;
                        var root = potHit != null ? potHit.transform
                            : rb != null ? rb.transform : hit.collider.transform;
                        ScanTarget = root;
                        Point(hit.collider, root, hit, "F");
                        ScanOfferLive = true;
                        if (_caption != null) _caption.text = Loc.T("scan.aim");
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

            // DRAWN-ON = LIFTABLE, AND IT SAYS SO ("I don't get
            // an indicator that I can grab something if I draw on it... which
            // should popup the moment I can actually lift it. That'd feel
            // natural"). The badge appears at the exact moment your ink
            // authority lifts the thing free - the same law TryGrab enforces,
            // asked ahead of time instead of after the keypress.
            // ROOTED THINGS COUNT TOO (a freshly inked bench that
            // was NEVER lifted showed no badge) - but the badge must ask the
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

        /// The badge sits NEAR THE AIM, not over the object's roof (
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
