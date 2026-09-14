using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The grimoire's pages: page 0 = the verb (scan/absorb), page 1 = the
    /// seal lesson, then one rune per page. G opens/closes; arrows turn pages,
    /// no wrap. Page art loads from Resources/Custom/GrimoirePage_*; content
    /// spawns under a child named "PageAnchor" (+Y off the paper, +Z toward
    /// the top edge). An Animator bool "Open" mirrors the G state if present;
    /// the page turn itself is the PageFlipFx quad.
    public class GrimoirePages : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.15f, 0.1f, 0.2f);        // owned: dark ink
        static readonly Color Locked = new Color(0.5f, 0.47f, 0.55f, 0.55f); // unowned: faded
        static readonly Color SealInk = new Color(0.55f, 0.12f, 0.16f);  // seals sign in red

        /// True while the local player's grimoire is raised open - HandIK
        /// holds the book up to read; closed, the book hand hangs free.
        public static bool BookOpen { get; private set; }

        /// The page the local book is on (0 when no book has opened yet) - NetSync ships it.
        public static int LocalPage => _owner != null ? _owner._page : 0;

        /// The rune on the open page (one rune per page). None when closed,
        /// mid-flip, or on the seal-lesson page. The declare flow reads this.
        public static RuneType PageRune { get; private set; }

        /// True while the open book shows the seal-lesson page - also the
        /// seal declare target.
        public static bool SealPageOpen { get; private set; }

        static bool _taughtOpen; // the G hint retires after the first open
        bool _liftHidden;        // the book is poofed away while both hands carry
        static bool _popRequested;
        static GrimoirePages _owner;  // the book the statics describe

        /// An outside event asks the book to open - fired on hover-enter,
        /// consumed once.
        public static void RequestOpen() => _popRequested = true;

        /// At the easel the wheel has two jobs: on the floating book it turns
        /// pages, anywhere else it zooms the orbit. True while it is on the book.
        public static bool WheelOnBook { get; private set; }
        static readonly List<Renderer> _rendBuf = new List<Renderer>();

        /// Has the grimoire ever been opened this session? ModeGuide waits on this.
        public static bool TaughtOpen => _taughtOpen;
        bool _open;
        int _page;
        int _autoTarget = -1;    // an unlock riffles the book toward this page
        float _autoNext, _wheelNext;
        const float AutoOpenDelay = 0.45f, AutoFlipEvery = 0.08f;
        const float WheelCooldown = 0.04f; // one notch spread over two frames is still one page
        int _cardsShown = int.MinValue;
        int _writingShown = int.MinValue;
        readonly List<GameObject> _content = new List<GameObject>();
        Transform _anchor;
        [Header("COVER SWING")]
        [Tooltip("Page_Left local rotation with the book open, degrees.")]
        public Vector3 PageOpenEuler = Vector3.zero;
        [Tooltip("Page_Left local rotation with the book closed, degrees.")]
        public Vector3 PageClosedEuler = new Vector3(30.6f, -170.5f, -176.2f);
        [Tooltip("Seconds the cover takes to swing.")]
        public float PageSwingSeconds = 0.25f;

        Transform _pageBone;      // the Page_Left: its rotation is the whole open/close
        Vector3 _pageBoneRest;
        float _pageK;             // 0 closed, 1 open
        bool _pageOpen;
        bool _tucked;             // full hands: the book waits at the belt
        Transform _handParent;
        Vector3 _handLocalPos;
        Quaternion _handLocalRot;

        // the placeholder book's page surface floats 0.028 above its root;
        // the PageAnchor sits ON the paper, so content barely lifts
        float Lift => _anchor == transform ? 0.028f : 0.001f;

        /// Set by CharacterRig when the grimoire model supplied the book -
        /// only then is a missing PageAnchor worth complaining about.
        [System.NonSerialized] public bool AuthoredSkin;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "PageAnchor") { _anchor = t; break; }
            if (_anchor == null) _anchor = transform;
            // the cover is swung by code; the model's animator never writes again
            var flip = GetComponentInChildren<Animator>();
            if (flip != null) flip.enabled = false;

            // the bone's position is structural, only its rotation moves
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Page_Left") { _pageBone = t; _pageBoneRest = t.localPosition; break; }
            ApplyCover();
        }

        void ApplyCover()
        {
            if (_pageBone == null) return;
            _pageBone.localRotation = Quaternion.Slerp(Quaternion.Euler(PageClosedEuler),
                Quaternion.Euler(PageOpenEuler), Mathf.SmoothStep(0f, 1f, _pageK));
            _pageBone.localPosition = _pageBoneRest;
        }

        void LateUpdate()
        {
            if (!_remote) TuckOrDraw(HandGrab.LocalHolding || InkRuneStone.Carried != null);
            // a friend's open book follows their unlocks and side as they land
            else if (_pageOpen && (OwnedMask() != _cardsShown || _sideShown != ShownSide(OwnerId)))
                RebuildRemote();
            _pageK = Mathf.MoveTowards(_pageK, _pageOpen ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.01f, PageSwingSeconds));
            ApplyCover();
        }

        /// A friend's copy: their announced open state and page. The cover
        /// swings, the paper flips and THEIR pages show (their unlocks, their side).
        public void RemoteSet(bool open, int page, bool fx = true)
        {
            if (!_remote) return;
            bool wasOpen = _pageOpen;
            bool turned = open && page != _page;
            if (open != wasOpen)
            {
                SetCoverOpen(open);
                if (fx) Juice.Chime(transform.position);
            }
            else if (turned && fx)
            {
                PageFlipFx.Play(_anchor, Lift, page > _page ? -1 : 1);
                Juice.Chime(transform.position); // the page-turn flourish
            }
            _page = page;
            if (!open) ClearContent();
            else if (!wasOpen || turned) RebuildRemote();
        }

        /// The page build for a friend's book. The statics describe the local
        /// book only, so they are put back after.
        void RebuildRemote()
        {
            var rune = PageRune;
            bool seal = SealPageOpen, scan = ScanPageOpen, absorb = AbsorbPageOpen;
            int mask = OwnedMask();
            _cardsShown = mask;
            _sideShown = ShownSide(OwnerId);
            _page = Mathf.Clamp(_page, 0, Mathf.Max(1, PageCount) - 1);
            Rebuild(mask);
            PageRune = rune;
            SealPageOpen = seal;
            ScanPageOpen = scan;
            AbsorbPageOpen = absorb;
        }

        /// A friend's copy: their hands filled or emptied.
        public void RemoteTuck(bool full)
        {
            if (_remote) TuckOrDraw(full);
        }

        /// Full hands: the book waits at the belt socket, closed. Empty hands
        /// bring it back to the palm exactly as it sat there.
        void TuckOrDraw(bool full)
        {
            if (SelfPaint.FloatingBook == transform) return; // the easel owns it
            if (full == _tucked) return;
            if (full)
            {
                var rig = GetComponentInParent<CharacterRig>();
                var belt = rig != null ? rig.BookSocket : null;
                if (belt == null) // a puppet has no rig: its sockets answer directly
                {
                    var set = GetComponentInParent<SocketSet>();
                    belt = set != null ? (set.Get("Book") ?? set.Get("Belt")) : null;
                }
                if (belt == null) return;
                _handParent = transform.parent;
                _handLocalPos = transform.localPosition;
                _handLocalRot = transform.localRotation;
                transform.SetParent(belt, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                SetArrows(false);
                _tucked = true;
            }
            else
            {
                if (_handParent != null)
                {
                    transform.SetParent(_handParent, false);
                    transform.localPosition = _handLocalPos;
                    transform.localRotation = _handLocalRot;
                }
                _tucked = false;
            }
        }

        void Start()
        {
            // Awake runs inside AddComponent (before the caller can set
            // AuthoredSkin), so the check lives in Start
            if (!AuthoredSkin || _anchor != transform) return;
            Debug.LogWarning($"[SpellyZombie] Grimoire '{name}': no child named \"PageAnchor\". Page art is " +
                "sitting on the book ROOT, not on the paper. Add an empty PageAnchor on the open spread " +
                "(+Y off the paper, +Z toward the top edge). Its SCALE sets the spread size.", gameObject);
        }

        void Update()
        {
            if (_remote || PoseStudio.IsOpen || GameMenu.IsOpen || UIKit.Typing) return;
            // at the belt the book is closed and G belongs to the load
            if (_tucked)
            {
                if (_open) { _open = false; BookOpen = false; SetCoverOpen(false); ResetPages(); }
                return;
            }
            var kb = Keyboard.current;
            if (kb == null) return;

            // G only raises/closes the book (absorb and declare live on F).
            // An outside event can also ask it to open - consumed once.
            bool toggle = kb.gKey.wasPressedThisFrame;
            if (_popRequested)
            {
                _popRequested = false;
                if (!_open) toggle = true;
            }
            // the easel book never closes
            if (toggle && _open && SelfPaint.IsActive) toggle = false;
            if (toggle)
            {
                _open = !_open;
                BookOpen = _open;
                if (_open) _owner = this;
                _taughtOpen = true;
                SetCoverOpen(_open);
                Juice.Chime(transform.position);
                if (_open)
                {
                    _cardsShown = int.MinValue; // force a fresh build below
                }
                else ResetPages();
            }
            if (!_open)
            {
                WheelOnBook = false;
                SetArrows(false);
                // taught once, then the prompt stays out of E-pickup's way -
                // and NEVER while carrying. (He tried removing this and it
                // made the first minute WORSE - no information beats a small
                // prompt. It stays.)
                if (!_taughtOpen && !HandGrab.LocalHolding) UIPrompt.Show("G", Loc.T("grimoire.open"));
                return;
            }
            // the open book offers close and turn chips; declare/absorb Show
            // outranks them, and a live scan offer silences everything else
            if (!GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach
                && !HandGrab.LocalHolding && !AimBadge.ScanOfferLive)
            {
                // at the easel the book can't close, so no close chip there
                if (!SelfPaint.IsActive) UIPrompt.Offer("G", Loc.T("grimoire.close"));
            }

            int pages = PageCount; // wizard: seal + 12 runes · acolyte: seal + kit (+ scan)
            SetArrows(true);

            // the mouse wheel turns pages: away from you is the next page,
            // toward you is back. One page per notch, as fast as the wheel
            // goes; the paper flips each time and the content lands once the
            // wheel rests
            Hints.Offer(Hints.Id.Pages);
            WheelOnBook = SelfPaint.IsActive && CursorOnBook();
            int step = 0;
            var mouse = Mouse.current;
            if (mouse != null && Time.time >= _wheelNext && (!SelfPaint.IsActive || WheelOnBook))
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.01f) step = 1;
                else if (wheel < -0.01f) step = -1;
                if (step != 0) _wheelNext = Time.time + WheelCooldown;
            }
            if (step != 0) _autoTarget = -1; // a hand on the wheel wins
            else if (_autoTarget >= 0 && Time.time >= _autoNext)
            {
                // the unlock riffle: one page at a time toward the new rune
                if (_autoTarget == _page || _autoTarget >= pages) _autoTarget = -1;
                else { step = _autoTarget > _page ? 1 : -1; _autoNext = Time.time + AutoFlipEvery; }
            }
            int target = Mathf.Clamp(_page + step, 0, pages - 1); // ends STOP, no wrap
            if (step != 0 && target != _page)
            {
                Hints.Retire(Hints.Id.Pages);
                _page = target;
                ClearContent();
                // mirrored book axes: sweep the paper the way the turn reads
                PageFlipFx.Play(_anchor, Lift, -step);
                _pendingFlip = true;
                _pendingTimer = 0.12f; // the new page shows as the paper comes down
                Juice.Chime(transform.position); // the page-turn flourish
                DrawingWorld.Instance?.LogEvent($"Grimoire page {_page + 1}/{pages}");
            }

            if (_pendingFlip)
            {
                _pendingTimer -= Time.deltaTime;
                if (_pendingTimer <= 0f)
                {
                    _pendingFlip = false;
                    Rebuild(OwnedMask());
                }
                return; // the stamp check below would double-rebuild mid-flip
            }

            // rebuild when a card lands or the writing bar moved - the bar
            // must fill in front of the player, not on the next flip
            int stamp = OwnedMask();
            if (stamp != _cardsShown || _writingShown != Grimoire.WritingVersion
                || _sideShown != ShownSide(Grimoire.LocalPlayerId)) // C mid-open = new book
            {
                _cardsShown = stamp;
                _writingShown = Grimoire.WritingVersion;
                _sideShown = ShownSide(Grimoire.LocalPlayerId);
                _page = Mathf.Min(_page, pages - 1);
                Rebuild(stamp);
            }
        }

        [Tooltip("Object on the book meaning NEXT page (mouse wheel away from you, wheel up). Shown only while a next page exists.")]
        public GameObject ArrowNext;
        [Tooltip("Object on the book meaning PREVIOUS page (mouse wheel toward you, wheel down). Shown only while a previous page exists.")]
        public GameObject ArrowBack;
        bool _arrowsWarned;

        bool _remote;
        int _remoteOwner;

        /// Whose book this is: the local player's, or the friend a puppet copies.
        int OwnerId => _remote ? _remoteOwner : Grimoire.LocalPlayerId;

        /// A remote copy: the book stays visible but the page arrows are local UI.
        /// The component stays on, so the page bone pin (LateUpdate) holds their
        /// book together too; only the keyboard and the riffle are local.
        public void HideForRemote(int owner)
        {
            if (ArrowNext != null) ArrowNext.SetActive(false);
            if (ArrowBack != null) ArrowBack.SetActive(false);
            _remote = true;
            _remoteOwner = owner;
        }

        /// The page arrows are authored objects on the book: parented there,
        /// they ride it naturally. Code only shows each where a turn exists -
        /// the first spread offers only next, the last only back.
        void SetArrows(bool open)
        {
            if (ArrowNext == null || ArrowBack == null)
            {
                if (open && !_arrowsWarned)
                {
                    _arrowsWarned = true;
                    Debug.LogWarning("[SpellyZombie] GrimoirePages: ArrowNext / ArrowBack are EMPTY. Place two arrow objects on the book and assign them.", this);
                }
                return;
            }
            bool back = open && _page > 0;
            bool next = open && _page < PageCount - 1;
            if (ArrowBack.activeSelf != back) ArrowBack.SetActive(back);
            if (ArrowNext.activeSelf != next) ArrowNext.SetActive(next);
        }

        /// The book closes when it leaves the hand (third person, stow) -
        /// it comes back CLOSED, the default.
        void OnEnable() => Grimoire.Unlocked += OnUnlocked;

        /// A fresh rune: the book in hand opens and riffles to its page. A
        /// disguised acolyte, full hands or a stowed book hold no book at all.
        void OnUnlocked(int owner, RuneType rune)
        {
            if (_remote || !isActiveAndEnabled || owner != Grimoire.LocalPlayerId) return;
            if (ShapeShift.LocalIsShaped || HandGrab.LocalHolding || SimpleFPSController.ThirdPersonActive) return;
            int target = PageOf(rune);
            if (target < 0) return;
            _autoTarget = target;
            if (!_open) { _popRequested = true; _autoNext = Time.time + AutoOpenDelay; }
            else _autoNext = Time.time + AutoFlipEvery;
        }

        /// The page a rune sits on right now, -1 while it has no page.
        int PageOf(RuneType rune)
        {
            var list = Acolyte ? AcolytePageList() : WizardPageList();
            int i = list.IndexOf(rune);
            return i < 0 ? -1 : 2 + i;
        }

        void OnDisable()
        {
            Grimoire.Unlocked -= OnUnlocked;
            _autoTarget = -1;
            _open = false;
            SetCoverOpen(false);
            _pageK = 0f;
            ApplyCover(); // no LateUpdate while disabled: close it now
            SetArrows(false);
            WheelOnBook = false;
            _pendingFlip = false;
            ClearContent();   // _page is KEPT - the book remembers where you were
            // another body's book leaving (a friend's copy, a bot) must not
            // close the one in your hands
            if (_owner != this) return;
            _owner = null;
            BookOpen = false;
            PageRune = RuneType.None;
            SealPageOpen = false;
            ScanPageOpen = false;
            AbsorbPageOpen = false;
        }

        void SetCoverOpen(bool open) => _pageOpen = open;

        /// The pen's aim point (cursor, or the crosshair while locked) over
        /// the book's screen rectangle.
        bool CursorOnBook()
        {
            var cam = Camera.main;
            var mouse = Mouse.current;
            if (cam == null || mouse == null) return false;
            Vector2 at = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouse.position.ReadValue();
            GetComponentsInChildren(false, _rendBuf);
            bool any = false;
            Bounds b = default;
            foreach (var r in _rendBuf)
            {
                if (!r.enabled || r is TrailRenderer || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector3 e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(b.center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                if (sp.z <= 0f) return false;
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
            }
            return at.x >= minX && at.x <= maxX && at.y >= minY && at.y <= maxY;
        }

        void ResetPages()
        {
            ClearContent();
            _pendingFlip = false;
            _autoTarget = -1;
            PageRune = RuneType.None;
            SealPageOpen = false;
            ScanPageOpen = false;
            AbsorbPageOpen = false;
        }

        bool _pendingFlip;
        float _pendingTimer;

        static readonly RuneCardType[] Families =
            (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));

        // acolyte book: the shared seal page, then the runes they have EARNED
        // by deed (AcolyteDeeds), then the Scan page. Nothing is owned at start.
        static readonly List<RuneType> _acoPages = new List<RuneType>();
        List<RuneType> AcolytePageList()
        {
            _acoPages.Clear();
            int me = OwnerId;
            foreach (var r in RuneLibrary.AcolyteKit)      // canonical order
                if (RuneLibrary.IsUnlocked(me, r)) _acoPages.Add(r);
            foreach (var r in RuneLibrary.AcolyteMischief) // then the earned mischief glyphs
                if (RuneLibrary.IsUnlocked(me, r)) _acoPages.Add(r);
            return _acoPages;
        }
        /// Page zero is the verb: scan (acolyte) or absorb (wizard), then the
        /// seal lesson, then the runes.
        public static bool ScanPageOpen { get; private set; }
        public static bool AbsorbPageOpen { get; private set; }
        bool Acolyte => Grimoires.HeldBy(OwnerId) == BookKind.Acolyte;
        /// The side whose book is in these hands: the Life curse hands a wizard the acolyte's.
        static Side ShownSide(int owner) => Grimoires.HeldBy(owner) == BookKind.Acolyte ? Side.Acolyte : Side.Wizard;
        Side _sideShown = (Side)255; // force the first build to pick a side

        /// Arena: a rune's page exists only once the rune is absorbed. The
        /// lobby keeps the whole book from the start.
        static readonly List<RuneType> _wizPages = new List<RuneType>();
        List<RuneType> WizardPageList()
        {
            _wizPages.Clear();
            int me = OwnerId;
            foreach (var fam in Families)
            {
                Pair(fam, out var up, out var down);
                if (RuneLibrary.IsUnlocked(me, up)) _wizPages.Add(up);
                if (RuneLibrary.IsUnlocked(me, down)) _wizPages.Add(down);
            }
            return _wizPages;
        }

        // page 0 = the verb (scan / absorb), page 1 = the seal, then the runes
        int PageCount => Acolyte
            ? 2 + AcolytePageList().Count
            : 2 + WizardPageList().Count;

        /// Bit per family: which chapters are in working ink. The book shows
        /// ALL chapters everywhere - unowned ones just render faded.
        int OwnedMask()
        {
            int mask = 0;
            foreach (var c in Grimoire.CardsOf(OwnerId))
                mask |= 1 << (int)c;
            return mask;
        }

        void ClearContent()
        {
            foreach (var g in _content)
                if (g != null) Destroy(g);
            _content.Clear();
        }

        void Rebuild(int mask)
        {
            ClearContent();
            PageRune = RuneType.None;
            SealPageOpen = _page == 1;   // page 0 is the verb now
            ScanPageOpen = false;
            AbsorbPageOpen = false;

            // PAGE 0 - the verb: SCAN for acolytes, ABSORB for wizards
            if (_page == 0)
            {
                if (Acolyte)
                {
                    ScanPageOpen = true;
                    if (!CustomPage("GrimoirePage_Scan", true))
                        Label("SCAN", new Vector3(0f, 0.001f, 0.02f), 0.004f, Ink);
                }
                else
                {
                    AbsorbPageOpen = true;
                    if (!CustomPage("GrimoirePage_Absorb", true))
                        Label("ABSORB", new Vector3(0f, 0.001f, 0.02f), 0.004f, Ink);
                }
                return;
            }

            if (_page == 1)
            {
                BuildSealLesson(mask); // the seal page is shared by both sides
                return;
            }

            // ---- THE ACOLYTE'S PAGES
            if (Acolyte)
            {
                var acoPages = AcolytePageList();
                int ai = _page - 2;
                if (ai < acoPages.Count)
                {
                    var arune = acoPages[ai];
                    PageRune = arune; // declare flow works on the kit pages too
                    // art: acolyte variant ("_Acolyte") first, wizard art as the stand-in
                    if (CustomPage($"GrimoirePage_{AcolytePageKey(arune)}_Acolyte", true)) return;
                    if (CustomPage($"GrimoirePage_{PageKey(arune)}", true)) return;
                    Label(RuneLibrary.IconFor(arune, OwnerId), new Vector3(0f, 0.001f, 0.094f), 0.003f, Ink);
                    var atex = Wardrobe.RuneIcon(arune, Ink);
                    if (atex != null) Quad(atex, new Vector3(0f, 0f, 0.012f), 0.092f);
                    return;
                }
                return;
            }

            // one rune per page, family by family, up then down. The open
            // page is the rune you can declare.
            var earned = WizardPageList(); // arena: only absorbed runes have pages
            int idx = _page - 2;
            if (idx >= earned.Count) { PageRune = RuneType.None; return; }
            var rune = earned[idx];
            var family = RuneLibrary.CardOf(rune);
            PageRune = rune;

            bool owned = true; // a page that exists is a page you own now

            // the ART, most specific first: per-rune page, then family page
            if (CustomPage($"GrimoirePage_{PageKey(rune)}", owned)) return;
            if (CustomPage($"GrimoirePage_{PageKey(family)}", owned)) return;

            Label(RuneLibrary.Icon(rune), new Vector3(0f, 0.001f, 0.094f),
                0.003f, owned ? Ink : Locked);
            var tex = Wardrobe.RuneIcon(rune, owned ? Ink : Locked);
            if (tex != null) Quad(tex, new Vector3(0f, 0f, 0.012f), 0.092f);
            Label(family.ToString().ToUpper(), new Vector3(0f, 0.001f, -0.092f),
                0.0021f, Locked); // the chapter it belongs to, small
            WritingBar(rune);
        }

        /// The handwriting bar on the rune's page. Shown only while a ramp
        /// exists; free-play runes without a ramp show nothing.
        void WritingBar(RuneType rune)
        {
            int me = OwnerId;
            if (!Grimoire.WritingTracked(me, rune)) return;
            float level = Grimoire.WritingLevelOf(me, rune);
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            Label("handwriting", new Vector3(-0.052f, 0.001f, -0.064f), 0.0019f, Ink);

            const float W = 0.088f, H = 0.006f;
            const float x = 0.022f, z = -0.064f; // bar sits right of its label
            var track = GameObject.CreatePrimitive(PrimitiveType.Quad);
            track.name = "PageWritingTrack";
            Destroy(track.GetComponent<Collider>());
            Place(track.transform, new Vector3(x, 0.0004f, z));
            track.transform.localScale = new Vector3(W, H, 1f);
            track.GetComponent<Renderer>().material =
                new Material(shader) { color = new Color(0.2f, 0.16f, 0.24f, 0.3f) };
            if (level > 0.01f)
            {
                var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
                fill.name = "PageWritingFill";
                Destroy(fill.GetComponent<Collider>());
                Place(fill.transform, new Vector3(x - W * 0.5f * (1f - level), 0.0006f, z));
                fill.transform.localScale = new Vector3(W * level, H, 1f);
                fill.GetComponent<Renderer>().material = new Material(shader) { color = SealInk };
            }
        }

        /// Page art keeps the names it was drawn under. The Affinity pair was
        /// renamed in code (Attract, Repel); the page files were not.
        public static string PageKey(RuneType rune) => rune switch
        {
            RuneType.Attract => "DirectionAway",
            RuneType.Repel => "DirectionToward",
            _ => rune.ToString()
        };

        /// His acolyte pages carry the other glyph under each file name: the march
        /// page (DirectionAway_Acolyte) draws the arrow = Repel, the scatter page
        /// (DirectionToward_Acolyte) the Y = Attract.
        public static string AcolytePageKey(RuneType rune) => rune switch
        {
            RuneType.Attract => "DirectionToward",
            RuneType.Repel => "DirectionAway",
            _ => PageKey(rune)
        };

        public static string PageKey(RuneCardType family)
            => family == RuneCardType.Affinity ? "Direction" : family.ToString();

        /// Page art naming, most specific first: Custom/GrimoirePage_&lt;RuneType&gt;;
        /// _Full (complete spread, nothing stamped on top) and &lt;Family&gt; variants
        /// still resolve. Outside callers (unlock toast, UI) ask PageArt so the
        /// naming lives in one place.
        public static Texture2D PageArt(RuneType rune, bool acolyte)
        {
            Texture2D Load(string n) => PageImage(n);
            if (acolyte)
            {
                var acolytePage = Load($"GrimoirePage_{AcolytePageKey(rune)}_Acolyte");
                if (acolytePage != null) return acolytePage;
            }
            var mine = Load($"GrimoirePage_{PageKey(rune)}");
            return mine != null ? mine : Load($"GrimoirePage_{PageKey(RuneLibrary.CardOf(rune))}");
        }

        bool CustomPage(string pageName, bool owned)
        {
            var art = PageImage(pageName);
            if (art == null) return false;
            ArtQuad(art, owned ? Color.white : new Color(1f, 1f, 1f, 0.45f));
            return true;
        }

        /// ONE place page art is found. The CollectionManager's list is the
        /// real home - set there, a page lives with the game and shows in every
        /// scene, lobby included. Resources is only the old path, kept until
        /// the slots are filled, and it complains once per page so nothing
        /// stays there quietly.
        public static Texture2D PageImage(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return null;

            var mine = CollectionManager.PageNamed(pageName);
            if (mine != null) return mine;

            var old = Resources.Load<Texture2D>($"Custom/{pageName}")
                   ?? Resources.Load<Texture2D>($"Custom/{pageName}_Full");
            if (old != null && _warnedResources.Add(pageName))
                Debug.LogWarning($"[SpellyZombie] '{pageName}' is still coming from Resources. "
                    + "Add a row for it in CollectionManager > Grimoire Pages and the image can "
                    + "leave that folder - Resources ships readable to everyone.");
            return old;
        }

        static readonly System.Collections.Generic.HashSet<string> _warnedResources
            = new System.Collections.Generic.HashSet<string>();

        void ArtQuad(Texture2D tex, Color tint)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PageArtFull";
            Destroy(quad.GetComponent<Collider>());
            // the measured position: (-0.0125, 0, 0) exactly - the -Lift
            // cancels Place()'s lift so Y lands on the zero
            Place(quad.transform, new Vector3(-0.0125f, -Lift, 0f));
            quad.transform.localScale = new Vector3(0.19f, 0.21f, 1f); // the whole spread
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                quad.GetComponent<Renderer>().material =
                    new Material(shader) { mainTexture = tex, color = tint };
        }

        // ------------------------------------------------- page one: seals --

        /// The seal lesson: the same rune sealed in a triangle (quick) and a
        /// circle (long), each with a duration bar.
        void BuildSealLesson(int mask)
        {
            // "Lesson" is the legacy name and still resolves
            if (CustomPage("GrimoirePage_Seal", true)) return;
            if (CustomPage("GrimoirePage_Lesson", true)) return;

            Label("SEAL OVER RUNE", new Vector3(0f, 0.001f, 0.096f), 0.0032f, Ink);

            // demo rune: the first family you own (heat until you own one) -
            // shown in YOUR handwriting, the shape you'll actually draw
            RuneType rune = RuneType.HeatUp;
            for (int i = 0; i < Families.Length; i++)
                if ((mask & (1 << i)) != 0) { Pair(Families[i], out rune, out _); break; }

            SealDemo(-0.042f, rune, Triangle(), "TRIANGLE = QUICK", 0.020f);
            SealDemo(0.042f, rune, Circle(), "CIRCLE = LONG", 0.058f);

            Label("more detail = spell lasts longer", new Vector3(0f, 0.001f, -0.078f), 0.0024f, Ink);
            Label("←  and  →  turn the pages", new Vector3(0f, 0.001f, -0.096f), 0.0019f, Locked);
            WritingBar(RuneType.None); // seal corrections have their bar too
        }

        void SealDemo(float x, RuneType rune, List<Vector2> seal, string caption, float barLen)
        {
            var runeTex = Wardrobe.RuneIcon(rune, Ink);
            if (runeTex != null) Quad(runeTex, new Vector3(x, 0f, 0.034f), 0.032f);
            var sealTex = Wardrobe.InkTexture(new List<List<Vector2>> { seal }, SealInk);
            if (sealTex != null) Quad(sealTex, new Vector3(x, 0.0005f, 0.034f), 0.064f);

            Label(caption, new Vector3(x, 0.001f, -0.008f), 0.0021f, Ink);

            // the duration bar - longer bar, longer spell
            var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bar.name = "PageBar";
            Destroy(bar.GetComponent<Collider>());
            Place(bar.transform, new Vector3(x, 0.0005f, -0.036f));
            bar.transform.localScale = new Vector3(barLen, 0.005f, 1f);
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                bar.GetComponent<Renderer>().material = new Material(shader) { color = SealInk };
        }

        static List<Vector2> Triangle() => new List<Vector2>
        {
            new Vector2(0f, 1f), new Vector2(0.9f, -0.7f),
            new Vector2(-0.9f, -0.7f), new Vector2(0f, 1f)
        };

        static List<Vector2> Circle()
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            return pts;
        }

        // ------------------------------------------------------- builders --

        /// Parents to the page anchor, flat on the paper, tracked for rebuild.
        void Place(Transform t, Vector3 pagePos)
        {
            t.SetParent(_anchor, false);
            t.localPosition = new Vector3(pagePos.x, Lift + pagePos.y, pagePos.z);
            // (90, 180, 0): the Blender book's axes are mirrored, so -90
            // would show the art flipped left-right
            t.localRotation = Quaternion.Euler(90f, 180f, 0f);
            t.gameObject.layer = gameObject.layer;
            _content.Add(t.gameObject);
        }

        void Quad(Texture2D tex, Vector3 pagePos, float scale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PageArt";
            Destroy(quad.GetComponent<Collider>());
            Place(quad.transform, pagePos);
            quad.transform.localScale = Vector3.one * scale;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                quad.GetComponent<Renderer>().material = new Material(shader) { mainTexture = tex };
        }

        void Label(string text, Vector3 pagePos, float charSize, Color color)
        {
            var go = new GameObject("PageLabel");
            Place(go.transform, pagePos);
            // TMP: the page shows a rune's EMOJI, which is a sprite - legacy
            // TextMesh can't draw one. charSize kept as the caller's scale.
            var tm = go.AddComponent<TMPro.TextMeshPro>();
            tm.text = text;
            tm.fontSize = charSize * 1400f; // legacy characterSize to TMP point size
            tm.alignment = TMPro.TextAlignmentOptions.Top;
            tm.color = color;
            tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tm.rectTransform.sizeDelta = new Vector2(charSize * 3000f, charSize * 1200f);
        }

        /// The family's polarity pair - public so dropped spell pages stamp
        /// the same runes as the book.
        public static void Pair(RuneCardType card, out RuneType up, out RuneType down)
        {
            switch (card)
            {
                case RuneCardType.Heat: up = RuneType.HeatUp; down = RuneType.HeatDown; break;
                case RuneCardType.State: up = RuneType.StateSolid; down = RuneType.StateLiquid; break;
                case RuneCardType.Luminance: up = RuneType.LuminanceUp; down = RuneType.LuminanceDown; break;
                case RuneCardType.Sticky: up = RuneType.StickyUp; down = RuneType.StickyDown; break;
                case RuneCardType.Affinity: up = RuneType.Attract; down = RuneType.Repel; break;
                default: up = RuneType.DensityUp; down = RuneType.DensityDown; break;
            }
        }
    }

    /// Code-side page turn: a paper quad hinged on the spine pivots over the
    /// book and dies when it lands. Optional art hook:
    /// Resources/Custom/GrimoirePage_Flip skins the flying page.
    public class PageFlipFx : MonoBehaviour
    {
        const float Life = 0.24f;
        float _t;
        int _dir;

        public static void Play(Transform anchor, float lift, int dir)
        {
            if (anchor == null) return;
            var pivot = new GameObject("PageFlip");
            pivot.transform.SetParent(anchor, false);
            pivot.transform.localPosition = new Vector3(0f, lift + 0.002f, 0f);
            pivot.layer = anchor.gameObject.layer;
            var fx = pivot.AddComponent<PageFlipFx>();
            fx._dir = dir;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Paper";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(pivot.transform, false);
            // half a spread, lying flat, hinged at the spine: the pivot sits
            // ON the spine and the quad hangs half a page to the turning side
            quad.transform.localPosition = new Vector3(dir > 0 ? 0.0475f : -0.0475f, 0f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 180f, 0f); // faces up, same as Place

            quad.transform.localScale = new Vector3(0.095f, 0.21f, 1f);
            quad.layer = anchor.gameObject.layer;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var m = new Material(shader) { color = new Color(0.96f, 0.93f, 0.82f) };
                var tex = GrimoirePages.PageImage("GrimoirePage_Flip");
                if (tex != null) m.mainTexture = tex;
                quad.GetComponent<Renderer>().material = m;
            }
            fx.Apply(0f);
        }

        void Apply(float a)
        {
            // rotate the hinge over the spine with an eased arc
            float ang = Mathf.SmoothStep(0f, 180f, a) * (_dir > 0 ? 1f : -1f);
            transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        void Update()
        {
            _t += Time.deltaTime;
            Apply(Mathf.Clamp01(_t / Life));
            if (_t >= Life) Destroy(gameObject);
        }
    }
}
