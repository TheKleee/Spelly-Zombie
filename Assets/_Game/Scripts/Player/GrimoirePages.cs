using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The open grimoire teaches. PAGE ONE is the seal lesson — how casting
    /// works (seal drawn AROUND a rune; more detailed boundary = spell lasts
    /// longer). Then ONE PAGE PER RUNE — all twelve, STRICTLY one rune per
    /// page (Marko: "heat rune is not on the same page as chill rune even if
    /// they are opposites"), family by family, up then down. Every chapter
    /// always shows (the book never hides chapters) — owned runes in working
    /// ink, the rest faded like unlearned chapters. Comma and period flip
    /// back / forward. Rebuilds itself when you learn a card. The OPEN page
    /// is also the DECLARE target: F on a misread drawing states it IS this
    /// page's rune (GrimoireAbsorb runs that flow).
    ///
    /// MARKO'S PAGE-ART CONTRACT (design the pages yourself, no code):
    ///   Resources/Custom/GrimoirePage_&lt;Family&gt;       — your art UNDER the glyphs
    ///   Resources/Custom/GrimoirePage_&lt;Family&gt;_Full  — your COMPLETE spread,
    ///                                                  code adds nothing on top
    ///   Resources/Custom/GrimoirePage_Lesson(_Full)  — same for page one
    /// (Family = Heat/State/Luminance/Sticky/Direction/Density. Textures:
    /// Read/Write not needed, any import. Unowned _Full pages render faded.)
    ///
    /// MARKO'S BLENDER BOOK CONTRACT: name the prefab Weapon_Grimoire with
    /// the shared grip pivot. Put a child named "PageAnchor" on the open
    /// spread's surface (+Y off the paper, +Z toward the top edge) — all page
    /// content spawns under it, so the same physical page shows different
    /// things as you flip. The anchor may be a BONE inside the book: content
    /// rides it through the animation.
    ///
    /// G RAISES AND OPENS the book; G again closes it and it hangs low in
    /// the hand (Marko's spec — closed is the default, the view stays clear).
    /// Only an OPEN book turns pages, and pages STOP at the ends, no wrap.
    ///
    /// HIS BOOK ANIMATES ONLY OPEN/CLOSE (Marko: "I only create a 3d
    /// grimoire with open and close animations, while page flip animation is
    /// an effect"): an Animator BOOL named "Open" mirrors the G state when
    /// it exists, so his open/close clips play. The page TURN is OURS — a
    /// paper quad pivots over the spine (left or right by the arrow pressed)
    /// and the new page's content lands mid-turn. His optional art hook:
    /// Resources/Custom/GrimoirePage_Flip texture skins the flying page.
    /// ONLY ← and → turn pages (his ruling removed , and .).
    public class GrimoirePages : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.15f, 0.1f, 0.2f);        // owned: dark ink
        static readonly Color Locked = new Color(0.5f, 0.47f, 0.55f, 0.55f); // unowned: faded
        static readonly Color SealInk = new Color(0.55f, 0.12f, 0.16f);  // seals sign in red

        /// True while the local player's grimoire is raised open — HandIK
        /// holds the book up to read; closed, the book hand hangs free.
        public static bool BookOpen { get; private set; }

        /// THE rune on the open page — ONE RUNE PER PAGE, strictly (Marko:
        /// "heat rune is not on the same page as chill rune even if they are
        /// opposites — each rune gets a dedicated page"). None when closed,
        /// mid-flip, or on the seal-lesson page. The DECLARE flow reads this:
        /// open the rune's page, aim at a wrongly read drawing, F states that
        /// it IS this rune.
        public static RuneType PageRune { get; private set; }

        /// True while the open book shows PAGE ONE — the seal lesson. That
        /// page is also the SEAL DECLARE target (Marko: "a page for seals…
        /// when you recognize it it will be activated").
        public static bool SealPageOpen { get; private set; }

        static bool _taughtOpen; // the G hint retires after the first open
        bool _open;
        int _page;
        int _cardsShown = int.MinValue;
        int _writingShown = int.MinValue;
        readonly List<GameObject> _content = new List<GameObject>();
        Transform _anchor;
        Animator _flip;

        // the placeholder book's page surface floats 0.028 above its root;
        // Marko's PageAnchor sits ON the paper, so content barely lifts
        float Lift => _anchor == transform ? 0.028f : 0.001f;

        /// Set by CharacterRig when HIS grimoire model supplied the book —
        /// only then is a missing PageAnchor worth complaining about.
        [System.NonSerialized] public bool AuthoredSkin;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "PageAnchor") { _anchor = t; break; }
            if (_anchor == null) _anchor = transform;
            _flip = GetComponentInChildren<Animator>();
        }

        void Start()
        {
            // AXIOM (Marko Jul 25): with no PageAnchor, page art silently
            // parents to the book ROOT — floating off the paper, not riding
            // his page-turn. Awake runs inside AddComponent (before the caller
            // can set AuthoredSkin), so the check lives in Start.
            if (!AuthoredSkin || _anchor != transform) return;
            Debug.LogWarning($"[SpellyZombie] Grimoire '{name}': no child named \"PageAnchor\" — page art is " +
                "sitting on the book ROOT, not on the paper. Add an empty PageAnchor on the open spread " +
                "(+Y off the paper, +Z toward the top edge). Its SCALE sets the spread size.", gameObject);
        }

        void Update()
        {
            if (PoseStudio.IsOpen || GameMenu.IsOpen || UIKit.Typing) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // G raises/closes the book and does NOTHING ELSE (Marko: "G is
            // for putting grimoire up and down" — absorbing and declaring
            // both live on F, where absorb takes priority).
            if (kb.gKey.wasPressedThisFrame)
            {
                _open = !_open;
                BookOpen = _open;
                _taughtOpen = true;
                SetAnimatorOpen(_open);
                Juice.Chime(transform.position);
                if (_open)
                {
                    _cardsShown = int.MinValue; // force a fresh build below
                }
                else
                {
                    ClearContent();
                    _pendingFlip = false;
                    PageRune = RuneType.None;
                    SealPageOpen = false;
                }
            }
            if (!_open)
            {
                // taught once, then the prompt stays out of E-pickup's way
                if (!_taughtOpen) UIPrompt.Show("G", Loc.T("grimoire.open"));
                return;
            }
            // the prompt slot is last-caller-wins — while a DECLARE or an
            // absorb is on offer, that prompt matters more than "close".
            // (No page readout anywhere — Marko's ruling: the declare prompt
            // already names the page's rune when it matters, "people are not
            // that dumb.")
            if (!GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach)
                UIPrompt.Show("G", Loc.T("grimoire.close"));

            int pages = 1 + Families.Length * 2; // seal lesson + ONE PAGE PER RUNE (all 12)

            // ← → turn pages — ONLY the arrows (Marko removed , and .). The
            // turn itself is our paper-quad effect; content lands mid-turn.
            Hints.Offer(Hints.Id.Pages);
            int step = 0;
            if (kb.rightArrowKey.wasPressedThisFrame) step = 1;
            if (kb.leftArrowKey.wasPressedThisFrame) step = -1;
            int target = Mathf.Clamp(_page + step, 0, pages - 1); // ends STOP, no wrap
            if (step != 0 && target != _page)
            {
                Hints.Retire(Hints.Id.Pages);
                _page = target;
                ClearContent();
                PageFlipFx.Play(_anchor, Lift, step);
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

            // rebuild when a card lands OR the writing bar moved — a declare
            // happens with the book open on that very page, so the bar must
            // fill in front of the player, not on the next flip
            int stamp = OwnedMask();
            if (stamp != _cardsShown || _writingShown != Grimoire.WritingVersion)
            {
                _cardsShown = stamp;
                _writingShown = Grimoire.WritingVersion;
                _page = Mathf.Min(_page, pages - 1);
                Rebuild(stamp);
            }
        }

        /// The book closes when it leaves the hand (third person, stow) —
        /// it comes back CLOSED, Marko's default.
        void OnDisable()
        {
            _open = false;
            BookOpen = false;
            _pendingFlip = false;
            PageRune = RuneType.None;
            SealPageOpen = false;
            ClearContent();
        }

        // (the floating top-of-screen page readout is GONE — Marko: "why is
        // there text above making the game look buggy while we have a proper
        // text at the bottom?" The bottom prompt names the page instead.)

        void SetAnimatorOpen(bool open)
        {
            if (_flip == null) return;
            foreach (var p in _flip.parameters)
                if (p.type == AnimatorControllerParameterType.Bool && p.name == "Open")
                {
                    _flip.SetBool("Open", open);
                    return;
                }
        }

        bool _pendingFlip;
        float _pendingTimer;

        static readonly RuneCardType[] Families =
            (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));

        /// Bit per family: which chapters are in working ink. Free-practice
        /// grounds (lobby/menu/sandboxes) own everything; in the arena the
        /// book still shows ALL chapters — unowned ones just render faded.
        int OwnedMask()
        {
            if (!RuneLibrary.RestrictedArena) return (1 << Families.Length) - 1;
            int mask = 0;
            foreach (var c in Grimoire.CardsOf(Grimoire.LocalPlayerId))
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
            SealPageOpen = _page == 0;

            if (_page == 0)
            {
                BuildSealLesson(mask);
                return;
            }

            // ONE RUNE PER PAGE, STRICTLY (Marko: "heat rune is not on the
            // same page as chill rune even if they are opposites — each rune
            // gets a dedicated page"): pages run family by family, up then
            // down. The open page IS the rune you can declare.
            int idx = _page - 1;
            var family = Families[idx / 2];
            Pair(family, out var up, out var down);
            var rune = idx % 2 == 0 ? up : down;
            PageRune = rune;

            int me = Grimoire.LocalPlayerId;
            bool owned = !RuneLibrary.RestrictedArena || Grimoire.HasRune(me, rune);

            // HIS ART, most specific first: per-rune page, then family page
            if (CustomPage($"GrimoirePage_{rune}", owned)) return;
            if (CustomPage($"GrimoirePage_{family}", owned)) return;

            Label(RuneLibrary.Icon(rune), new Vector3(0f, 0.001f, 0.094f),
                0.003f, owned ? Ink : Locked);
            var tex = Wardrobe.RuneIcon(rune, owned ? Ink : Locked);
            if (tex != null) Quad(tex, new Vector3(0f, 0f, 0.012f), 0.092f);
            Label(family.ToString().ToUpper(), new Vector3(0f, 0.001f, -0.092f),
                0.0021f, Locked); // the chapter it belongs to, small
            WritingBar(rune);
        }

        /// THE WRITING LEVEL on the rune's own page (Marko: "a handwriting
        /// label on a bar that's filling up… seemingly maxed out after 10
        /// drawing corrections") — how practiced YOUR hand is. Shown only
        /// while a ramp exists: a fresh unlock starts it empty; wholesale
        /// free-play runes (no ramp) show nothing.
        void WritingBar(RuneType rune)
        {
            int me = Grimoire.LocalPlayerId;
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

        /// Marko's hand-designed page art. "<name>_Full" = his complete
        /// spread, nothing drawn over it (faded when unowned); "<name>" =
        /// his background, glyphs still stamp on top. True = page handled.
        ///
        /// EVERY PAGE IS DRAWN BY HIM (his ruling Jul 25) — one drawing per
        /// RUNE. Names, most specific first:
        ///   Custom/GrimoirePage_&lt;RuneType&gt;      e.g. GrimoirePage_HeatUp
        ///   Custom/GrimoirePage_&lt;RuneType&gt;_Full  his complete spread
        ///   Custom/GrimoirePage_&lt;Family&gt;(_Full)  covers both halves at once
        ///   Custom/GrimoirePage_Lesson(_Full)     page one
        bool CustomPage(string pageName, bool owned)
        {
            var full = Resources.Load<Texture2D>($"Custom/{pageName}_Full");
            if (full != null)
            {
                ArtQuad(full, owned ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                return true;
            }
            var bg = Resources.Load<Texture2D>($"Custom/{pageName}");
            if (bg != null) ArtQuad(bg, owned ? Color.white : new Color(1f, 1f, 1f, 0.55f));
            return false;
        }

        void ArtQuad(Texture2D tex, Color tint)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PageArtFull";
            Destroy(quad.GetComponent<Collider>());
            Place(quad.transform, new Vector3(0f, -0.0004f, 0f));
            quad.transform.localScale = new Vector3(0.19f, 0.21f, 1f); // the whole spread
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                quad.GetComponent<Renderer>().material =
                    new Material(shader) { mainTexture = tex, color = tint };
        }

        // ------------------------------------------------- page one: seals --

        /// The lesson, drawn not written: the same rune sealed in a triangle
        /// (3 strokes of effort — a quick spark) and in a circle (more pen,
        /// more power), each with a duration bar you can compare at a glance.
        void BuildSealLesson(int mask)
        {
            if (CustomPage("GrimoirePage_Lesson", true)) return;

            Label("SEAL OVER RUNE", new Vector3(0f, 0.001f, 0.096f), 0.0032f, Ink);

            // demo rune: the first family you own (heat until you own one) —
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

            // the duration bar — longer bar, longer spell
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
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
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
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.UpperCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            if (tm.font != null)
                go.GetComponent<MeshRenderer>().material = tm.font.material;
        }

        /// The family's polarity pair — public: dropped spell pages stamp the
        /// same runes so page-in-book and page-on-ground match (Marko's rule).
        public static void Pair(RuneCardType card, out RuneType up, out RuneType down)
        {
            switch (card)
            {
                case RuneCardType.Heat: up = RuneType.HeatUp; down = RuneType.HeatDown; break;
                case RuneCardType.State: up = RuneType.StateSolid; down = RuneType.StateLiquid; break;
                case RuneCardType.Luminance: up = RuneType.LuminanceUp; down = RuneType.LuminanceDown; break;
                case RuneCardType.Sticky: up = RuneType.StickyUp; down = RuneType.StickyDown; break;
                case RuneCardType.Direction: up = RuneType.DirectionAway; down = RuneType.DirectionToward; break;
                default: up = RuneType.DensityUp; down = RuneType.DensityDown; break;
            }
        }
    }

    /// THE CODE-SIDE PAGE TURN (Marko: his Blender book carries only
    /// open/close — "page flip animation is an effect"): a paper quad hinged
    /// on the spine pivots over the book, left or right by the arrow that
    /// was pressed, and dies when it lands. His optional art hook:
    /// Resources/Custom/GrimoirePage_Flip texture skins the flying page.
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
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(0.095f, 0.21f, 1f);
            quad.layer = anchor.gameObject.layer;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var m = new Material(shader) { color = new Color(0.96f, 0.93f, 0.82f) };
                var tex = Resources.Load<Texture2D>("Custom/GrimoirePage_Flip");
                if (tex != null) m.mainTexture = tex;
                quad.GetComponent<Renderer>().material = m;
            }
            fx.Apply(0f);
        }

        void Apply(float a)
        {
            // rotate the hinge over the spine — right page sweeps up and over
            // to the left (→), the mirror for ← — with an eased arc
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
