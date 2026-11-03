using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Sound;

namespace UwEditor.Core.Sound
{
    /// <summary>What a .VOC file is for: it decides the size limit and the rate the game uses.</summary>
    public enum SoundUse
    {
        /// <summary>UW1 SOUND\nn.VOC: the introduction's speech.</summary>
        Uw1Speech,
        /// <summary>UW2 SOUND\BSPnn.VOC: cutscene speech, streamed.</summary>
        Uw2Speech,
        /// <summary>UW2 SOUND\SPnn.VOC: a digitised effect.</summary>
        Uw2Effect,
        /// <summary>UW2 SOUND\UWnn.VOC: a sound played by a special-effects trap.</summary>
        Uw2Trap,
    }

    /// <summary>The limits the games' code puts on a .VOC (AUDIO-STUDY section 2).</summary>
    public static class SoundLimits
    {
        /// <summary>UW1's speech: six 16 KB EMS pages are read, the rest of the file is never played (uw1/src/sound/SOUND.C play_speech).</summary>
        public const int Uw1SpeechFile = 6 * 0x4000;
        /// <summary>
        /// UW2's digitised effects and trap sounds: the cache has four 16 KB pages and a file
        /// takes (size >> 14) + 1 of them, so it must be under 65,536 bytes (uw2/src/sound/SOUND.C
        /// digi_fx_play). A file that does not fit is not played and big ones evict the others.
        /// </summary>
        public const int Uw2EffectFile = 4 * 0x4000 - 1;
        /// <summary>UW2's cutscene speech is streamed through four pages (gfx/CUTS.C big_speech_play): no limit but the .VOC block's 24-bit length.</summary>
        public const int Uw2SpeechFile = 0xFFFFFF;
        /// <summary>The Sound Blaster's ordinary DMA mode tops out near 22–23 kHz.</summary>
        public const int MaxRate = 22050;
        /// <summary>The lowest rate the editor lowers a clip to so that it fits.</summary>
        public const int MinRate = 4000;
        /// <summary>UW1's rate: 12048 Hz (time constant 0xAD).</summary>
        public const int Uw1TimeConstant = 0xAD;
        /// <summary>UW2's rate: 11111 Hz (time constant 0xA6).</summary>
        public const int Uw2TimeConstant = 0xA6;

        /// <summary>The largest file the game plays whole.</summary>
        public static int MaxFileBytes(SoundUse use)
        {
            switch (use)
            {
                case SoundUse.Uw1Speech: return Uw1SpeechFile;
                case SoundUse.Uw2Speech: return Uw2SpeechFile;
                default: return Uw2EffectFile;
            }
        }

        /// <summary>The most samples a game-shaped file (32 bytes before them, a terminator after) can hold.</summary>
        public static int MaxSamples(SoundUse use) { return MaxFileBytes(use) - VocFile.Overhead; }

        public static bool HasLimit(SoundUse use) { return use != SoundUse.Uw2Speech; }

        public static int GameTimeConstant(SoundUse use) { return use == SoundUse.Uw1Speech ? Uw1TimeConstant : Uw2TimeConstant; }

        /// <summary>"at most 98,304 bytes (8.2 s at 12048 Hz)" and the like.</summary>
        public static string Describe(SoundUse use)
        {
            if (!HasLimit(use)) return "any length (streamed)";
            double s = MaxSamples(use) / VocFile.RateOfExact(GameTimeConstant(use));
            return use == SoundUse.Uw1Speech
                ? string.Format("at most {0:N0} bytes ({1:0.0} s at {2} Hz)", MaxFileBytes(use), s, VocFile.RateOf(GameTimeConstant(use)))
                : string.Format("under {0:N0} bytes ({1:0.0} s at {2} Hz)", MaxFileBytes(use) + 1, s, VocFile.RateOf(GameTimeConstant(use)));
        }

        /// <summary>Why a file of this length breaks the limit, or null when it fits.</summary>
        public static string Check(SoundUse use, int fileBytes)
        {
            if (fileBytes <= MaxFileBytes(use)) return null;
            if (use == SoundUse.Uw1Speech) return string.Format("{0:N0} bytes: the game reads only the first {1:N0}, the rest is never played", fileBytes, MaxFileBytes(use));
            if (use == SoundUse.Uw2Speech) return string.Format("{0:N0} bytes: too long for one .VOC block", fileBytes);
            return string.Format("{0:N0} bytes: does not fit the game's cache of four 16 KB pages (it must be under 65,536), so it is not played", fileBytes);
        }
    }

    /// <summary>What to do with a clip that is over the limit.</summary>
    public enum OverLimit { Refuse, Shorten, LowerRate }

