using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishNet.Broadcast;
using Steamworks;
using UnityEngine;

namespace SpellyZombie
{
    /// Two builds can play together only when they speak the same messages.
    /// Proto is a fingerprint of every network message's layout, worked out by
    /// the game itself, so nobody has to remember to bump a number. A host
    /// publishes it with its lobby (Steam lobby data, the local network call);
    /// a joiner with another one stays out before it connects, and is told
    /// which side has to update. Steam never updates a running game and says
    /// nothing inside it, so without this a stale session walks into a newer
    /// lobby and breaks in ways nobody can read.
    public static class NetVersion
    {
        const int Salt = 2; // bump when the wire changes in a way the message layouts do not show (2: the mischief effect slots)
        const float NoticeSeconds = 10f;

        static string _proto;
        static int _build = -1;
        static float _until;
        static bool _outdated, _theirsOld;

        public static string Proto => _proto ?? (_proto = Fingerprint());

        /// Steam's number for the installed build; it grows with every upload.
        /// 0 = not from Steam (the editor, a loose exe): which side is older is then unknown.
        public static int Build
        {
            get
            {
                if (_build < 0)
                    _build = !Application.isEditor && SteamLobby.SteamReady ? SteamApps.GetAppBuildId() : 0;
                return _build;
            }
        }

        /// The line on top of the screen (NetGame draws it in the menu and the lobby), or nothing.
        /// A newer lobby was met = it stays for the session; the others pass after a few seconds.
        public static string Notice
        {
            get
            {
                if (Time.unscaledTime < _until) return _theirsOld ? Loc.T("ver.theirs") : Loc.T("ver.differs");
                return _outdated ? Loc.T("ver.mine") : "";
            }
        }

        /// True when a lobby that names this fingerprint can be joined. When not, the player is told why.
        public static bool Admit(string theirProto, int theirBuild)
        {
            if (theirProto == Proto) return true;
            bool known = Build > 0 && theirBuild > 0;
            if (known && theirBuild > Build)
            {
                _outdated = true;
                _until = 0f;
            }
            else
            {
                // a lobby from before this gate names no fingerprint: that host is the old one
                _theirsOld = string.IsNullOrEmpty(theirProto) || (known && theirBuild < Build);
                _until = Time.unscaledTime + NoticeSeconds;
            }
            Debug.Log($"[SpellyZombie] Version gate: this game speaks {Proto} (build {Build}), that lobby " +
                      $"{(string.IsNullOrEmpty(theirProto) ? "names nothing" : theirProto)} (build {theirBuild}). Not joined.");
            return false;
        }

        static string Fingerprint()
        {
            try
            {
                var messages = new List<Type>();
                foreach (var t in typeof(NetSync).Assembly.GetTypes())
                    if (t.IsValueType && typeof(IBroadcast).IsAssignableFrom(t)) messages.Add(t);
                messages.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

                var sb = new StringBuilder(8192);
                sb.Append(Salt).Append('|').Append(Application.version).Append('|');
                var seen = new HashSet<Type>();
                foreach (var t in messages) Describe(t, sb, seen);

                ulong h = 14695981039346656037UL; // FNV-1a
                for (int i = 0; i < sb.Length; i++) { h ^= sb[i]; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpellyZombie] Version gate: the message layouts could not be read ({e.Message}). This game will only meet others in the same state.");
                return "unread";
            }
        }

        /// A message's fields in the order they were written (the order they travel in),
        /// and the same for every type of ours that rides inside it.
        static void Describe(Type t, StringBuilder sb, HashSet<Type> seen)
        {
            if (!seen.Add(t)) return;
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            sb.Append(t.FullName).Append('{');
            foreach (var f in fields) sb.Append(NameOf(f.FieldType)).Append(' ').Append(f.Name).Append(';');
            sb.Append('}');
            foreach (var f in fields) DescribeInside(f.FieldType, t.Assembly, sb, seen);
        }

        /// A type's name without the library it came from: the editor and a build
        /// may name the same List or Dictionary through different libraries.
        static string NameOf(Type t)
        {
            if (t.IsArray) return NameOf(t.GetElementType()) + "[]";
            if (!t.IsGenericType) return t.FullName;
            var args = t.GetGenericArguments();
            var names = new string[args.Length];
            for (int i = 0; i < args.Length; i++) names[i] = NameOf(args[i]);
            return t.Namespace + "." + t.Name + "<" + string.Join(",", names) + ">";
        }

        static void DescribeInside(Type t, Assembly ours, StringBuilder sb, HashSet<Type> seen)
        {
            while (t.IsArray) t = t.GetElementType();
            if (t.IsGenericType)
                foreach (var arg in t.GetGenericArguments()) DescribeInside(arg, ours, sb, seen);
            if (t.Assembly == ours && !t.IsEnum && !t.IsPrimitive) Describe(t, sb, seen);
        }
    }
}
