using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwEditor.Core;

namespace UwEditor.App
{
    public sealed class ModelsViewModel : Observable
    {
        public GameSession Session { get; private set; }
        public ObservableCollection<ModelEntry> Models { get; } = new ObservableCollection<ModelEntry>();
        ModelEntry selected;
        string info = "", status = "";

        public ModelsViewModel(GameSession s)
        {
            Session = s;
            foreach (var m in ModelLibrary.Of(s)) Models.Add(m);
            Status = s.ExeModels == null ? "No " + (s.IsUw2 ? "UW2.EXE" : "UW.EXE") + " next to DATA, and the models are stored in the executable."
                : string.Format("{0} models in {1}, table at 0x{2:X}.", Models.Count, Path.GetFileName(s.ExePath ?? ""), s.ExeModels.TableOffset);
        }

        public string Intro
        {
            get
            {
                return "The models built into " + (Session.IsUw2 ? "UW2.EXE" : "UW.EXE") + " are bytecode for the renderer's model interpreter (3d/INTERP.ASM), " +
                    "drawn for furniture, doors, boulders, the shrine and the like. Drag to turn and use the wheel to zoom. Exported OBJ files come with an MTL file of the palette colours; " +
                    "1.0 is one tile, and y is up.";
            }
        }
        public string Info { get { return info; } set { Set(ref info, value); } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public bool HasModel { get { return selected != null && selected.FaceCount > 0; } }

        public ModelEntry Selected
        {
            get { return selected; }
            set
            {
                if (!Set(ref selected, value)) return;
                Raise(nameof(HasModel));
                Info = value == null ? "" : Describe(value);
            }
        }

        string Describe(ModelEntry e)
        {
            var m = e.Model;
            var sb = new StringBuilder();
            sb.AppendFormat("Model {0} (0x{0:X2}): {1}\n\n", m.Index, m.Name);
            if (m.Error != null) sb.Append("Cannot be read: ").Append(m.Error).Append("\n\n");
            sb.AppendFormat("{0} points, {1} faces ({2} textured)\n", e.VertexCount, e.FaceCount, m.Faces.Count(f => f.Textured));
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "Extent: {0:0.###} × {1:0.###} × {2:0.###} (header)\n", m.ExtentX, m.ExtentY, m.ExtentZ);
            var colours = m.Faces.Where(f => !f.Textured && f.Color >= 0).Select(f => f.Color).Distinct().ToList();
            if (colours.Count > 0) sb.Append("Palette colours: ").Append(string.Join(", ", colours)).Append('\n');
            sb.AppendFormat("At file offset 0x{0:X}\n", m.FileOffset);
            sb.Append(e.Budget >= 0 ? string.Format("Room in place: {0} bytes (to the next model)\n", e.Budget) : "Room in place: unknown (the last model)\n");
            if (e.SharedWith.Count > 0) sb.Append("Same bytes as model ").Append(string.Join(", ", e.SharedWith.Select(i => "0x" + i.ToString("X2")))).Append('\n');
            sb.Append('\n');
            if (m.Index == 1) sb.Append("Drawn round every door (0x140-0x14F): do_door draws the frame, then the leaf.\n");
            if (e.Items.Count > 0)
            {
                sb.Append("Drawn for:\n");
                foreach (var i in e.Items) sb.AppendFormat("  0x{0:X3} {1}\n", i, Session.Names.Get(i));
            }
            else sb.Append("No item is drawn with it.\n");
            return sb.ToString();
        }

        public string Export(ModelEntry e, string objPath)
        {
            string mtlName = Path.GetFileNameWithoutExtension(objPath) + ".mtl";
            string mtl;
            string obj = ModelLibrary.ToObj(e.Model, Session.Graphics.Palette, mtlName, out mtl);
            File.WriteAllText(objPath, obj);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(objPath) ?? ".", mtlName), mtl);
            return objPath;
        }

        public static string FileName(ModelEntry e)
        {
            var name = new string(e.Model.Name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray()).Trim('-');
            while (name.Contains("--")) name = name.Replace("--", "-");
            return string.Format("model-{0:X2}-{1}.obj", e.Index, name);
        }

        public int ExportAll(string folder)
        {
            int n = 0;
            foreach (var e in Models.Where(x => x.FaceCount > 0)) { Export(e, Path.Combine(folder, FileName(e))); n++; }
            Status = n + " models written to " + folder + ".";
            return n;
        }
    }

    /// <summary>Tools ▸ 3D models: the executable's models, viewed and exported to OBJ.</summary>
    public partial class ModelsWindow : Window
    {
        public ModelsViewModel Model { get; private set; }

        public ModelsWindow() { InitializeComponent(); }

        public ModelsWindow(GameSession s)
        {
            InitializeComponent();
            Model = new ModelsViewModel(s);
            DataContext = Model;
            ManualWindow.Attach(this, "3D models");
            Title = "3D models — " + s.Title;
            Model.PropertyChanged += (o, e) => { if (e.PropertyName == nameof(ModelsViewModel.Selected)) Preview.SetModel(Model.Selected?.Model, s.Graphics.Palette); };
            Model.Selected = Model.Models.FirstOrDefault(m => m.FaceCount > 0);
        }

        public void Select(int index) { Model.Selected = Model.Models.FirstOrDefault(m => m.Index == index); }

        async void OnExport(object sender, RoutedEventArgs e)
        {
            var m = Model.Selected;
            if (m == null) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the model", SuggestedFileName = ModelsViewModel.FileName(m), DefaultExtension = "obj",
                FileTypeChoices = new[] { new FilePickerFileType("Wavefront OBJ") { Patterns = new[] { "*.obj" } } },
            });
            var p = f?.TryGetLocalPath();
            if (p == null) return;
            try { Model.Export(m, p); Model.Status = "Written: " + p + " (and its .mtl)."; }
            catch (Exception ex) { Model.Status = "Could not write: " + ex.Message; }
        }

        async void OnExportAll(object sender, RoutedEventArgs e)
        {
            var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder for the OBJ files" });
            var p = f.FirstOrDefault()?.TryGetLocalPath();
            if (p == null) return;
            try { Model.ExportAll(p); }
            catch (Exception ex) { Model.Status = "Could not write: " + ex.Message; }
        }
    }
}