    /// <summary>The settings of a WAV → VOC conversion.</summary>
    public sealed class SoundConvertOptions
    {
        /// <summary>The time constant of the rate to write; -1: the game's own (12048 Hz in UW1, 11111 Hz in UW2).</summary>
        public int TimeConstant = -1;
        /// <summary>Raise (or lower) the loudest sample to 98 % of full scale.</summary>
        public bool Normalise = true;
        /// <summary>Cut quiet stretches at the start and the end (below 1 % of the peak), keeping 20 ms.</summary>
        public bool TrimSilence = true;
        /// <summary>TPDF dither when going to 8 bits.</summary>
        public bool Dither = true;
        public OverLimit OverLimit = OverLimit.Refuse;
        /// <summary>The dither's random seed (the same input gives the same file).</summary>
        public int Seed = 12345;
    }

    /// <summary>The outcome of a conversion: the game-shaped .VOC or why there is none.</summary>
    public sealed class SoundConvertResult
    {
        public bool Ok;
        /// <summary>Why the clip was refused.</summary>
        public string Problem;
        /// <summary>Refused because it is over the file's limit (shortening it would make it fit).</summary>
        public bool TooLong;
        public byte[] Samples;
        public int TimeConstant;
        public double Rate;
        public double SourceSeconds;
        public double Seconds { get { return Samples == null || Rate <= 0 ? 0 : Samples.Length / Rate; } }
        public readonly List<string> Notes = new List<string>();
        /// <summary>The .VOC file the game plays (null when refused).</summary>
        public byte[] Voc { get { return Samples == null ? null : VocFile.FromSamples(Samples, TimeConstant).ToBytes(); } }
        public string Summary
        {
            get
            {
                if (!Ok) return Problem;
                return string.Format("{0:0.00} s at {1:0} Hz, {2:N0} bytes", Seconds, Rate, Samples.Length + VocFile.Overhead) + (Notes.Count > 0 ? "; " + string.Join("; ", Notes) : "") + ".";
            }
        }
    }

    /// <summary>
    /// WAV → the games' .VOC: mixed down to mono, silence trimmed, resampled with a
    /// Blackman-windowed sinc (16 zero crossings, cut-off at 95 % of the lower Nyquist
    /// frequency), normalised, and dithered (TPDF) to 8-bit unsigned; then checked against the
    /// game's limit for that file, and refused, shortened or lowered in rate to fit.
    /// </summary>
    public static class SoundConversion
    {
        const int ZeroCrossings = 16, TableSteps = 512;
        static readonly float[] Kernel = BuildKernel();

        static float[] BuildKernel()
        {
            var k = new float[ZeroCrossings * TableSteps + 2];
            for (int i = 0; i < k.Length; i++)
            {
                double v = (double)i / TableSteps;                 // distance in zero crossings
                if (v >= ZeroCrossings) { k[i] = 0; continue; }
                double sinc = v == 0 ? 1 : Math.Sin(Math.PI * v) / (Math.PI * v);
                double z = v / ZeroCrossings;                      // 0..1 across the window
                double w = 0.42 + 0.5 * Math.Cos(Math.PI * z) + 0.08 * Math.Cos(2 * Math.PI * z);
                k[i] = (float)(sinc * w);
            }
            return k;
        }

        /// <summary>Resamples from one rate to another (windowed sinc, band-limited to the lower rate).</summary>
        public static float[] Resample(float[] x, double from, double to)
        {
            if (x.Length == 0) return new float[0];
            if (Math.Abs(from - to) < 1e-6) return (float[])x.Clone();
            double ratio = to / from;
            double g = Math.Min(1.0, ratio) * 0.95;               // the cut-off, as a fraction of the source's Nyquist
            double half = ZeroCrossings / g;                      // the kernel's half width in source samples
            int n = (int)Math.Floor(x.Length * ratio);
            var y = new float[Math.Max(1, n)];
            for (int i = 0; i < y.Length; i++)
            {
                double t = i / ratio;
                int a = (int)Math.Ceiling(t - half), b = (int)Math.Floor(t + half);
                if (a < 0) a = 0;
                if (b > x.Length - 1) b = x.Length - 1;
                double s = 0;
                for (int k = a; k <= b; k++)
                {
                    double v = Math.Abs(t - k) * g * TableSteps;
                    int vi = (int)v;
                    if (vi >= Kernel.Length - 1) continue;
                    double f = v - vi;
                    s += x[k] * (Kernel[vi] + (Kernel[vi + 1] - Kernel[vi]) * f);
                }
                y[i] = (float)(s * g);
            }
            return y;
        }

        /// <summary>Cuts the quiet start and end (below 1 % of the peak), keeping margin seconds of each.</summary>
        public static float[] Trim(float[] x, double rate, double margin = 0.02)
        {
            float peak = Peak(x);
            if (peak <= 0) return new float[0];
            float th = Math.Max(peak * 0.01f, 1f / 512);
            int a = 0, b = x.Length - 1;
            while (a < x.Length && Math.Abs(x[a]) < th) a++;
            while (b > a && Math.Abs(x[b]) < th) b--;
            int m = (int)(margin * rate);
            a = Math.Max(0, a - m); b = Math.Min(x.Length - 1, b + m);
            if (a == 0 && b == x.Length - 1) return x;
            var r = new float[b - a + 1];
            Array.Copy(x, a, r, 0, r.Length);
            return r;
        }

