using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>One copied object: its record, what lies inside it, and (for the top ones) where it lay relative to the first.</summary>
    public sealed class ClipNode
    {
        public byte[] Record;
        public bool Mobile;
        public readonly List<ClipNode> Children = new List<ClipNode>();
        public int Dx, Dy, Above;
        public int Item { get { return (Record[0] | Record[1] << 8) & 0x1FF; } }
    }

    /// <summary>Objects copied from a level (Edit ▸ Copy), ready to paste on any level of the same game.</summary>
    public sealed class ObjectClip
    {
        public GameKind Kind;
        public int FromLevel;
        public readonly List<ClipNode> Roots = new List<ClipNode>();

        public int Count(bool mobile)
        {
            int n = 0;
            Action<ClipNode> walk = null;
            walk = c => { if (c.Mobile == mobile) n++; foreach (var k in c.Children) walk(k); };
            foreach (var r in Roots) walk(r);
            return n;
        }

        public int Total { get { return Count(true) + Count(false); } }

        public string Describe(ObjectNames names)
        {
            if (Roots.Count == 1) return names.Get(Roots[0].Item) + (Total > 1 ? " and what it holds" : "");
            return Roots.Count + " objects";
        }

        /// <summary>A creature with a conversation (its conversation number) is among the copies: the game finds talkers by that number, so a copy is a second one.</summary>
        public bool HasTalker { get { return Roots.Any(r => r.Mobile && r.Item >= 0x40 && r.Item < 0x80 && r.Record.Length > 0x1A && r.Record[0x1A] > 0); } }
    }
}
