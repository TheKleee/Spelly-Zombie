using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ LOBBIES ON THIS NETWORK, for when Steam is not there. A public lobby
    /// hosted on this PC (Tugboat) calls out on the local network every second;
    /// the Join list at the stand hears the calls and shows those lobbies
    /// marked LH. A lobby that stops calling drops off the list.
    public class LanLobby : MonoBehaviour
    {
        const int Port = 47779;          // the calls; the game itself rides Tugboat's port
        const string Magic = "SZLH1";
        const float CallEvery = 1f, ForgetAfter = 4f, StartGrace = 5f;

        public struct Found
        {
            public string Address;       // what to connect to
            public string Name, Region, Lang;
            public int Players, Max, Tags;
            public bool Locked, InGame;
            public string Proto;         // the messages that host speaks (NetVersion); empty from a build before the gate
            public int Build;
            public float Seen;
            public float UpStamp;        // unscaled time the lobby went up, as this machine counts; < 0 = unknown
        }
        static long _upUnix; // this PC's clock when its lobby went up

        /// Lobbies heard lately, oldest call first.
        public static readonly List<Found> Lobbies = new List<Found>();
        /// Bumps whenever the heard list changes: the stand redraws on it.
        public static int Stamp { get; private set; }
        /// A match is running in this PC's lobby (MatchLobby, through SteamLobby.SetInGame).
        public static bool InGame;

        static LanLobby _i;
        static bool _calling, _listen;
        static float _callingSince;
        UdpClient _sender, _listener;
        float _nextCall;
        bool _warned;
        readonly string _me = Guid.NewGuid().ToString("N");
        readonly Dictionary<string, (Found lobby, string packet)> _heard = new Dictionary<string, (Found, string)>();
        readonly List<string> _quiet = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~LanLobby");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<LanLobby>();
        }

        /// Whether this PC's local lobby calls out on the network: a public one does (NetGame.HostLocal).
        public static void Call(bool on)
        {
            _calling = on;
            _callingSince = Time.unscaledTime;
            if (on) _upUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// The stand's Join list is up: hear the calls.
        public static bool Listen
        {
            set
            {
                if (_listen == value) return;
                _listen = value;
                if (!value) _i?.StopListening();
            }
        }

        void OnDestroy()
        {
            StopListening();
            _sender?.Close();
            _sender = null;
        }

        void Update()
        {
            // a lobby that is no longer up stops calling
            if (_calling && !NetGame.Connected && !NetGame.ClientStarting && Time.unscaledTime - _callingSince > StartGrace)
                _calling = false;
            // an away host calls nothing: the listeners forget it in a few seconds
            if (_calling && NetGame.IsHost && !SteamLobby.HostAway && Time.unscaledTime >= _nextCall)
            {
                _nextCall = Time.unscaledTime + CallEvery;
                SendCall();
            }
            if (_listen) Hear();
        }

        // ---------------------------------------------------------- calling --
        void SendCall()
        {
            try
            {
                if (_sender == null) _sender = new UdpClient { EnableBroadcast = true };
                string name = string.IsNullOrEmpty(SteamLobby.PendingName) ? "lobby" : SteamLobby.PendingName;
                string packet = string.Join("|", Magic, _me, Esc(name),
                    (NetSync.RemoteCount + 1).ToString(), Mathf.Clamp(SteamLobby.PendingSize, 2, SteamLobby.MaxPlayers).ToString(),
                    string.IsNullOrEmpty(NetGame.HostPassword) ? "0" : "1", InGame ? "1" : "0",
                    Esc(SteamLobby.PendingRegion), Esc(string.IsNullOrEmpty(SteamLobby.PendingLang) ? Loc.LanguageCode : SteamLobby.PendingLang),
                    SteamLobby.PendingTags.ToString(),
                    NetVersion.Proto, NetVersion.Build.ToString(), // added at the end: an older listener reads the first ten
                    _upUnix.ToString()); // when it went up, by this PC's clock
                var bytes = Encoding.UTF8.GetBytes(packet);
                _sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, Port)); // a second window on this PC
                _sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port));
            }
            catch (Exception e) { WarnOnce("calling", e); }
        }

        static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        static string Unesc(string s) => Uri.UnescapeDataString(s ?? "");

        // ---------------------------------------------------------- hearing --
        void Hear()
        {
            try
            {
                if (_listener == null)
                {
                    var l = new UdpClient { ExclusiveAddressUse = false };
                    l.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    l.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                    _listener = l;
                }
                while (_listener.Available > 0)
                {
                    IPEndPoint from = null;
                    var data = _listener.Receive(ref from);
                    Take(Encoding.UTF8.GetString(data), from);
                }
            }
            catch (Exception e)
            {
                WarnOnce("listening", e);
                StopListening();
                _listen = false;
            }

            // lobbies that went quiet leave the list
            if (_heard.Count == 0) return;
            _quiet.Clear();
            foreach (var kv in _heard)
                if (Time.unscaledTime - kv.Value.lobby.Seen > ForgetAfter) _quiet.Add(kv.Key);
            foreach (var k in _quiet) _heard.Remove(k);
            if (_quiet.Count > 0) Publish();
        }

        void Take(string packet, IPEndPoint from)
        {
            var f = packet.Split('|');
            if (f.Length < 10 || f[0] != Magic || f[1] == _me) return;
            int.TryParse(f[3], out int players);
            int.TryParse(f[4], out int max);
            int.TryParse(f[9], out int tags);
            int build = 0;
            if (f.Length > 11) int.TryParse(f[11], out build);
            long upUnix = 0;
            if (f.Length > 12) long.TryParse(f[12], out upUnix);
            float upStamp = upUnix <= 0 ? -1f
                : Time.unscaledTime - Mathf.Max(0f, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - upUnix);
            string name = Unesc(f[2]);
            if (name.Length > 40) name = name.Substring(0, 40);
            var lobby = new Found
            {
                Address = from.Address.ToString(),
                Name = name, Players = players, Max = max,
                Locked = f[5] == "1", InGame = f[6] == "1",
                Region = Unesc(f[7]), Lang = Unesc(f[8]), Tags = tags,
                Proto = f.Length > 10 ? f[10] : "", Build = build,
                Seen = Time.unscaledTime,
                UpStamp = upStamp,
            };
            string body = string.Join("|", f, 2, f.Length - 2);
            bool changed = !_heard.TryGetValue(f[1], out var was) || was.packet != body;
            _heard[f[1]] = (lobby, body);
            if (changed) Publish();
        }

        void Publish()
        {
            Lobbies.Clear();
            foreach (var kv in _heard) Lobbies.Add(kv.Value.lobby);
            Stamp++;
        }

        void StopListening()
        {
            _listener?.Close();
            _listener = null;
            if (_heard.Count > 0) { _heard.Clear(); Publish(); }
        }

        void WarnOnce(string what, Exception e)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning($"[SpellyZombie] local network lobbies: {what} failed ({e.Message}).");
        }
    }
}
