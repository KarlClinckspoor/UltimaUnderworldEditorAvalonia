using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// The level picker again when a slot gained or lost its level (Level ▸ Make this slot a
        /// level, or its undo): a slot holds a level when its edited block has the magic word
        /// (map/MAP.C Map_Load), else as LEV.ARK had it. The level shown stays. Cheap when
        /// nothing changed, so it can follow every edit.
        /// </summary>
        public void RefreshLevelSlots()
        {
            if (session == null || Edits == null) return;
            bool changed = false;
            var now = slots.Select(d =>
            {
                var e = Edits.Existing(d.Level);
                bool present = e != null ? new LevelBlock(e.Data, session.Kind).MagicOk : d.Present;
                if (present == d.Present) return d;
                changed = true;
                return new LevelSlot(session, d.Level, present);
            }).ToList();
            if (!changed) return;
            slots = now;
            var names = worlds.ToList();
            for (int w = 0; w < session.Profile.WorldCount && w < names.Count; w++)
            {
                int used = slots.Count(x => x.World == w && x.Present), all = slots.Count(x => x.World == w);
                names[w] = session.IsUw2 ? string.Format("{0} ({1} of {2})", session.Profile.WorldName(w), used, all) : "Levels";
            }
            int keepWorld = worldIndex;
            worlds = names;
            Raise(nameof(Worlds));
            suppressSlot = true;
            worldIndex = -1;
            WorldIndex = keepWorld;
            suppressSlot = false;
            if (level != null) selectedSlot = slots.FirstOrDefault(x => x.Level == level.Level) ?? selectedSlot;
            Raise(nameof(SelectedSlot));
            Raise(nameof(LevelSummary));
        }

        LevelEdits slotWatch;

        /// <summary>Follows the edits of the open game so the picker shows slots made levels, and undone (once per game).</summary>
        public void WatchLevelSlots()
        {
            if (Edits == null || slotWatch == Edits) return;
            slotWatch = Edits;
            var watched = Edits;
            watched.Changed += e => { if (Edits == watched) Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLevelSlots); };
        }
    }
}
