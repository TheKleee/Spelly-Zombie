using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace SpellyZombie
{
    /// ★ CLIPS AND PHOTOS WHILE PLAYING (his idea, Sep 20 2026): I starts and
    /// stops a video, P takes a photo, anywhere in the game; in the lobby O
    /// opens the folder they are in. The picture is the finished screen, as the
    /// game shows it, UI and all (his call): only what is marked hidable - the
    /// chips that explain controls, a ClipHidden marker on anything else - is
    /// hidden while a clip runs or a photo is taken. Videos are written by Windows' own encoder
    /// (ClipWriter, MfMp4Writer), photos as PNG, both to Videos/Spelly Zombie:
    /// one place to look. The sound is the game as it is heard.
    public class ClipRecorder : MonoBehaviour
    {
        const int MaxHeight = 1080, MaxWidth = 2560; // a clip is never bigger than this; the screen is scaled down to fit
        const float MaxMinutes = 15f;                // a forgotten recording stops by itself
        const long NeedsBytes = 1500L * 1024 * 1024; // free disk space a recording starts with
        const long StopsAtBytes = 300L * 1024 * 1024; // and the space left at which it closes itself

        static ClipRecorder _i;
        /// A clip is being taken, or a photo is about to be: what is only a helper may hide (the booth's grid).
        public static bool Busy => _i != null && (_i._capturing || _i._photoWanted);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~ClipRecorder");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<ClipRecorder>();
        }

        // the clip
        ClipWriter _clip;
        bool _capturing;          // pictures and sound are being taken
        bool _closing, _finishSent;
        float _closeBy;           // the last pictures are given up on after this
        double _stopTime;
        readonly Stopwatch _clock = new Stopwatch();
        long _lastIndex;
        bool _frameDue;
        long _dueIndex;
        int _readbacks;           // pictures asked of the graphics card and not back yet
        int _screenW, _screenH;
        float _spaceCheck, _tapCheck;
        RenderTexture _clipRT;
        ClipSoundTap _tap;
        AudioListener _tapOn;
        float _fps = 60f;         // how fast the game runs, smoothed

        // the photo
        bool _photoWanted, _photoBusy;
        float _photoAsked;
        RenderTexture _photoRT;
        volatile string _photoSaved, _photoFailed; // set by the thread that writes the file

        bool _captureWarned;

        void Awake() => StartCoroutine(EndOfFrames());

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);

            var kb = Keyboard.current;
            bool typing = UIKit.Typing || (PoseStudio.IsOpen && GUIUtility.keyboardControl != 0);
            if (kb != null && !typing)
            {
                if (kb.iKey.wasPressedThisFrame) ToggleClip();
                if (kb.pKey.wasPressedThisFrame) TakePhoto();
                // never in a match: a file window over the game takes the player out of it
                if (kb.oKey.wasPressedThisFrame && ActiveScene.Name == "Lobby") OpenFolder();
            }
            if (_clip != null) TickClip();
            TickPhoto();
            TickUI();
        }

        /// After everything has moved, before anything is drawn: what hides, and whether a picture is due.
        void LateUpdate()
        {
            Hide(_capturing || _photoWanted);
            _frameDue = false;
            if (_capturing && _clip != null)
            {
                if (!_clock.IsRunning) _clock.Start(); // the clip begins with its first picture
                long index = (long)(_clock.Elapsed.TotalSeconds * _clip.Fps);
                // a place on the frame grid nobody filled yet, and a buffer to put it in (else the encoder is behind: skip)
                if (index > _lastIndex && _clip.FreeFrames - _readbacks > 0) { _frameDue = true; _dueIndex = index; }
            }
        }

        // ------------------------------------------------------------- grab --
        // The picture is the screen once the frame is finished: the world, every canvas and the
        // sight tints, exactly as the player sees them, minus what Hide put away.
        readonly WaitForEndOfFrame _frameEnd = new WaitForEndOfFrame();
        RenderTexture _screenRT;

        System.Collections.IEnumerator EndOfFrames()
        {
            while (true)
            {
                yield return _frameEnd;
                try { Grab(); }
                catch (Exception e)
                {
                    if (_captureWarned) continue;
                    _captureWarned = true;
                    Debug.LogError($"[SpellyZombie] clip recorder: the picture could not be taken: {e}");
                }
            }
        }

        void Grab()
        {
            bool clipDue = _frameDue && _capturing && _clip != null && _clipRT != null;
            if (clipDue || _photoWanted)
            {
                int sw = Screen.width, sh = Screen.height;
                if (_screenRT == null || _screenRT.width != sw || _screenRT.height != sh)
                {
                    DropScreenRT();
                    _screenRT = new RenderTexture(sw, sh, 0, ColourFormat(false)) { name = "ClipScreen" };
                    _screenRT.Create();
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRT);
                if (clipDue)
                {
                    _frameDue = false;
                    _lastIndex = _dueIndex;
                    var clip = _clip;
                    long index = _dueIndex;
                    Copy(_screenRT, _clipRT); // scaled to the clip's size
                    _readbacks++;
                    AsyncGPUReadback.Request(_clipRT, 0, r => OnFrame(clip, index, r));
                }
                if (_photoWanted)
                {
                    _photoWanted = false;
                    FitPhotoRT(sw, sh);
                    int w = _photoRT.width, h = _photoRT.height;
                    var format = _photoRT.graphicsFormat;
                    Copy(_screenRT, _photoRT);
                    AsyncGPUReadback.Request(_photoRT, 0, r => OnPhoto(w, h, format, r));
                    Blink(); // after the picture, so the frame of light is never in it
                }
                RenderTexture.active = null;
            }
            if (_capturing) DrawDot();
        }

        /// A screen grab arrives upside down where the graphics API counts rows from the top (Direct3D, Metal).
        static void Copy(RenderTexture screen, RenderTexture to)
        {
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(screen, to, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(screen, to);
        }

        void DropScreenRT()
        {
            if (_screenRT == null) return;
            _screenRT.Release();
            Destroy(_screenRT);
            _screenRT = null;
        }

        // The recording light is painted onto the screen AFTER the grab, so the player sees it and the clip never does.
        static Texture2D _dot;
        void DrawDot()
        {
            if (_dot == null)
            {
                const int n = 32;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (n * 0.5f)) / (n * 0.5f);
                        px[y * n + x] = new Color(0.95f, 0.12f, 0.1f, Mathf.Clamp01((1f - d) * 6f));
                    }
                _dot.SetPixels(px);
                _dot.Apply(false);
            }
            float s = Mathf.Round(Screen.height * 0.022f);
            RenderTexture.active = null;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            Graphics.DrawTexture(new Rect(Screen.width - s * 2.4f, s * 3.4f, s, s), _dot);
            GL.PopMatrix();
        }

        // ------------------------------------------------------- what hides --
        // Hidable = the chips that explain controls, and anything carrying a ClipHidden marker.
        // Hidden by a CanvasGroup of its own, so whoever owns the thing never notices.
        static readonly string[] HiddenGroups = { "PromptGroup", "PromptChips" };
        readonly System.Collections.Generic.List<CanvasGroup> _hidden = new System.Collections.Generic.List<CanvasGroup>();
        readonly System.Collections.Generic.List<float> _hiddenAlpha = new System.Collections.Generic.List<float>();
        bool _hiding;
        float _hideScanAt;

        void Hide(bool on)
        {
            if (on)
            {
                // chips are built the first time they are needed: look again now and then
                if (_hiding && Time.unscaledTime < _hideScanAt) return;
                _hiding = true;
                _hideScanAt = Time.unscaledTime + 0.5f;
                var root = UIKit.Root;
                if (root != null)
                    foreach (var name in HiddenGroups)
                    {
                        var t = root.Find(name);
                        if (t != null) PutAway(t.gameObject);
                    }
                foreach (var m in FindObjectsByType<ClipHidden>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    PutAway(m.gameObject);
                if (_ui != null) PutAway(_ui.gameObject); // the recorder's own note too
                return;
            }
            if (!_hiding) return;
            _hiding = false;
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].alpha = _hiddenAlpha[i];
            _hidden.Clear();
            _hiddenAlpha.Clear();
        }

        void PutAway(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            if (_hidden.Contains(group)) return;
            _hidden.Add(group);
            _hiddenAlpha.Add(group.alpha);
            group.alpha = 0f;
        }

        static GraphicsFormat ColourFormat(bool blueFirst)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (blueFirst) return linear ? GraphicsFormat.B8G8R8A8_SRGB : GraphicsFormat.B8G8R8A8_UNorm;
            return linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
        }

        // ------------------------------------------------------------- clip --
        void ToggleClip()
        {
            if (_clip == null) StartClip();
            else if (_capturing) StopClip();
            // a press while the last file is still closing does nothing
        }

        void StartClip()
        {
            string folder = VideosFolder();
            if (FreeSpace(folder) < NeedsBytes) { Fail(Loc.T("clip.disk")); return; }

            float k = Mathf.Min(1f, Mathf.Min(MaxHeight / (float)Screen.height, MaxWidth / (float)Screen.width));
            int w = Even(Screen.width * k), h = Even(Screen.height * k);
            int fps = _fps >= 55f ? 60 : 30; // a game running slower gains nothing from 60
            float full = fps == 60 ? 16000000f : 10000000f; // bits a second at 1920x1080
            int bitrate = (int)Mathf.Clamp(full * w * h / (1920f * 1080f), 3000000f, 40000000f);
            int rate = AudioSettings.outputSampleRate;
            if (rate != 44100 && rate != 48000) rate = 0; // what Windows' AAC encoder takes; else a silent film

            // blue first is what the encoder reads; a card that cannot draw into it gets its bytes swapped instead
            bool blueFirst = SystemInfo.IsFormatSupported(ColourFormat(true), GraphicsFormatUsage.Render);
            _clipRT = new RenderTexture(w, h, 0, ColourFormat(blueFirst)) { name = "ClipFrame" };
            _clipRT.Create();

            string path;
            try { path = NewFile(folder, ".mp4"); }
            catch (Exception e)
            {
                Debug.LogError($"[SpellyZombie] clip recorder: no place for the video: {e.Message}");
                Fail(Loc.T("clip.failed"));
                DropClip();
                return;
            }
            _clip = new ClipWriter(path, w, h, fps, bitrate, rate) { SwapRedBlue = !blueFirst };
            _clip.Start();
            _clock.Reset();
            _lastIndex = -1;
            _readbacks = 0;
            _closing = _finishSent = false;
            _screenW = Screen.width;
            _screenH = Screen.height;
            _spaceCheck = Time.unscaledTime + 5f;
            _tapCheck = 0f;
            _capturing = true;
            Debug.Log($"[SpellyZombie] recording {w}x{h} at {fps} to {path}");
        }

        void StopClip()
        {
            if (!_capturing) return;
            _capturing = false;
            _frameDue = false;
            _stopTime = _clock.Elapsed.TotalSeconds;
            _clock.Stop();
            DropTap();
            _closing = true;
            _closeBy = Time.unscaledTime + 1f;
        }

        void TickClip()
        {
            if (_clip.Done) { ClipEnded(); return; }
            if (_capturing)
            {
                if (_clock.Elapsed.TotalMinutes >= MaxMinutes) { StopClip(); return; }
                // another window size would stretch the rest of the clip
                if (Screen.width != _screenW || Screen.height != _screenH) { StopClip(); return; }
                if (Time.unscaledTime > _spaceCheck)
                {
                    _spaceCheck = Time.unscaledTime + 5f;
                    if (FreeSpace(_clip.Path) < StopsAtBytes) { StopClip(); return; }
                }
                KeepTap();
            }
            else if (_closing && !_finishSent && (_readbacks <= 0 || Time.unscaledTime > _closeBy))
            {
                _finishSent = true;
                _clip.Finish(_stopTime);
            }
        }

        /// The writing thread is through: the file plays, or it gave up (and removed it).
        void ClipEnded()
        {
            string error = _clip.Error, path = _clip.Path;
            if (error == null) Toast(ShortPath(path));
            else
            {
                Debug.LogError($"[SpellyZombie] clip recorder: {error}");
                Fail(Loc.T("clip.failed"));
            }
            DropClip();
        }

        void DropClip()
        {
            _capturing = _closing = _finishSent = false;
            _frameDue = false;
            _clock.Stop();
            DropTap();
            _clip = null;
            if (_clipRT != null) { _clipRT.Release(); Destroy(_clipRT); _clipRT = null; }
        }

        /// A picture came back from the graphics card (some frames after it was asked for).
        void OnFrame(ClipWriter clip, long index, AsyncGPUReadbackRequest r)
        {
            if (clip != _clip || _finishSent) return; // a straggler of a clip that is over
            _readbacks--;
            if (r.hasError) return; // its place shows the picture before it
            var px = clip.RentFrame();
            if (px == null) return;
            var data = r.GetData<byte>();
            if (data.Length != px.Length) { clip.ReturnFrame(px); return; }
            data.CopyTo(px);
            clip.PushFrame(px, index);
        }

        // ------------------------------------------------------------ sound --
        /// The tap sits beside the listener that is heard; listeners come and go with scenes and cameras.
        void KeepTap()
        {
            if (_clip.SoundRate <= 0) return;
            if (_tap != null && _tapOn != null && _tapOn.isActiveAndEnabled) return;
            if (Time.unscaledTime < _tapCheck) return;
            _tapCheck = Time.unscaledTime + 0.5f;
            DropTap();
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled) continue;
                _tapOn = l;
                _tap = l.gameObject.AddComponent<ClipSoundTap>();
                _tap.Clip = _clip;
                _tap.Clock = _clock;
                break;
            }
        }

        void DropTap()
        {
            if (_tap != null) { _tap.Clip = null; Destroy(_tap); }
            _tap = null;
            _tapOn = null;
        }

        // ------------------------------------------------------------ photo --
        void TakePhoto()
        {
            if (_photoBusy) return;
            _photoBusy = true;
            _photoWanted = true;
            _photoAsked = Time.unscaledTime;
        }

        void FitPhotoRT(int w, int h)
        {
            if (_photoRT != null && _photoRT.width == w && _photoRT.height == h) return;
            DropPhotoRT();
            _photoRT = new RenderTexture(w, h, 0, ColourFormat(false)) { name = "ClipPhoto" };
            _photoRT.Create();
        }

        void DropPhotoRT()
        {
            if (_photoRT == null) return;
            _photoRT.Release();
            Destroy(_photoRT);
            _photoRT = null;
        }

        void OnPhoto(int w, int h, GraphicsFormat format, AsyncGPUReadbackRequest r)
        {
            DropPhotoRT();
            if (r.hasError) { _photoBusy = false; return; }
            byte[] px;
            string path;
            try
            {
                var data = r.GetData<byte>();
                px = new byte[data.Length];
                data.CopyTo(px);
                path = NewFile(VideosFolder(), ".png");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpellyZombie] clip recorder: no place for the photo: {e.Message}");
                _photoBusy = false;
                return;
            }
            // the PNG is made and written off the main thread, so the game never waits for it
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int i = 3; i < px.Length; i += 4) px[i] = 255; // the camera's alpha means nothing: a solid picture
                    File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(px, format, (uint)w, (uint)h));
                    _photoSaved = path;
                }
                catch (Exception e) { _photoFailed = e.Message; }
            });
        }

        void TickPhoto()
        {
            // no camera drew for a second: the photo is called off
            if (_photoWanted && Time.unscaledTime - _photoAsked > 1f) { _photoWanted = false; _photoBusy = false; DropPhotoRT(); }
            string saved = _photoSaved, failed = _photoFailed;
            if (saved != null) { _photoSaved = null; _photoBusy = false; Toast(ShortPath(saved)); }
            if (failed != null)
            {
                _photoFailed = null;
                _photoBusy = false;
                Debug.LogError($"[SpellyZombie] clip recorder: the photo was not written: {failed}");
            }
        }

        // ------------------------------------------------------------ files --
        /// The clips and photos, in the system's file window. Windows brings the
        /// folder's window forward when it is open already, so there is never a second one.
        void OpenFolder()
        {
            if (Time.unscaledTime < _openedAt + 1f) return;
            _openedAt = Time.unscaledTime;
            PhotoLibrary.Reveal(VideosFolder());
        }
        float _openedAt = -10f;

        static string VideosFolder()
        {
            string videos = "";
            try { videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos); }
            catch (Exception) { videos = ""; }
            if (string.IsNullOrEmpty(videos)) videos = Path.Combine(Application.persistentDataPath, "Videos");
            return Path.Combine(videos, "Spelly Zombie");
        }

        /// A file name nobody has yet: the game's name and the moment.
        static string NewFile(string folder, string extension)
        {
            Directory.CreateDirectory(folder);
            string stem = "Spelly Zombie " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string path = Path.Combine(folder, stem + extension);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, stem + " " + n + extension);
            return path;
        }

        /// Free bytes on the drive a path sits on; unknown counts as plenty.
        static long FreeSpace(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch (Exception) { return long.MaxValue; }
        }

        /// The last three parts of a path: enough to find the file, short enough to read.
        static string ShortPath(string path)
        {
            var parts = path.Replace('/', '\\').Split('\\');
            int from = Mathf.Max(0, parts.Length - 3);
            return string.Join("\\", parts, from, parts.Length - from);
        }

        static int Even(float v) => Mathf.Max(16, Mathf.RoundToInt(v) & ~1);

        // --------------------------------------------------------------- UI --
        // A canvas of its own, above the game's: it shows in immersive mode too, and like
        // every overlay it is never in a clip or a photo.
        RectTransform _ui, _pill;
        Text _time, _toast;
        Image[] _blink;
        float _toastUntil, _blinkUntil;
        int _shownSeconds = -1;
        const float BlinkSeconds = 0.3f;

        void EnsureUI()
        {
            if (_ui != null) return;
            var main = UIKit.Root.GetComponent<CanvasScaler>(); // the skin canvas first, so its scaler can be copied
            var go = new GameObject("SZ_ClipUI");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            if (main != null)
            {
                var s = go.AddComponent<CanvasScaler>();
                s.uiScaleMode = main.uiScaleMode;
                s.referenceResolution = main.referenceResolution;
                s.screenMatchMode = main.screenMatchMode;
                s.matchWidthOrHeight = main.matchWidthOrHeight;
            }
            _ui = (RectTransform)go.transform;

            // a red dot and the clip's length, top right, under where the creators keep a corner button
            _pill = UIKit.Group(_ui, "Recording");
            UIKit.Place(_pill, new Vector2(1f, 1f), new Vector2(-16f, -76f), new Vector2(96f, 26f));
            _pill.pivot = new Vector2(1f, 1f);
            var skin = UISkin.I;
            var dot = UIKit.Panel(_pill, skin != null ? skin.RoundGrey : null, new Color(0.95f, 0.12f, 0.1f));
            dot.raycastTarget = false;
            var drt = dot.rectTransform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0f, 0.5f);
            drt.anchoredPosition = Vector2.zero;
            drt.sizeDelta = new Vector2(20f, 20f);
            _time = UIKit.Label(_pill, "0:00", 18, UIKit.Parchment, TextAnchor.MiddleLeft, true);
            _time.resizeTextForBestFit = false;
            var trt = _time.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(28f, 0f);
            trt.offsetMax = Vector2.zero;
            _time.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            _pill.gameObject.SetActive(false);

            // where the file went (a path needs no translating), or why there is none
            _toast = UIKit.Label(_ui, "", 15, UIKit.Parchment, TextAnchor.MiddleRight, true);
            var tort = _toast.rectTransform;
            tort.anchorMin = tort.anchorMax = tort.pivot = new Vector2(1f, 1f);
            tort.anchoredPosition = new Vector2(-16f, -106f);
            tort.sizeDelta = new Vector2(900f, 24f);
            _toast.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            _toast.gameObject.SetActive(false);

            // a photo shows as a thin white frame that fades; nothing flashes over the picture
            _blink = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var edge = UIKit.Panel(_ui, null, Color.white);
                edge.raycastTarget = false;
                var ert = edge.rectTransform;
                bool flat = i < 2; // bottom, top, left, right
                ert.anchorMin = new Vector2(i == 3 ? 1f : 0f, i == 1 ? 1f : 0f);
                ert.anchorMax = new Vector2(i == 2 ? 0f : 1f, i == 0 ? 0f : 1f);
                ert.pivot = new Vector2(i == 3 ? 1f : 0f, i == 1 ? 1f : 0f);
                ert.anchoredPosition = Vector2.zero;
                ert.sizeDelta = flat ? new Vector2(0f, 6f) : new Vector2(6f, 0f);
                edge.gameObject.SetActive(false);
                _blink[i] = edge;
            }
        }

        void Fail(string text)
        {
            Toast(text);
            Juice.Sound2D(Sfx.UiError);
        }

        void Toast(string text)
        {
            EnsureUI();
            _toast.font = UIKit.Font; // the language may have changed since it was built
            _toast.text = text;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 5f;
        }

        void Blink()
        {
            EnsureUI();
            _blinkUntil = Time.unscaledTime + BlinkSeconds;
        }

        void TickUI()
        {
            if (_ui == null) return;
            // the screen IS the picture now: the light of a running clip is painted after the grab (DrawDot)
            if (_pill.gameObject.activeSelf) _pill.gameObject.SetActive(false);
            if (_toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.gameObject.SetActive(false);
            float left = _blinkUntil - Time.unscaledTime;
            bool blinking = left > 0f;
            foreach (var edge in _blink)
            {
                if (edge.gameObject.activeSelf != blinking) edge.gameObject.SetActive(blinking);
                if (blinking) edge.color = new Color(1f, 1f, 1f, Mathf.Clamp01(left / BlinkSeconds));
            }
        }

        // ------------------------------------------------------------- ends --
        void OnApplicationQuit() => Shutdown();
        void OnDestroy() => Shutdown();

        /// Leaving the game or play mode mid-clip: the file is closed properly, or it would not play.
        void Shutdown()
        {
            if (_clip != null)
            {
                StopClip();
                if (!_finishSent)
                {
                    try { AsyncGPUReadback.WaitAllRequests(); } catch (Exception) { }
                    _finishSent = true;
                    _clip.Finish(_stopTime);
                }
                _clip.Join(8000);
                DropClip();
            }
            Hide(false);
            DropPhotoRT();
            DropScreenRT();
        }
    }

    /// Put this on any UI object that clips and photos should leave out (the chips that explain
    /// controls are left out already). It is hidden only while a clip runs or a photo is taken.
    public class ClipHidden : MonoBehaviour { }

    /// Sits beside the AudioListener while a clip is taken: Unity hands every block of
    /// the final mix through here, on the audio thread. It goes to the clip as 16-bit stereo.
    public class ClipSoundTap : MonoBehaviour
    {
        public volatile ClipWriter Clip;
        public Stopwatch Clock;

        void OnAudioFilterRead(float[] data, int channels)
        {
            var clip = Clip;
            var clock = Clock;
            if (clip == null || clock == null || !clock.IsRunning || channels < 1) return;
            double heardAt = clock.Elapsed.TotalSeconds;
            int frames = data.Length / channels;
            int bytes = frames * ClipWriter.SoundBytesPerFrame;
            var pcm = clip.RentSound(bytes);
            for (int f = 0, i = 0, o = 0; f < frames; f++, i += channels)
            {
                // the front pair of whatever the speakers are; one channel plays on both sides
                int l = (int)(Mathf.Clamp(data[i], -1f, 1f) * 32767f);
                int r = channels > 1 ? (int)(Mathf.Clamp(data[i + 1], -1f, 1f) * 32767f) : l;
                pcm[o++] = (byte)l; pcm[o++] = (byte)(l >> 8);
                pcm[o++] = (byte)r; pcm[o++] = (byte)(r >> 8);
            }
            clip.PushSound(pcm, bytes, heardAt);
        }
    }
}
