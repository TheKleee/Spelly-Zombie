using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Lobby only: aim at another player and a floating I appears. Press it
    /// for a small card with their name and a Steam add-friend button (the
    /// Steam overlay handles the actual request). Commends join this card
    /// when the commend system lands.
    public static class LobbyInspect
    {
        public static bool PanelOpen { get; private set; }

        static NetAvatar _aimed;
        static RectTransform _card;
        static int _cardFor = -1;

        public static void Tick()
        {
            if (ActiveScene.Name != "Lobby" || GameMenu.IsOpen || UIKit.Typing
                || HatPillar.PanelOpen || LobbyStand.PanelOpen || PoseStudio.IsOpen)
            {
                if (PanelOpen) Close();
                _aimed = null;
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            if (PanelOpen)
            {
                if (kb.iKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) Close();
                return;
            }

            _aimed = Aimed();
            if (_aimed == null) return;

            AimBadge.OfferAt(_aimed.transform.position + Vector3.up * 2f, "I");
            if (kb.iKey.wasPressedThisFrame) Open(_aimed);
        }

        /// The most centred remote player within reach, no colliders needed.
        static NetAvatar Aimed()
        {
            var cam = Camera.main;
            if (cam == null) return null;

            NetAvatar best = null;
            float bestDot = 0.985f;
            foreach (var t in Object.FindObjectsByType<NetAvatar>(FindObjectsSortMode.None))
            {
                Vector3 to = t.transform.position + Vector3.up * 1.2f - cam.transform.position;
                float dist = to.magnitude;
                if (dist > 6f || dist < 0.5f) continue;
                float dot = Vector3.Dot(cam.transform.forward, to / dist);
                if (dot > bestDot) { bestDot = dot; best = t; }
            }
            return best;
        }

        static void Open(NetAvatar who)
        {
            PanelOpen = true;
            _cardFor = who.Id;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildCard(who);
        }

        static void Close()
        {
            PanelOpen = false;
            _cardFor = -1;
            if (_card != null) Object.Destroy(_card.gameObject);
            _card = null;
            if (!GameMenu.IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // floating elements only, no container box
        static void BuildCard(NetAvatar who)
        {
            UIKit.Retire(_card);
            _card = UIKit.Group(UIKit.Root, "InspectCard");
            UIKit.Place(_card, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(340f, 200f));

            bool known = NetSync.IdentityOf(who.Id, out string name, out ulong steamId);
            var nameLabel = UIKit.Label(_card, known ? name : "...", 26, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)nameLabel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(330f, 34f));

            if (known && steamId != 0UL && SteamLobby.SteamReady)
            {
                var add = UIKit.Button(_card, Loc.T("inspect.add"), () =>
                {
                    Steamworks.SteamFriends.ActivateGameOverlayToUser(
                        "friendadd", new Steamworks.CSteamID(steamId));
                    Close();
                }, UISkin.I != null ? UISkin.I.ButtonBrown : null, 16);
                UIKit.Place((RectTransform)add.transform, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(240f, 42f));
            }

            // the host disciplines from here too: exact target, no stand walk
            if (NetGame.IsHost)
            {
                int cid = who.Id;
                var kick = UIKit.Button(_card, Loc.T("stand.kick"), () => { NetSync.Kick(cid); Close(); },
                    UISkin.I != null ? UISkin.I.ButtonGrey : null, 14);
                UIKit.Place((RectTransform)kick.transform, new Vector2(0.5f, 1f), new Vector2(-62f, -116f), new Vector2(112f, 34f));
                var ban = UIKit.Button(_card, Loc.T("stand.ban"), () =>
                {
                    BanList.Ban(steamId, name);
                    NetSync.Kick(cid);
                    Close();
                }, UISkin.I != null ? UISkin.I.ButtonRed : null, 14);
                UIKit.Place((RectTransform)ban.transform, new Vector2(0.5f, 1f), new Vector2(62f, -116f), new Vector2(112f, 34f));
            }

            var hint = UIKit.Label(_card, Loc.T("inspect.close"), 13,
                new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(330f, 20f));
        }
    }
}
