using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The open grimoire teaches. PAGE ONE is the seal lesson — how casting
    /// works (seal drawn AROUND a rune; more detailed boundary = spell lasts
    /// longer). Then ONE PAGE PER FAMILY, always all six (Marko's rule: the
    /// book never hides chapters) — the family you own in working ink, the
    /// rest faded like unlearned chapters. Left page = the up rune, right
    /// page = the down rune, in your recorded handwriting. Comma and period
    /// flip back / forward. Rebuilds itself when you learn a card.
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
    /// His flip is a simple CLOSE→OPEN: one Animator trigger named "Flip"
    /// covers both directions (a "FlipBack" trigger is used for , when it
    /// exists). An Animator BOOL named "Open" mirrors the G state when it
    /// exists, so his book can play open/close clips. While the book closes
    /// the old page is already gone; the new page appears when an ANIMATION
    /// EVENT calls OnPageReveal() — put it on the frame the book lies open
    /// again (the Animator must live on the same object as this component:
    /// the book root). No event within 0.7s = the page shows anyway, so a
    /// missing event never breaks the book.
    public class GrimoirePages : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.15f, 0.1f, 0.2f);        // owned: dark ink
        static readonly Color Locked = new Color(0.5f, 0.47f, 0.55f, 0.55f); // unowned: faded
        static readonly Color SealInk = new Color(0.55f, 0.12f, 0.16f);  // seals sign in red

        /// True while the local player's grimoire is raised open — HandIK
        /// holds the book up to read; closed, the book hand hangs free.
        public static bool BookOpen { get; private set; }

        static bool _taughtOpen; // the G hint retires after the first open
        bool _open;
        int _page;
        int _cardsShown = int.MinValue;
        readonly List<GameObject> _content = new List<GameObject>();
        Transform _anchor;
        Animator _flip;

        // the placeholder book's page surface floats 0.028 above its root;
        // Marko's PageAnchor sits ON the paper, so content barely lifts
        float Lift => _anchor == transform ? 0.028f : 0.001f;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "PageAnchor") { _anchor = t; break; }
            if (_anchor == null) _anchor = transform;
            _flip = GetComponentInChildren<Animator>();
        }

        void Update()
        {
            if (PoseStudio.IsOpen || GameMenu.IsOpen || UIKit.Typing) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // G raises/closes the book; while closed the pen hand is free and
            // the pages don't respond. The bottom prompt teaches the key.
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
                    _revealed = false;
                }
            }
            if (!_open)
            {
                // taught once, then the prompt stays out of E-pickup's way
                if (!_taughtOpen) UIPrompt.Show("G", Loc.T("grimoire.open"));
                return;
            }
            UIPrompt.Show("G", Loc.T("grimoire.close"));

            int pages = 1 + Families.Length; // seal lesson + one page per family

            int step = 0;
            if (kb.periodKey.wasPressedThisFrame) step = 1;
            if (kb.commaKey.wasPressedThisFrame) step = -1;
            int target = Mathf.Clamp(_page + step, 0, pages - 1); // ends STOP, no wrap
            if (step != 0 && target != _page)
            {
                _page = target;
                if (PlayFlip(step))
                {
                    // his close→open plays: old page vanishes now, new page
                    // waits for the OnPageReveal animation event (or timeout)
                    ClearContent();
                    _pendingFlip = true;
                    _pendingTimer = 0.7f;
                }
                else
                {
                    Rebuild(OwnedMask()); // placeholder book: instant
                }
                Juice.Chime(transform.position); // the page-turn flourish
                DrawingWorld.Instance?.LogEvent($"Grimoire page {_page + 1}/{pages}");
            }

            if (_pendingFlip)
            {
                _pendingTimer -= Time.deltaTime;
                if (_revealed || _pendingTimer <= 0f)
                {
                    _pendingFlip = false;
                    _revealed = false;
                    Rebuild(OwnedMask());
                }
                return; // the stamp check below would double-rebuild mid-flip
            }

            int stamp = OwnedMask();
            if (stamp != _cardsShown)
            {
                _cardsShown = stamp;
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
            _revealed = false;
            ClearContent();
        }

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
        bool _revealed;

        /// Called by an ANIMATION EVENT in Marko's book clip, on the frame the
        /// book lies open again — the new page's content appears right then.
        public void OnPageReveal() => _revealed = true;

        /// Fires the book skin's page-turn animation if Marko authored one.
        /// True = an animation is playing and the content swap should wait.
        bool PlayFlip(int dir)
        {
            if (_flip == null) return false;
            string trigger = dir > 0 ? "Flip" : "FlipBack";
            bool hasBack = false, hasFlip = false;
            foreach (var p in _flip.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger)
                {
                    if (p.name == "Flip") hasFlip = true;
                    if (p.name == "FlipBack") hasBack = true;
                }
            if (trigger == "FlipBack" && !hasBack) trigger = "Flip"; // close→open covers both ways
            if (trigger == "Flip" && !hasFlip) return false;
            _flip.SetTrigger(trigger);
            return true;
        }

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

            if (_page == 0)
            {
                BuildSealLesson(mask);
                return;
            }

            // ONE FAMILY PER SPREAD: left page the up rune, right the down —
            // never two chapters crammed onto one spread (Marko: "it combined
            // the page for light with the page for solid")
            var family = Families[_page - 1];
            bool owned = (mask & (1 << (int)family)) != 0;
            Pair(family, out var up, out var down);

            if (CustomPage($"GrimoirePage_{family}", owned)) return;

            Label(family.ToString().ToUpper(), new Vector3(0f, 0.001f, 0.094f),
                0.003f, owned ? Ink : Locked);
            Glyph(up, new Vector3(-0.042f, 0f, 0.022f), owned);
            Glyph(down, new Vector3(0.042f, 0f, 0.022f), owned);
        }

        /// Marko's hand-designed page art. "<name>_Full" = his complete
        /// spread, nothing drawn over it (faded when unowned); "<name>" =
        /// his background, glyphs still stamp on top. True = page handled.
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
            Label(",  and  .  turn the pages", new Vector3(0f, 0.001f, -0.096f), 0.0019f, Locked);
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

        // --------------------------------------------------- rune spreads --

        void Glyph(RuneType rune, Vector3 pagePos, bool owned)
        {
            var tex = Wardrobe.RuneIcon(rune, owned ? Ink : Locked);
            if (tex != null) Quad(tex, pagePos, 0.062f);
            // the rune's NAME under the glyph — the book teaches, it doesn't quiz
            Label(RuneLibrary.ShortName(rune), pagePos + new Vector3(0f, 0.001f, -0.036f),
                0.0022f, owned ? Ink : Locked);
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
}
