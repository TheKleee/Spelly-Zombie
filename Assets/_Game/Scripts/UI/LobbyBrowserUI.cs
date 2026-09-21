using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Two-tab lobby screen. JOIN = filter chips left, lobby list right.
    /// HOST = the same chips selecting your lobby's card, settings rows right.
    /// Embedded by the lobby stand and main menu.
    public static class LobbyBrowserUI
    {
        public static bool TabHost;
        public static string Search = "";
        public static string FilterRegion = ""; // "" = all
        public static string FilterLang = "";   // "" = all
        public static int FilterTags;           // selected tags must all be present

        /// The embedding panel rebuilds through this when a chip flips.
        public static System.Action Changed;

        public static int Stamp => SteamLobby.ListStamp * 100000 + LanLobby.Stamp;

        // the stand's look, shared with the map picker beside it
        public static readonly Color ChipOff = new Color(0.10f, 0.11f, 0.15f, 0.92f);
        public static readonly Color ChipOn = new Color(0.72f, 0.48f, 0.18f, 0.96f);
        public static readonly Color RowBack = new Color(0.07f, 0.08f, 0.11f, 0.90f);
        public static readonly Color HeadInk = new Color(1f, 0.92f, 0.75f);

        // ------------------------------------------------------- widgets --
        public static void Chip(RectTransform parent, float x, float y, float w, string label,
            bool on, System.Action click, bool clickable = true)
        {
            var b = UIKit.Button(parent, label, () => { click(); Changed?.Invoke(); },
                null, 12,
                !clickable ? new Color(0.5f, 0.5f, 0.55f)
                : on ? Color.white : new Color(0.85f, 0.85f, 0.88f));
            b.interactable = clickable;
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = null;
                img.color = !clickable ? new Color(0.07f, 0.08f, 0.10f, 0.55f)
                    : on ? ChipOn : ChipOff;
            }
            UIKit.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, 24f));
        }

        static void Head(RectTransform parent, float x, ref float y, string text)
        {
            var l = UIKit.Label(parent, text, 15, HeadInk, TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)l.transform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(200f, 20f));
            y -= 26f;
        }

        public static void ArrowRow(RectTransform parent, float x, float y, float w, string value,
            System.Action prev, System.Action next, bool canPrev = true, bool canNext = true)
        {
            Chip(parent, x, y, 26f, "<", false, prev, canPrev);
            var l = UIKit.Label(parent, value, 14, Color.white, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)l.transform, new Vector2(0f, 1f), new Vector2(x + 30f, y), new Vector2(w - 64f, 24f));
            Chip(parent, x + w - 26f, y, 26f, ">", false, next, canNext);
        }

        // ---------------------------------------------------- JOIN view --
        static bool Matches(SteamLobby.PublicLobby l) => Matches(l.Region, l.Lang, l.Tags, l.Name);
        static bool Matches(LanLobby.Found l) => Matches(l.Region, l.Lang, l.Tags, l.Name);

        static bool Matches(string region, string lang, int tags, string name)
        {
            if (!string.IsNullOrEmpty(FilterRegion) && region != FilterRegion) return false;
            if (!string.IsNullOrEmpty(FilterLang) && lang != FilterLang) return false;
            if ((tags & FilterTags) != FilterTags) return false;
            if (string.IsNullOrEmpty(Search)) return true;
            return (name ?? "").IndexOf(Search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void BuildJoinView(RectTransform parent, float left, float top,
            float width, int maxRows)
        {
            float lx = left, y = top;
            BuildFilterColumn(parent, lx, ref y, selecting: false);

            float rx = left + 190f, ry = top;
            var search = UIKit.Input(parent, Search, v => Search = v);
            UIKit.Place((RectTransform)search.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f - 96f, 26f));
            Chip(parent, left + width - 88f, ry, 88f, Loc.T("browse.refresh"), false, SteamLobby.RefreshList);
            ry -= 34f;

            int shown = 0;
            foreach (var l in SteamLobby.Lobbies)
            {
                if (shown >= maxRows) break;
                if (!Matches(l)) continue;

                var back = UIKit.Panel(parent, null, RowBack);
                UIKit.Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f, 40f));

                string line = $"{l.Name}   {l.Players}" + (l.Max < SteamLobby.MaxPlayers ? $"/{l.Max}" : "")
                    + (l.Ping >= 0 ? $"   {l.Ping}ms" : "")
                    + (l.Locked ? "   " + Loc.T("browse.locked") : "");
                var name = UIKit.Label(parent, line, 14, Color.white, TextAnchor.MiddleLeft, true);
                UIKit.Place((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(rx + 8f, ry - 2f), new Vector2(width - 300f, 18f));

                var sub = UIKit.Label(parent, TagLine(l), 11, new Color(0.75f, 0.82f, 0.75f), TextAnchor.MiddleLeft);
                UIKit.Place((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(rx + 8f, ry - 21f), new Vector2(width - 300f, 14f));

                var id = l.Id;
                bool locked = l.Locked;
                Chip(parent, left + width - 84f, ry - 6f, 76f, Loc.T("browse.join"), true, () =>
                {
                    if (locked) OpenPasswordPrompt(pw => SteamLobby.JoinListed(id, pw));
                    else SteamLobby.JoinListed(id, "");
                });

                ry -= 46f;
                shown++;
            }

            // lobbies on this network (no Steam), marked LH
            foreach (var l in LanLobby.Lobbies)
            {
                if (shown >= maxRows) break;
                if (!Matches(l)) continue;

                var back = UIKit.Panel(parent, null, RowBack);
                UIKit.Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f, 40f));
                var mark = UIKit.Panel(parent, null, ChipOn);
                UIKit.Place((RectTransform)mark.transform, new Vector2(0f, 1f), new Vector2(rx + 8f, ry - 9f), new Vector2(30f, 22f));
                var lh = UIKit.Label(parent, "LH", 12, Color.white, TextAnchor.MiddleCenter, true);
                UIKit.Place((RectTransform)lh.transform, new Vector2(0f, 1f), new Vector2(rx + 8f, ry - 9f), new Vector2(30f, 22f));

                string line = $"{l.Name}   {l.Players}" + (l.Max < SteamLobby.MaxPlayers ? $"/{l.Max}" : "")
                    + (l.Locked ? "   " + Loc.T("browse.locked") : "");
                var name = UIKit.Label(parent, line, 14, Color.white, TextAnchor.MiddleLeft, true);
                UIKit.Place((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(rx + 46f, ry - 2f), new Vector2(width - 338f, 18f));
                var sub = UIKit.Label(parent, TagLine(l.Region, l.Lang, l.Tags), 11, new Color(0.75f, 0.82f, 0.75f), TextAnchor.MiddleLeft);
                UIKit.Place((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(rx + 46f, ry - 21f), new Vector2(width - 338f, 14f));

                string address = l.Address, proto = l.Proto;
                int build = l.Build;
                bool locked = l.Locked;
                Chip(parent, left + width - 84f, ry - 6f, 76f, Loc.T("browse.join"), true, () =>
                {
                    if (!NetVersion.Admit(proto, build)) return;
                    if (locked) OpenPasswordPrompt(pw => NetGame.JoinLocal(address, pw));
                    else NetGame.JoinLocal(address, "");
                });

                ry -= 46f;
                shown++;
            }

            if (shown == 0)
            {
                var none = UIKit.Label(parent, Loc.T("browse.none"), 13,
                    new Color(0.85f, 0.85f, 0.88f), TextAnchor.MiddleCenter);
                UIKit.Place((RectTransform)none.transform, new Vector2(0f, 1f), new Vector2(rx, ry - 8f), new Vector2(width - 190f, 18f));
            }
        }

        // the password prompt: a small box over everything, join or cancel
        static RectTransform _pwPrompt;
        static string _pwTyped = "";

        static void OpenPasswordPrompt(System.Action<string> join)
        {
            ClosePasswordPrompt();
            _pwTyped = "";
            _pwPrompt = UIKit.Group(UIKit.Root, "PwPrompt");
            UIKit.Place(_pwPrompt, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(320f, 130f));
            var back = UIKit.Panel(_pwPrompt, null, new Color(0.05f, 0.06f, 0.09f, 0.96f));
            UIKit.Stretch((RectTransform)back.transform);

            var msg = UIKit.Label(_pwPrompt, Loc.T("browse.needpw"), 14, HeadInk, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)msg.transform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(300f, 20f));

            var field = UIKit.Input(_pwPrompt, "", v => _pwTyped = v);
            field.contentType = InputField.ContentType.Password;
            UIKit.Place((RectTransform)field.transform, new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(280f, 28f));

            Chip(_pwPrompt, 20f, -78f, 130f, Loc.T("browse.join"), true, () =>
            {
                join(_pwTyped);
                ClosePasswordPrompt();
            });
            Chip(_pwPrompt, 170f, -78f, 130f, Loc.T("browse.cancel"), false, ClosePasswordPrompt);
        }

        public static void ClosePasswordPrompt()
        {
            if (_pwPrompt != null) Object.Destroy(_pwPrompt.gameObject);
            _pwPrompt = null;
        }

        public static string TagLine(SteamLobby.PublicLobby l) => TagLine(l.Region, l.Lang, l.Tags);

        public static string TagLine(string region, string lang, int tags)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(region)) sb.Append(Loc.T("region." + region));
            if (!string.IsNullOrEmpty(lang))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Loc.NativeName(lang));
            }
            for (int i = 0; i < SteamLobby.TagKeys.Length; i++)
                if ((tags & (1 << i)) != 0)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(Loc.T(SteamLobby.TagKeys[i]));
                }
            return sb.ToString();
        }

        // ---------------------------------------------------- HOST view --
        public static void BuildHostView(RectTransform parent, float left, float top, float width)
        {
            float lx = left, y = top;
            BuildFilterColumn(parent, lx, ref y, selecting: true);

            float rx = left + 190f, ry = top;
            Head(parent, rx, ref ry, Loc.T("stand.settings"));

            var nameField = UIKit.Input(parent, string.IsNullOrEmpty(SteamLobby.PendingName)
                ? (SteamLobby.SteamReady ? Steamworks.SteamFriends.GetPersonaName() + "'s lobby" : "lobby")
                : SteamLobby.PendingName, v => SteamLobby.PendingName = v);
            UIKit.Place((RectTransform)nameField.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(240f, 26f));
            var nameLbl = UIKit.Label(parent, Loc.T("stand.name"), 12, new Color(0.85f, 0.85f, 0.88f), TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)nameLbl.transform, new Vector2(0f, 1f), new Vector2(rx + 248f, ry - 4f), new Vector2(width - 190f - 248f, 18f));
            ry -= 32f;

            ArrowRow(parent, rx, ry, width - 190f, SteamLobby.SizeLabel(SteamLobby.PendingSize),
                () => SteamLobby.PendingSize = SteamLobby.StepSize(SteamLobby.PendingSize, -1),
                () => SteamLobby.PendingSize = SteamLobby.StepSize(SteamLobby.PendingSize, 1),
                SteamLobby.PendingSize > 2, SteamLobby.PendingSize < SteamLobby.MaxPlayers);
            ry -= 30f;
            if (SteamLobby.PendingSize > SteamLobby.ComfortPlayers)
            {
                // past the comfortable crowd the host's upload is the match's ceiling
                var heavy = UIKit.Label(parent, Loc.T("stand.heavy"), 12, new Color(1f, 0.78f, 0.4f), TextAnchor.MiddleCenter);
                UIKit.Place((RectTransform)heavy.transform, new Vector2(0f, 1f), new Vector2(rx, ry + 4f), new Vector2(width - 190f, 16f));
                ry -= 18f;
            }

            ArrowRow(parent, rx, ry, width - 190f, Loc.F("stand.duration", MatchLobby.DurationLabel),
                () => MatchLobby.DurationMin = Mathf.Max(5, MatchLobby.DurationMin - 5),
                () => MatchLobby.DurationMin = Mathf.Min(15, MatchLobby.DurationMin + 5),
                MatchLobby.DurationMin > 5, MatchLobby.DurationMin < 15);
            ry -= 30f;

            // the map opens the picker: every map at once, the picked one described
            Chip(parent, rx, ry, width - 190f, Loc.F("net.map", MatchLobby.SelectedMap), MapPicker.IsOpen,
                () => MapPicker.Open(() => Changed?.Invoke()));
            ry -= 30f;

            ArrowRow(parent, rx, ry, width - 190f, Loc.F("stand.share", MatchLobby.AcolytePercent),
                () => MatchLobby.AcolytePercent = Mathf.Max(10, MatchLobby.AcolytePercent - 10),
                () => MatchLobby.AcolytePercent = Mathf.Min(90, MatchLobby.AcolytePercent + 10),
                MatchLobby.AcolytePercent > 10, MatchLobby.AcolytePercent < 90);
            ry -= 34f;

            var pw = UIKit.Input(parent, HostPw, v => HostPw = v);
            ((RectTransform)pw.transform).name = "PwField";
            pw.contentType = InputField.ContentType.Password;
            UIKit.Place((RectTransform)pw.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(110f, 26f));
            var pwLbl = UIKit.Label(parent, Loc.T("stand.pw"), 12, new Color(0.85f, 0.85f, 0.88f), TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)pwLbl.transform, new Vector2(0f, 1f), new Vector2(rx + 118f, ry - 4f), new Vector2(140f, 18f));
            ry -= 38f;

            var skin = UISkin.I;
            var create = UIKit.Button(parent, Loc.T("stand.hostpublic"),
                () => SteamLobby.HostPublic(HostPw),
                skin != null ? skin.ButtonBrown : null, 17);
            UIKit.Place((RectTransform)create.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f, 44f));
            ry -= 52f;
            var priv = UIKit.Button(parent, Loc.T("stand.hostprivate"), SteamLobby.HostFriends,
                skin != null ? skin.ButtonGrey : null, 13);
            UIKit.Place((RectTransform)priv.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f, 30f));
            ry -= 36f;
            // alone: the picked map as a practice run on the clock
            var solo = UIKit.Button(parent, Loc.T("stand.start"), MatchLobby.StartMatch,
                skin != null ? skin.ButtonGrey : null, 13);
            UIKit.Place((RectTransform)solo.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(width - 190f, 30f));
            ry -= 40f;
            MapPicture(parent, rx, ry, 300f);
        }

        public static string HostPw = "";
        static readonly System.Collections.Generic.HashSet<string> _noPicture =
            new System.Collections.Generic.HashSet<string>();

        /// The picture of the picked map under its MAP row (MapLibrary.PictureOf).
        /// No picture = one warning and no card.
        public static void MapPicture(RectTransform parent, float x, float y, float width)
        {
            string map = MatchLobby.SelectedMap;
            var tex = MapLibrary.PictureOf(map, MapDef.Active);
            if (tex == null)
            {
                if (_noPicture.Add(map))
                    Debug.LogWarning($"[SpellyZombie] No picture for the map '{map}'. Open it, frame the Scene view, " +
                        "run Spelly Zombie/Maps/Take Map Picture, then add it to the Lobby's Collection Manager with the same menu.");
                return;
            }
            var card = UIKit.PageCard(parent, "MapPicture", width, 16f / 9f, out var art, out var group);
            art.texture = tex;
            group.alpha = 1f;
            UIKit.Place(card, new Vector2(0f, 1f), new Vector2(x, y), card.sizeDelta);
        }


        // one column, two meanings: JOIN filters the list, HOST selects the card
        static void BuildFilterColumn(RectTransform parent, float x, ref float y, bool selecting)
        {
            Head(parent, x, ref y, Loc.T("stand.regions"));
            if (!selecting)
            {
                Chip(parent, x, y, 84f, Loc.T("filter.all"), FilterRegion == "",
                    () => FilterRegion = "");
            }
            int col = selecting ? 0 : 1;
            foreach (var r in SteamLobby.Regions)
            {
                float cx = x + (col % 2) * 90f;
                bool on = selecting ? SteamLobby.PendingRegion == r : FilterRegion == r;
                string code = r;
                Chip(parent, cx, y, 84f, Loc.T("region." + r), on, () =>
                {
                    if (selecting) SteamLobby.PendingRegion = code;
                    else FilterRegion = FilterRegion == code ? "" : code;
                });
                if (col % 2 == 1) y -= 28f;
                col++;
            }
            if (col % 2 == 1) y -= 28f;
            y -= 8f;

            Head(parent, x, ref y, Loc.T("stand.langs"));
            string myLang = string.IsNullOrEmpty(SteamLobby.PendingLang) ? Loc.LanguageCode : SteamLobby.PendingLang;
            if (!selecting)
            {
                Chip(parent, x, y, 84f, Loc.T("filter.all"), FilterLang == "", () => FilterLang = "");
                col = 1;
            }
            else col = 0;
            foreach (var lang in Loc.Languages)
            {
                float cx = x + (col % 2) * 90f;
                bool on = selecting ? myLang == lang.Code : FilterLang == lang.Code;
                string code = lang.Code;
                Chip(parent, cx, y, 84f, lang.Native, on, () =>
                {
                    if (selecting) SteamLobby.PendingLang = code;
                    else FilterLang = FilterLang == code ? "" : code;
                });
                if (col % 2 == 1) y -= 28f;
                col++;
            }
            if (col % 2 == 1) y -= 28f;
            y -= 8f;

            Head(parent, x, ref y, Loc.T("stand.behaviors"));
            for (int i = 0; i < SteamLobby.TagKeys.Length; i++)
            {
                int bit = 1 << i;
                float cx = x + (i % 2) * 90f;
                bool on = selecting ? (SteamLobby.PendingTags & bit) != 0 : (FilterTags & bit) != 0;
                Chip(parent, cx, y, 84f, Loc.T(SteamLobby.TagKeys[i]), on, () =>
                {
                    if (selecting) SteamLobby.PendingTags ^= bit;
                    else FilterTags ^= bit;
                });
                if (i % 2 == 1) y -= 28f;
            }
            if (SteamLobby.TagKeys.Length % 2 == 1) y -= 28f;
        }
    }
}
