using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Unlock toast: the earned spread pops in on the left, holds and fades;
    /// new pages push older ones upward, dimming. Cards never touch - the
    /// stack step is the card's own height plus RuneToastGap. Anything
    /// granting a rune calls Show. A "RuneToasts" surface prefab replaces it.
    public class RuneToast : MonoBehaviour
    {
        static readonly Color Paper = new Color(0.96f, 0.92f, 0.80f);

        static RuneToast _me;

        class Card
        {
            public RectTransform Rt;
            public CanvasGroup Cg;
            public float Age;
            public int Slot;
            public float Height;   // this card's own height, so the stack can't clip
        }

        readonly List<Card> _cards = new List<Card>();
        RectTransform _root;

        /// The page for a rune - acolyte art when an acolyte is learning it.
        public static void Show(RuneType rune)
        {
            if (rune == RuneType.None) return;
            bool acolyte = Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte;
            var art = GrimoirePages.PageArt(rune, acolyte);
            if (art == null) art = Wardrobe.RuneIcon(rune, new Color(0.15f, 0.1f, 0.2f));
            Show(art);
        }

        /// Any page-shaped art. Acolyte deeds land here.
        public static void Show(Texture2D art)
        {
            if (art == null) return;
            if (_me == null)
            {
                var go = new GameObject("RuneToasts");
                DontDestroyOnLoad(go);
                _me = go.AddComponent<RuneToast>();
            }
            _me.Push(art);
        }

        void Push(Texture2D art)
        {
            if (_root == null)
            {
                _root = UIKit.Group(UIKit.Root, "RuneToasts");
                // a bare Group is a 100x100 box at screen centre - stretch it
                // or "anchored to the left edge" means the left edge of THAT
                if (_root != null) UIKit.Stretch(_root);
            }
            if (_root == null) return;

            foreach (var c in _cards) c.Slot++; // everyone already here climbs

            // the page art IS a full two-page spread, so the card takes the
            // art's own aspect - landscape, never squashed into a portrait
            float w = DrawingConfig.RuneToastWidth;
            float aspect = art.height > 0 ? (float)art.width / art.height : 2f;
            float h = w / Mathf.Max(0.2f, aspect);

            var go = new GameObject("Page", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(DrawingConfig.RuneToastMargin, 0f);

            // paper card behind the transparent page art
            var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image));
            paper.transform.SetParent(rt, false);
            var prt = (RectTransform)paper.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = prt.offsetMax = Vector2.zero;
            var img = paper.GetComponent<Image>();
            var skin = UISkin.I;
            img.sprite = skin != null ? skin.PanelBrown : null;
            img.type = img.sprite != null && img.sprite.border != Vector4.zero
                ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Paper;
            img.raycastTarget = false;

            var page = new GameObject("Art", typeof(RectTransform), typeof(RawImage));
            page.transform.SetParent(rt, false);
            var art_rt = (RectTransform)page.transform;
            art_rt.anchorMin = Vector2.zero;
            art_rt.anchorMax = Vector2.one;
            art_rt.offsetMin = new Vector2(9f, 9f);
            art_rt.offsetMax = new Vector2(-9f, -9f);
            var raw = page.GetComponent<RawImage>();
            raw.texture = art;
            raw.raycastTarget = false;

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            _cards.Add(new Card { Rt = rt, Cg = cg, Height = h });
            Juice.Chime(Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }

        void Update()
        {
            if (_cards.Count == 0) return;
            float dt = Time.unscaledDeltaTime; // a pause must not freeze the tell
            float gap = DrawingConfig.RuneToastGap;
            float life = DrawingConfig.RuneToastSeconds;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (c.Rt == null) { _cards.RemoveAt(i); continue; }
                c.Age += dt;

                // climb by the full height of every card below, so two cards
                // can never overlap whatever their art's aspect is
                float targetY = 0f;
                for (int j = 0; j < _cards.Count; j++)
                    if (_cards[j].Slot < c.Slot) targetY += _cards[j].Height + gap;

                Vector2 p = c.Rt.anchoredPosition;
                p.y = Mathf.Lerp(p.y, targetY, 1f - Mathf.Exp(-9f * dt));
                c.Rt.anchoredPosition = p;

                // POP: the button click spring, but grown from small so it
                // reads as ARRIVING - a button is already on screen when it
                // wiggles, a card is not
                float pop = ButtonJuice.SpringLife * DrawingConfig.RuneToastPopScale;
                if (c.Age < pop)
                {
                    float k = c.Age / pop;
                    float grow = Mathf.Lerp(0.3f, 1f, 1f - (1f - k) * (1f - k));
                    c.Rt.localScale = Vector3.one * (grow * ButtonJuice.SpringScale(k));
                    c.Rt.localRotation = Quaternion.Euler(0f, 0f, ButtonJuice.SpringRollDeg(k));
                }
                else
                {
                    c.Rt.localScale = Vector3.one;
                    c.Rt.localRotation = Quaternion.identity;
                }

                float a = Mathf.Clamp01(c.Age / 0.05f);          // in fast, so the pop is seen
                float left = life - c.Age;
                if (left < 0.7f) a *= Mathf.Clamp01(left / 0.7f); // out
                a *= Mathf.Clamp01(1f - c.Slot * 0.24f);          // older = dimmer
                c.Cg.alpha = a;

                if (c.Age >= life)
                {
                    Destroy(c.Rt.gameObject);
                    _cards.RemoveAt(i);
                }
            }
        }
    }
}
