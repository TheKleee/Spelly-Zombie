using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S SOCKET CATALOG: one asset per body family — SZ_WardrobePlayer,
    /// SZ_WardrobeZombie, SZ_WardrobeDemon (created via Spelly Zombie →
    /// Wardrobe → Create Wardrobe Catalogs). For every socket, an EXPANDABLE
    /// list of prefabs he builds in Blender and adds by hand; OPTION 0 IS THE
    /// DEFAULT LOOK. Pieces instantiate LOCKED to the socket at identity —
    /// model them in plain character space (+Z facing, +Y up, pivot at the
    /// attachment point). Renderers named "*_Team" take the team tint.
    public class SocketWardrobe : ScriptableObject
    {
        [System.Serializable]
        public class SocketSlot
        {
            [Tooltip("SocketSet name: Hat, Head, Cape, Chest, Belt, ShoulderL, ShoulderR, HandL, HandR, LegL, LegR")]
            public string Socket = "Hat";

            [Tooltip("The looks for this socket — index 0 is the default. Add as many as you like.")]
            public List<GameObject> Options = new List<GameObject>();
        }

        public List<SocketSlot> Slots = new List<SocketSlot>();

        public SocketSlot Slot(string socket) => Slots.Find(s => s != null && s.Socket == socket);
    }

    /// Runtime side: loads the three catalogs, remembers the LOCAL player's
    /// outfit choices (PlayerPrefs — the lobby outfit picker drives SetChoice
    /// later; the demo wears option 0 everywhere), and locks pieces onto
    /// sockets. Zombies/demons draw random options for variety.
    public static class SocketManager
    {
        static SocketWardrobe _player, _zombie, _demon;
        static bool _tried;

        public static SocketWardrobe Player { get { Load(); return _player; } }
        public static SocketWardrobe Zombie { get { Load(); return _zombie; } }
        public static SocketWardrobe Demon { get { Load(); return _demon; } }

        static void Load()
        {
            if (_tried) return;
            _tried = true;
            _player = Resources.Load<SocketWardrobe>("SZ_WardrobePlayer");
            _zombie = Resources.Load<SocketWardrobe>("SZ_WardrobeZombie");
            _demon = Resources.Load<SocketWardrobe>("SZ_WardrobeDemon");
        }

        // ---- the local player's outfit (the lobby picker will drive these) --

        public static int GetChoice(string socket)
            => PlayerPrefs.GetInt("sz_outfit_" + socket, 0);

        public static void SetChoice(string socket, int index)
            => PlayerPrefs.SetInt("sz_outfit_" + socket, index);

        // AXIOM (Marko Jul 25): a catalog slot HE filled used to be skipped in
        // total silence when the socket name didn't resolve (a typo, a trailing
        // space, or a rig whose bone SocketSet couldn't find). Say it once, and
        // list the sockets this body DOES have so the fix is obvious.
        static readonly HashSet<string> _warnedSockets = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWarnings() => _warnedSockets.Clear();

        static void WarnMissingSocket(SocketSet set, SocketWardrobe catalog, SocketWardrobe.SocketSlot slot)
        {
            string key = (catalog != null ? catalog.name : "?") + "|" + slot.Socket;
            if (!_warnedSockets.Add(key)) return;
            Debug.LogWarning($"[SpellyZombie] {(catalog != null ? catalog.name : "catalog")}: slot " +
                $"'{slot.Socket}' has {slot.Options.Count} option(s) but this body has NO such socket — " +
                $"nothing worn. Sockets on this body: {string.Join(", ", set.Names)}", catalog);
        }

        /// Locks the CHOSEN piece of every filled catalog slot onto the
        /// matching socket (chooser null = defaults). Returns the pieces so
        /// callers can retint / cloth them.
        public static List<GameObject> Dress(SocketSet set, SocketWardrobe catalog,
            System.Func<string, int> chooser = null)
        {
            var pieces = new List<GameObject>();
            if (set == null || catalog == null) return pieces;
            foreach (var slot in catalog.Slots)
            {
                if (slot?.Options == null || slot.Options.Count == 0) continue;
                var socket = set.Get(slot.Socket);
                if (socket == null) { WarnMissingSocket(set, catalog, slot); continue; }
                int idx = Mathf.Clamp(chooser != null ? chooser(slot.Socket) : 0,
                    0, slot.Options.Count - 1);
                Lock(slot.Options[idx], socket, pieces);
            }
            return pieces;
        }

        /// A RANDOM option per filled slot, each with `chance` to appear —
        /// zombie/demon variety without choices. A non-zero SEED makes the
        /// rolls DETERMINISTIC: host and clients feeding the same creature id
        /// dress it identically without sending a single byte about looks.
        public static List<GameObject> DressRandom(SocketSet set, SocketWardrobe catalog,
            float chance = 1f, int seed = 0)
        {
            var pieces = new List<GameObject>();
            if (set == null || catalog == null) return pieces;
            var rng = seed != 0 ? new System.Random(seed) : null;
            foreach (var slot in catalog.Slots)
            {
                if (slot?.Options == null || slot.Options.Count == 0) continue;
                float roll = rng != null ? (float)rng.NextDouble() : Random.value;
                if (roll > chance) continue;
                var socket = set.Get(slot.Socket);
                if (socket == null) { WarnMissingSocket(set, catalog, slot); continue; }
                if (socket.childCount > 0) continue; // a baked/worn piece already lives here
                int pick = rng != null ? rng.Next(slot.Options.Count)
                    : Random.Range(0, slot.Options.Count);
                Lock(slot.Options[pick], socket, pieces);
            }
            return pieces;
        }

        // ---- outfit wire format (multiplayer): indices by catalog slot order --

        /// The local player's choices as a compact code ("2,0,1") — slot order
        /// comes from the Player catalog asset, identical on every machine.
        public static string LocalOutfitCode()
        {
            var c = Player;
            if (c == null || c.Slots.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < c.Slots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GetChoice(c.Slots[i] != null ? c.Slots[i].Socket : ""));
            }
            return sb.ToString();
        }

        /// Turns a received outfit code back into a chooser for Dress().
        /// Null/garbage code = defaults, never an error.
        public static System.Func<string, int> ChooserFromCode(string code)
        {
            var c = Player;
            if (c == null || string.IsNullOrEmpty(code)) return null;
            var parts = code.Split(',');
            var map = new Dictionary<string, int>();
            for (int i = 0; i < c.Slots.Count && i < parts.Length; i++)
                if (c.Slots[i] != null && int.TryParse(parts[i], out int v))
                    map[c.Slots[i].Socket] = v;
            return socket => map.TryGetValue(socket, out int v) ? v : 0;
        }

        static void Lock(GameObject prefab, Transform socket, List<GameObject> pieces)
        {
            if (prefab == null) return;
            var piece = Object.Instantiate(prefab, socket, false); // identity = locked
            foreach (var t in piece.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = socket.gameObject.layer;
            pieces.Add(piece);
        }
    }
}
