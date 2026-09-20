using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace SpellyZombie
{
    /// ★ SHARED MAPS on the Steam Workshop. An item is a folder holding the
    /// map's JSON and its picture. Browse a page, fetch one to preview it,
    /// keep it (it joins your maps and the lobby list), share your own: a new
    /// item the first time, an update after.
    public class MapWorkshop : MonoBehaviour
    {
        public const string MapFile = "map.json", PictureFile = "preview.png";

        public class Item
        {
            public ulong Id;
            public string Title = "", Description = "";
            public ulong Owner;
            public uint Updated;
            public string PreviewUrl = "";
            public Texture2D Picture;
            public MapDef Map;        // read once fetched
            public string Folder;     // where Steam installed it
            public bool Fetching;
        }

        /// The last page of shared maps.
        public static readonly List<Item> Items = new List<Item>();
        /// Bumps whenever Items, pictures or the status change: screens rebuild on it.
        public static int Stamp { get; private set; }
        public static string Status { get; private set; } = "";

        static MapWorkshop _i;
        CallResult<SteamUGCQueryCompleted_t> _query;
        CallResult<CreateItemResult_t> _create;
        CallResult<SubmitItemUpdateResult_t> _submit;
        Callback<DownloadItemResult_t> _downloaded;
        readonly Dictionary<ulong, List<Action<MapDef>>> _waiting = new Dictionary<ulong, List<Action<MapDef>>>();
        (MapDef map, string folder, Action<bool, string> done) _sharing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~MapWorkshop");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<MapWorkshop>();
        }

        static void Say(string status)
        {
            Status = status;
            Stamp++;
        }

        /// Steam is up and the calls are wired.
        static bool Hook()
        {
            if (_i == null || !SteamLobby.SteamReady) return false;
            if (_i._query == null)
            {
                _i._query = CallResult<SteamUGCQueryCompleted_t>.Create(_i.OnQuery);
                _i._create = CallResult<CreateItemResult_t>.Create(_i.OnCreated);
                _i._submit = CallResult<SubmitItemUpdateResult_t>.Create(_i.OnSubmitted);
                _i._downloaded = Callback<DownloadItemResult_t>.Create(_i.OnDownloaded);
            }
            return true;
        }

        // ------------------------------------------------------------ browse --
        /// Asks Steam for a page of shared maps, newest first.
        public static void Refresh(uint page = 1)
        {
            if (!Hook()) { Items.Clear(); Say(Loc.T("maps.nosteam")); return; }
            var app = SteamUtils.GetAppID();
            var q = SteamUGC.CreateQueryAllUGCRequest(EUGCQuery.k_EUGCQuery_RankedByPublicationDate,
                EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, app, app, page);
            SteamUGC.SetReturnLongDescription(q, true);
            _i._query.Set(SteamUGC.SendQueryUGCRequest(q));
            Say(Loc.T("maps.loading"));
        }

        void OnQuery(SteamUGCQueryCompleted_t r, bool ioFailure)
        {
            foreach (var it in Items) if (it.Picture != null) Destroy(it.Picture);
            Items.Clear();
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Say(Loc.F("maps.failed", ioFailure ? "IO" : r.m_eResult.ToString()));
                if (!ioFailure) SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
                return;
            }
            for (uint i = 0; i < r.m_unNumResultsReturned; i++)
            {
                if (!SteamUGC.GetQueryUGCResult(r.m_handle, i, out var d)) continue;
                SteamUGC.GetQueryUGCPreviewURL(r.m_handle, i, out string url, 1024);
                SteamFriends.RequestUserInformation(new CSteamID(d.m_ulSteamIDOwner), true); // the author's name arrives later
                Items.Add(new Item
                {
                    Id = d.m_nPublishedFileId.m_PublishedFileId, Title = d.m_rgchTitle ?? "",
                    Description = d.m_rgchDescription ?? "", Owner = d.m_ulSteamIDOwner,
                    Updated = d.m_rtimeUpdated, PreviewUrl = url ?? "",
                });
            }
            SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
            Say(Items.Count == 0 ? Loc.T("maps.none") : "");
            foreach (var it in Items)
                if (!string.IsNullOrEmpty(it.PreviewUrl)) StartCoroutine(LoadPicture(it));
        }

        IEnumerator LoadPicture(Item it)
        {
            using (var req = UnityWebRequestTexture.GetTexture(it.PreviewUrl))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                it.Picture = DownloadHandlerTexture.GetContent(req);
                Stamp++;
            }
        }

        /// The author's Steam name, once Steam knows it.
        public static string AuthorOf(Item it) =>
            SteamLobby.SteamReady ? SteamFriends.GetFriendPersonaName(new CSteamID(it.Owner)) : "";

        // ------------------------------------------------------------- fetch --
        /// Downloads the item and reads its map; `done` hears the map, null = it failed.
        public static void Fetch(Item it, Action<MapDef> done)
        {
            if (it == null) { done?.Invoke(null); return; }
            if (it.Map != null) { done?.Invoke(it.Map); return; }
            if (!Hook()) { done?.Invoke(null); return; }
            if (TryRead(it)) { done?.Invoke(it.Map); return; }
            if (!_i._waiting.TryGetValue(it.Id, out var list)) _i._waiting[it.Id] = list = new List<Action<MapDef>>();
            if (done != null) list.Add(done);
            it.Fetching = true;
            if (!SteamUGC.DownloadItem(new PublishedFileId_t(it.Id), true))
            {
                it.Fetching = false;
                _i.Finish(it.Id, null);
                Say(Loc.F("maps.failed", "download"));
                return;
            }
            Say(Loc.T("maps.downloading"));
        }

        static bool TryRead(Item it)
        {
            var id = new PublishedFileId_t(it.Id);
            if ((SteamUGC.GetItemState(id) & (uint)EItemState.k_EItemStateInstalled) == 0) return false;
            if (!SteamUGC.GetItemInstallInfo(id, out _, out string folder, 1024, out _)) return false;
            string path = Path.Combine(folder, MapFile);
            if (!File.Exists(path)) return false;
            it.Map = MapDef.FromJson(File.ReadAllText(path));
            if (it.Map == null) return false;
            it.Map.WorkshopId = it.Id;
            it.Map.PagesDir = Path.Combine(folder, MapPages.SubFolder);
            it.Folder = folder;
            return true;
        }

        void OnDownloaded(DownloadItemResult_t r)
        {
            if (r.m_unAppID != SteamUtils.GetAppID()) return;
            ulong id = r.m_nPublishedFileId.m_PublishedFileId;
            var it = Items.Find(x => x.Id == id);
            if (it == null) return;
            it.Fetching = false;
            bool ok = r.m_eResult == EResult.k_EResultOK && TryRead(it);
            Say(ok ? "" : Loc.F("maps.failed", r.m_eResult.ToString()));
            Finish(id, ok ? it.Map : null);
        }

        void Finish(ulong id, MapDef map)
        {
            if (!_waiting.TryGetValue(id, out var list)) return;
            _waiting.Remove(id);
            foreach (var a in list) a?.Invoke(map);
        }

        /// ★ Keeps a fetched map: it joins your maps and the lobby list, and
        /// Steam keeps it up to date.
        public static bool Keep(Item it)
        {
            if (it?.Map == null) return false;
            byte[] png = null;
            string pic = it.Folder != null ? Path.Combine(it.Folder, PictureFile) : null;
            if (pic != null && File.Exists(pic)) png = File.ReadAllBytes(pic);
            if (!MapLibrary.Save(it.Map, png)) return false; // a map with no biome is not kept
            if (it.Folder != null) MapPages.CopyFolder(Path.Combine(it.Folder, MapPages.SubFolder), MapLibrary.PagesFolder(it.Map.Name));
            if (Hook()) SteamUGC.SubscribeItem(new PublishedFileId_t(it.Id));
            Stamp++;
            return true;
        }

        // ------------------------------------------------------------- share --
        /// ★ Shares a saved map: a new Workshop item the first time, an update
        /// after. `done(ok, message)` hears the outcome.
        public static void Share(MapDef map, Action<bool, string> done)
        {
            if (map == null) return;
            if (!MapLibrary.CanSave(map)) { done?.Invoke(false, Loc.T("mc.needbiome")); return; }
            if (!Hook()) { done?.Invoke(false, Loc.T("maps.nosteam")); return; }
            string folder = Path.Combine(MapLibrary.Folder, "Share", map.Name.GetHashCode().ToString("x8"));
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, MapFile), MapDef.ToJson(map));
                string pic = MapLibrary.PictureFor(map.Name);
                if (File.Exists(pic)) File.Copy(pic, Path.Combine(folder, PictureFile), true);
                MapPages.CopyFolder(MapLibrary.PagesFolder(map.Name), Path.Combine(folder, MapPages.SubFolder));
            }
            catch (Exception e)
            {
                done?.Invoke(false, e.Message);
                return;
            }
            _i._sharing = (map, folder, done);
            if (map.WorkshopId == 0)
                _i._create.Set(SteamUGC.CreateItem(SteamUtils.GetAppID(), EWorkshopFileType.k_EWorkshopFileTypeCommunity));
            else
                _i.Submit();
            Say(Loc.T("maps.sharing"));
        }

        void OnCreated(CreateItemResult_t r, bool ioFailure)
        {
            var (map, _, done) = _sharing;
            if (ioFailure || r.m_eResult != EResult.k_EResultOK || map == null)
            {
                string why = ioFailure ? "IO" : r.m_eResult.ToString();
                Say(Loc.F("maps.failed", why));
                done?.Invoke(false, why);
                return;
            }
            map.WorkshopId = r.m_nPublishedFileId.m_PublishedFileId;
            MapLibrary.Save(map); // the id stays with the map: the next share updates it
            if (r.m_bUserNeedsToAcceptWorkshopLegalAgreement) ShowItem(map.WorkshopId);
            Submit();
        }

        void Submit()
        {
            var (map, folder, _) = _sharing;
            var h = SteamUGC.StartItemUpdate(SteamUtils.GetAppID(), new PublishedFileId_t(map.WorkshopId));
            SteamUGC.SetItemTitle(h, map.Name);
            SteamUGC.SetItemDescription(h, Loc.F("maps.description", map.Biomes.Count, map.Author));
            SteamUGC.SetItemContent(h, folder);
            string pic = Path.Combine(folder, PictureFile);
            if (File.Exists(pic)) SteamUGC.SetItemPreview(h, pic);
            SteamUGC.SetItemVisibility(h, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
            SteamUGC.SetItemTags(h, new List<string> { "Map" });
            _submit.Set(SteamUGC.SubmitItemUpdate(h, ""));
        }

        void OnSubmitted(SubmitItemUpdateResult_t r, bool ioFailure)
        {
            var (map, _, done) = _sharing;
            _sharing = default;
            bool ok = !ioFailure && r.m_eResult == EResult.k_EResultOK;
            if (ok && r.m_bUserNeedsToAcceptWorkshopLegalAgreement && map != null) ShowItem(map.WorkshopId);
            string msg = ok ? Loc.T("maps.done.shared") : Loc.F("maps.failed", ioFailure ? "IO" : r.m_eResult.ToString());
            Say(msg);
            done?.Invoke(ok, msg);
        }

        /// The item's Steam page in the overlay (where the Workshop agreement is accepted).
        public static void ShowItem(ulong id)
        {
            if (SteamLobby.SteamReady && id != 0)
                SteamFriends.ActivateGameOverlayToWebPage("steam://url/CommunityFilePage/" + id);
        }
    }
}
