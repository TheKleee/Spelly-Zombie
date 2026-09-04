using System.Collections.Generic;
using FishNet;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Proximity voice. The mic is open (his rule: a friend game talks): the
    /// chosen microphone is captured through the engine, packed as 4-bit
    /// ADPCM at 16 kHz, sent to the host, relayed to all, and played from
    /// the speaker's avatar in 3D with distance falloff. Works with or
    /// without Steam. The tell is the EYES: they swell with the volume, on
    /// you and on whoever hears you. Spirits hear the world and are heard
    /// only by spirits.
    public class VoiceChat : MonoBehaviour
    {
        public const int Rate = 16000;
        const int Frame = 320;            // 20 ms
        const string DevicePref = "sz_mic";
        const string ModePref = "sz_voice_mode";

        static VoiceChat _instance;
        public static VoiceChat Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("VoiceChat");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<VoiceChat>();
                }
                return _instance;
            }
        }
        public static void Touch() { var _ = Instance; }

        /// True on the frames the local player is sending voice.
        public static bool LocalTalking { get; private set; }
        /// The local mic level right now, 0..1, for the settings meter.
        public static float LocalLevel { get; private set; }
        /// Owners this machine refuses to hear.
        public static readonly HashSet<int> Muted = new HashSet<int>();

        public enum MicMode { Open = 0, PushToTalk = 1, Off = 2 }

        static int _modeCache = -1;

        /// How the mic opens: always, while V is held, or never. Persisted.
        public static MicMode Mode
        {
            get
            {
                if (_modeCache < 0)
                    _modeCache = PlayerPrefs.GetInt(ModePref, DrawingConfig.VoiceOpenMic ? 0 : 1);
                return (MicMode)_modeCache;
            }
            set
            {
                _modeCache = (int)value;
                PlayerPrefs.SetInt(ModePref, _modeCache);
                PlayerPrefs.Save();
            }
        }

        /// The chosen microphone; empty = the system default.
        public static string Device
        {
            get => PlayerPrefs.GetString(DevicePref, "");
            set
            {
                PlayerPrefs.SetString(DevicePref, value ?? "");
                PlayerPrefs.Save();
                if (_instance != null) _instance.StopMic();
            }
        }

        readonly Dictionary<int, VoiceSpeaker> _speakers = new Dictionary<int, VoiceSpeaker>();
        readonly Dictionary<int, bool> _mutePersisted = new Dictionary<int, bool>();
        AudioClip _mic;
        string _micDevice;
        bool _micOn;
        int _micRate, _readPos;
        float[] _grab = new float[Rate];
        readonly float[] _frame = new float[Frame];
        int _frameFill;
        float _resamplePos;
        float _tailUntil, _speechUntil;
        GooglyEyes _localEyes;
        float _localEyesAt;
        static bool _saidNoMic;

        void OnDestroy() { StopMic(); }

        void StopMic()
        {
            if (_micOn) Microphone.End(_micDevice);
            _micOn = false;
            _mic = null;
            _micDevice = null;
            _readPos = 0;
            _frameFill = 0;
            _resamplePos = 0f;
        }

        bool StartMic()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                if (!_saidNoMic) { _saidNoMic = true; Debug.Log("[SpellyZombie] No microphone found. Voice is off."); }
                return false;
            }
            string want = Device;
            string dev = null; // null = the default device
            if (!string.IsNullOrEmpty(want))
                foreach (var d in Microphone.devices)
                    if (d == want) { dev = d; break; }
            Microphone.GetDeviceCaps(dev, out int lo, out int hi);
            _micRate = hi > 0 ? Mathf.Clamp(Rate, lo, hi) : Rate;
            _mic = Microphone.Start(dev, true, 1, _micRate);
            if (_mic == null) return false;
            _micOn = true;
            _micDevice = dev;
            _readPos = 0;
            if (_grab.Length < _micRate) _grab = new float[_micRate];
            return true;
        }

        void Update()
        {
            if (!_micOn && !StartMic()) return;

            var kb = Keyboard.current;
            var mode = Mode;
            bool want = mode == MicMode.Open
                || (mode == MicMode.PushToTalk && kb != null && kb.vKey.isPressed
                    && !GameMenu.IsOpen && !UIKit.Typing);
            if (want) _tailUntil = Time.time + 0.25f;
            bool send = want || Time.time < _tailUntil;
            LocalTalking = false;

            // everything the mic captured since last frame, in order
            int pos = Microphone.GetPosition(_micDevice);
            if (pos < 0 || pos == _readPos) return;
            int total = _mic.samples;
            int count = pos > _readPos ? pos - _readPos : total - _readPos + pos;
            if (count > _grab.Length) { _readPos = pos; return; } // fell too far behind: skip
            if (pos > _readPos) _mic.GetData(_grab, _readPos);
            else
            {
                int first = total - _readPos;
                var head = new float[first];
                _mic.GetData(head, _readPos);
                System.Array.Copy(head, 0, _grab, 0, first);
                if (pos > 0)
                {
                    var tail = new float[pos];
                    _mic.GetData(tail, 0);
                    System.Array.Copy(tail, 0, _grab, first, pos);
                }
            }
            _readPos = pos;

            // to 16 kHz frames of 20 ms, sent one by one
            float step = _micRate / (float)Rate;
            float i = _resamplePos;
            for (; i < count; i += step)
            {
                _frame[_frameFill++] = _grab[(int)i];
                if (_frameFill < Frame) continue;
                _frameFill = 0;
                SendFrame(send);
            }
            _resamplePos = i - count;
        }

        void SendFrame(bool send)
        {
            float level = Rms(_frame, Frame);
            LocalLevel = Mathf.Clamp01(level * DrawingConfig.VoiceGain * 6f);
            if (!send) return;
            // an open mic sends speech, not the room: a frame quieter than
            // the gate is dropped unless speech just stopped (no clipped ends)
            bool loud = level >= DrawingConfig.VoiceGate;
            if (loud) _speechUntil = Time.time + 0.4f;
            if (!loud && Time.time >= _speechUntil) return;

            var data = Adpcm.Encode(_frame, Frame);
            var cm = InstanceFinder.ClientManager;
            if (cm != null && cm.Started)
                cm.Broadcast(new NetSync.VoiceMsg
                {
                    Owner = NetSync.LocalOwnerId,
                    Ghost = GhostState.LocalIsGhost,
                    Data = data,
                }, Channel.Unreliable);
            if (loud)
            {
                LocalTalking = true;
                SwellLocal(level);
            }
        }

        static float Rms(float[] pcm, int count)
        {
            if (count == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < count; i++) sum += pcm[i] * pcm[i];
            return (float)System.Math.Sqrt(sum / count);
        }

        void SwellLocal(float level)
        {
            if (_localEyes == null || Time.time > _localEyesAt)
            {
                _localEyesAt = Time.time + 2f;
                foreach (var p in SimpleFPSController.All)
                    if (p != null && p.IsLocalViewer)
                    {
                        _localEyes = p.GetComponentInChildren<GooglyEyes>();
                        break;
                    }
            }
            Swell(_localEyes, level);
        }

        /// The one tell: eyes go a little wider the louder the voice, and
        /// settle right back. Held speech reads as eyes breathing with it.
        public static void Swell(GooglyEyes eyes, float level)
        {
            if (eyes == null) return;
            float k = Mathf.Clamp01(level * DrawingConfig.VoiceGain * 6f);
            eyes.Swell(0.2f, 1f + k * DrawingConfig.VoiceEyeSwell);
        }

        // ---- mute: by owner for the session, by Steam id across sessions ----
        public static bool IsMuted(int owner)
        {
            if (Muted.Contains(owner)) return true;
            var me = Instance;
            if (me._mutePersisted.TryGetValue(owner, out bool known)) return known;
            bool muted = NetSync.IdentityOf(owner - 1, out _, out ulong sid) && sid != 0UL
                && PlayerPrefs.GetInt("sz_mute_" + sid, 0) == 1;
            me._mutePersisted[owner] = muted;
            if (muted) Muted.Add(owner);
            return muted;
        }

        public static void SetMuted(int owner, bool muted)
        {
            if (muted) Muted.Add(owner); else Muted.Remove(owner);
            Instance._mutePersisted[owner] = muted;
            if (NetSync.IdentityOf(owner - 1, out _, out ulong sid) && sid != 0UL)
            {
                PlayerPrefs.SetInt("sz_mute_" + sid, muted ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// A relayed frame from another player, played from their avatar.
        public static void Receive(int owner, bool ghost, byte[] data)
        {
            if (owner == NetSync.LocalOwnerId) return;          // our own relay
            if (data == null || data.Length < 4) return;
            if (ghost && !GhostState.LocalIsGhost) return;       // spirits are heard only by spirits
            if (IsMuted(owner)) return;
            var me = Instance;

            var at = NetSync.AvatarTransformOf(owner);
            if (at == null) return;
            if (!me._speakers.TryGetValue(owner, out var sp) || sp == null)
            {
                sp = at.GetComponentInChildren<VoiceSpeaker>();
                if (sp == null) sp = at.gameObject.AddComponent<VoiceSpeaker>();
                sp.Setup(Rate);
                me._speakers[owner] = sp;
            }
            var pcm = Adpcm.Decode(data, out int n);
            if (n == 0) return;
            sp.Push(pcm, n);
            Swell(sp.Eyes, Rms(pcm, n));
        }
    }

    /// IMA ADPCM, 4 bits a sample: telephone-grade speech at 8 KB/s.
    /// Frame = 2 bytes predictor, 1 byte step index, 1 byte spare, then nibbles.
    public static class Adpcm
    {
        static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
            253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
            1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
            3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
            11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
            32767
        };
        static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        [System.ThreadStatic] static float[] _decodeBuf;

        public static byte[] Encode(float[] pcm, int count)
        {
            var outp = new byte[4 + (count + 1) / 2];
            int predictor = Mathf.RoundToInt(Mathf.Clamp(pcm[0], -1f, 1f) * 32767f);
            int index = 0;
            outp[0] = (byte)(predictor & 0xFF);
            outp[1] = (byte)((predictor >> 8) & 0xFF);
            outp[2] = (byte)index;
            for (int i = 0; i < count; i++)
            {
                int sample = Mathf.RoundToInt(Mathf.Clamp(pcm[i], -1f, 1f) * 32767f);
                int step = StepTable[index];
                int diff = sample - predictor;
                int nibble = 0;
                if (diff < 0) { nibble = 8; diff = -diff; }
                int delta = 0, temp = step;
                if (diff >= temp) { nibble |= 4; diff -= temp; delta += step; }
                temp >>= 1;
                if (diff >= temp) { nibble |= 2; diff -= temp; delta += temp; }
                temp >>= 1;
                if (diff >= temp) { nibble |= 1; delta += temp; }
                delta += step >> 3;
                predictor += (nibble & 8) != 0 ? -delta : delta;
                predictor = Mathf.Clamp(predictor, -32768, 32767);
                index = Mathf.Clamp(index + IndexTable[nibble], 0, 88);
                int at = 4 + i / 2;
                if ((i & 1) == 0) outp[at] = (byte)nibble;
                else outp[at] |= (byte)(nibble << 4);
            }
            return outp;
        }

        public static float[] Decode(byte[] data, out int count)
        {
            count = (data.Length - 4) * 2;
            if (_decodeBuf == null || _decodeBuf.Length < count) _decodeBuf = new float[Mathf.Max(count, 640)];
            var pcm = _decodeBuf;
            int predictor = (short)(data[0] | (data[1] << 8));
            int index = Mathf.Clamp(data[2], 0, 88);
            for (int i = 0; i < count; i++)
            {
                int nibble = (i & 1) == 0 ? data[4 + i / 2] & 0xF : (data[4 + i / 2] >> 4) & 0xF;
                int step = StepTable[index];
                int delta = step >> 3;
                if ((nibble & 4) != 0) delta += step;
                if ((nibble & 2) != 0) delta += step >> 1;
                if ((nibble & 1) != 0) delta += step >> 2;
                predictor += (nibble & 8) != 0 ? -delta : delta;
                predictor = Mathf.Clamp(predictor, -32768, 32767);
                index = Mathf.Clamp(index + IndexTable[nibble], 0, 88);
                pcm[i] = predictor / 32768f;
            }
            return pcm;
        }
    }

    /// One talking avatar: a 3D source fed a ring of decoded samples, and
    /// the eyes on that body for the tell.
    public class VoiceSpeaker : MonoBehaviour
    {
        AudioSource _src;
        float[] _ring;
        int _write, _read, _count, _rate;
        readonly object _lock = new object();
        GooglyEyes _eyes;
        float _eyesAt;

        public GooglyEyes Eyes
        {
            get
            {
                if (_eyes == null && Time.time > _eyesAt)
                {
                    _eyesAt = Time.time + 1f;
                    _eyes = GetComponentInChildren<GooglyEyes>();
                }
                return _eyes;
            }
        }

        public void Setup(int rate)
        {
            if (_src != null) return;
            _rate = rate;
            _ring = new float[rate * 3];
            _src = gameObject.AddComponent<AudioSource>();
            _src.spatialBlend = 1f;
            _src.rolloffMode = AudioRolloffMode.Linear;
            _src.minDistance = 1.5f;
            _src.maxDistance = DrawingConfig.VoiceRangeMeters;
            _src.dopplerLevel = 0f;
            _src.spread = 30f;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.clip = AudioClip.Create("voice", rate, 1, rate, true, OnRead);
            _src.Play();
        }

        public void Push(float[] pcm, int n)
        {
            lock (_lock)
            {
                // a small cushion against jitter, then skip ahead: voice must
                // stay live, a backlog would play every later word late
                int cushion = _rate * 3 / 10;
                if (_count + n > cushion)
                {
                    int drop = _count + n - _rate / 8;
                    if (drop > _count) drop = _count;
                    _read = (_read + drop) % _ring.Length;
                    _count -= drop;
                }
                for (int i = 0; i < n; i++)
                {
                    _ring[_write] = pcm[i];
                    _write = (_write + 1) % _ring.Length;
                    if (_count < _ring.Length) _count++;
                    else _read = (_read + 1) % _ring.Length;
                }
            }
        }

        void OnRead(float[] data)
        {
            lock (_lock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (_count > 0)
                    {
                        data[i] = _ring[_read];
                        _read = (_read + 1) % _ring.Length;
                        _count--;
                    }
                    else data[i] = 0f;
                }
            }
        }
    }
}
