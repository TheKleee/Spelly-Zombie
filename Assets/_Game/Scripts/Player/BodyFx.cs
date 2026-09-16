using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The status looks a body wears - flames, eye wisps and glares, blood
    /// drips, ice crystals - on its FX sockets. The pilot's BodyState feeds
    /// it (Tick); a friend's puppet reads the same numbers off its Element,
    /// the one the host's beat and StateMsg/HealthMsg write.
    public class BodyFx : MonoBehaviour
    {
        /// The board feeding this body; null = read the sibling Element.
        [System.NonSerialized] public BodyState Driver;
        /// The body's real eye height - the fallback anchor when a model has
        /// no FX sockets at all (the graybox bean).
        [System.NonSerialized] public float EyeHeight = 1.5f;

        Element _el;

        void Update()
        {
            if (Driver != null) return; // the board ticks it
            if (_el == null) _el = GetComponent<Element>();
            if (_el == null) return;
            float temp = _el.Data.Temp;
            float lum = BodyState.NaturalLum + (_el.Data.Lum - _el.Natural.Lum);
            float hurt = _el.MaxStrength > 0f ? 1f - Mathf.Clamp01(_el.Health / _el.MaxStrength) : 0f;
            Tick(BodyState.BurnOf(temp), BodyState.DarknessOf(lum), BodyState.BloomOf(lum),
                BodyState.FreezeOf(temp), hurt, !_el.DeadStill);
        }

        readonly GameObject[] _bodyFlames = new GameObject[3];
        readonly GameObject[] _eyeWisps = new GameObject[2];
        readonly GameObject[] _eyeGlares = new GameObject[2];
        float _iceFxTick, _bleedTick;


        // FX sockets: empties named Socket_Burn / Socket_Freeze / Socket_Bleed
        // / Socket_Eyes, several of each fine; effects spawn as children so
        // they ride the bones. No sockets = bone/eye-height fallbacks;
        // authored sockets always win.
        readonly List<Transform> _burnS = new List<Transform>();
        readonly List<Transform> _freezeS = new List<Transform>();
        readonly List<Transform> _bleedS = new List<Transform>();
        readonly List<Transform> _eyeS = new List<Transform>();
        float _socketScan;

        void ResolveSockets()
        {
            // bodies build/rebuild at runtime (CharacterRig, bakes) - rescan
            // when stale, at most every 2s
            bool stale = _burnS.Count == 0 || _burnS[0] == null;
            if (!stale || Time.time < _socketScan) return;
            _socketScan = Time.time + 2f;
            _burnS.Clear(); _freezeS.Clear(); _bleedS.Clear(); _eyeS.Clear();

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Socket_Burn")) _burnS.Add(t);
                else if (t.name.StartsWith("Socket_Freeze")) _freezeS.Add(t);
                else if (t.name.StartsWith("Socket_Bleed")) _bleedS.Add(t);
                else if (t.name.StartsWith("Socket_Eyes")) _eyeS.Add(t);
            }
            // fallbacks fill only the EMPTY categories, on the bones themselves:
            // spine, chest, head, shoulders, hips, legs - never the arms (their
            // bones point the wrong way). A small forward nudge keeps effects
            // on the skin, not inside the ribs.
            var anim = GetComponentInChildren<Animator>();
            Transform B(HumanBodyBones b) =>
                anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;
            Vector3 fwd = transform.forward;
            Vector3 root = transform.position;
            float e = EyeHeight;

            Transform Mk(string n, Transform bone, float beanY)
            {
                var go = new GameObject(n);
                go.transform.SetParent(bone != null ? bone : transform, false);
                // world position: at the bone (or a body-height fraction on
                // the bean), pushed slightly out the character's front
                go.transform.position = bone != null
                    ? bone.position + fwd * 0.07f
                    : root + Vector3.up * (beanY * e) + fwd * (0.09f * e);
                return go.transform;
            }

            var chest = B(HumanBodyBones.Chest);
            var spine = B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);
            var hips = B(HumanBodyBones.Hips);
            var shoulder = B(HumanBodyBones.RightShoulder);

            if (_burnS.Count == 0)
            {
                _burnS.Add(Mk("Socket_Burn_Auto", chest, 0.62f));
                _burnS.Add(Mk("Socket_Burn_Auto2", spine, 0.45f));
                // third flame high - shoulder first, head only as its stand-in
                _burnS.Add(Mk("Socket_Burn_Auto3", shoulder != null ? shoulder : head, 0.8f));
            }
            if (_freezeS.Count == 0)
            {
                _freezeS.Add(Mk("Socket_Freeze_Auto", chest, 0.55f));
                _freezeS.Add(Mk("Socket_Freeze_Auto2", hips, 0.35f));
            }
            if (_bleedS.Count == 0)
            {
                _bleedS.Add(Mk("Socket_Bleed_Auto", spine, 0.5f));
                _bleedS.Add(Mk("Socket_Bleed_Auto2", chest, 0.62f));
            }
            if (_eyeS.Count == 0)    // the one legitimate face socket
            {
                var go = new GameObject("Socket_Eyes_Auto");
                go.transform.SetParent(head != null ? head : transform, false);
                go.transform.localPosition = head != null
                    ? new Vector3(0f, 0.04f, 0.09f) : new Vector3(0f, e, 0.1f * e);
                _eyeS.Add(go.transform);
            }
        }

        /// CFXR prefabs ignore a small localScale unless their particle
        /// systems scale with the hierarchy - force it, then shrink for real.
        /// Sockets live on BONES, and bones carry rig scale (FBX armatures are
        /// often 0.01, baked parts can be anything) - cancel the parent's
        /// scale so the WORLD size is the size we asked for, on every rig.
        static GameObject Fit(GameObject fx, float scale)
        {
            if (fx == null) return null;
            var p = fx.transform.parent != null ? fx.transform.parent.lossyScale : Vector3.one;
            var s = fx.transform.localScale * scale;
            fx.transform.localScale = new Vector3(
                s.x / Mathf.Max(0.0001f, Mathf.Abs(p.x)),
                s.y / Mathf.Max(0.0001f, Mathf.Abs(p.y)),
                s.z / Mathf.Max(0.0001f, Mathf.Abs(p.z)));
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return fx;
        }

        /// One pass of the looks from the given severities (0..1) and hurt (0..1).
        /// Every machine draws them itself, so nothing here is relayed.
        public void Tick(float burn, float darkness, float bloom, float freeze, float hurt, bool alive)
        {
            var lib = FxLibrary.I;
            if (lib == null) return;

            ResolveSockets();

            // every status: socket-mounted, same size family, severity read as
            // count first, pace second, slight growth third

            // burning: 1..3 small licks, one per Socket_Burn
            int want = burn > 0.55f ? 3 : burn > 0.28f ? 2 : burn > 0.08f ? 1 : 0;
            for (int i = 0; i < _bodyFlames.Length; i++)
            {
                bool on = i < want && i < _burnS.Count && _burnS[i] != null;
                if (on && _bodyFlames[i] == null && lib.Fire != null)
                {
                    var fx = Fit(Instantiate(lib.Fire, _burnS[i]),
                        Mathf.Lerp(0.16f, 0.26f, burn));
                    fx.name = "BodyFlame";
                    fx.transform.localPosition = Vector3.zero; // the socket IS the spot
                    // bones carry arbitrary axes - start the effect world-upright
                    fx.transform.rotation = Quaternion.identity;
                    _bodyFlames[i] = fx;
                }
                else if (!on && _bodyFlames[i] != null)
                {
                    Destroy(_bodyFlames[i]);
                    _bodyFlames[i] = null;
                }
            }

            // darkness: 1 or 2 wisps at the eyes; glare mirrors it in white
            int wisps = darkness > 0.55f ? 2 : darkness > 0.18f ? 1 : 0;
            int glares = bloom > 0.55f ? 2 : bloom > 0.18f ? 1 : 0;
            EyeFx(_eyeWisps, wisps, lib.Smoke, 0.08f, "EyeDark");
            EyeFx(_eyeGlares, glares, lib.HealShine, 0.09f, "EyeGlare");

            // bleeding is the HP readout: drips come more and faster as HP falls
            if (hurt > 0.25f && alive)
            {
                _bleedTick -= Time.deltaTime;
                if (_bleedTick <= 0f)
                {
                    _bleedTick = Mathf.Lerp(2.2f, 0.6f, hurt);
                    int drips = hurt > 0.75f ? 3 : hurt > 0.5f ? 2 : 1;
                    for (int i = 0; i < drips && _bleedS.Count > 0; i++)
                    {
                        var s = _bleedS[Random.Range(0, _bleedS.Count)];
                        if (s == null) continue;
                        Fit(FxLibrary.Spawn(lib.Blood, s.position, s, 2.5f, false),
                            Mathf.Lerp(0.12f, 0.2f, hurt)); // small smears - count+pace tell the story
                    }
                }
            }

            // freezing: ice crystals FORM on you on a beat faster and
            // bigger the deeper you are
            if (freeze > 0.08f)
            {
                _iceFxTick -= Time.deltaTime;
                if (_iceFxTick <= 0f)
                {
                    _iceFxTick = Mathf.Lerp(1.4f, 0.45f, freeze);
                    if (_freezeS.Count > 0)
                    {
                        var s = _freezeS[Random.Range(0, _freezeS.Count)];
                        if (s != null)
                            Fit(FxLibrary.Spawn(lib.IceHit, s.position, s, 2f, false),
                                Mathf.Lerp(0.12f, 0.24f, freeze)); // crystals ON the skin
                    }
                }
            }
        }

        /// One eye status loop for wisps and glares.
        void EyeFx(GameObject[] cache, int want, GameObject prefab, float scale, string fxName)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                bool on = i < want && _eyeS.Count > 0 && _eyeS[0] != null;
                if (on && cache[i] == null && prefab != null)
                {
                    // two authored eye sockets = one per eye; one = split around it
                    var anchor = _eyeS[Mathf.Min(i, _eyeS.Count - 1)];
                    var fx = Fit(Instantiate(prefab, anchor), scale);
                    fx.name = fxName;
                    fx.transform.position = _eyeS.Count > 1 ? anchor.position
                        : anchor.position + transform.right * (i == 0 ? 0.05f : -0.05f);
                    fx.transform.rotation = Quaternion.identity; // head-bone axes lie
                    cache[i] = fx;
                }
                else if (!on && cache[i] != null)
                {
                    Destroy(cache[i]);
                    cache[i] = null;
                }
            }
        }
    }
}
