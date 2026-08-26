using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Ores are destroyed into ink; the pool level is the meter; standing close refills the wand.
    /// (Class name kept: renaming touches saved scenes.)
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

            // the visible ink pool rises as ores are fed
            var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pool.name = "InkPool";
            // Conjure also runs from the editor build menu, where Destroy is illegal
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
            rb.isKinematic = true; // particles donate to rigidbodies - sparks can heat it

            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            var dmg = root.AddComponent<Element>();
            dmg.RemoveOnDeath = false;  // the pot survives the mayhem around it
            dmg.Health = 999999f;      // heat can't kill it

            var c = root.AddComponent<CaveCauldron>();
            c._thermal = root.AddComponent<Thermal>();
            c._thermal.HeatCapacity = 2.5f; // heat is flavour, not a gate
            c._fire = fire;
            c._pool = pool.transform;
            return c;
        }

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;

            // outside a run the well stays full
            if (!RoundDirector.RunActive) Fill = Capacity;

            // the fire under the pot is pure flavour
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
                    if (p.Ink >= DrawingConfig.InkMax - 0.5f) continue;
                    p.Award(DrawingConfig.CauldronInkPerSec * dt);
                    Fill = Mathf.Max(0f, Fill - dt * 0.14f); // an ore ≈ 7s of drinking
                }
        }

        /// A carried ore feeds the pot: the ore is spent, the pool gains one.
        public void FeedOre(InkRuneStone ore)
        {
            if (ore == null) return;
            Fill = Mathf.Min(Capacity, Fill + 1f);
            ore.Blacken();
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent("the cauldron DRINKS");
        }
    }

    /// A fuel stone: white and glowing until fed to the cauldron, then black. E takes, E drops.
    public class InkRuneStone : MonoBehaviour
    {
        public bool Spent { get; private set; }

        /// The ore the local player is holding (one pair of hands, one ore) -
        /// the cauldron feed will read this and call Blacken().
        public static InkRuneStone Carried { get; private set; }

        /// Live registry - one manager scan replaces N per-stone Update pollers (WeaponSlots.FindPickup pattern).
        public static readonly List<InkRuneStone> All = new List<InkRuneStone>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

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

        /// One scan for the whole pile (InkOreManager calls this once per frame).
        public static void Tick()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return;
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null) return;

            if (Carried != null) { Carried.TickCarried(UnityEngine.InputSystem.Keyboard.current, player); return; }

            // aim, not proximity: the most centred ore wins
            InkRuneStone best = null;
            float bestAim = 0f;
            foreach (var s in All)
            {
                if (s == null) continue;
                float aim = player.AimScore(s.transform.position, 2.2f, 0.9f, s.transform);
                if (aim > bestAim) { bestAim = aim; best = s; }
            }
            if (best == null) return;

            UIPrompt.Show("E", best.Spent ? "take the dead ore" : "take the ore",
                new Color(0.95f, 0.95f, 0.8f));
            if (!UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame || _actionFrame == Time.frameCount) return;
            _actionFrame = Time.frameCount;
            Carried = best;
            if (best._col != null) best._col.enabled = false;
            var rb = best.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        void TickCarried(UnityEngine.InputSystem.Keyboard kb, SimpleFPSController player)
        {
            // ride in front of the chest - visible in first and third person
            var t = player.transform;
            transform.position = t.position + t.forward * 0.65f + Vector3.up * 0.35f;

            // near any cauldron, E means feed, not drop
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

    /// Ticks the single ore scan (self-bootstrapped like RoundDirector/HUD).
    public class InkOreManager : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("InkOreManager");
            DontDestroyOnLoad(go);
            go.AddComponent<InkOreManager>();
        }

        void Update() => InkRuneStone.Tick();
    }
}
