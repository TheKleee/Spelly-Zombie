using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Video settings, saved per machine, applied at boot and again whenever
    /// the main camera changes. A row the player never touched keeps the
    /// authored value, so nothing looks different until someone moves it.
    public static class VideoOptions
    {
        public enum Display { Fullscreen, Borderless, Windowed }
        public enum Level { Low, Medium, High }
        public enum ShadowLevel { Off, Low, High }
        public enum Aa { Off, Fxaa, Smaa, Taa, Msaa }
        public static readonly int[] FpsCaps = { 0, 60, 120, 144, 240 };

        // the three presets: textures, shadows, render scale
        static readonly Level[] PresetTextures = { Level.Low, Level.Medium, Level.High };
        static readonly ShadowLevel[] PresetShadows = { ShadowLevel.Off, ShadowLevel.Low, ShadowLevel.High };
        static readonly float[] PresetScale = { 0.8f, 1f, 1f };

        static bool _loaded;
        static int _width, _height;          // 0 = as launched
        static Display _mode;
        static Level _textures;
        static ShadowLevel _shadows;
        static float _scale;
        static int _effects = -1, _motionBlur = -1, _aa = -1; // -1 = never touched, the authored value stands
        static int _fps;
        static bool _vsync;

        // the pipeline asset as authored, put back when the editor stops playing
        static bool _captured;
        static float _shadowDistance0, _scale0;
        static int _shadowRes0, _msaa0;

        public static int CurrentWidth => Screen.width;
        public static int CurrentHeight => Screen.height;
        public static Display Mode { get { Load(); return _mode; } }
        public static Level Textures { get { Load(); return _textures; } }
        public static ShadowLevel Shadows { get { Load(); return _shadows; } }
        public static int FpsIndex { get { Load(); return _fps; } }
        public static bool VSync { get { Load(); return _vsync; } }

        public static bool Effects
        {
            get
            {
                Load();
                if (_effects >= 0) return _effects == 1;
                var cam = Camera.main;
                return cam != null && cam.GetUniversalAdditionalCameraData().renderPostProcessing;
            }
        }

        public static bool MotionBlur
        {
            get
            {
                Load();
                if (_motionBlur >= 0) return _motionBlur == 1;
                foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                    if (v.sharedProfile != null && v.sharedProfile.TryGet<UnityEngine.Rendering.Universal.MotionBlur>(out var mb))
                        return mb.active;
                return false;
            }
        }

        public static Aa Antialias
        {
            get
            {
                Load();
                if (_aa >= 0) return (Aa)_aa;
                var pipe = Pipe;
                if (pipe != null && pipe.msaaSampleCount > 1) return Aa.Msaa;
                var cam = Camera.main;
                if (cam == null) return Aa.Off;
                switch (cam.GetUniversalAdditionalCameraData().antialiasing)
                {
                    case AntialiasingMode.FastApproximateAntialiasing: return Aa.Fxaa;
                    case AntialiasingMode.SubpixelMorphologicalAntiAliasing: return Aa.Smaa;
                    case AntialiasingMode.TemporalAntiAliasing: return Aa.Taa;
                    default: return Aa.Off;
                }
            }
        }

        /// The preset every row matches, or -1 once the player mixed them.
        public static int Preset
        {
            get
            {
                Load();
                for (int i = 0; i < 3; i++)
                    if (_textures == PresetTextures[i] && _shadows == PresetShadows[i]
                        && Mathf.Approximately(_scale, PresetScale[i])) return i;
                return -1;
            }
        }

        static UniversalRenderPipelineAsset Pipe =>
            (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _width = PlayerPrefs.GetInt("sz_res_w", 0);
            _height = PlayerPrefs.GetInt("sz_res_h", 0);
            _mode = (Display)PlayerPrefs.GetInt("sz_display", (int)ModeOf(Screen.fullScreenMode));
            _textures = (Level)PlayerPrefs.GetInt("sz_textures", (int)Level.High);
            _shadows = (ShadowLevel)PlayerPrefs.GetInt("sz_shadows", (int)ShadowLevel.High);
            _scale = PlayerPrefs.GetFloat("sz_renderscale", 1f);
            _effects = PlayerPrefs.GetInt("sz_effects", -1);
            _motionBlur = PlayerPrefs.GetInt("sz_motionblur", -1);
            _aa = PlayerPrefs.GetInt("sz_aa", -1);
            _fps = Mathf.Clamp(PlayerPrefs.GetInt("sz_fps", 0), 0, FpsCaps.Length - 1);
            _vsync = PlayerPrefs.GetInt("sz_vsync", QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        }

        static Display ModeOf(FullScreenMode m) => m == FullScreenMode.Windowed ? Display.Windowed
            : m == FullScreenMode.FullScreenWindow ? Display.Borderless : Display.Fullscreen;

        static void Save()
        {
            PlayerPrefs.SetInt("sz_res_w", _width);
            PlayerPrefs.SetInt("sz_res_h", _height);
            PlayerPrefs.SetInt("sz_display", (int)_mode);
            PlayerPrefs.SetInt("sz_textures", (int)_textures);
            PlayerPrefs.SetInt("sz_shadows", (int)_shadows);
            PlayerPrefs.SetFloat("sz_renderscale", _scale);
            PlayerPrefs.SetInt("sz_effects", _effects);
            PlayerPrefs.SetInt("sz_motionblur", _motionBlur);
            PlayerPrefs.SetInt("sz_aa", _aa);
            PlayerPrefs.SetInt("sz_fps", _fps);
            PlayerPrefs.SetInt("sz_vsync", _vsync ? 1 : 0);
        }

        // ---- the rows ----
        public static void SetResolution(int w, int h) { Load(); _width = w; _height = h; Save(); ApplyScreen(); }
        public static void SetDisplay(Display d) { Load(); _mode = d; Save(); ApplyScreen(); }
        public static void SetPreset(int i)
        {
            Load();
            i = Mathf.Clamp(i, 0, 2);
            _textures = PresetTextures[i]; _shadows = PresetShadows[i]; _scale = PresetScale[i];
            Save(); ApplyQuality();
        }
        public static void SetTextures(Level l) { Load(); _textures = l; Save(); ApplyQuality(); }
        public static void SetShadows(ShadowLevel s) { Load(); _shadows = s; Save(); ApplyQuality(); }
        public static void SetEffects(bool on) { Load(); _effects = on ? 1 : 0; Save(); ApplyCamera(Camera.main); }
        public static void SetMotionBlur(bool on) { Load(); _motionBlur = on ? 1 : 0; Save(); ApplyVolumes(); }
        public static void SetAntialias(Aa a) { Load(); _aa = (int)a; Save(); ApplyQuality(); ApplyCamera(Camera.main); }
        public static void SetFps(int index) { Load(); _fps = Mathf.Clamp(index, 0, FpsCaps.Length - 1); Save(); ApplyFrame(); }
        public static void SetVSync(bool on) { Load(); _vsync = on; Save(); ApplyFrame(); }

        /// Every distinct size the display offers, largest first, the current one always in.
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                if (r.height < 600) continue;
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var now = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(now)) list.Add(now);
            list.Sort((a, b) => b.x * b.y - a.x * a.y);
            return list;
        }

        // ---- applying ----
        static void ApplyScreen()
        {
            int w = _width > 0 ? _width : Screen.width;
            int h = _height > 0 ? _height : Screen.height;
            var mode = _mode == Display.Fullscreen ? FullScreenMode.ExclusiveFullScreen
                : _mode == Display.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (w == Screen.width && h == Screen.height && mode == Screen.fullScreenMode) return;
            Screen.SetResolution(w, h, mode);
        }

        static void ApplyQuality()
        {
            QualitySettings.globalTextureMipmapLimit = _textures == Level.High ? 0 : _textures == Level.Medium ? 1 : 2;
            var pipe = Pipe;
            if (pipe == null) return;
            if (!_captured)
            {
                _captured = true;
                _shadowDistance0 = pipe.shadowDistance;
                _shadowRes0 = pipe.mainLightShadowmapResolution;
                _msaa0 = pipe.msaaSampleCount;
                _scale0 = pipe.renderScale;
            }
            pipe.shadowDistance = _shadows == ShadowLevel.Off ? 0f
                : _shadows == ShadowLevel.Low ? Mathf.Min(_shadowDistance0, 25f) : _shadowDistance0;
            pipe.mainLightShadowmapResolution = _shadows == ShadowLevel.High ? _shadowRes0 : Mathf.Min(_shadowRes0, 1024);
            pipe.renderScale = _scale0 * _scale;
            if (_aa >= 0) pipe.msaaSampleCount = (Aa)_aa == Aa.Msaa ? 4 : 1;
        }

        public static void ApplyCamera(Camera cam)
        {
            Load();
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            if (_effects >= 0) data.renderPostProcessing = _effects == 1;
            if (_aa >= 0)
            {
                data.antialiasing = (Aa)_aa == Aa.Fxaa ? AntialiasingMode.FastApproximateAntialiasing
                    : (Aa)_aa == Aa.Smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                    : (Aa)_aa == Aa.Taa ? AntialiasingMode.TemporalAntiAliasing
                    : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }

        /// Motion blur lives in the scene volumes; the instance profile is
        /// edited, never the asset.
        public static void ApplyVolumes()
        {
            Load();
            if (_motionBlur < 0) return;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var prof = v.profile;
                if (prof != null && prof.TryGet<UnityEngine.Rendering.Universal.MotionBlur>(out var mb))
                    mb.active = _motionBlur == 1;
            }
        }

        static void ApplyFrame()
        {
            QualitySettings.vSyncCount = _vsync ? 1 : 0;
            Application.targetFrameRate = FpsCaps[_fps] > 0 ? FpsCaps[_fps] : -1;
        }

        /// The pipeline asset back to its authored values: play mode edits
        /// would otherwise stay on the asset in the editor.
        public static void RestoreAuthored()
        {
            var pipe = Pipe;
            if (pipe == null || !_captured) return;
            pipe.shadowDistance = _shadowDistance0;
            pipe.mainLightShadowmapResolution = _shadowRes0;
            pipe.msaaSampleCount = _msaa0;
            pipe.renderScale = _scale0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Load();
            if (_width > 0 || _mode != ModeOf(Screen.fullScreenMode)) ApplyScreen();
            ApplyQuality();
            ApplyFrame();
            var go = new GameObject("VideoOptions");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<VideoOptionsApplier>();
        }
    }

    /// Re-applies the camera rows when the main camera changes and the
    /// volume rows when a scene loads.
    public class VideoOptionsApplier : MonoBehaviour
    {
        Camera _last;

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            _last = null;
            VideoOptions.ApplyVolumes();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == _last) return;
            _last = cam;
            VideoOptions.ApplyCamera(cam);
        }

        void OnApplicationQuit() => VideoOptions.RestoreAuthored();
    }
}
