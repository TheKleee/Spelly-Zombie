using UnityEngine;

namespace SpellyZombie
{
    /// The test-match bot: dumb on purpose. It wanders, faces you, and drops
    /// a clumsy curse on your position now and then.
    public class BotPlayer : MonoBehaviour
    {
        public const int OwnerId = 777;

        static bool _queued;

        /// The stand's "test match vs a bot" queues one for the next map.
        public static void QueueForNextMatch() => _queued = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, _) =>
            {
                if (!_queued || scene.name == "Lobby" || scene.name == "Menu") return;
                _queued = false;
                Spawn();
            };
        }

        static void Spawn()
        {
            // a bot stands in for a player, so it wears the player's body
            var model = CollectionManager.PlayerBody;
            if (model == null) return;   // the slot's own error already said so

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            Vector3 at = player != null
                ? player.transform.position + player.transform.forward * 10f
                : Vector3.zero;
            if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out var hit, 60f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                at = hit.point;

            var go = new GameObject("Bot");
            go.transform.position = at;
            var visual = Object.Instantiate(model, go.transform, false);
            visual.name = "Body";
            go.AddComponent<BotPlayer>();

            Sides.Set(OwnerId, Sides.LocalIsAcolyte ? Side.Wizard : Side.Acolyte);
            DrawingWorld.Instance?.LogEvent("a bot shuffles in. it is not very good");
        }

        Vector3 _home, _target;
        float _repick, _cast;

        void Start()
        {
            _home = transform.position;
            _target = transform.position;
        }

        void Update()
        {
            // wander: new spot every few seconds, or when a wall says no
            _repick -= Time.deltaTime;
            Vector3 flat = _target - transform.position; flat.y = 0f;
            if (_repick <= 0f || flat.magnitude < 0.4f
                || Physics.Raycast(transform.position + Vector3.up, transform.forward, 0.9f))
            {
                _repick = Random.Range(3f, 6f);
                Vector2 c = Random.insideUnitCircle * 12f;
                _target = _home + new Vector3(c.x, 0f, c.y);
            }

            Vector3 dir = _target - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.1f)
            {
                dir.Normalize();
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), Time.deltaTime * 3f);
                transform.position += dir * (2.1f * Time.deltaTime);
            }

            // stick to the ground
            if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down,
                    out var ground, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                transform.position = new Vector3(transform.position.x, ground.point.y, transform.position.z);

            // the clumsy cast: a slow curse on your position, easy to walk out of
            _cast -= Time.deltaTime;
            if (_cast <= 0f)
            {
                _cast = Random.Range(9f, 14f);
                var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
                if (player != null
                    && Vector3.Distance(player.transform.position, transform.position) < 20f)
                {
                    var m = Matter.Spawn(SurfaceMaterialType.Stone,
                        Random.value < 0.5f ? MatterPhase.Solid : MatterPhase.Liquid,
                        0.5f, player.transform.position + Vector3.up * 4f);
                    if (m != null) { m.StampOwner(OwnerId); m.SpellBorn = true; } // its spell, its golems
                    WorldEvents.Report(WorldEventKind.Spell, player.transform.position, 2f);
                }
            }
        }
    }
}
