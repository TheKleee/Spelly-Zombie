using UnityEngine;

namespace SpellyZombie
{
    /// A friend's Steam name over their head. In the lobby everyone's shows; in a match only
    /// your own team's, so acolytes find each other under their disguises and the other side
    /// learns nothing. The same world text the zombies mumble with.
    public class NameTag : MonoBehaviour
    {
        const float AboveHead = 0.45f;   // metres over the head bone, or over the top of a worn object
        const float FullSizeTo = 8f;     // past this the letters grow with the distance, so far names stay readable
        const float MaxGrow = 4f;
        const int MaxLetters = 24;

        NetAvatar _avatar;
        TextMesh _text;
        float _lookIn;
        bool _shown;
        float _wornTop; // how far over the avatar's root the worn object reaches

        public static void Give(NetAvatar avatar)
        {
            if (avatar == null || avatar.GetComponent<NameTag>() != null) return;
            avatar.gameObject.AddComponent<NameTag>()._avatar = avatar;
        }

        void LateUpdate()
        {
            if (_avatar == null) return;
            if ((_lookIn -= Time.unscaledDeltaTime) <= 0f)
            {
                _lookIn = 0.5f;
                Look();
            }
            if (!_shown || _text == null) return;

            var head = _avatar.Head;
            Vector3 at = _avatar.Disguised ? transform.position + Vector3.up * (_wornTop + AboveHead)
                : head != null ? head.position + Vector3.up * AboveHead
                : transform.position + Vector3.up * 2.2f;
            _text.transform.position = at;
            ZombieBrain.FaceMumble(_text);

            var cam = Camera.main;
            float far = cam != null ? Vector3.Distance(cam.transform.position, at) : 0f;
            _text.transform.localScale = Vector3.one * Mathf.Clamp(far / FullSizeTo, 1f, MaxGrow);
        }

        // a body switched off takes its name with it; the next look brings it back
        void OnDisable()
        {
            if (_text != null) _text.gameObject.SetActive(false);
            _shown = false;
            _lookIn = 0f;
        }

        void OnDestroy()
        {
            if (_text != null) Destroy(_text.gameObject);
        }

        /// Who sees this name, and what it says: asked twice a second, never per frame.
        void Look()
        {
            bool show = NetSync.IdentityOf(_avatar.Id, out string name, out _) && !string.IsNullOrEmpty(name);
            if (show && ActiveScene.Name != "Lobby")
            {
                int owner = NetSync.OwnerIdOf(_avatar.Id);
                show = Sides.Known(owner) && Sides.Known(Sides.LocalPlayerId)
                    && !Teams.Enemies(Teams.OfOwner(Sides.LocalPlayerId), Teams.OfOwner(owner));
            }
            if (show && _text == null)
            {
                _text = ZombieBrain.BuildMumbleText(transform);
                _text.gameObject.name = "NameTag";
                // on its own in the scene: a disguise hides every renderer under the body, side paint tints them
                _text.transform.SetParent(null, true);
                _text.richText = false; // a name is letters, never markup
                _text.characterSize = 0.07f;
                _text.color = new Color(1f, 0.97f, 0.88f);
            }
            if (_text != null)
            {
                if (show)
                {
                    if (name.Length > MaxLetters) name = name.Substring(0, MaxLetters);
                    if (_text.text != name) _text.text = name;
                }
                if (_text.gameObject.activeSelf != show) _text.gameObject.SetActive(show);
            }
            _shown = show;

            var worn = show && _avatar.Disguised ? _avatar.Worn : null;
            if (worn != null)
                _wornTop = Mathf.Max(0.3f, ShapeShift.FindObjectBounds(worn).max.y - transform.position.y);
        }
    }
}
