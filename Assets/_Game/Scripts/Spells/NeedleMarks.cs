using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ HIS CALL: while a Death or Life Needle of yours exists (asleep on its seal, held or flying),
    /// every wizard near you it could take its revenge on wears its mark over his head: the Death
    /// Needle's glyph on the left, the Life Needle's on the right, both when both apply. The marks
    /// ask the needles' own rules (MischiefLaw.DeathTarget / LifeTarget). Only you see them, and a
    /// wall hides them like anything else in the world.
    public class NeedleMarks : MonoBehaviour
    {
        const float Range = 20f;    // a wizard farther from your eye than this wears no mark
        const float Above = 0.75f;  // metres over the head bone, clear of a tall hat
        const float Apart = 0.2f;   // each mark this far off the middle when both show
        const float Size = 0.3f;    // metres across

        static readonly Color DeathInk = new Color(0.8f, 0.1f, 0.1f);
        static Sprite _death, _life;

        class Pair { public Transform Root, Head; public SpriteRenderer Death, Life; }
        readonly Dictionary<int, Pair> _on = new Dictionary<int, Pair>();
        readonly List<int> _drop = new List<int>();
        SimpleFPSController _pilot;
        float _lookIn;

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        void OnDisable() => Clear();

        void LateUpdate()
        {
            if (_pilot == null || !_pilot.IsLocalViewer) { Clear(); return; }
            if ((_lookIn -= Time.unscaledDeltaTime) <= 0f) { _lookIn = 0.25f; Look(); }
            Place();
        }

        /// Which wizards wear which mark, asked four times a second.
        void Look()
        {
            int me = Grimoire.LocalPlayerId;
            MischiefLaw.NeedlesOf(me, out bool death, out bool life);
            _drop.Clear();
            foreach (var kv in _on) _drop.Add(kv.Key);
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : transform.position;
            if (death || life)
                foreach (var av in NetAvatar.All)
                {
                    if (av == null || av.Downed) continue;
                    int owner = NetSync.OwnerIdOf(av.Id);
                    // wizards; and the acolyte whose curse you are under, the Life Needle's way back
                    bool curser = Grimoires.CurserOf(me) == owner;
                    if (Sides.Of(owner) != Side.Wizard && !curser) continue;
                    if ((av.transform.position - eye).sqrMagnitude > Range * Range) continue;
                    bool d = death && !curser && MischiefLaw.DeathTarget(owner, me);
                    bool l = life && MischiefLaw.LifeTarget(owner, me);
                    if (!d && !l) continue;
                    var pair = Get(av);
                    if (pair == null) continue;
                    pair.Death.enabled = d;
                    pair.Life.enabled = l;
                    _drop.Remove(av.Id);
                }
            foreach (int id in _drop) Remove(id);
        }

        /// Over the head, facing the eye, side by side when both show.
        void Place()
        {
            if (_on.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 side = cam.transform.right;
            foreach (var kv in _on)
            {
                var p = kv.Value;
                if (p.Root == null) continue;
                Vector3 top = (p.Head != null ? p.Head.position : p.Root.position + Vector3.up * 1.7f) + Vector3.up * Above;
                var face = Quaternion.LookRotation(top - cam.transform.position);
                bool both = p.Death.enabled && p.Life.enabled;
                p.Death.transform.SetPositionAndRotation(both ? top - side * Apart : top, face);
                p.Life.transform.SetPositionAndRotation(both ? top + side * Apart : top, face);
            }
        }

        Pair Get(NetAvatar av)
        {
            if (_on.TryGetValue(av.Id, out var p) && p.Death != null && p.Life != null)
            {
                p.Root = av.transform;
                p.Head = av.Head;
                return p;
            }
            if (p != null) Remove(av.Id);
            if (_death == null) _death = Glyph(MischiefLaw.RuneOf(MischiefKind.DeathNeedle), DeathInk);
            if (_life == null) _life = Glyph(MischiefLaw.RuneOf(MischiefKind.LifeNeedle), DrawingConfig.CorruptInkColor);
            if (_death == null || _life == null) return null;
            p = new Pair { Root = av.transform, Head = av.Head, Death = Mark("DeathNeedleMark", _death), Life = Mark("LifeNeedleMark", _life) };
            _on[av.Id] = p;
            return p;
        }

        /// The needle's own glyph, as the book draws it, Size metres across.
        static Sprite Glyph(RuneType rune, Color ink)
        {
            var tex = Wardrobe.RuneIcon(rune, ink);
            if (tex == null)
            {
                Debug.LogWarning($"[SpellyZombie] NeedleMarks: no glyph for {rune}, its mark stays unseen.");
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / Size);
        }

        static SpriteRenderer Mark(string name, Sprite glyph)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = glyph;
            sr.enabled = false;
            return sr;
        }

        void Remove(int id)
        {
            if (!_on.TryGetValue(id, out var p)) return;
            if (p.Death != null) Destroy(p.Death.gameObject);
            if (p.Life != null) Destroy(p.Life.gameObject);
            _on.Remove(id);
        }

        void Clear()
        {
            _drop.Clear();
            foreach (var kv in _on) _drop.Add(kv.Key);
            foreach (int id in _drop) Remove(id);
        }
    }
}
