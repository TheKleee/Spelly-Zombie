#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

namespace SpellyZombie
{
    /// An MP4 (H.264 picture, AAC sound) written by Windows' own Media
    /// Foundation, so nothing is shipped with the game. COM objects are plain
    /// pointers and their methods are called through the vtable: no COM
    /// marshalling, the same under Mono and IL2CPP. Slots and GUIDs are from the
    /// Windows SDK headers (mfobjects.h, mfreadwrite.h, mfapi.h). One thread
    /// makes, feeds and closes a writer.
    public sealed class MfMp4Writer : IDisposable
    {
        [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IntPtr attrs, uint initialSize);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr type);
        [DllImport("mfplat.dll")] static extern int MFCreateSample(out IntPtr sample);
        [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coInit);
        [DllImport("ole32.dll")] static extern void CoUninitialize();

        const uint MF_VERSION = 0x00020070;
        const uint COINIT_MULTITHREADED = 0;

        static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        static Guid MF_MT_MPEG2_PROFILE = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        static Guid MF_MT_YUV_MATRIX = new Guid("3e23d450-2c75-4d25-a00e-b91670d12327");
        static Guid MF_MT_VIDEO_PRIMARIES = new Guid("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
        static Guid MF_MT_TRANSFER_FUNCTION = new Guid("5fb0fce9-be5c-4935-a811-ec838f8eed93");
        static Guid MF_MT_VIDEO_NOMINAL_RANGE = new Guid("c21b8ee5-b956-4071-8daf-325edf5cab11");
        static Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        static Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        static Guid MFAudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");
        static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetUINT32Fn(IntPtr self, ref Guid key, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetUINT64Fn(IntPtr self, ref Guid key, ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetGUIDFn(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AddStreamFn(IntPtr self, IntPtr mediaType, out uint streamIndex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetInputMediaTypeFn(IntPtr self, uint streamIndex, IntPtr mediaType, IntPtr encodingParameters);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NoArgsFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int WriteSampleFn(IntPtr self, uint streamIndex, IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetInt64Fn(IntPtr self, long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AddBufferFn(IntPtr self, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int LockFn(IntPtr self, out IntPtr data, out uint maxLength, out uint currentLength);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetCurrentLengthFn(IntPtr self, uint length);

        const int Slot_Release = 2;
        const int Attr_SetUINT32 = 21, Attr_SetUINT64 = 22, Attr_SetGUID = 24;
        const int Sample_SetSampleTime = 36, Sample_SetSampleDuration = 38, Sample_AddBuffer = 42;
        const int Buffer_Lock = 3, Buffer_Unlock = 4, Buffer_SetCurrentLength = 6;
        const int Writer_AddStream = 3, Writer_SetInputMediaType = 4, Writer_BeginWriting = 5,
            Writer_WriteSample = 6, Writer_Finalize = 11;

        static T Method<T>(IntPtr obj, int slot) where T : Delegate
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size));
        }

        static void Release(IntPtr obj)
        {
            if (obj != IntPtr.Zero) Method<ReleaseFn>(obj, Slot_Release)(obj);
        }

        static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException(what + " failed: 0x" + hr.ToString("X8"));
        }

        static void Set(IntPtr attrs, Guid key, uint value) => Check(Method<SetUINT32Fn>(attrs, Attr_SetUINT32)(attrs, ref key, value), "SetUINT32");
        static void Set(IntPtr attrs, Guid key, uint hi, uint lo) => Check(Method<SetUINT64Fn>(attrs, Attr_SetUINT64)(attrs, ref key, ((ulong)hi << 32) | lo), "SetUINT64");
        static void Set(IntPtr attrs, Guid key, Guid value) => Check(Method<SetGUIDFn>(attrs, Attr_SetGUID)(attrs, ref key, ref value), "SetGUID");

        IntPtr _writer;
        uint _video, _audio;
        bool _hasAudio, _started, _com, _begun;
        readonly int _width, _height, _fps, _audioRate;
        WriteSampleFn _write;

        /// Sound is always two channels of 16-bit samples.
        public const int SoundBytesPerFrame = 4;
        public bool HasAudio => _hasAudio;

        /// Opens the file. `audioRate` 0 = no sound; Windows' AAC encoder takes
        /// 44100 or 48000 Hz. Width and height must be even.
        public MfMp4Writer(string path, int width, int height, int fps, int videoBitrate, int audioRate, bool hardware)
        {
            _width = width; _height = height; _fps = fps; _audioRate = audioRate;
            // S_OK or S_FALSE is ours to undo; another apartment (RPC_E_CHANGED_MODE) is left alone
            _com = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED) >= 0;
            IntPtr attrs = IntPtr.Zero, outType = IntPtr.Zero, inType = IntPtr.Zero;
            try
            {
                Check(MFStartup(MF_VERSION, 0), "MFStartup");
                _started = true;
                Check(MFCreateAttributes(out attrs, 2), "MFCreateAttributes");
                if (hardware) Set(attrs, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1u);
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out _writer), "MFCreateSinkWriterFromURL");

                // what is written: H.264, its colours named (HD video: BT.709, 16..235) so every player agrees
                Check(MFCreateMediaType(out outType), "MFCreateMediaType");
                Set(outType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
                Set(outType, MF_MT_SUBTYPE, MFVideoFormat_H264);
                Set(outType, MF_MT_AVG_BITRATE, (uint)videoBitrate);
                Set(outType, MF_MT_INTERLACE_MODE, 2u); // progressive
                Set(outType, MF_MT_FRAME_SIZE, (uint)width, (uint)height);
                Set(outType, MF_MT_FRAME_RATE, (uint)fps, 1u);
                Set(outType, MF_MT_PIXEL_ASPECT_RATIO, 1u, 1u);
                Set(outType, MF_MT_MPEG2_PROFILE, 100u); // High
                Set(outType, MF_MT_YUV_MATRIX, 1u);
                Set(outType, MF_MT_VIDEO_PRIMARIES, 2u);
                Set(outType, MF_MT_TRANSFER_FUNCTION, 5u);
                Set(outType, MF_MT_VIDEO_NOMINAL_RANGE, 2u);
                Check(Method<AddStreamFn>(_writer, Writer_AddStream)(_writer, outType, out _video), "AddStream(video)");
                Release(outType); outType = IntPtr.Zero;

                // what is handed in: BGRA, bottom row first (RGB32's own layout, and a Unity readback's)
                Check(MFCreateMediaType(out inType), "MFCreateMediaType");
                Set(inType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
                Set(inType, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
                Set(inType, MF_MT_INTERLACE_MODE, 2u);
                Set(inType, MF_MT_FRAME_SIZE, (uint)width, (uint)height);
                Set(inType, MF_MT_FRAME_RATE, (uint)fps, 1u);
                Set(inType, MF_MT_PIXEL_ASPECT_RATIO, 1u, 1u);
                Check(Method<SetInputMediaTypeFn>(_writer, Writer_SetInputMediaType)(_writer, _video, inType, IntPtr.Zero), "SetInputMediaType(video)");
                Release(inType); inType = IntPtr.Zero;

                if (audioRate > 0)
                {
                    Check(MFCreateMediaType(out outType), "MFCreateMediaType");
                    Set(outType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                    Set(outType, MF_MT_SUBTYPE, MFAudioFormat_AAC);
                    Set(outType, MF_MT_AUDIO_BITS_PER_SAMPLE, 16u);
                    Set(outType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioRate);
                    Set(outType, MF_MT_AUDIO_NUM_CHANNELS, 2u);
                    Set(outType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000u); // 192 kbit/s
                    Check(Method<AddStreamFn>(_writer, Writer_AddStream)(_writer, outType, out _audio), "AddStream(audio)");
                    Release(outType); outType = IntPtr.Zero;

                    Check(MFCreateMediaType(out inType), "MFCreateMediaType");
                    Set(inType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                    Set(inType, MF_MT_SUBTYPE, MFAudioFormat_PCM);
                    Set(inType, MF_MT_AUDIO_BITS_PER_SAMPLE, 16u);
                    Set(inType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioRate);
                    Set(inType, MF_MT_AUDIO_NUM_CHANNELS, 2u);
                    Set(inType, MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)SoundBytesPerFrame);
                    Set(inType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(audioRate * SoundBytesPerFrame));
                    Check(Method<SetInputMediaTypeFn>(_writer, Writer_SetInputMediaType)(_writer, _audio, inType, IntPtr.Zero), "SetInputMediaType(audio)");
                    Release(inType); inType = IntPtr.Zero;
                    _hasAudio = true;
                }

                Check(Method<NoArgsFn>(_writer, Writer_BeginWriting)(_writer), "BeginWriting");
                _begun = true;
                _write = Method<WriteSampleFn>(_writer, Writer_WriteSample);
            }
            catch
            {
                Release(outType); Release(inType);
                Dispose();
                throw;
            }
            finally { Release(attrs); }
        }

        /// One picture: width*height*4 bytes of BGRA, bottom row first, shown from `time` (seconds).
        public void WriteFrame(byte[] bgra, double time) =>
            WriteBuffer(_video, bgra, _width * _height * 4, time, 1.0 / _fps);

        /// Interleaved 16-bit stereo; `time` = where its first sample sits.
        public void WriteSound(byte[] pcm, int byteCount, double time)
        {
            if (!_hasAudio || byteCount <= 0) return;
            WriteBuffer(_audio, pcm, byteCount, time, byteCount / (double)(_audioRate * SoundBytesPerFrame));
        }

        void WriteBuffer(uint stream, byte[] bytes, int count, double time, double duration)
        {
            IntPtr buffer = IntPtr.Zero, sample = IntPtr.Zero;
            try
            {
                Check(MFCreateMemoryBuffer((uint)count, out buffer), "MFCreateMemoryBuffer");
                Check(Method<LockFn>(buffer, Buffer_Lock)(buffer, out IntPtr dest, out _, out _), "Lock");
                Marshal.Copy(bytes, 0, dest, count);
                Check(Method<NoArgsFn>(buffer, Buffer_Unlock)(buffer), "Unlock");
                Check(Method<SetCurrentLengthFn>(buffer, Buffer_SetCurrentLength)(buffer, (uint)count), "SetCurrentLength");
                Check(MFCreateSample(out sample), "MFCreateSample");
                Check(Method<AddBufferFn>(sample, Sample_AddBuffer)(sample, buffer), "AddBuffer");
                Check(Method<SetInt64Fn>(sample, Sample_SetSampleTime)(sample, (long)(time * 10000000.0)), "SetSampleTime");
                Check(Method<SetInt64Fn>(sample, Sample_SetSampleDuration)(sample, (long)(duration * 10000000.0)), "SetSampleDuration");
                Check(_write(_writer, stream, sample), "WriteSample");
            }
            finally { Release(sample); Release(buffer); }
        }

        /// Closes the file so it plays. Throws when Windows could not finish it.
        public void Finish()
        {
            if (_writer == IntPtr.Zero || !_begun) return;
            _begun = false;
            Check(Method<NoArgsFn>(_writer, Writer_Finalize)(_writer), "Finalize");
        }

        public void Dispose()
        {
            if (_writer != IntPtr.Zero) { Release(_writer); _writer = IntPtr.Zero; }
            if (_started) { MFShutdown(); _started = false; }
            if (_com) { CoUninitialize(); _com = false; }
        }
    }
}
#endif
