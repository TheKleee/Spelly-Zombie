using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE CAULDRON — the heart of the locked loop (Marko Jul 23/25): the pot
    /// is your ink. Feed it white ores and they're DESTROYED into ink; the
    /// fill is a visible white pool, and anyone standing close refills their
    /// wand from it while it lasts. HEATING CUT (Jul 25): the pot always
    /// accepts ore — put ore in, get ink, one rule.
    ///
    /// (Extracted from the retired cave generator Jul 28 — the cave is gone,
    /// the economy is the game. Class name kept so every reference survives;
    /// rename to InkCauldron in a calm moment, it touches saved scenes.)
    public class CaveCauldron : MonoBehaviour
    {
        public static readonly List<CaveCauldron> All = new List<CaveCauldron>();

        public float Fill;                 // ores banked
        public const float Capacity = 12f;

        Thermal _thermal;
        Light _fire;
        Transform _pool;

        public bool Burning => _thermal != null && _thermal.Temperature > 60f;

        public static CaveCauldron Conjure(Vector3 floorAt)
        {
            var root = new GameObject("CaveCauldron");
            root.transform.position = floorAt;

            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            pot.transform.SetParent(root.transform, false);
            pot.transform.localPosition = Vector3.up * 0.45f;
            pot.transform.localScale = new Vector3(1.15f, 0.45f, 1.15f);
            pot.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.16f, 0.15f, 0.17f), MoteShade.Opaque);

            // the visible INK POOL — rises as ores are fed (no bars, no text)
            var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pool.name = "InkPool";
            // Conjure also runs from the EDITOR build menu, where Destroy is
            // illegal (logs an error, destroys nothing — the ghost collider
            // then blocked the pen above the pot)
            var poolCol = pool.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(poolCol);
            else Object.DestroyImmediate(poolCol);
            pool.transform.SetParent(root.transform, false);
            pool.transform.localPosition = Vector3.up * 0.62f;
            pool.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);
            pool.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.92f, 0.94f, 1f), MoteShade.Additive);

            var fireGo = new GameObject("FireGlow");
            fireGo.transform.SetParent(root.transform, false);
            fireGo.transform.localPosition = Vector3.up * 0.25f;
            var fire = fireGo.AddComponent<Light>();
            fire.type = LightType.Point;
            fire.range = 6f;
            fire.intensity = 0f;
            fire.color = new Color(1f, 0.55f, 0.2f);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; // particles DONATE to rigidbodies — sparks can heat it

            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            var dmg = root.AddComponent<Damageable>();
            dmg.Destructible = false;  // the pot survives the mayhem around it
            dmg.Health = 999999f;      // and heat can't "kill" it either — a hot
                                       // pot burning itself to death broke the
                                       // refill loop (the wand-test bug)

            var c = root.AddComponent<CaveCauldron>();
            c._thermal = root.AddComponent<Thermal>();
            c._thermal.HeatCapacity = 2.5f; // heat is flavour now, not a gate
            c._fire = fire;
            c._pool = pool.transform;
            return c;
        }

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;

            // THE LOBBY WELL NEVER RUNS DRY (Marko: "the central cauldron
            // should have liquid ink ore in it and be automatically refilled
            // in the lobby — there's ultimately infinite of it"). Everything
            // else about the Lobby plays by the real rules; only the SOURCE
            // is bottomless, so you can test without hunting for ore.
            if (!RoundDirector.RunActive) Fill = Capacity;

            // the fire under the pot is pure flavour since the heating cut
            if (_fire != null)
            {
                float target = Burning ? 1.6f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.9f : 0f;
                _fire.intensity = Mathf.MoveTowards(_fire.intensity, target, 4f * dt);
            }

            // the pool level IS the fill meter
            if (_pool != null)
            {
                float f = Mathf.Clamp01(Fill / Capacity);
                var s = _pool.localScale;
                s.y = Mathf.Lerp(0.005f, 0.09f, f);
                _pool.localScale = s;
                _pool.localPosition = new Vector3(0f, 0.62f + f * 0.12f, 0f);
                _pool.gameObject.SetActive(Fill > 0.02f);
            }

            // dispensing: stand close while it holds ink and your wand drinks
            if (Fill > 0.02f)
                foreach (var p in PlayerInk.All)
                {
                    if (p == null) continue;
                    if ((p.transform.position - transform.position).sqrMagnitude > 2.8f * 2.8f) continue;
                    if (p.Ink >= Perks.InkMax - 0.5f) continue;
                    p.Award(DrawingConfig.CauldronInkPerSec * dt);
                    Fill = Mathf.Max(0f, Fill - dt * 0.14f); // an ore ≈ 7s of drinking
                }
        }

        /// A carried ore meets the pot: the ore DIES, the ink LIVES.
        /// HEATING CUT (Marko Jul 25): the pot no longer has to be lit — that
        /// was a fourth job on an object that already had three, and nothing
        /// about it was clear from the get-go. ONE rule now: put ore in, get ink.
        public void FeedOre(InkRuneStone ore)
        {
            if (ore == null) return;
            Fill = Mathf.Min(Capacity, Fill + 1f);
            ore.Blacken();
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent("the cauldron DRINKS");
        }
    }

    /// A fuel stone: WHITE and glowing until it's fed to the cauldron, then
    /// BLACK and dark (Marko's economy). PICKABLE (Marko's ruling): E takes
    /// it, E drops it. A carried ore is a walking torch.
    public class InkRuneStone : MonoBehaviour
    {
        public bool Spent { get; private set; }

        /// The ore the local player is holding (one pair of hands, one ore) —
        /// the cauldron feed will read this and call Blacken().
        public static InkRuneStone Carried { get; private set; }

        Light _glow;
        Collider _col;
        static int _actionFrame; // one E-action per frame (no drop-and-regrab)

        void Start()
        {
            _col = GetComponentInChildren<Collider>();
            _glow = GetComponentInChildren<Light>();
            if (_glow == null)
            {
                var go = new GameObject("StoneGlow");
                go.transform.SetParent(transform, false);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = 8.5f;
                _glow.intensity = 2f;
                _glow.color = new Color(0.95f, 0.95f, 0.88f);
            }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null) return;

            if (Carried == this)
            {
                // ride in front of the chest — visible in first and third person
                var t = player.transform;
                transform.position = t.position + t.forward * 0.65f + Vector3.up * 0.35f;

                // near ANY cauldron, E means FEED, not drop (heating cut Jul 25 —
                // the pot always accepts ore; put ore in, get ink)
                CaveCauldron pot = null;
                foreach (var c in CaveCauldron.All)
                    if (c != null
                        && (c.transform.position - t.position).sqrMagnitude < 2.6f * 2.6f)
                    { pot = c; break; }

                if (pot != null && !Spent)
                {
                    UIPrompt.Show("E", "feed the ore to the cauldron", new Color(1f, 0.85f, 0.4f));
                    if (kb.eKey.wasPressedThisFrame && _actionFrame != Time.frameCount)
                    {
                        _actionFrame = Time.frameCount;
                        pot.FeedOre(this);
                        Drop(t.forward * 0.3f); // the blackened husk tumbles by the pot
                    }
                    return;
                }

                if (kb.eKey.wasPressedThisFrame && _actionFrame != Time.frameCount)
                {
                    _actionFrame = Time.frameCount;
                    Drop(t.forward);
                }
                return;
            }

            // AIM, NOT PROXIMITY (Marko's rule): you take the ore you're
            // LOOKING at. Standing in a pile no longer grabs a random one —
            // every stone scores how centred it is and the winner is resolved
            // in LateUpdate, after all of them have had their say.
            if (Carried == null)
            {
                float aim = player.AimScore(transform.position, 2.2f, 0.9f, transform);
                if (aim > 0f)
                {
                    if (_bidFrame != Time.frameCount) { _bidFrame = Time.frameCount; _bidScore = 0f; _bidWinner = null; }
                    if (aim > _bidScore) { _bidScore = aim; _bidWinner = this; }
                }
            }
        }

        static int _bidFrame;
        static float _bidScore;
        static InkRuneStone _bidWinner;

        void LateUpdate()
        {
            if (_bidWinner != this || _bidFrame != Time.frameCount) return;
            UIPrompt.Show("E", Spent ? "take the dead ore" : "take the ore",
                new Color(0.95f, 0.95f, 0.8f));

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;
            if (_actionFrame == Time.frameCount) return;
            _actionFrame = Time.frameCount;
            Carried = this;
            if (_col != null) _col.enabled = false;
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        void Drop(Vector3 forward)
        {
            Carried = null;
            if (_col != null) _col.enabled = true;
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 2f;
            rb.linearVelocity = forward * 2.2f + Vector3.up * 1.2f; // a gentle toss

            // TOUCH = WORLD: a handled ore is an object now (and can never
            // have been spell-born — ores only spawn from the map)
        }

        void OnDestroy()
        {
            if (Carried == this) Carried = null;
        }

        /// The cauldron drank it: the stone goes black, its light goes out.
        public void Blacken()
        {
            if (Spent) return;
            Spent = true;
            if (_glow != null) _glow.enabled = false;
            var r = GetComponentInChildren<Renderer>();
            if (r != null) r.sharedMaterial = MatterFX.Get(new Color(0.05f, 0.05f, 0.06f), MoteShade.Opaque);
        }
    }
}
