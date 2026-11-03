using System;
using Silk.NET.OpenAL;

namespace UwEditor.App
{
    /// <summary>
    /// The editor's sound output: one OpenAL source playing one 8-bit mono clip, through OpenAL
    /// Soft (Silk.NET.OpenAL.Soft.Native, LGPL, loaded at run time). Opened on the first Play.
    /// When the library or a device is missing (a server, the headless tests) Play returns why
    /// and nothing else happens. UWEDITOR_NO_AUDIO=1 turns the output off.
    /// </summary>
    public static unsafe class AudioOutput
    {
        static ALContext alc;
        static AL al;
        static Device* device;
        static Context* context;
        static uint source, buffer;
        static bool tried;
        static string failure;

        /// <summary>Why there is no output (null when there is one or it was not tried yet).</summary>
        public static string Failure { get { return failure; } }

        static bool Open()
        {
            if (tried) return failure == null;
            tried = true;
            if (Environment.GetEnvironmentVariable("UWEDITOR_NO_AUDIO") == "1") { failure = "Sound output is off (UWEDITOR_NO_AUDIO)."; return false; }
            try
            {
                alc = ALContext.GetApi(true);
                al = AL.GetApi(true);
                device = alc.OpenDevice("");
                if (device == null) { failure = "No sound device: OpenAL found no output to play on."; return false; }
                context = alc.CreateContext(device, null);
                if (context == null || !alc.MakeContextCurrent(context)) { failure = "No sound device: OpenAL could not start on it."; return false; }
                source = al.GenSource();
                return true;
            }
            catch (Exception e)
            {
                failure = "No sound output (OpenAL Soft could not be loaded: " + e.GetType().Name + ").";
                return false;
            }
        }

        /// <summary>Plays 8-bit unsigned mono samples at a rate, stopping what was playing. Returns null, or why nothing plays.</summary>
        public static string Play(byte[] samples, int rate)
        {
            if (samples == null || samples.Length == 0) return "There is nothing to play.";
            if (!Open()) return failure;
            try
            {
                Stop();
                al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                if (buffer != 0) { al.DeleteBuffer(buffer); buffer = 0; }
                buffer = al.GenBuffer();
                fixed (byte* p = samples) al.BufferData(buffer, BufferFormat.Mono8, p, samples.Length, Math.Max(1000, rate));
                al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
                al.SourcePlay(source);
                return null;
            }
            catch (Exception e) { return "Could not play: " + e.Message; }
        }

        /// <summary>Stops the clip playing, if any.</summary>
        public static void Stop()
        {
            if (!tried || failure != null) return;
            try { al.SourceStop(source); } catch (Exception) { }
        }

        /// <summary>A clip is playing.</summary>
        public static bool IsPlaying
        {
            get
            {
                if (!tried || failure != null) return false;
                try { int st; al.GetSourceProperty(source, GetSourceInteger.SourceState, out st); return st == (int)SourceState.Playing; }
                catch (Exception) { return false; }
            }
        }
    }
}
