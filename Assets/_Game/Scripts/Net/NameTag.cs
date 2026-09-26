using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpellyZombie
{
    /// A friend's Steam name over their head. In the lobby everyone's shows; in a match only
    /// your own team's, so acolytes find each other under their disguises and the other side
    /// learns nothing. One size on screen at any distance, gone past ShowTo, and always flat
    /// to the screen: each camera turns the names to itself right before it draws.
    public class NameTag : MonoBehaviour
    {
        const float AboveHead = 0.45f; // metres over the head bone, or over the top of a worn object
        const float SizeAt = 8f;       // every name keeps the size on screen it has at 8 m: close up it never grows
        const float ShowTo = 25f;      // past this a name is gone, so it never dwarfs a far player
        const float FadeOver = 3f;     // it fades out over the last metres instead of popping
        const int MaxLetters = 24;
        static readonly Color Ink = new Color(1f, 0.97f, 0.88f);

        static readonly List<NameTag> _live = new List<NameTag>();

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        void OnEnable() => _live.Add(this);

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
            _text.transform.position = _avatar.Disguised ? transform.position + Vector3.up * (_wornTop + AboveHead)
                : head != null ? head.position + Vector3.up * AboveHead
                : transform.position + Vector3.up * 2.2f;
        }

        /// Right before a camera draws: every name turned flat to its screen, sized by its
        /// distance to it, faded out at the edge of ShowTo.
        static void BeforeCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game) return;
            var view = cam.transform;
            foreach (var tag in _live)
            {
                if (tag == null || !tag._shown || tag._text == null) continue;
                var t = tag._text.transform;
                float far = Vector3.Distance(view.position, t.position);
                t.rotation = view.rotation;
                t.localScale = Vector3.one * Mathf.Max(0.05f, far / SizeAt);
                float alpha = 1f - Mathf.Clamp01((far - (ShowTo - FadeOver)) / FadeOver);
                if (!Mathf.Approximately(tag._text.color.a, alpha))
                    tag._text.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
            }
        }

        // a body switched off takes its name with it; the next look brings it back
        void OnDisable()
        {
            _live.Remove(this);
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
                _text.color = Ink;
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
