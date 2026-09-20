using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// ★ A CUSTOM MAP, made in the Map Creator: data only. Its own biomes (the
    /// base scene's stand down, only its sky and light stay), the pieces and
    /// creatures placed on them, its own spellbook and the rune drawings it
    /// teaches. Saved as JSON, shipped to joiners as JSON, shared through the
    /// Workshop as JSON and a picture: the host's is the law, like the book.
    [Serializable]
    public class MapDef
    {
        public string Name = "New Map";
        public string Author = "";
        public string Base = "Spelly Island";           // the scene whose sky, light and managers it borrows
        public int Seed;                                 // the ground grows from this, every time
        public int TimerMinutes = 10;                    // 0 = no clock
        public List<string> Spells = new List<string>(); // allowed spells; empty = every spell
        public List<SpawnDef> Spawns = new List<SpawnDef>();
        public List<PropDef> Props = new List<PropDef>();
        /// True = this map's biomes replace the base scene's own.
        public bool OwnBiomes;
        public List<BiomeDef> Biomes = new List<BiomeDef>();
        /// The map's spellbook (spells and runes) as JSON; empty = the game's.
        public string BookJson = "";
        /// Drawings that teach the matcher this map's runes.
        public List<RuneSampleDef> RuneSamples = new List<RuneSampleDef>();
        /// The Workshop item this map was shared as or saved from; 0 = none.
        public ulong WorkshopId;
        /// ★ THE MAP'S TEAMS, WC3 style (MapRules). Empty = the classic match:
        /// wizards against acolytes, the environment counts for nothing.
        public List<TeamDef> Teams = new List<TeamDef>();
        /// Where its pages sit when they are not beside a saved map (a downloaded shared map).
        [NonSerialized] public string PagesDir;

        /// A creature that rises when the match goes live (CreatureSpawn).
        [Serializable]
        public class SpawnDef
        {
            public string Body = "";
            public Vector3 Pos;
            public float Size = 1f;
            public int Team;              // Team: 0 neutral (wild), 1 wizard, 2 acolyte
            public bool Permanent = true;
            public float Delay, Every;
            public int Count = 1, MaxAlive;
            public float Scatter = 1.5f;
        }

        /// One of the game's own prefabs, placed.
        [Serializable]
        public class PropDef
        {
            public string Prefab = "";
            public Vector3 Pos;
            public float Yaw;
            public float Scale = 1f;
        }

        /// One drawing of one rune, in the sample frame.
        [Serializable]
        public class RuneSampleDef
        {
            public int Rune;
            public List<StrokeDef> Strokes = new List<StrokeDef>();
        }

        [Serializable]
        public class StrokeDef { public List<Vector2> Points = new List<Vector2>(); }

        /// The map the next base scene load wears; null = the plain scene.
        public static MapDef Active;
        public const string PiecesName = "~MapPieces";
        public const string BiomesName = "~MapBiomes";

        public static string ToJson(MapDef d) => JsonUtility.ToJson(d, true);

        public static MapDef FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var d = JsonUtility.FromJson<MapDef>(json);
                d?.Repair();
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] map unreadable: {e.Message}");
                return null;
            }
        }

        public void Repair()
        {
            if (Spells == null) Spells = new List<string>();
            if (Spawns == null) Spawns = new List<SpawnDef>();
            if (Props == null) Props = new List<PropDef>();
            if (Biomes == null) Biomes = new List<BiomeDef>();
            if (RuneSamples == null) RuneSamples = new List<RuneSampleDef>();
            if (Teams == null) Teams = new List<TeamDef>();
            if (BookJson == null) BookJson = "";
            if (Author == null) Author = "";
            if (string.IsNullOrEmpty(Base)) Base = "Spelly Island";
            if (string.IsNullOrEmpty(Name)) Name = "New Map";
            foreach (var b in Biomes) b?.Repair();
        }

        // ------------------------------------------------------------ runes --
        /// The drawings as the matcher takes them.
        public List<(RuneType type, List<List<Vector2>> sample)> SampleList()
        {
            var list = new List<(RuneType, List<List<Vector2>>)>();
            foreach (var s in RuneSamples)
            {
                if (s == null || s.Strokes == null) continue;
                var strokes = new List<List<Vector2>>();
                foreach (var st in s.Strokes)
                    if (st?.Points != null && st.Points.Count >= 2) strokes.Add(new List<Vector2>(st.Points));
                if (strokes.Count > 0) list.Add(((RuneType)s.Rune, strokes));
            }
            return list;
        }

        public static RuneSampleDef ToSampleDef(RuneType type, List<List<Vector2>> sample)
        {
            var d = new RuneSampleDef { Rune = (int)type };
            foreach (var s in sample) d.Strokes.Add(new StrokeDef { Points = new List<Vector2>(s) });
            return d;
        }

        static string _rulesKey;

        /// The active map's spellbook and rune drawings become the game's, or
        /// the player's own come back. A host tells everyone the book.
        public static void UseActiveRules()
        {
            MapPages.Use(Active);
            string key = Active == null ? "" : Active.Name + "|" + Active.BookJson.Length + "|" + Active.RuneSamples.Count;
            if (key == _rulesKey) return;
            _rulesKey = key;
            if (Active != null && !string.IsNullOrEmpty(Active.BookJson)) SpellBook.Adopt(Active.BookJson);
            else SpellBook.Forget();
            RuneLibrary.UseMapSamples(Active != null ? Active.SampleList() : null);
            NetSync.PushBook();
        }

        /// A client: the host's map carries its drawings; the book comes on its own message.
        public static void UseActiveDrawings()
        {
            MapPages.Use(Active);
            RuneLibrary.UseMapSamples(Active != null ? Active.SampleList() : null);
        }

        // ------------------------------------------------------------ scene --
        /// ★ The base scene becomes this map's ground: the scene's own biomes,
        /// roads and containers stand down and this map's biomes stand up.
        /// True = there is ground to grow.
        public static bool PrepareScene()
        {
            var map = Active;
            if (map == null || !map.OwnBiomes || SceneManager.GetActiveScene().name != map.Base) return true;
            MapPalette.Gather();       // the scene's own pieces, before they stand down
            StandDownAuthored();
            BuildBiomes(map);
            return map.Biomes.Count > 0;
        }

        /// The base scene's own layout goes quiet: its biomes, roads and containers.
        public static void StandDownAuthored()
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Biome>(FindObjectsSortMode.None))
                if (!IsMapMade(b.transform)) b.gameObject.SetActive(false);
            foreach (var n in UnityEngine.Object.FindObjectsByType<PathNode>(FindObjectsSortMode.None))
                n.gameObject.SetActive(false);
            foreach (var p in UnityEngine.Object.FindObjectsByType<PathPoint>(FindObjectsSortMode.None))
                p.gameObject.SetActive(false);
            foreach (var c in UnityEngine.Object.FindObjectsByType<BiomeContainer>(FindObjectsSortMode.None))
                c.gameObject.SetActive(false);
        }

        /// This map's biomes as scene boxes, replacing the last set at once.
        public static Transform BuildBiomes(MapDef map)
        {
            var old = GameObject.Find(BiomesName);
            if (old != null)
            {
                old.name = BiomesName + "_gone";
                old.SetActive(false); // unregistered now, before the generator looks
                UnityEngine.Object.Destroy(old);
            }
            var root = new GameObject(BiomesName).transform;
            foreach (var d in map.Biomes)
            {
                if (d == null) continue;
                var go = new GameObject(string.IsNullOrEmpty(d.Name) ? "Biome" : d.Name);
                go.SetActive(false); // every field first, then it registers
                go.transform.SetParent(root, false);
                go.transform.position = d.Pos;
                Biome b = d.Liquid ? go.AddComponent<LiquidBiome>() : (Biome)go.AddComponent<GroundBiome>();
                d.ApplyTo(b);
                go.SetActive(true);
            }
            return root;
        }

        public static bool IsMapMade(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.parent == null) return t.name == BiomesName;
            return false;
        }

        /// Stands the active map's pieces in the loaded scene: creature markers
        /// and props under one root, its spell list on MapSpells. Every machine
        /// does this from the same JSON. Returns the root, null = none.
        public static Transform ApplyActive()
        {
            var old = GameObject.Find(PiecesName);
            if (old != null) { old.name = PiecesName + "_gone"; UnityEngine.Object.Destroy(old); }
            if (Active == null) return null;
            if (SceneManager.GetActiveScene().name != Active.Base) return null; // it stands only on its own scene

            var root = new GameObject(PiecesName).transform;
            for (int i = 0; i < Active.Spawns.Count; i++)
            {
                var s = Active.Spawns[i];
                var go = new GameObject("Spawn " + s.Body);
                go.transform.SetParent(root, false);
                go.transform.position = s.Pos;
                var m = go.AddComponent<CreatureSpawn>();
                m.Body = s.Body; m.Size = s.Size; m.Team = (Team)Mathf.Clamp(s.Team, 0, 2);
                m.Permanent = s.Permanent; m.Delay = s.Delay; m.Every = s.Every;
                m.Count = s.Count; m.MaxAlive = s.MaxAlive; m.Scatter = s.Scatter;
                var piece = go.AddComponent<MapPiece>();
                piece.Kind = MapPiece.Spawn; piece.Index = i;
            }
            for (int i = 0; i < Active.Props.Count; i++)
            {
                var p = Active.Props[i];
                var prefab = MapPalette.Find(p.Prefab);
                if (prefab == null)
                {
                    Debug.LogWarning($"[SpellyZombie] map '{Active.Name}': no piece named '{p.Prefab}' in this scene.");
                    continue;
                }
                // yaw on top of the prefab's own tilt (kit roots keep theirs)
                var go = UnityEngine.Object.Instantiate(prefab, p.Pos,
                    Quaternion.Euler(0f, p.Yaw, 0f) * prefab.transform.rotation, root);
                go.transform.localScale = prefab.transform.localScale * Mathf.Max(0.05f, p.Scale);
                go.name = p.Prefab;
                var piece = go.AddComponent<MapPiece>();
                piece.Kind = MapPiece.Prop; piece.Index = i;
            }
            // a map with its own book plays that book, whatever the scene listed
            var spells = UnityEngine.Object.FindFirstObjectByType<MapSpells>();
            if (spells != null && (Active.Spells.Count > 0 || !string.IsNullOrEmpty(Active.BookJson)))
            {
                spells.Spells.Clear();
                spells.Spells.AddRange(Active.Spells);
            }
            return root;
        }
    }

    /// Who makes up a team. Saved as a number: new kinds only go at the end.
    public enum TeamWho { Everyone, Wizards, Acolytes, Environment }

    /// ★ ONE TEAM OF A MAP. Counting = in the race for victory: among the
    /// teams that count, the last one standing wins.
    [Serializable]
    public class TeamDef
    {
        public TeamWho Who;
        public bool Counts = true;
    }

    /// ★ ONE BIOME OF A CUSTOM MAP: every number a biome box carries, and its
    /// pieces and brushes by name (the base scene's own prefabs).
    [Serializable]
    public class BiomeDef
    {
        public string Name = "Biome";
        public bool Liquid;
        public Vector3 Pos;
        public Vector3 Size = new Vector3(30f, 8f, 30f);
        public int Layer = 1;
        public float ProtectedCore;
        public float FloorNoise = 1.5f, NoiseScale = 14f;
        public string FloorLayer = "", PathLayer = "";
        public bool CanPath = true;
        public float PathCurve = 0.35f;
        public float FieldSize = 2f, MaxSlope = 45f;
        public List<string> Props = new List<string>();
        public List<string> Sources = new List<string>();
        public int MinSources = 1, MaxSources = 3;
        public string WallLight = "";
        public float WallLightHeight = 1.1f;
        public string Cauldron = "", Landmark = "";
        public int HeatOffset, LightOffset, DensityOffset, StickOffset, AffinityOffset;
        public int StateOffset = -150;
        public int IntCap = 25, CourageCap = 25, ClonesCap;
        public float StrengthCap, RegenScale = 1f;
        public bool WizardSpawn;
        public float WizardSpawnSpread = 0.25f;
        public bool ByObjects;
        public float ObjectFull = 3f, ObjectReach = 8f;
        public Color Color = new Color(0.25f, 0.75f, 0.35f);
        public string Surface = "";
        public float Buoyancy = 0.92f;

        public void Repair()
        {
            if (Props == null) Props = new List<string>();
            if (Sources == null) Sources = new List<string>();
        }

        public BiomeDef Clone()
        {
            var c = JsonUtility.FromJson<BiomeDef>(JsonUtility.ToJson(this));
            c.Repair();
            return c;
        }

        /// A biome box as data: the palette's templates are the base scene's own.
        public static BiomeDef From(Biome b)
        {
            var d = new BiomeDef
            {
                Name = b.name, Liquid = b is LiquidBiome,
                Pos = b.transform.position, Size = b.Size, Layer = b.Layer, ProtectedCore = b.ProtectedCore,
                FloorNoise = b.FloorNoise, NoiseScale = b.NoiseScale,
                FloorLayer = b.FloorLayer != null ? b.FloorLayer.name : "",
                PathLayer = b.PathLayer != null ? b.PathLayer.name : "",
                CanPath = b.CanPath, PathCurve = b.PathCurve, FieldSize = b.FieldSize, MaxSlope = b.MaxSlope,
                Props = Names(b.Props), Sources = Names(b.Sources),
                MinSources = b.MinSources, MaxSources = b.MaxSources,
                WallLight = NameOf(b.WallLight), WallLightHeight = b.WallLightHeight,
                Cauldron = NameOf(b.Cauldron), Landmark = NameOf(b.Landmark),
                HeatOffset = b.HeatOffset, LightOffset = b.LightOffset, DensityOffset = b.DensityOffset,
                StickOffset = b.StickOffset, AffinityOffset = b.AffinityOffset, StateOffset = b.StateOffset,
                IntCap = b.IntCap, CourageCap = b.CourageCap, ClonesCap = b.ClonesCap,
                StrengthCap = b.StrengthCap, RegenScale = b.RegenScale,
                WizardSpawn = b.WizardSpawn, WizardSpawnSpread = b.WizardSpawnSpread,
                ByObjects = b.ByObjects, ObjectFull = b.ObjectFull, ObjectReach = b.ObjectReach,
            };
            if (b is GroundBiome g) d.Color = g.GizmoColor;
            if (b is LiquidBiome l) { d.Color = l.Tint; d.Surface = NameOf(l.Surface); d.Buoyancy = l.Buoyancy; }
            return d;
        }

        /// The data onto a live box; pieces and brushes found by name.
        public void ApplyTo(Biome b)
        {
            b.Size = new Vector3(Mathf.Max(1f, Size.x), Mathf.Max(1f, Size.y), Mathf.Max(1f, Size.z));
            b.Layer = Layer;
            b.ProtectedCore = Mathf.Clamp01(ProtectedCore);
            b.FloorNoise = Mathf.Max(0f, FloorNoise);
            b.NoiseScale = Mathf.Max(1f, NoiseScale);
            b.FloorLayer = MapPalette.Layer(FloorLayer);
            b.PathLayer = MapPalette.Layer(PathLayer);
            b.CanPath = CanPath;
            b.PathCurve = Mathf.Clamp01(PathCurve);
            b.RandomPosition = false; // a made map is laid out by hand
            b.FieldSize = Mathf.Max(0.5f, FieldSize);
            b.MaxSlope = Mathf.Clamp(MaxSlope, 0f, 89f);
            b.Props = Objects(Props);
            b.Sources = Objects(Sources);
            b.MinSources = Mathf.Max(0, MinSources);
            b.MaxSources = Mathf.Max(b.MinSources, MaxSources);
            b.WallLight = MapPalette.Find(WallLight);
            b.WallLightHeight = WallLightHeight;
            b.Cauldron = MapPalette.Find(Cauldron);
            b.Landmark = MapPalette.Find(Landmark);
            b.HeatOffset = HeatOffset; b.LightOffset = LightOffset; b.DensityOffset = DensityOffset;
            b.StickOffset = StickOffset; b.AffinityOffset = AffinityOffset; b.StateOffset = StateOffset;
            b.IntCap = IntCap; b.CourageCap = CourageCap; b.ClonesCap = ClonesCap;
            b.StrengthCap = Mathf.Max(0f, StrengthCap); b.RegenScale = Mathf.Max(0f, RegenScale);
            b.WizardSpawn = WizardSpawn; b.WizardSpawnSpread = Mathf.Clamp(WizardSpawnSpread, 0.05f, 1f);
            b.ByObjects = ByObjects; b.ObjectFull = Mathf.Max(0f, ObjectFull); b.ObjectReach = Mathf.Max(b.ObjectFull, ObjectReach);
            if (b is GroundBiome g) g.GizmoColor = Color;
            if (b is LiquidBiome l) { l.Tint = Color; l.Surface = MapPalette.Find(Surface); l.Buoyancy = Mathf.Clamp01(Buoyancy); }
        }

        static string NameOf(GameObject g) => g != null ? g.name : "";

        static List<string> Names(GameObject[] arr)
        {
            var list = new List<string>();
            if (arr != null) foreach (var g in arr) if (g != null && !list.Contains(g.name)) list.Add(g.name);
            return list;
        }

        static GameObject[] Objects(List<string> names)
        {
            var list = new List<GameObject>();
            if (names != null)
                foreach (var n in names)
                {
                    var g = MapPalette.Find(n);
                    if (g != null) list.Add(g);
                }
            return list.ToArray();
        }
    }

    /// A placed piece of the active map: which list it came from, and where.
    public class MapPiece : MonoBehaviour
    {
        public const int Spawn = 0, Prop = 1;
        public int Kind, Index;
    }

    /// The saved maps: one JSON and one picture each under persistentDataPath/Maps.
    public static class MapLibrary
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Maps");

        /// ★ MAPS THAT COME WITH THE GAME (his official boss map), shipped in
        /// StreamingAssets and listed with the player's own. In a build nobody
        /// overwrites or deletes them; in the Unity editor their author's working
        /// copy wins, so he can keep editing one and ship it again.
        public static string ShippedFolder => Path.Combine(Application.streamingAssetsPath, "Maps");

        public static bool IsShipped(string name) =>
            !string.IsNullOrEmpty(name) && File.Exists(Path.Combine(ShippedFolder, Stem(name) + ".json"));

        /// Saving or deleting it would change the game's own copy.
        public static bool Locked(string name) => IsShipped(name) && !Application.isEditor;

        static bool UseShipped(string name) => IsShipped(name)
            && !(Application.isEditor && File.Exists(Path.Combine(Folder, Stem(name) + ".json")));

        static string Home(string name) => UseShipped(name) ? ShippedFolder : Folder;

        /// The map standing is the one that came with the game, untouched: an
        /// edited copy, or the same name with a softer boss, never counts.
        public static bool IsOfficial(MapDef def)
        {
            if (def == null || !IsShipped(def.Name)) return false;
            try
            {
                var shipped = MapDef.FromJson(File.ReadAllText(Path.Combine(ShippedFolder, Stem(def.Name) + ".json")));
                return shipped != null && MapDef.ToJson(shipped) == MapDef.ToJson(def);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] shipped map '{def.Name}' unreadable: {e.Message}");
                return false;
            }
        }

        internal static string Stem(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            string stem = sb.ToString().Trim();
            return stem.Length == 0 ? "map" : stem;
        }

        public static string FileFor(string name) => Path.Combine(Home(name), Stem(name) + ".json");
        public static string PictureFor(string name) => Path.Combine(Home(name), Stem(name) + ".png");
        /// The map's own grimoire pages (MapPages).
        public static string PagesFolder(string name) => Path.Combine(Home(name), Stem(name) + " pages");

        /// A map is saved only with at least one biome: without one there is nothing to play.
        public static bool CanSave(MapDef def) => def != null && def.Biomes != null && def.Biomes.Count > 0;

        /// Writes the map, and its picture when one is given. False when it cannot be saved (CanSave).
        public static bool Save(MapDef def, byte[] png = null)
        {
            if (!CanSave(def) || Locked(def.Name)) return false;
            // always the player's own folder, never the shipped one
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, Stem(def.Name) + ".json"), MapDef.ToJson(def));
            if (png != null && png.Length > 0)
            {
                File.WriteAllBytes(Path.Combine(Folder, Stem(def.Name) + ".png"), png);
                ForgetPicture(def.Name);
            }
            MatchLobby.ForgetMaps();
            return true;
        }

        public static bool Exists(string name) => !string.IsNullOrEmpty(name) && File.Exists(FileFor(name));

        public static MapDef Load(string name) =>
            Exists(name) ? MapDef.FromJson(File.ReadAllText(FileFor(name))) : null;

        public static void Delete(string name)
        {
            if (Locked(name)) return; // it comes with the game
            // only ever the player's own files
            string file = Path.Combine(Folder, Stem(name) + ".json");
            string pic = Path.Combine(Folder, Stem(name) + ".png");
            string pages = Path.Combine(Folder, Stem(name) + " pages");
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(pic)) File.Delete(pic);
            if (Directory.Exists(pages)) Directory.Delete(pages, true);
            ForgetPicture(name);
            MatchLobby.ForgetMaps();
        }

        static readonly Dictionary<string, Texture2D> _pictures = new Dictionary<string, Texture2D>();

        /// ★ What a map shows in the lobby: a saved map's own picture (taken on Save, read
        /// once and shared), else its scene's from the Collection Manager's Map Pictures.
        /// `def` is the saved map when the caller has it read already.
        public static Texture2D PictureOf(string name, MapDef def = null)
        {
            if (!Exists(name)) return CollectionManager.MapPicture(name);
            if (!_pictures.TryGetValue(name, out var tex) || tex == null) _pictures[name] = tex = LoadPicture(name);
            if (tex != null) return tex;
            if (def == null || def.Name != name) def = Load(name);
            return def != null ? CollectionManager.MapPicture(def.Base) : null;
        }

        static void ForgetPicture(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_pictures.TryGetValue(name, out var tex) && tex != null) UnityEngine.Object.Destroy(tex);
            _pictures.Remove(name);
        }

        /// The map's picture, or null. The caller destroys the texture.
        public static Texture2D LoadPicture(string name) => LoadPng(PictureFor(name));

        public static Texture2D LoadPng(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] map picture unreadable: {e.Message}");
                return null;
            }
        }

        /// The maps that come with the game first, then the player's own; a name
        /// shows once, as Load reads it.
        public static List<MapDef> All()
        {
            var list = new List<MapDef>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[] { ShippedFolder, Folder })
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    var d = MapDef.FromJson(File.ReadAllText(f));
                    if (d == null || !seen.Add(d.Name)) continue;
                    list.Add(Exists(d.Name) && FileFor(d.Name) != f ? Load(d.Name) ?? d : d);
                }
            }
            return list;
        }
    }

    /// The pieces a map can use: the base scene's own prefabs, brushes and
    /// biome setups, gathered once per scene, by name.
    public static class MapPalette
    {
        static readonly Dictionary<string, GameObject> _byName = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, TerrainLayer> _layers = new Dictionary<string, TerrainLayer>();
        static readonly List<string> _names = new List<string>();
        static readonly List<string> _props = new List<string>(), _sources = new List<string>(),
            _cauldrons = new List<string>(), _landmarks = new List<string>(), _wallLights = new List<string>(),
            _surfaces = new List<string>(), _layerNames = new List<string>();
        static readonly List<BiomeDef> _templates = new List<BiomeDef>();
        static readonly List<(string Name, List<string> Pieces)> _groups = new List<(string, List<string>)>();
        static int _scene = int.MinValue;

        /// Everything that can be placed by hand.
        public static IReadOnlyList<string> Names { get { Gather(); return _names; } }
        public static IReadOnlyList<string> PropNames { get { Gather(); return _props; } }
        public static IReadOnlyList<string> SourceNames { get { Gather(); return _sources; } }
        public static IReadOnlyList<string> CauldronNames { get { Gather(); return _cauldrons; } }
        public static IReadOnlyList<string> LandmarkNames { get { Gather(); return _landmarks; } }
        public static IReadOnlyList<string> WallLightNames { get { Gather(); return _wallLights; } }
        public static IReadOnlyList<string> SurfaceNames { get { Gather(); return _surfaces; } }
        public static IReadOnlyList<string> LayerNames { get { Gather(); return _layerNames; } }
        /// The placeable pieces by where they were authored: one group per biome
        /// (its props, sources, lights, cauldron, landmark), then the inside of
        /// houses under an empty name.
        public static IReadOnlyList<(string Name, List<string> Pieces)> Groups { get { Gather(); return _groups; } }
        /// The base scene's own biomes, as data: what the Biomes window places.
        public static IReadOnlyList<BiomeDef> Templates { get { Gather(); return _templates; } }

        public static GameObject Find(string name)
        {
            Gather();
            return !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var p) ? p : null;
        }

        public static TerrainLayer Layer(string name)
        {
            Gather();
            return !string.IsNullOrEmpty(name) && _layers.TryGetValue(name, out var l) ? l : null;
        }

        public static void Gather()
        {
            int h = SceneManager.GetActiveScene().handle;
            if (h == _scene) return;
            _scene = h;
            _byName.Clear(); _layers.Clear(); _names.Clear(); _templates.Clear(); _groups.Clear();
            _props.Clear(); _sources.Clear(); _cauldrons.Clear(); _landmarks.Clear();
            _wallLights.Clear(); _surfaces.Clear(); _layerNames.Clear();

            foreach (var map in UnityEngine.Object.FindObjectsByType<SpellyMap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AddLayer(map.CliffLayer);
            foreach (var b in UnityEngine.Object.FindObjectsByType<Biome>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (MapDef.IsMapMade(b.transform)) continue;
                var mine = GroupFor(b.name);
                if (b.Props != null) foreach (var g in b.Props) { Add(g, _props); Add(g, mine); }
                if (b.Sources != null) foreach (var g in b.Sources) { Add(g, _sources); Add(g, mine); }
                Add(b.Cauldron, _cauldrons); Add(b.Cauldron, mine);
                Add(b.Landmark, _landmarks); Add(b.Landmark, mine);
                Add(b.WallLight, _wallLights); Add(b.WallLight, mine);
                AddLayer(b.FloorLayer);
                AddLayer(b.PathLayer);
                if (b is LiquidBiome l) Add(l.Surface, _surfaces, placeable: false);
                _templates.Add(BiomeDef.From(b));
            }
            _groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            var inside = new List<string>();
            foreach (var f in UnityEngine.Object.FindObjectsByType<InteriorField>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (f.Details != null) foreach (var d in f.Details) if (d != null) { Add(d.Prefab, null); Add(d.Prefab, inside); }
                Add(f.Ceiling, null); Add(f.HoleSide, null); Add(f.Stairs, null);
                Add(f.Ceiling, inside); Add(f.HoleSide, inside); Add(f.Stairs, inside);
            }
            if (inside.Count > 0) _groups.Add(("", inside));
            foreach (var list in new[] { _names, _props, _sources, _cauldrons, _landmarks, _wallLights, _surfaces, _layerNames })
                list.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _groups) g.Pieces.Sort(StringComparer.OrdinalIgnoreCase);
            _templates.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        static void Add(GameObject g, List<string> role, bool placeable = true)
        {
            if (g == null) return;
            if (!_byName.ContainsKey(g.name))
            {
                _byName[g.name] = g;
                if (placeable) _names.Add(g.name);
            }
            if (role != null && !role.Contains(g.name)) role.Add(g.name);
        }

        /// One group per biome name; boxes sharing a name share it.
        static List<string> GroupFor(string biome)
        {
            foreach (var g in _groups) if (g.Name == biome) return g.Pieces;
            var list = new List<string>();
            _groups.Add((biome, list));
            return list;
        }

        static void AddLayer(TerrainLayer l)
        {
            if (l == null || _layers.ContainsKey(l.name)) return;
            _layers[l.name] = l;
            _layerNames.Add(l.name);
        }
    }
}
