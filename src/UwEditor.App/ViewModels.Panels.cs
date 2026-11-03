namespace UwEditor.App
{
    /// <summary>The properties panel's two sections fold away by clicking their headers.</summary>
    public sealed partial class MainViewModel
    {
        bool objectOpen = true, tileOpen = true, showFacing;

        /// <summary>Show ▸ Facing: a white arrow on each object that faces a way (creatures, 3D models, doors, levers, wall pictures).</summary>
        public bool ShowFacing { get { return showFacing; } set { if (Set(ref showFacing, value)) FacingChanged?.Invoke(); } }
        public event System.Action FacingChanged;

        public bool ObjectOpen { get { return objectOpen; } set { if (Set(ref objectOpen, value)) Raise(nameof(ObjectHeader)); } }
        public bool TileOpen { get { return tileOpen; } set { if (Set(ref tileOpen, value)) Raise(nameof(TileHeader)); } }
        public string ObjectHeader { get { return (objectOpen ? "▾ " : "▸ ") + "OBJECT"; } }
        public string TileHeader { get { return (tileOpen ? "▾ " : "▸ ") + "TILE"; } }
    }
}
