using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.App
{
    public sealed class Thumb : Observable
    {
        Bitmap bitmap;
        string label;
        public int Index { get; set; }
        public Bitmap Bitmap { get { return bitmap; } set { Set(ref bitmap, value); } }
        public string Label { get { return label; } set { Set(ref label, value); } }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>The state of the graphics window: picture files, critter art and cutscenes.</summary>
    public sealed class GraphicsViewModel : Observable
    {
        public GameSession Session { get; }
        public Palettes Palettes { get; }
        string status = "";

        public GraphicsViewModel(GameSession s)
        {
            Session = s;
            Palettes = Palettes.Load(s.DataFolder);
            PaletteNames = Enumerable.Range(0, Palettes.Main.Count).Select(i => "Palette " + i).ToList();
            foreach (var f in PictureFile.Find(s.DataFolder)) Files.Add(Path.GetFileName(f));
        }

        public string Status { get { return status; } set { Set(ref status, value); } }
        public List<string> PaletteNames { get; }

        // ---- pictures

        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        public ObservableCollection<Thumb> PictureThumbs { get; } = new ObservableCollection<Thumb>();
        /// <summary>The session's shared picture files (one copy per file for every window).</summary>
        IEnumerable<PictureFile> openFiles { get { return Session.Pictures.Open; } }
        string fileName;
        Thumb pictureThumb;
        int palette;
        Bitmap preview;
        string pictureInfo = "";
        bool nearest;

        public PictureFile File { get; private set; }
        public IndexedImage Picture { get; private set; }

        public string FileName
        {
            get { return fileName; }
            set
            {
                if (!Set(ref fileName, value)) return;
                File = null;
                PictureThumbs.Clear();
                if (value == null) return;
                try
                {
                    PictureFile f;
                    f = Session.Pictures.Get(value);
                    if (f == null) throw new FileNotFoundException("No " + value + " in DATA.");
                    File = f;
                    palette = f.DefaultPalette(0);
                    Raise(nameof(PaletteIndex));
                    FillThumbs();
                    Status = string.Format("{0}: {1} picture{2}.", f.Name, f.Count, f.Count == 1 ? "" : "s");
                }
                catch (Exception ex) { Status = value + ": " + ex.Message; }
                PictureThumb = PictureThumbs.FirstOrDefault(t => t.Bitmap != null);
            }
        }

        void FillThumbs()
        {
            PictureThumbs.Clear();
            for (int i = 0; i < File.Count; i++)
            {
                var img = File.Get(i, Palettes, palette);
                double scale = img == null ? 1 : Math.Min(1.0, 64.0 / Math.Max(img.Width, img.Height)) * (Math.Max(img.Width, img.Height) <= 24 ? 2 : 1);
                PictureThumbs.Add(new Thumb
                {
                    Index = i, Bitmap = Pictures.ToBitmap(img), Label = i + (File.IsChanged(i) ? " ●" : ""),
                    Width = img == null ? 16 : Math.Max(8, img.Width * scale), Height = img == null ? 16 : Math.Max(8, img.Height * scale),
                });
            }
        }

        public Thumb PictureThumb
        {
            get { return pictureThumb; }
            set { if (Set(ref pictureThumb, value)) ShowPicture(); }
        }

        public int PaletteIndex
        {
            get { return palette; }
            set { if (value >= 0 && Set(ref palette, value) && File != null) { int keep = pictureThumb?.Index ?? 0; FillThumbs(); PictureThumb = PictureThumbs.ElementAtOrDefault(keep); } }
        }

        public bool Nearest { get { return nearest; } set { Set(ref nearest, value); } }
        public Bitmap Preview { get { return preview; } set { Set(ref preview, value); } }
        public string PictureInfo { get { return pictureInfo; } set { Set(ref pictureInfo, value); } }
        public bool HasPicture { get { return Picture != null; } }

        void ShowPicture()
        {
            Picture = File == null || pictureThumb == null ? null : File.Get(pictureThumb.Index, Palettes, palette);
            Preview = Pictures.ToBitmap(Picture);
            PictureInfo = File == null || pictureThumb == null ? "" : string.Format("{0} #{1}: {2}{3}", File.Name, pictureThumb.Index, File.Describe(pictureThumb.Index, Picture),
                Picture == null ? "" : string.Format(" · {0} colours it may use", Picture.Colors.Count(c => (c >> 24) != 0) + (Picture.Colors.Any(c => (c >> 24) == 0) ? " + transparent" : "")));
            Raise(nameof(HasPicture));
        }

        /// <summary>Imports a PNG over the selected picture. Errors refuse it; the result says why.</summary>
        public ImportResult ImportPicture(byte[] png)
        {
            if (Picture == null) return null;
            var r = PictureImport.FromPng(png, Picture, nearest);
            if (!r.Ok) { Status = "Not imported: " + r.Errors[0]; return r; }
            try { File.Replace(pictureThumb.Index, Picture, r.Pixels); }
            catch (Exception ex) { r.Errors.Add(ex.Message); Status = "Not imported: " + ex.Message; return r; }
            int i = pictureThumb.Index;
            var img = File.Get(i, Palettes, palette);
            pictureThumb.Bitmap = Pictures.ToBitmap(img);
            pictureThumb.Label = i + " ●";
            ShowPicture();
            Status = string.Format("Picture {0} replaced (not saved). {1}", i, string.Join(" ", r.Notes));
            return r;
        }

        /// <summary>Puts drawn pixels into the picture shown (the pixel editor); null, or why not.</summary>
        public string ReplacePicturePixels(int[] pixels)
        {
            if (Picture == null || pictureThumb == null) return "No picture is shown.";
            try { File.Replace(pictureThumb.Index, Picture, pixels); }
            catch (Exception ex) { return ex.Message; }
            int i = pictureThumb.Index;
            pictureThumb.Bitmap = Pictures.ToBitmap(File.Get(i, Palettes, palette));
            pictureThumb.Label = i + " ●";
            ShowPicture();
            Status = string.Format("Picture {0} drawn (not saved).", i);
            return null;
        }

        /// <summary>Puts drawn pixels into the critter frame shown; null, or why not.</summary>
        public string ReplaceFramePixels(int[] pixels)
        {
            var f = CurrentFrame;
            if (f == null || CritterImage == null) return "No frame is shown.";
            try { f.Item1.Replace(f.Item2, pixels); }
            catch (Exception ex) { return ex.Message; }
            int keep = frame;
            FillFrames();
            frame = -1; FrameIndex = keep;
            Status = string.Format("Frame {0} of {1} drawn (not saved).", f.Item2, f.Item1.Name);
            return null;
        }

        /// <summary>Puts drawn pixels into the cutscene frame shown; null, or why not.</summary>
        public string ReplaceCutPixels(int[] pixels)
        {
            if (Lpf == null || CutImage == null) return "No frame is shown.";
            try { Lpf.Replace(cutFrame, pixels); }
            catch (Exception ex) { return ex.Message; }
            ShowCut();
            Status = string.Format("Frame {0} of {1} drawn (not saved).", cutFrame + 1, Lpf.Name);
            return null;
        }

        public void RevertPicture()
        {
            if (File == null || pictureThumb == null) return;
            File.Revert(pictureThumb.Index);
            pictureThumb.Bitmap = Pictures.ToBitmap(File.Get(pictureThumb.Index, Palettes, palette));
            pictureThumb.Label = pictureThumb.Index.ToString();
            ShowPicture();
        }

        /// <summary>Replaced pictures or critter frames not yet written.</summary>
        public bool HasChanges { get { return openFiles.Any(f => f.HasChanges) || (critters != null && critters.Any(c => c.Pages.Any(p => p.HasChanges))); } }

        /// <summary>Writes every replaced picture and critter frame.</summary>
        public void SaveAll()
        {
            foreach (var f in openFiles.Where(f => f.HasChanges).ToList()) { if (f == File) SavePictures(); else Session.Pictures.Save(f); }
            if (critters != null) foreach (var c in critters) foreach (var p in c.Pages.Where(p => p.HasChanges).ToList()) p.Save();
        }

        public List<string> SavePictures()
        {
            if (File == null || !File.HasChanges) { Status = "Nothing to save."; return new List<string>(); }
            var notes = Session.Pictures.Save(File);
            FillThumbs();
            Status = "Saved " + File.Path + " (the old file is kept as .BKP). " + string.Join(" ", notes);
            return notes;
        }

        // ---- critters

        List<CritterFile> critters;
        public ObservableCollection<CritterFile> Critters { get; } = new ObservableCollection<CritterFile>();
        public ObservableCollection<CritterAnimation> Animations { get; } = new ObservableCollection<CritterAnimation>();
        public ObservableCollection<Thumb> FrameThumbs { get; } = new ObservableCollection<Thumb>();
        CritterFile critter;
        CritterAnimation animation;
        int frame, auxPalette;
        Bitmap critterPreview;
        string critterInfo = "";
        public ObservableCollection<string> AuxNames { get; } = new ObservableCollection<string>();

        public void LoadCritters()
        {
            if (critters != null) return;
            critters = CritterArt.Load(Session.GameFolder, Session.Kind, Session.Names);
            foreach (var c in critters) Critters.Add(c);
        }

        public CritterFile Critter
        {
            get { return critter; }
            set
            {
                if (!Set(ref critter, value)) return;
                Animations.Clear();
                if (value == null) return;
                foreach (var a in value.Animations) Animations.Add(a);
                int maxPal = value.Pages.Max(p => p.Palettes.Count);
                AuxNames.Clear();
                foreach (var n in Enumerable.Range(0, Math.Max(1, maxPal)).Select(i => "Palette " + i + (value.Users.Any(u => u.Item2 == i) ? " (" + string.Join(", ", value.Users.Where(u => u.Item2 == i).Select(u => Session.Names.Get(0x40 + u.Item1))) + ")" : "")))
                    AuxNames.Add(n);
                auxPalette = value.Users.Count > 0 ? value.Users[0].Item2 : 0;
                Raise(nameof(AuxPalette));
                Animation = Animations.Count > 1 ? Animations[1] : Animations.FirstOrDefault();
            }
        }

        public CritterAnimation Animation
        {
            get { return animation; }
            set { if (Set(ref animation, value)) { FillFrames(); FrameIndex = 0; ShowFrame(); } }
        }

        public int AuxPalette
        {
            get { return auxPalette; }
            set { if (value >= 0 && Set(ref auxPalette, value)) { FillFrames(); ShowFrame(); } }
        }

        void FillFrames()
        {
            FrameThumbs.Clear();
            if (animation == null) return;
            for (int i = 0; i < animation.Frames.Count; i++)
            {
                var f = animation.Frames[i];
                var img = f.Item1.Image(f.Item2, auxPalette, Palettes);
                FrameThumbs.Add(new Thumb { Index = i, Bitmap = Pictures.ToBitmap(img), Label = f.Item2 + (f.Item1.IsChanged(f.Item2) ? " ●" : ""), Width = img?.Width ?? 8, Height = img?.Height ?? 8 });
            }
            Raise(nameof(FrameMax));
        }

        public int FrameMax { get { return animation == null ? 0 : Math.Max(0, animation.Frames.Count - 1); } }
        public int FrameIndex
        {
            get { return frame; }
            set { int v = Math.Max(0, Math.Min(FrameMax, value)); if (Set(ref frame, v)) ShowFrame(); }
        }
        public Bitmap CritterPreview { get { return critterPreview; } set { Set(ref critterPreview, value); } }
        public string CritterInfo { get { return critterInfo; } set { Set(ref critterInfo, value); } }
        public IndexedImage CritterImage { get; private set; }
        /// <summary>The palette entries the shown critter frame's colours stand for (its auxiliary palette), or null.</summary>
        public int[] CritterColourMap
        {
            get
            {
                var f = CurrentFrame;
                if (f == null || CritterImage == null) return null;
                var pals = f.Item1.Palettes;
                return auxPalette >= 0 && auxPalette < pals.Count ? pals[auxPalette].Take(CritterImage.Colors.Length).ToArray() : null;
            }
        }
        Tuple<CritterPage, int> CurrentFrame { get { return animation == null || frame >= animation.Frames.Count ? null : animation.Frames[frame]; } }

        void ShowFrame()
        {
            var f = CurrentFrame;
            CritterImage = f == null ? null : f.Item1.Image(f.Item2, auxPalette, Palettes);
            CritterPreview = Pictures.ToBitmap(CritterImage);
            if (f == null) { CritterInfo = ""; return; }
            var fr = f.Item1.Frames[f.Item2];
            CritterInfo = string.Format("{0}, frame {1}: {2} × {3}, hot spot ({4}, {5}), {6} · the page is {7} bytes; the game reads at most {8}",
                f.Item1.Name, f.Item2, fr.Width, fr.Height, fr.HotX, fr.HotY, fr.Format == 6 ? "5-bit run-length, 32 colours" : "4-bit run-length, 16 colours",
                f.Item1.Data.Length, CritterPage.MaxBytes);
        }

        public void NextFrame() { if (animation != null && animation.Frames.Count > 0) FrameIndex = (frame + 1) % animation.Frames.Count; }

        public ImportResult ImportFrame(byte[] png)
        {
            var f = CurrentFrame;
            if (f == null || CritterImage == null) return null;
            var r = PictureImport.FromPng(png, CritterImage, nearest);
            if (!r.Ok) { Status = "Not imported: " + r.Errors[0]; return r; }
            try { f.Item1.Replace(f.Item2, r.Pixels); }
            catch (Exception ex) { r.Errors.Add(ex.Message); Status = "Not imported: " + ex.Message; return r; }
            int keep = frame;
            FillFrames();
            frame = -1; FrameIndex = keep;
            Status = string.Format("Frame {0} of {1} replaced (not saved). {2}", f.Item2, f.Item1.Name, string.Join(" ", r.Notes));
            return r;
        }

        public void SaveCritter()
        {
            if (critter == null) return;
            var changed = critter.Pages.Where(p => p.HasChanges).ToList();
            if (changed.Count == 0) { Status = "Nothing to save."; return; }
            foreach (var p in changed) p.Save();
            FillFrames();
            Status = "Saved " + string.Join(", ", changed.Select(p => p.Name)) + " (the old files are kept as .BKP).";
        }

        // ---- cutscenes

        List<Cutscene> cutscenes;
        public ObservableCollection<Cutscene> Cutscenes { get; } = new ObservableCollection<Cutscene>();
        public ObservableCollection<string> CutsceneFiles { get; } = new ObservableCollection<string>();
        Cutscene cutscene;
        string cutsceneFile;
        int cutFrame;
        Bitmap cutPreview;
        string cutInfo = "";
        readonly Dictionary<string, LpfAnimation> lpfs = new Dictionary<string, LpfAnimation>();
        public LpfAnimation Lpf { get; private set; }

        public void LoadCutscenes()
        {
            if (cutscenes != null) return;
            cutscenes = Cutscene.Find(Session.GameFolder);
            foreach (var c in cutscenes) Cutscenes.Add(c);
        }

        public Cutscene CutsceneItem
        {
            get { return cutscene; }
            set
            {
                if (!Set(ref cutscene, value)) return;
                CutsceneFiles.Clear();
                if (value == null) return;
                foreach (var a in value.Animations) CutsceneFiles.Add(Path.GetFileName(a));
                CutsceneFile = CutsceneFiles.FirstOrDefault();
                if (CutsceneFile == null) { Lpf = null; CutPreview = null; CutInfo = "This cutscene has a script (" + Path.GetFileName(value.Script ?? "") + ") and no animation of its own."; }
            }
        }

        public string CutsceneFile
        {
            get { return cutsceneFile; }
            set
            {
                if (!Set(ref cutsceneFile, value)) return;
                Lpf = null;
                if (value == null || cutscene == null) return;
                string path = cutscene.Animations.First(a => Path.GetFileName(a) == value);
                try
                {
                    LpfAnimation a;
                    if (!lpfs.TryGetValue(path, out a)) lpfs[path] = a = LpfAnimation.Load(path);
                    Lpf = a;
                }
                catch (Exception ex) { Status = value + ": " + ex.Message; }
                Raise(nameof(CutFrameMax));
                cutFrame = -1;
                CutFrame = 0;
            }
        }

        public int CutFrameMax { get { return Lpf == null ? 0 : Math.Max(0, Lpf.FrameCount - 1); } }
        public int CutFrame
        {
            get { return cutFrame; }
            set { int v = Math.Max(0, Math.Min(CutFrameMax, value)); if (Set(ref cutFrame, v)) ShowCut(); }
        }
        public Bitmap CutPreview { get { return cutPreview; } set { Set(ref cutPreview, value); } }
        public string CutInfo { get { return cutInfo; } set { Set(ref cutInfo, value); } }
        public IndexedImage CutImage { get; private set; }

        void ShowCut()
        {
            if (Lpf == null || Lpf.FrameCount == 0) { CutImage = null; CutPreview = null; CutInfo = Lpf == null ? "" : Lpf.Name + " has no frames."; return; }
            CutImage = Lpf.Frame(cutFrame);
            CutPreview = Pictures.ToBitmap(CutImage);
            CutInfo = string.Format("{0}: frame {1} of {2}, {3} × {4}, {5} frames a second{6}{7}", Lpf.Name, cutFrame + 1, Lpf.FrameCount, Lpf.Width, Lpf.Height, Lpf.Rate,
                Lpf.LastDelta ? ", loops" : "", Lpf.Changed ? " · changed, not saved" : "");
        }

        public void NextCutFrame() { if (Lpf != null && Lpf.FrameCount > 0) CutFrame = (cutFrame + 1) % Lpf.FrameCount; }
        public int CutRate { get { return Lpf == null ? 10 : Lpf.Rate; } }

        public ImportResult ImportCutFrame(byte[] png)
        {
            if (Lpf == null || CutImage == null) return null;
            var r = PictureImport.FromPng(png, CutImage, nearest);
            if (!r.Ok) { Status = "Not imported: " + r.Errors[0]; return r; }
            try { Lpf.Replace(cutFrame, r.Pixels); }
            catch (Exception ex) { r.Errors.Add(ex.Message); Status = "Not imported: " + ex.Message; return r; }
            ShowCut();
            Status = string.Format("Frame {0} of {1} replaced (not saved). {2}", cutFrame + 1, Lpf.Name, string.Join(" ", r.Notes));
            return r;
        }

        /// <summary>Writes every frame of the animation as numbered PNG files in a folder.</summary>
        public int ExportAllCutFrames(string folder)
        {
            if (Lpf == null) return 0;
            Directory.CreateDirectory(folder);
            for (int i = 0; i < Lpf.FrameCount; i++)
                System.IO.File.WriteAllBytes(Path.Combine(folder, string.Format("{0}_{1:000}.png", Lpf.Name.Replace('.', '_'), i + 1)), Lpf.Frame(i).ToPng());
            Status = string.Format("{0} frames written to {1}.", Lpf.FrameCount, folder);
            return Lpf.FrameCount;
        }

        public void SaveCutscene()
        {
            if (Lpf == null || !Lpf.Changed) { Status = "Nothing to save."; return; }
            try { Lpf.Save(); ShowCut(); Status = "Saved " + Lpf.Path + " (the old file is kept as .BKP)."; }
            catch (Exception ex) { Status = "Not saved: " + ex.Message; }
        }
    }
}
