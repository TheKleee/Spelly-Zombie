using UnityEngine;

namespace SpellyZombie
{
    /// A rune card dropped by a slain zombie (design rule 4) — walk over it to
    /// learn that rune family in your grimoire. Bright spinning card so it reads
    /// across the arena.
    public class RuneCardPickup : MonoBehaviour
    {
        RuneCardType _card;
        Transform _player;
        float _bob;

        public static RuneCardPickup Spawn(Vector3 pos, RuneCardType card)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "RuneCard_" + card;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.35f, 0.5f, 0.05f); // card shaped
            go.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(1f, 0.9f, 0.3f, 0.95f), MoteShade.Additive);

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
                Debug.Log($"[SpellyZombie] Learned rune card: {_card} — {RuneLibrary.CardDescription(_card)}");
                Destroy(gameObject);
            }
        }
    }
}
