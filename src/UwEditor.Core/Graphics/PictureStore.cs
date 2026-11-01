using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// The open game's picture files as edited: each file read once and shared, so the graphics
    /// window, the pixel editor and the item slots all change the same copy and one save writes it.
    /// </summary>
    public sealed class PictureStore
    {
        readonly string data;
        readonly GameKind game;
        readonly Dictionary<string, PictureFile> open = new Dictionary<string, PictureFile>(StringComparer.OrdinalIgnoreCase);
        Palettes palettes;

        public PictureStore(string dataFolder, GameKind game) { data = dataFolder; this.game = game; }

        public Palettes Palettes { get { return palettes ?? (palettes = Palettes.Load(data)); } }

        /// <summary>A file of DATA by name (OBJECTS.GR, CHARHEAD.GR…), or null when it is not there.</summary>
        public PictureFile Get(string name)
        {
            PictureFile f;
            if (open.TryGetValue(name, out f)) return f;
            string path = GameProfile.Find(data, name);
            if (path == null) return null;
            f = PictureFile.Load(path, game);
            open[name] = f;
            return f;
        }

        public IEnumerable<PictureFile> Open { get { return open.Values; } }
        public bool HasChanges { get { return open.Values.Any(f => f.HasChanges); } }

        /// <summary>Raised after a file was written (its name), so views can read the pictures again.</summary>
        public event Action<string> Saved;

        public List<string> Save(PictureFile f)
        {
            var notes = f.Save();
            Saved?.Invoke(f.Name);
            return notes;
        }

        /// <summary>Writes every changed file; returns their names.</summary>
        public List<string> SaveAll()
        {
            var done = new List<string>();
            foreach (var f in open.Values.Where(x => x.HasChanges).ToList()) { Save(f); done.Add(f.Name); }
            return done;
        }
    }
}
