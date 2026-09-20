using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SpellyZombie
{
    /// ★ ONE VIDEO FILE BEING WRITTEN, on a thread of its own (ClipRecorder feeds
    /// it). Pictures arrive with their place on a fixed frame grid; a missed
    /// place shows the picture before it again, so the file keeps a constant
    /// frame rate. Sound arrives as it is heard and waits until the picture has
    /// reached it, so the two are written side by side (Windows stalls a writer
    /// whose tracks drift more than about two seconds apart). Where no sound
    /// came there is quiet. No Unity calls in here.
    public sealed class ClipWriter
    {
        public const int MaxFrames = 6;      // picture buffers lent out at once: the encoder may fall this far behind
        public const int SoundBytesPerFrame = 4; // sound is 16-bit, two channels
        const double SoundLead = 0.05;       // sound is written no further ahead of the picture than this
        const double GapIsReal = 0.15;       // sound arriving this late left a hole, not jitter
        const double QuietAfter = 0.5;       // no sound for this long: quiet is written instead
        const double HoldAfter = 1.0;        // no picture for this long while sound waits: the last one holds

        public readonly string Path;
        public readonly int Width, Height, Fps, Bitrate, SoundRate; // SoundRate 0 = no sound track
        /// Pictures come as RGBA: red and blue are swapped before writing.
        public bool SwapRedBlue;
        public int FrameBytes => Width * Height * 4;

        /// Set by the writing thread: why it gave up (null = fine), and that the file is closed.
        public volatile string Error;
        public volatile bool Done;

        struct Item { public int Kind; public byte[] Data; public int Bytes; public long Index; public double Time; }
        const int KFrame = 0, KSound = 1, KFinish = 2;

        readonly ConcurrentQueue<Item> _queue = new ConcurrentQueue<Item>();
        readonly ConcurrentQueue<byte[]> _freeFrames = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> _freeSound = new ConcurrentQueue<byte[]>();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        int _framesMade;
        Thread _thread;

        public ClipWriter(string path, int width, int height, int fps, int bitrate, int soundRate)
        {
            Path = path; Width = width; Height = height; Fps = fps; Bitrate = bitrate; SoundRate = soundRate;
        }

        public void Start()
        {
            _thread = new Thread(Work) { IsBackground = true, Name = "ClipWriter" };
            _thread.Start();
        }

        /// Waits for the file to close (leaving the game, leaving play mode).
        public void Join(int milliseconds)
        {
            if (_thread != null && _thread.IsAlive) _thread.Join(milliseconds);
        }

        // ------------------------------------------------ the feeding side --
        /// Picture buffers still to be had; none = the encoder is behind, skip a picture.
        public int FreeFrames => _freeFrames.Count + (MaxFrames - _framesMade);

        /// A buffer for one picture (FrameBytes long), or null when all are out. One thread rents.
        public byte[] RentFrame()
        {
            if (_freeFrames.TryDequeue(out var b)) return b;
            if (_framesMade >= MaxFrames) return null;
            _framesMade++;
            return new byte[FrameBytes];
        }

        /// A rented picture that was not filled after all.
        public void ReturnFrame(byte[] frame) => _freeFrames.Enqueue(frame);

        /// A filled picture and its place on the frame grid.
        public void PushFrame(byte[] frame, long index)
        {
            _queue.Enqueue(new Item { Kind = KFrame, Data = frame, Index = index });
            _wake.Set();
        }

        /// A buffer for sound, at least this long. Any thread.
        public byte[] RentSound(int bytes)
        {
            while (_freeSound.TryDequeue(out var b))
                if (b.Length >= bytes) return b; // a shorter one (the block size changed) is let go
            return new byte[bytes];
        }

        /// 16-bit stereo as it was heard; `heardAt` = the clip's clock when the block came.
        public void PushSound(byte[] pcm, int bytes, double heardAt)
        {
            if (SoundRate <= 0) { _freeSound.Enqueue(pcm); return; }
            _queue.Enqueue(new Item { Kind = KSound, Data = pcm, Bytes = bytes, Time = heardAt });
            _wake.Set();
        }

        /// Everything handed in is written, the file runs to `stopTime` and closes.
        public void Finish(double stopTime)
        {
            _queue.Enqueue(new Item { Kind = KFinish, Time = stopTime });
            _wake.Set();
        }

        // ------------------------------------------------ the writing side --
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        MfMp4Writer _mp4;
        bool _sound;
        long _lastIndex = -1;     // the last place on the grid that is written
        byte[] _last;             // its picture, shown again where one is missed
        long _soundFrames;        // sound written so far
        double _lined;            // where the sound lined up so far ends
        readonly Queue<Item> _pending = new Queue<Item>(); // sound waiting for the picture; Time = where it starts
        byte[] _zeros;

        double PictureTime => (_lastIndex + 1) / (double)Fps;
        double SoundTime => _soundFrames / (double)SoundRate;

        void Work()
        {
            try
            {
                try { _mp4 = new MfMp4Writer(Path, Width, Height, Fps, Bitrate, SoundRate, true); }
                catch (Exception)
                {
                    // the graphics card's encoder refused: Windows' own does it
                    try { File.Delete(Path); } catch (Exception) { }
                    _mp4 = new MfMp4Writer(Path, Width, Height, Fps, Bitrate, SoundRate, false);
                }
                _sound = _mp4.HasAudio;
                if (_sound) _zeros = new byte[SoundRate / 4 * SoundBytesPerFrame];

                while (true)
                {
                    if (!_queue.TryDequeue(out var it))
                    {
                        _wake.WaitOne(50);
                        if (_queue.IsEmpty) HoldPicture();
                        continue;
                    }
                    if (it.Kind == KFrame) TakeFrame(it);
                    else if (it.Kind == KSound) { LineUp(it); WriteSound(false, double.MaxValue); HoldPicture(); }
                    else { Close(it.Time); break; }
                }
                if (_last == null) throw new InvalidOperationException("no picture came");
                _mp4.Finish();
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            finally
            {
                try { _mp4?.Dispose(); } catch (Exception) { }
                if (Error != null) try { File.Delete(Path); } catch (Exception) { }
                Done = true;
            }
        }

        void Put(byte[] picture, long index)
        {
            _mp4.WriteFrame(picture, index / (double)Fps);
            _lastIndex = index;
            WriteSound(false, double.MaxValue);
        }

        void TakeFrame(Item it)
        {
            if (it.Index <= _lastIndex) { _freeFrames.Enqueue(it.Data); return; } // its place was filled while it was on its way
            if (SwapRedBlue)
            {
                var px = it.Data;
                for (int i = 0, n = FrameBytes; i < n; i += 4) { byte r = px[i]; px[i] = px[i + 2]; px[i + 2] = r; }
            }
            // missed places show the picture before them; before the first, the first
            var hold = _last ?? it.Data;
            for (long j = _lastIndex + 1; j < it.Index; j++) Put(hold, j);
            Put(it.Data, it.Index);
            if (_last != null) _freeFrames.Enqueue(_last);
            _last = it.Data;
        }

        /// Sound takes its place on the timeline: right after what came before, or after quiet where a hole was left.
        void LineUp(Item it)
        {
            if (!_sound) { _freeSound.Enqueue(it.Data); return; }
            double length = it.Bytes / SoundBytesPerFrame / (double)SoundRate;
            if (it.Time - _lined > GapIsReal) Quiet(it.Time - _lined);
            it.Time = _lined;
            _lined += length;
            _pending.Enqueue(it);
        }

        /// Quiet lined up in quarter seconds, so it is let out bit by bit like any sound.
        void Quiet(double seconds)
        {
            long frames = (long)(seconds * SoundRate);
            while (frames > 0)
            {
                int n = (int)Math.Min(frames, SoundRate / 4);
                _pending.Enqueue(new Item { Kind = KSound, Data = null, Bytes = n * SoundBytesPerFrame, Time = _lined });
                _lined += n / (double)SoundRate;
                frames -= n;
            }
        }

        /// Writes the sound the picture has reached (`all` = whatever starts before `until`).
        void WriteSound(bool all, double until)
        {
            if (!_sound) return;
            double reach = all ? until : PictureTime + SoundLead;
            while (_pending.Count > 0 && _pending.Peek().Time < reach)
            {
                var it = _pending.Dequeue();
                _mp4.WriteSound(it.Data ?? _zeros, it.Bytes, SoundTime);
                _soundFrames += it.Bytes / SoundBytesPerFrame;
                if (it.Data != null) _freeSound.Enqueue(it.Data);
            }
            // no sound is coming (no listener yet, a scene changing): quiet keeps the tracks side by side
            if (!all && _pending.Count == 0 && PictureTime - SoundTime > QuietAfter)
            {
                Quiet(PictureTime - SoundTime - QuietAfter * 0.5);
                WriteSound(false, double.MaxValue);
            }
        }

        /// Sound waits for a picture that does not come (the game froze, no camera): the last one holds.
        void HoldPicture()
        {
            if (!_sound || _last == null) return;
            while (_pending.Count > 0 && _pending.Peek().Time > PictureTime + HoldAfter) Put(_last, _lastIndex + 1);
        }

        void Close(double stopTime)
        {
            if (_last == null) return;
            long final = (long)Math.Floor(stopTime * Fps);
            for (long j = _lastIndex + 1; j <= final; j++) Put(_last, j);
            if (!_sound) return;
            WriteSound(true, stopTime);
            if (stopTime - SoundTime > 0.01)
            {
                _pending.Clear();
                _lined = SoundTime;
                Quiet(stopTime - SoundTime);
                WriteSound(true, double.MaxValue);
            }
        }
#else
        void Work()
        {
            Error = "video is written by Windows only";
            Done = true;
        }
#endif
    }
}
