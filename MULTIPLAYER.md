# Multiplayer setup — Marko's 20-minute checklist

Stack (locked in DEVPLAN): **FishNet** (netcode) + **FishySteamworks**
(Steam transport) + **Steamworks.NET**. These ship as `.unitypackage`
imports, NOT UPM git packages — do these steps in order, in the editor:

## 1. FishNet (free)
- Package Manager → My Assets → search **"FishNet: Networking Evolved"**
  (add it to your account from the Asset Store page first if needed) →
  Download → Import. Import everything.
- Sanity check: no console errors, `FishNet` folder appears in Assets.

## 2. Steamworks.NET
- https://github.com/rlabrecque/Steamworks.NET/releases → download the
  latest `Steamworks.NET_x.x.x.unitypackage` → Assets → Import Package →
  Custom Package.
- It auto-creates `steam_appid.txt` in the project root with **480**
  (Valve's test app — we use it until our own appid exists).

## 3. FishySteamworks
- https://github.com/FirstGearGames/FishySteamworks/releases → latest
  `.unitypackage` → import.

## 4. Scene wiring (Lobby scene)
- Empty GameObject "NetworkManager" → add **NetworkManager** (FishNet).
- Same object: add **Tugboat** (FishNet's UDP transport — for localhost
  testing) AND **FishySteamworks** component.
- Add **TransportManager** if not auto-added; set its Transport to
  Tugboat for now (we flip to FishySteamworks once Steam lobbies work).
- Steam must be RUNNING when you test with appid 480.

## 5. Tell Claude it's done
Next session I build, in order (DEVPLAN Phase B):
- B1 lobby: host/join via Steam friends + invites
- B2 player sync (transforms/health/ink)
- B3 stroke replication (surface-anchored node batches) + host-authoritative seals
- B4 host-run zombies/matter/spells replicated to clients
- B5 Steam-id ownership, per-player grimoire/wallet
- B6 join-in-progress + disconnects

## ⚠ Still blocking the store page (not the code)
- Steamworks registration ($100) → our own appid → real lobbies among
  friends without appid-480 weirdness, and the page itself.
