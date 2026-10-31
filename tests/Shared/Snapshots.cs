using System;
using System.IO;

namespace UwEditor.TestSupport
{
    /// <summary>A picture as 8-bit RGBA, top row first.</summary>
    public sealed class RawImage
    {
        public readonly int Width, Height;
        public readonly byte[] Rgba;
        public RawImage(int w, int h, byte[] rgba) { Width = w; Height = h; Rgba = rgba; }

        /// <summary>A rectangle of the picture.</summary>
        public RawImage Crop(int x, int y, int w, int h)
        {
            x = Math.Clamp(x, 0, Width); y = Math.Clamp(y, 0, Height);
            w = Math.Clamp(w, 0, Width - x); h = Math.Clamp(h, 0, Height - y);
            var r = new byte[w * h * 4];
            for (int row = 0; row < h; row++) Array.Copy(Rgba, ((y + row) * Width + x) * 4, r, row * w * 4, w * 4);
            return new RawImage(w, h, r);
        }

        /// <summary>How many different colours appear (up to a limit): a blank view has one or two.</summary>
        public int DistinctColours(int limit = 1000)
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i + 3 < Rgba.Length && seen.Count < limit; i += 4) seen.Add(Rgba[i] << 16 | Rgba[i + 1] << 8 | Rgba[i + 2]);
            return seen.Count;
        }
    }

    /// <summary>Reads and writes PNG files for one kind of test (Avalonia bitmaps, or ImageMagick).</summary>
    public interface IPngCodec
    {
        RawImage Load(string path);
        void Save(RawImage image, string path);
    }

    public enum SnapshotOutcome { Matched, Recorded, Different }

    public sealed class SnapshotResult
    {
        public SnapshotOutcome Outcome;
        public double DifferentFraction;
        public string BaselinePath, ActualPath, DiffPath;
        public string Message;
    }

    /// <summary>
    /// Compares a picture with its baseline: a pixel differs when any channel is off by more than
    /// the tolerance, and the pictures match when at most maxFraction of the pixels differ
    /// (software rendering and fonts move a few pixels between machines). A missing baseline,
    /// or UPDATE_SNAPSHOTS=1, records the picture as the new baseline. On a mismatch the actual
    /// picture and a diff (differing pixels in magenta) are written under TestResults/snapshots.
    /// Baselines hold game graphics, so they are made from your own game files and not committed.
    /// </summary>
    public static class Snapshots
    {
        public static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorFree.sln"))) dir = dir.Parent;
                return dir?.FullName ?? Directory.GetCurrentDirectory();
            }
        }

        public static SnapshotResult Check(string baselineDir, string name, RawImage actual, IPngCodec codec, double maxFraction = 0.01, int tolerance = 40)
        {
            var r = new SnapshotResult { BaselinePath = Path.Combine(baselineDir, name + ".png") };
            string outDir = Path.Combine(RepoRoot, "TestResults", "snapshots");
            Directory.CreateDirectory(outDir);
            r.ActualPath = Path.Combine(outDir, name + ".actual.png");
            codec.Save(actual, r.ActualPath);
            bool update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";
            if (update || !File.Exists(r.BaselinePath))
            {
                Directory.CreateDirectory(baselineDir);
                codec.Save(actual, r.BaselinePath);
                r.Outcome = SnapshotOutcome.Recorded;
                r.Message = "Baseline recorded: " + r.BaselinePath + ". Look at it; later runs compare against it.";
                return r;
            }
            var expected = codec.Load(r.BaselinePath);
            if (expected.Width != actual.Width || expected.Height != actual.Height)
            {
                r.Outcome = SnapshotOutcome.Different;
                r.DifferentFraction = 1;
                r.Message = string.Format("{0}: size {1}x{2}, baseline {3}x{4}. Actual: {5}", name, actual.Width, actual.Height, expected.Width, expected.Height, r.ActualPath);
                return r;
            }
            var diff = new byte[actual.Rgba.Length];
            int differing = 0, n = actual.Width * actual.Height;
            for (int p = 0; p < n; p++)
            {
                int k = p * 4;
                bool off = Math.Abs(actual.Rgba[k] - expected.Rgba[k]) > tolerance
                    || Math.Abs(actual.Rgba[k + 1] - expected.Rgba[k + 1]) > tolerance
                    || Math.Abs(actual.Rgba[k + 2] - expected.Rgba[k + 2]) > tolerance;
                if (off) { differing++; diff[k] = 255; diff[k + 1] = 0; diff[k + 2] = 255; }
                else { byte g = (byte)((actual.Rgba[k] + actual.Rgba[k + 1] + actual.Rgba[k + 2]) / 9); diff[k] = diff[k + 1] = diff[k + 2] = g; }
                diff[k + 3] = 255;
            }
            r.DifferentFraction = differing / (double)Math.Max(1, n);
            if (r.DifferentFraction <= maxFraction)
            {
                r.Outcome = SnapshotOutcome.Matched;
                File.Delete(r.ActualPath);
                return r;
            }
            r.DiffPath = Path.Combine(outDir, name + ".diff.png");
            codec.Save(new RawImage(actual.Width, actual.Height, diff), r.DiffPath);
            r.Outcome = SnapshotOutcome.Different;
            r.Message = string.Format("{0}: {1:P2} of the pixels differ (allowed {2:P2}). Actual: {3}  Diff: {4}  Baseline: {5}. If the change is intended, run with UPDATE_SNAPSHOTS=1.",
                name, r.DifferentFraction, maxFraction, r.ActualPath, r.DiffPath, r.BaselinePath);
            return r;
        }
    }
}
