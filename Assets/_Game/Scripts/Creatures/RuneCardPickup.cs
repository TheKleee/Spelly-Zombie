using UnityEngine;

namespace SpellyZombie
{
    /// A SPELL PAGE dropped by a slain zombie (design rule 4) - walk over it
    /// to learn that rune family. the rule: dropped pages look EXACTLY
    /// like the pages in the grimoire - the page art if he's authored it
    /// (Resources/Custom/GrimoirePage_&lt;Family&gt;, or Custom/SpellPage as the
    /// generic sheet), parchment placeholder otherwise, with the family's two
    /// runes stamped on in the player's recorded handwriting. A soft gold
    /// halo keeps it readable across the arena.
    public class RuneCardPickup : MonoBehaviour
    {
        static readonly Color PageInk = new Color(0.15f, 0.1f, 0.2f);
        static readonly Color Parchment = new Color(0.93f, 0.88f, 0.74f, 0.96f);

        RuneCardType _card;
        Transform _player;
        float _bob;

        public static RuneCardPickup Spawn(Vector3 pos, RuneCardType card)
        {
            var go = new GameObject("RuneCard_" + card);
            go.transform.position = pos;

            // the whole-pickup override: Resources/Custom/SpellPage prefab
            // replaces the generated page entirely (spin/bob/collect stay)
            var skin = PrefabVault.Get("SpellPage");
            if (skin != null)
            {
                Object.Instantiate(skin, go.transform, false).name = "Page";
                var pSkinned = go.AddComponent<RuneCardPickup>();
                pSkinned._card = card;
                Destroy(go, 30f);
                return pSkinned;
            }

            var bg = Resources.Load<Texture2D>($"Custom/GrimoirePage_{card}");
            if (bg == null) bg = Resources.Load<Texture2D>("Custom/SpellPage");
            var shader = Shader.Find("Sprites/Default");
            GrimoirePages.Pair(card, out var up, out var down);

            void Quad(string quadName, Transform parent, Vector3 localPos, float yaw,
                Vector2 size, Texture2D tex, Color color, bool additive)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = quadName;
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(parent, false);
                q.transform.localPosition = localPos;
                q.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                q.transform.localScale = new Vector3(size.x, size.y, 1f);
                var rend = q.GetComponent<Renderer>();
                if (additive)
                    rend.sharedMaterial = MatterFX.Get(color, MoteShade.Additive);
                else if (shader != null)
                    rend.material = new Material(shader) { mainTexture = tex, color = tex != null ? Color.white : color };
            }

            for (int side = 0; side < 2; side++)
            {
                float yaw = side * 180f;
                float outZ = side == 0 ? -0.002f : 0.002f; // glyphs sit toward each viewer
                Quad("Halo", go.transform, Vector3.zero, yaw, new Vector2(0.52f, 0.66f),
                    null, new Color(1f, 0.85f, 0.3f, 0.5f), additive: true);
                Quad("Page", go.transform, new Vector3(0f, 0f, outZ * 0.5f), yaw,
                    new Vector2(0.38f, 0.5f), bg, Parchment, additive: false);
                var upTex = Wardrobe.RuneIcon(up, PageInk);
                var downTex = Wardrobe.RuneIcon(down, PageInk);
                if (upTex != null)
                    Quad("Rune", go.transform, new Vector3(0f, 0.1f, outZ), yaw,
                        new Vector2(0.17f, 0.17f), upTex, Color.white, additive: false);
                if (downTex != null)
                    Quad("Rune", go.transform, new Vector3(0f, -0.12f, outZ), yaw,
                        new Vector2(0.17f, 0.17f), downTex, Color.white, additive: false);
            }

            var p = go.AddComponent<RuneCardPickup>();
            p._card = card;
            Destroy(go, 30f);
            return p;
        }

        void Update()
        {
            _bob += Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, _bob * 120f, 0f);
            transform.position += Vector3.up * Mathf.Sin(_bob * 3f) * 0.002f;

            if (_player == null)
            {
                if (SimpleFPSController.All.Count > 0 && SimpleFPSController.All[0] != null)
                    _player = SimpleFPSController.All[0].transform;
                return;
            }
            if ((transform.position - _player.position).sqrMagnitude < 1.6f * 1.6f)
            {
                RuneLibrary.UnlockCard(_card);
                Debug.Log($"[SpellyZombie] Learned rune card {_card}: {RuneLibrary.CardDescription(_card)}");
                Destroy(gameObject);
            }
        }
    }
}
