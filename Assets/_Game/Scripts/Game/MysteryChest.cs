using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// THE MYSTERY CHEST (CoD mystery box, witch edition): spawns at one random
    /// MysteryChestSpawnPoint per scene. Pay riches, press E, the lid creaks
    /// open with a light show, and out comes:
    ///
    ///   60%  a rune card — weighted toward families you DON'T own yet
    ///   20%  a weapon pickup
    ///   10%  full ink for the whole team
    ///   10%  THE BEAR — the chest slams shut and MOVES to another spawn
    ///        point (half refund). Classic.
    ///
    /// Local-sim like Perks/Powerups until B4 host authority (accepted
    /// divergence; the roll only touches local buffs and world pickups).
    public class MysteryChest : MonoBehaviour
    {
        const float Reach = 2.4f;

        static MysteryChest _instance;

        Transform _lid;
        Light _glow;
        bool _rolling;
        float _lidOpen; // 0 shut, 1 open

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded += (_, __) => SpawnAtRandomPoint();
            SpawnAtRandomPoint();
        }

        static void SpawnAtRandomPoint()
        {
            if (_instance != null) return;
            var points = Object.FindObjectsByType<MysteryChestSpawnPoint>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (points.Length == 0) return;
            Build(points[Random.Range(0, points.Length)].transform.position);
        }

        /// Marko's prefab first (Resources/Custom/Chest) — a child named
        /// "Lid" (pivot on the hinge edge, authored CLOSED at identity
        /// rotation) swings open. Primitives fill in until his exists.
        static void Build(Vector3 at)
        {
            GameObject root;
            Transform lidT = null;
            var customChest = PrefabVault.Get("Chest");
            if (customChest != null)
            {
                root = Object.Instantiate(customChest, at, Quaternion.identity);
                root.name = "SZ_MysteryChest";
                // AXIOM (Marko Jul 25): exact "Lid" only meant any other name
                // → the chest never opens, silently. Exact first, then a TOKEN
                // match (a raw Contains("lid") would grab "Collider"/"Solid").
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Lid") { lidT = t; break; }
                if (lidT == null)
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (var tok in t.name.Split('_', '.', '-', ' '))
                            if (tok.Equals("lid", System.StringComparison.OrdinalIgnoreCase)
                             || tok.Equals("cover", System.StringComparison.OrdinalIgnoreCase))
                            { lidT = t; break; }
                        if (lidT != null) break;
                    }
                if (lidT == null)
                    Debug.LogWarning($"[SpellyZombie] Chest prefab '{customChest.name}' has no child named " +
                        "\"Lid\". It spawns, glows and pays out fine, it just won't SWING OPEN. Name the " +
                        "hinge child Lid, pivot it on the hinge edge, and author it CLOSED.", root);

                // a Blender export without colliders would be a ghost — the
                // pen, spells and zombies would pass straight through it
                if (root.GetComponentInChildren<Collider>() == null)
                {
                    var rends = root.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        var box = root.AddComponent<BoxCollider>();
                        box.center = root.transform.InverseTransformPoint(b.center);
                        box.size = b.size;
                    }
                }
                if (root.GetComponentInChildren<SurfaceMaterialTag>() == null)
                    root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            }
            else
            {
                root = new GameObject("SZ_MysteryChest");
                root.transform.position = at;

                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                body.transform.localScale = new Vector3(1.1f, 0.7f, 0.7f);
                body.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.4f, 0.26f, 0.14f), MoteShade.Opaque);
                body.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;

                var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lid.name = "Lid";
                lid.transform.SetParent(root.transform, false);
                lid.transform.localPosition = new Vector3(0f, 0.74f, -0.35f); // pivot at the hinge edge
                lid.transform.localScale = new Vector3(1.14f, 0.1f, 0.74f);
                lid.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.5f, 0.34f, 0.18f), MoteShade.Opaque);
                lidT = lid.transform;
            }

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.55f, 0.9f, 1f);
            glow.intensity = 0.7f;
            glow.range = 4f;

            var chest = root.AddComponent<MysteryChest>();
            chest._lid = lidT;
            chest._glow = glow;
            _instance = chest;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            // lid ease + idle glow breathing
            if (_lid != null)
                _lid.localRotation = Quaternion.Slerp(_lid.localRotation,
                    Quaternion.Euler(_lidOpen * -95f, 0f, 0f), Time.deltaTime * 6f);
            if (_glow != null && !_rolling)
                _glow.intensity = 0.7f + Mathf.Sin(Time.time * 1.7f) * 0.25f;

            if (_rolling || PoseStudio.IsOpen || GameMenu.IsOpen) return;
            if (SimpleFPSController.ThirdPersonActive || SelfPaint.IsActive) return;

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null || player.IsDowned) return;
            if ((player.transform.position - transform.position).sqrMagnitude > Reach * Reach) return;
            UIPrompt.Show("E", Loc.T("chest.try"), new Color(0.6f, 0.95f, 1f));

            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

            StartCoroutine(Roll()); // no currency gate — riches are gone
        }

        IEnumerator Roll()
        {
            _rolling = true;
            _lidOpen = 1f;
            Juice.Crackle(transform.position);

            // the light show — hope rises with the pitch of the flicker
            float t = 0f;
            while (t < 2.1f)
            {
                t += Time.deltaTime;
                if (_glow != null)
                {
                    _glow.intensity = 1.2f + Mathf.PingPong(t * (2f + t * 3f), 2.2f);
                    _glow.color = Color.Lerp(new Color(0.55f, 0.9f, 1f),
                        new Color(1f, 0.85f, 0.3f), t / 2.1f);
                }
                yield return null;
            }

            float roll = Random.value;
            if (roll < 0.60f) GiveCard();
            else if (roll < 0.80f) GiveWeapon();
            else if (roll < 0.90f) GiveInk();
            else yield return TheBear();

            yield return new WaitForSeconds(1.4f);
            _lidOpen = 0f;
            if (_glow != null) _glow.color = new Color(0.55f, 0.9f, 1f);
            _rolling = false;
        }

        Vector3 PrizeSpot => transform.position + Vector3.up * 1.3f;

        void GiveCard()
        {
            // weight toward families the buyer hasn't learned
            var all = (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));
            var unowned = new System.Collections.Generic.List<RuneCardType>();
            foreach (var c in all)
                if (!Grimoire.Has(Grimoire.LocalPlayerId, c)) unowned.Add(c);

            RuneCardType prize = unowned.Count > 0 && Random.value < 0.8f
                ? unowned[Random.Range(0, unowned.Count)]
                : all[Random.Range(0, all.Length)];
            RuneCardPickup.Spawn(PrizeSpot, prize);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"The chest offers: the {prize} card");
        }

        void GiveWeapon()
        {
            Vector3 at = transform.position + transform.forward * 1.2f;
            bool chamber = Random.value < 0.5f;
            if (chamber) RuneChamberWeapon.CreatePickup(at);
            else SealWeapon.CreatePickup(at);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent(chamber
                ? "The chest offers: a RUNE CHAMBER (engrave the strip, cycle the shots)"
                : "The chest offers: a slide tablet");
        }

        void GiveInk()
        {
            PlayerInk.RefillAll();
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent("The chest overflows: ink refilled for everyone");
        }

        IEnumerator TheBear()
        {
            Juice.Sting(transform.position);
            DrawingWorld.Instance?.LogEvent("...the chest GROWLS and leaves.");

            var points = Object.FindObjectsByType<MysteryChestSpawnPoint>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var away = new System.Collections.Generic.List<Vector3>();
            foreach (var p in points)
                if ((p.transform.position - transform.position).sqrMagnitude > 1f)
                    away.Add(p.transform.position);

            _lidOpen = 0f;
            yield return new WaitForSeconds(0.8f);
            if (away.Count > 0)
                transform.position = away[Random.Range(0, away.Count)];
            // only one spawn point in this scene? it sulks in place instead
        }

    }
}
