using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE PICTURE: the light (the scene's sun turned and brightened, a rim
    /// light from behind), the look (brightness, contrast, warmth, colour, glow,
    /// vignette, background blur, laid over the scene's own grade), what is
    /// behind the subjects (the world, a colour, or nothing), and the photo
    /// itself at any size inside the frame drawn on screen. A see-through photo
    /// is taken twice, over black and over white: where the two differ is how
    /// much shows, so glows and smoke keep their soft edges.
    public partial class PhotoBooth
    {
        public const int MaxSide = 8192;

        /// Canvas sizes by where the picture goes (checked Sep 19 2026).
        struct CanvasSize
        {
            public string Key;
            public int W, H;
            public Vector2 Safe; // Steam's inner safe box, 0 = none
            public bool SeeThrough; // the platform wants a see-through picture
            public CanvasSize(string key, int w, int h, Vector2 safe = default, bool seeThrough = false)
            { Key = key; W = w; H = h; Safe = safe; SeeThrough = seeThrough; }
        }

        static readonly CanvasSize[] Sizes =
        {
            new CanvasSize("photo.size.youtube", 1280, 720),
            new CanvasSize("photo.size.hd", 1920, 1080),
            new CanvasSize("photo.size.4k", 3840, 2160),
            new CanvasSize("photo.size.square", 1080, 1080),
            new CanvasSize("photo.size.portrait", 1080, 1350),
            new CanvasSize("photo.size.story", 1080, 1920),
            new CanvasSize("photo.size.x", 1600, 900),
            new CanvasSize("photo.size.steamheader", 920, 430),
            new CanvasSize("photo.size.steamsmall", 462, 174),
            new CanvasSize("photo.size.steammain", 1232, 706),
            new CanvasSize("photo.size.steamvertical", 748, 896),
            new CanvasSize("photo.size.steampage", 1438, 810),
            new CanvasSize("photo.size.librarycapsule", 600, 900),
            new CanvasSize("photo.size.libraryhero", 3840, 1240, new Vector2(860f, 380f)),
            new CanvasSize("photo.size.librarylogo", 1280, 720, default, true),
        };

        static string SizeName(CanvasSize c)
        {
            switch (c.Key)
            {
                case "photo.size.youtube": return Loc.T("photo.size.youtube");
                case "photo.size.hd": return Loc.T("photo.size.hd");
                case "photo.size.4k": return Loc.T("photo.size.4k");
                case "photo.size.square": return Loc.T("photo.size.square");
                case "photo.size.portrait": return Loc.T("photo.size.portrait");
                case "photo.size.story": return Loc.T("photo.size.story");
                case "photo.size.x": return Loc.T("photo.size.x");
                case "photo.size.steamheader": return Loc.T("photo.size.steamheader");
                case "photo.size.steamsmall": return Loc.T("photo.size.steamsmall");
                case "photo.size.steammain": return Loc.T("photo.size.steammain");
                case "photo.size.steamvertical": return Loc.T("photo.size.steamvertical");
                case "photo.size.steampage": return Loc.T("photo.size.steampage");
                case "photo.size.librarycapsule": return Loc.T("photo.size.librarycapsule");
                case "photo.size.libraryhero": return Loc.T("photo.size.libraryhero");
                default: return Loc.T("photo.size.librarylogo");
            }
        }

        /// The preset the canvas is now, or -1.
        static int SizeNow()
        {
            for (int i = 0; i < Sizes.Length; i++)
                if (Sizes[i].W == Editing.Width && Sizes[i].H == Editing.Height) return i;
            return -1;
        }

        // ------------------------------------------------------------- stage --
        GameObject _stage;
        Light _sun, _rim;
        Quaternion _sunRot0;
        float _sunPower0;
        bool _fog0;
        Volume _volume;
        VolumeProfile _profile;
        ColorAdjustments _grade;
        WhiteBalance _white;
        Bloom _bloom;
        Vignette _vignette;
        DepthOfField _dof;
        float _baseExposure, _baseContrast, _baseSaturation, _baseTemp, _baseGlow, _baseVignette;
        bool _shooting;

        /// The booth's own light and look, laid over the scene's.
        void BuildStage()
        {
            _stage = new GameObject("~PhotoStage");
            _sun = RenderSettings.sun;
            if (_sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.isActiveAndEnabled && (_sun == null || l.intensity > _sun.intensity)) _sun = l;
            if (_sun != null)
            {
                RenderSettings.sun = _sun; // the rim light never becomes the sun
                _sunRot0 = _sun.transform.rotation;
                _sunPower0 = _sun.intensity;
            }
            _fog0 = RenderSettings.fog;

            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(_stage.transform, false);
            _rim = rimGo.AddComponent<Light>();
            _rim.type = LightType.Directional;
            _rim.color = new Color(1f, 0.95f, 0.86f);
            _rim.shadows = LightShadows.None;
            _rim.enabled = false;

            // read the scene's own grade before the booth's lies over it
            _baseExposure = Base<ColorAdjustments>(c => c.postExposure);
            _baseContrast = Base<ColorAdjustments>(c => c.contrast);
            _baseSaturation = Base<ColorAdjustments>(c => c.saturation);
            _baseTemp = Base<WhiteBalance>(c => c.temperature);
            _baseGlow = Base<Bloom>(c => c.intensity);
            _baseVignette = Base<Vignette>(c => c.intensity);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _grade = _profile.Add<ColorAdjustments>();
            _white = _profile.Add<WhiteBalance>();
            _bloom = _profile.Add<Bloom>();
            _vignette = _profile.Add<Vignette>();
            _dof = _profile.Add<DepthOfField>();
            _volume = _stage.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _volume.sharedProfile = _profile;
        }

        void TeardownStage()
        {
            if (_profile != null)
            {
                foreach (var c in _profile.components) if (c != null) Destroy(c);
                Destroy(_profile);
                _profile = null;
            }
            if (_stage != null) Destroy(_stage);
            _stage = null;
            _volume = null;
            _sun = null;
            _rim = null;
        }

        /// The value the scene's own global volumes give a float setting, the booth's left out.
        float Base<T>(System.Func<T, FloatParameter> param) where T : VolumeComponent
        {
            float best = 0f, prio = float.MinValue;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == null || v == _volume || !v.isGlobal || !v.isActiveAndEnabled) continue;
                var p = v.sharedProfile;
                if (p == null || !p.TryGet(out T comp) || !comp.active) continue;
                var fp = param(comp);
                if (fp == null || !fp.overrideState || v.priority < prio) continue;
                prio = v.priority;
                best = fp.value;
            }
            return best;
        }

        /// The ground, the light, the look and the background as the setup says.
        void ApplyStage()
        {
            SyncIsland();
            ApplyLight();
            ApplyLook();
            ApplyBackground();
        }

        // ------------------------------------------------------------ ground --
        /// ★ GROUND (his ask: "why can't we add ground... an image in the actual environment"):
        /// the island's own biomes grow around the subjects, the Map Creator's way, from the
        /// setup's seed; none = the empty stage. It is the world behind a photo.
        bool _islandUp;
        int _islandSeed;

        /// The ground as the setup says, grown again only when it differs from what stands.
        void SyncIsland()
        {
            bool want = Editing.Island;
            if (want == _islandUp && (!want || Editing.IslandSeed == _islandSeed)) return;
            var map = FindFirstObjectByType<SpellyMap>();
            if (map == null) return;
            MapDef.StandDownAuthored();
            if (want)
            {
                MapDef.BuildBiomes(new MapDef { OwnBiomes = true, Biomes = MapPalette.BaseBiomes() });
                map.Generate(Editing.IslandSeed);
            }
            else
            {
                MapDef.BuildBiomes(new MapDef { OwnBiomes = true });
                map.ClearGenerated();
            }
            _islandUp = want;
            _islandSeed = Editing.IslandSeed;
        }

        /// Picked in Add: the island or none, another island when `again`. Everything placed rises
        /// or sinks with the ground under it (what stands on something else stays on it); the photo
        /// shows the world, so the island is in it.
        void SetIsland(bool on, bool again)
        {
            if (on == Editing.Island && !again) return;
            var was = new float[_subjects.Count];
            for (int i = 0; i < _subjects.Count; i++)
                if (_subjects[i].Root != null) was[i] = StageGround(_subjects[i].Root.transform.position);
            float eyeWas = _cam != null ? StageGround(_cam.transform.position) : 0f;
            Editing.Island = on;
            if (on && (again || Editing.IslandSeed == 0)) Editing.IslandSeed = Random.Range(1, int.MaxValue);
            if (on) Editing.Background = 0;
            SyncIsland();
            Physics.SyncTransforms();
            for (int i = 0; i < _subjects.Count; i++)
            {
                var s = _subjects[i];
                if (s.Root == null) continue;
                var t = s.Root.transform;
                t.position += Vector3.up * (StageGround(t.position) - was[i]);
                s.Item.Pos = t.position;
                Moved(s);
                SettleEyes(s);
            }
            if (_cam != null)
            {
                // the eye moves with the ground too, and never ends up inside a hill
                var p = _cam.transform.position;
                float g = StageGround(p);
                p.y = Mathf.Max(p.y + g - eyeWas, g + 1.5f);
                _cam.transform.position = p;
            }
            ApplyBackground();
            BuildPhotoWindow();
            CommitStep();
        }

        /// The stage's own ground under a point: the island's terrain while it stands, else the
        /// zero plane. Never a tree or a house on it.
        float StageGround(Vector3 at)
        {
            if (!_islandUp) return 0f;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position, size = t.terrainData.size;
                if (at.x < o.x || at.z < o.z || at.x > o.x + size.x || at.z > o.z + size.z) continue;
                return o.y + t.SampleHeight(at);
            }
            return 0f;
        }

        void ApplyLight()
        {
            if (_sun != null)
            {
                _sun.transform.rotation = Editing.SunMoved ? Quaternion.Euler(Editing.SunHeight, Editing.SunTurn, 0f) : _sunRot0;
                _sun.intensity = Editing.SunMoved ? _sunPower0 * Editing.SunPower : _sunPower0;
            }
            ApplyRim();
        }

        /// The rim light comes from behind the subjects: it turns with the eye.
        void ApplyRim()
        {
            if (_rim == null) return;
            bool on = Editing.Rim > 0.001f;
            if (_rim.enabled != on) _rim.enabled = on;
            if (!on) return;
            _rim.intensity = Editing.Rim;
            _rim.transform.rotation = Quaternion.Euler(25f, (_fly != null ? _fly.Yaw : 0f) + Editing.RimTurn, 0f);
        }

        /// The sun as the sliders show it: the setup's, or where the scene put it.
        (float turn, float height, float power) SunNow()
        {
            if (Editing.SunMoved || _sun == null) return (Editing.SunTurn, Editing.SunHeight, Editing.SunPower);
            var e = _sunRot0.eulerAngles;
            float h = e.x > 180f ? e.x - 360f : e.x;
            return (e.y, Mathf.Clamp(h, 0f, 90f), 1f);
        }

        void ApplyLook()
        {
            if (_volume == null) return;
            var d = Editing;
            // a transparent photo keeps the grade (his go) but never a glow, a blur or dark corners:
            // they would spill past what is there, and its edges stay clean
            bool clean = d.Background == 2;
            Set(_grade.postExposure, _baseExposure + d.Brightness, d.Brightness != 0f);
            Set(_grade.contrast, Mathf.Clamp(_baseContrast + d.Contrast, -100f, 100f), d.Contrast != 0f);
            Set(_grade.saturation, Mathf.Clamp(_baseSaturation + d.Saturation, -100f, 100f), d.Saturation != 0f);
            Set(_white.temperature, Mathf.Clamp(_baseTemp + d.Warmth, -100f, 100f), d.Warmth != 0f);
            Set(_bloom.intensity, clean ? 0f : Mathf.Max(0f, _baseGlow + d.Glow), clean || d.Glow != 0f);
            Set(_vignette.intensity, clean ? 0f : Mathf.Clamp01(_baseVignette + d.Vignette), clean || d.Vignette != 0f);
            bool blur = !clean && d.Blur > 0.001f;
            _dof.active = blur;
            _dof.mode.overrideState = blur;
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focusDistance.overrideState = blur;
            _dof.focusDistance.value = Mathf.Max(0.1f, d.Focus);
            _dof.focalLength.overrideState = blur;
            _dof.focalLength.value = Mathf.Lerp(35f, 150f, d.Blur);
            _dof.aperture.overrideState = blur;
            _dof.aperture.value = Mathf.Lerp(16f, 1.2f, d.Blur);
        }

        static void Set(VolumeParameter<float> p, float value, bool on)
        {
            p.overrideState = on;
            p.value = value;
        }

        /// What is drawn behind the subjects, on screen as in the photo.
        void ApplyBackground()
        {
            if (_cam == null) return;
            var data = _cam.GetUniversalAdditionalCameraData();
            switch (Editing.Background)
            {
                case 1: // a colour
                    _cam.cullingMask = 1 << Layer;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = Editing.BackColor;
                    data.renderPostProcessing = true;
                    RenderSettings.fog = false;
                    break;
                case 2: // see-through: the grey stands for nothing; the grade shows as the photo takes it
                    _cam.cullingMask = 1 << Layer;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
                    data.renderPostProcessing = true;
                    RenderSettings.fog = false;
                    break;
                default:
                    _cam.cullingMask = ~(1 << PreviewPane.Layer);
                    _cam.clearFlags = CameraClearFlags.Skybox;
                    data.renderPostProcessing = true;
                    RenderSettings.fog = _fog0;
                    break;
            }
            ApplyLook(); // a transparent photo keeps its edges clean: no glow, blur or dark corners
        }

        /// Every frame: the rim follows the eye, the eyes that look at it follow it too.
        void TickStage()
        {
            ApplyRim();
            if (_sun != null && Editing.SunMoved) ApplyLight(); // nothing else turns the sun back
            foreach (var s in _subjects) if (s.Eyes != null && s.Item.LookAtCamera) SettleEyes(s);
            UpdateOverlay();
        }

        // ------------------------------------------------------------- frame --
        RectTransform _overlay, _frame, _safe;
        readonly Image[] _dim = new Image[4];
        readonly Image[] _edge = new Image[8];
        Image _shutter;
        float _shutterUntil;

        /// The canvas's frame on screen: dim outside it, gold around it, the
        /// safe box inside when the size has one, a white blink when a photo is taken.
        void BuildOverlay(RectTransform ui)
        {
            _overlay = UIKit.Group(ui, "PhotoFrame");
            UIKit.Stretch(_overlay);
            for (int i = 0; i < 4; i++) _dim[i] = Bar(_overlay, new Color(0f, 0f, 0f, 0.55f));
            _frame = UIKit.Stretch(UIKit.Group(_overlay, "Frame"));
            _safe = UIKit.Stretch(UIKit.Group(_overlay, "Safe"));
            for (int i = 0; i < 4; i++) _edge[i] = Bar(_frame, new Color(1f, 0.82f, 0.25f, 0.95f));
            for (int i = 4; i < 8; i++) _edge[i] = Bar(_safe, new Color(1f, 1f, 1f, 0.8f));
            _shutter = Bar(_overlay, new Color(1f, 1f, 1f, 0f));
            var srt = _shutter.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = srt.offsetMax = Vector2.zero;
        }

        static Image Bar(RectTransform parent, Color c)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false; // the frame never catches a click
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            return img;
        }

        /// The canvas as big as the screen holds it, in the middle: in UI units.
        Rect CropRect()
        {
            if (_overlay == null) return default;
            Vector2 size = _overlay.rect.size;
            float aspect = Editing.Width / (float)Mathf.Max(1, Editing.Height);
            float w = size.x - 24f, h = size.y - 24f;
            if (w / h > aspect) w = h * aspect;
            else h = w / aspect;
            return new Rect((size.x - w) * 0.5f, (size.y - h) * 0.5f, w, h);
        }

        /// How much of the screen's height the frame takes: the photo's lens narrows by it.
        float CropShare()
        {
            if (_overlay == null) return 1f;
            float full = Mathf.Max(1f, _overlay.rect.height);
            return Mathf.Clamp01(CropRect().height / full);
        }

        void UpdateOverlay()
        {
            if (_overlay == null) return;
            Vector2 size = _overlay.rect.size;
            var r = CropRect();
            Place(_dim[0], 0f, 0f, size.x, r.yMin);                  // below
            Place(_dim[1], 0f, r.yMax, size.x, size.y - r.yMax);     // above
            Place(_dim[2], 0f, r.yMin, r.xMin, r.height);            // left
            Place(_dim[3], r.xMax, r.yMin, size.x - r.xMax, r.height); // right
            Edges(0, r, 3f);
            int preset = SizeNow();
            bool safe = preset >= 0 && Sizes[preset].Safe != Vector2.zero;
            _safe.gameObject.SetActive(safe);
            if (safe)
            {
                var s = Sizes[preset].Safe;
                float w = r.width * s.x / Editing.Width, h = r.height * s.y / Editing.Height;
                Edges(4, new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h), 2f);
            }
            float blink = Mathf.Clamp01((_shutterUntil - Time.unscaledTime) / 0.35f);
            _shutter.color = new Color(1f, 1f, 1f, blink * 0.8f);
        }

        void Edges(int from, Rect r, float t)
        {
            Place(_edge[from], r.xMin - t, r.yMin - t, r.width + t * 2f, t);
            Place(_edge[from + 1], r.xMin - t, r.yMax, r.width + t * 2f, t);
            Place(_edge[from + 2], r.xMin - t, r.yMin, t, r.height);
            Place(_edge[from + 3], r.xMax, r.yMin, t, r.height);
        }

        static void Place(Image img, float x, float y, float w, float h)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        // ------------------------------------------------------------- photo --
        /// ★ TAKE THE PHOTO: the canvas's size, what the frame holds, into the
        /// player's Pictures.
        void TakePhoto()
        {
            if (_cam == null) return;
            CaptureAll();
            byte[] png = null;
            try { png = Shoot(Editing.Width, Editing.Height, Editing.Background, CropShare()); }
            catch (System.Exception e) { Debug.LogError($"[SpellyZombie] Photo Booth: the photo failed: {e}"); }
            if (png == null) { FlashError(Loc.T("photo.failed")); return; }
            // his rule: a photo taken is a setup saved, so it shows in the photos with its picture
            // and opens again to edit; leaving the booth never loses it
            if (!Store()) return;
            string path;
            try { path = PhotoLibrary.WritePhoto(Editing.Folder, Editing.Name, png); }
            catch (System.Exception e)
            {
                Debug.LogError($"[SpellyZombie] Photo Booth: the photo could not be written: {e}");
                FlashError(Loc.T("photo.failed"));
                return;
            }
            _shutterUntil = Time.unscaledTime + 0.35f;
            Flash(Loc.F("photo.taken", Path.GetFileName(path)));
        }

        /// The setup's picture for its card: the photo as it is framed, small, in its own shape.
        byte[] Thumbnail()
        {
            float aspect = Editing.Width / (float)Mathf.Max(1, Editing.Height);
            bool wide = aspect >= 16f / 9f;
            int w = wide ? 640 : Mathf.Max(16, Mathf.RoundToInt(360f * aspect));
            int h = wide ? Mathf.Max(16, Mathf.RoundToInt(640f / aspect)) : 360;
            try { return Shoot(w, h, Editing.Background == 2 ? 1 : Editing.Background, CropShare()); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] Photo Booth: no picture for the card: {e.Message}");
                return null;
            }
        }

        /// A picture from the eye: `share` of the screen's height is what fills it.
        byte[] Shoot(int w, int h, int background, float share)
        {
            w = Mathf.Clamp(w, 16, MaxSide);
            h = Mathf.Clamp(h, 16, MaxSide);
            _shooting = true;
            UpdateSelectBox();
            bool grid = _grid != null && _grid.activeSelf;
            if (grid) _grid.SetActive(false);
            var go = new GameObject("~PhotoShot");
            Texture2D a = null, b = null, g = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.CopyFrom(_cam);
                cam.aspect = w / (float)h;
                float half = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Clamp(share, 0.01f, 1f);
                cam.fieldOfView = 2f * Mathf.Atan(half) * Mathf.Rad2Deg;
                var mine = _cam.GetUniversalAdditionalCameraData();
                var data = cam.GetUniversalAdditionalCameraData();
                data.volumeLayerMask = mine.volumeLayerMask;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderPostProcessing = true;
                bool fog = RenderSettings.fog;
                if (background == 0)
                {
                    cam.cullingMask = ~(1 << PreviewPane.Layer) & ~(1 << GridLayer);
                    cam.clearFlags = CameraClearFlags.Skybox;
                    RenderSettings.fog = _fog0;
                }
                else
                {
                    cam.cullingMask = 1 << Layer;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    RenderSettings.fog = false;
                }
                try
                {
                    if (background == 2)
                    {
                        // the see-through from a plain pair (a grade would bend the black against the
                        // white), the colours from one taken with the look on
                        g = Render(cam, w, h, new Color(0f, 0f, 0f, 0f), true);
                        data.renderPostProcessing = false;
                        a = Render(cam, w, h, new Color(0f, 0f, 0f, 0f), true);
                        b = Render(cam, w, h, new Color(1f, 1f, 1f, 0f), true);
                        Cutout(a, b, g);
                    }
                    else a = Render(cam, w, h, Editing.BackColor, false);
                }
                finally { RenderSettings.fog = fog; }
                return a.EncodeToPNG();
            }
            finally
            {
                if (a != null) Destroy(a);
                if (b != null) Destroy(b);
                if (g != null) Destroy(g);
                Destroy(go);
                if (grid && _grid != null) _grid.SetActive(true);
                _shooting = false;
            }
        }

        /// One render at the canvas size; `alpha` keeps a channel for the see-through pair.
        static Texture2D Render(Camera cam, int w, int h, Color clear, bool alpha)
        {
            int msaa = (long)w * h <= 3840L * 2160L ? 4 : 1;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, msaa);
            var flat = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            var active = RenderTexture.active;
            try
            {
                cam.backgroundColor = clear;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(rt, flat); // the smoothed edges settle into one sample before reading
                RenderTexture.active = flat;
                var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(flat);
            }
        }

        /// Over black and over white without the look: what is solid is the same in both, what is
        /// not there turns from black to white, and a glow sits between. That is the see-through;
        /// the colours come from `graded` (the same view with the game's grade and the sliders on,
        /// over black), freed from the black under whatever is part see-through.
        static void Cutout(Texture2D black, Texture2D white, Texture2D graded)
        {
            var bp = black.GetPixels32();
            var wp = white.GetPixels32();
            var gp = graded.GetPixels32();
            var lin = new float[256];
            for (int i = 0; i < 256; i++) lin[i] = Mathf.GammaToLinearSpace(i / 255f);
            for (int i = 0; i < bp.Length; i++)
            {
                Color32 k = bp[i], w = wp[i], c = gp[i];
                // how much of the white came through, the least of the three
                float through = Mathf.Min(lin[w.r] - lin[k.r], Mathf.Min(lin[w.g] - lin[k.g], lin[w.b] - lin[k.b]));
                float alpha = Mathf.Clamp01(1f - through);
                if (alpha < 0.002f) { bp[i] = new Color32(0, 0, 0, 0); continue; }
                bp[i] = new Color32(Enc(lin[c.r] / alpha), Enc(lin[c.g] / alpha), Enc(lin[c.b] / alpha),
                    (byte)Mathf.RoundToInt(alpha * 255f));
            }
            black.SetPixels32(bp);
            black.Apply(false);
        }

        static byte Enc(float linear) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(Mathf.Max(0f, linear))) * 255f);
    }
}