        public static float Peak(float[] x) { float p = 0; foreach (var v in x) { float a = Math.Abs(v); if (a > p) p = a; } return p; }

        /// <summary>To 8-bit unsigned (0x80 the middle), with TPDF dither of ±1 step when dither is set.</summary>
        public static byte[] To8Bit(float[] x, bool dither, int seed)
        {
            var r = new Random(seed);
            var o = new byte[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i] * 128.0 + 128.0;
                if (dither) v += r.NextDouble() + r.NextDouble() - 1.0;
                int q = (int)Math.Round(v);
                o[i] = (byte)(q < 0 ? 0 : q > 255 ? 255 : q);
            }
            return o;
        }

        /// <summary>Converts a WAV file for a use, with the options; the result says whether it fits and what was done.</summary>
        public static SoundConvertResult Convert(WavFile wav, SoundUse use, SoundConvertOptions opt)
        {
            var res = new SoundConvertResult();
            float[] mono = wav.Mono();
            res.SourceSeconds = wav.Seconds;
            if (wav.ChannelCount > 1) res.Notes.Add("mixed " + wav.ChannelCount + " channels to mono");
            if (opt.TrimSilence)
            {
                int before = mono.Length;
                mono = Trim(mono, wav.SampleRate);
                if (mono.Length < before) res.Notes.Add(string.Format("trimmed {0:0.00} s of silence", (before - mono.Length) / (double)wav.SampleRate));
            }
            if (mono.Length == 0) { res.Problem = "The WAV file holds only silence."; return res; }
            int tc = opt.TimeConstant >= 0 ? opt.TimeConstant : SoundLimits.GameTimeConstant(use);
            if (VocFile.RateOfExact(tc) > SoundLimits.MaxRate + 200) { res.Problem = string.Format("{0:0} Hz is above what the Sound Blaster plays in the games' mode (about 22 kHz).", VocFile.RateOfExact(tc)); return res; }
            int max = SoundLimits.MaxSamples(use);
            double seconds = mono.Length / (double)wav.SampleRate;
            double rate = VocFile.RateOfExact(tc);
            if (seconds * rate > max)
            {
                if (opt.OverLimit == OverLimit.LowerRate)
                {
                    int ntc = VocFile.TimeConstantFor(max / seconds, max / seconds);
                    while (ntc > 0 && seconds * VocFile.RateOfExact(ntc) > max) ntc--;
                    if (VocFile.RateOfExact(ntc) < SoundLimits.MinRate)
                    {
                        res.Problem = string.Format("{0:0.00} s does not fit {1} even at {2} Hz: shorten it.", seconds, SoundLimits.Describe(use), SoundLimits.MinRate);
                        res.TooLong = true;
                        return res;
                    }
                    res.Notes.Add(string.Format("rate lowered to {0:0} Hz to fit", VocFile.RateOfExact(ntc)));
                    tc = ntc; rate = VocFile.RateOfExact(tc);
                }
                else if (opt.OverLimit == OverLimit.Refuse)
                {
                    res.Problem = string.Format("{0:0.00} s at {1:0} Hz is {2:N0} bytes: over the limit for this file, {3}. Shorten it, lower the rate, or choose \"shorten\" or \"lower the rate\".",
                        seconds, rate, (long)Math.Ceiling(seconds * rate) + VocFile.Overhead, SoundLimits.Describe(use));
                    res.TooLong = true;
                    return res;
                }
            }
            float[] y = Resample(mono, wav.SampleRate, rate);
            if (opt.Normalise)
            {
                float p = Peak(y);
                if (p > 0) { float k = 0.98f / p; for (int i = 0; i < y.Length; i++) y[i] *= k; res.Notes.Add(string.Format("normalised ({0:+0.0;-0.0} dB)", 20 * Math.Log10(k))); }
            }
            if (y.Length > max)
            {
                res.Notes.Add(string.Format("shortened from {0:0.00} s to {1:0.00} s to fit", y.Length / rate, max / rate));
                Array.Resize(ref y, max);
                int fade = Math.Min(y.Length, (int)(0.02 * rate));
                for (int i = 0; i < fade; i++) y[y.Length - 1 - i] *= (float)i / fade;
            }
            res.Samples = To8Bit(y, opt.Dither, opt.Seed);
            res.TimeConstant = tc;
            res.Rate = rate;
            res.Ok = true;
            return res;
        }

        /// <summary>A .VOC's sound as an 8-bit mono WAV file at its rate.</summary>
        public static byte[] VocToWav(byte[] voc)
        {
            var v = VocFile.Parse(voc);
            return WavFile.FromUnsigned8(v.Samples8(), Math.Max(1, v.SampleRate)).ToBytes(8);
        }
    }
}
