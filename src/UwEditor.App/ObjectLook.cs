using System.Collections.Generic;
using UltimateEditor.Uw1;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>How an object's name is coloured in the object list and the properties panel.</summary>
    public enum ObjectTint { None, Hostile, Upset, Mellow, Friendly, Enchanted }

    /// <summary>
    /// The colour and underline of an object's name: a creature by its attitude (hostile red, upset
    /// brown, mellow green, friendly blue), any other enchanted object purple, and traps and
    /// triggers underlined. A creature keeps its attitude's colour whatever it carries: what it
    /// holds has its own line. The colours are in Styles.axaml (Obj* in both colour schemes).
    /// </summary>
    public sealed class ObjectLook
    {
        public ObjectTint Tint { get; private set; }
        public bool Underlined { get; private set; }
        public bool Hostile { get { return Tint == ObjectTint.Hostile; } }
        public bool Upset { get { return Tint == ObjectTint.Upset; } }
        public bool Mellow { get { return Tint == ObjectTint.Mellow; } }
        public bool Friendly { get { return Tint == ObjectTint.Friendly; } }
        public bool Enchanted { get { return Tint == ObjectTint.Enchanted; } }

        static readonly Dictionary<int, ObjectLook> all = new Dictionary<int, ObjectLook>();

        /// <summary>The plain look: no colour, no underline.</summary>
        public static readonly ObjectLook Plain = Get(ObjectTint.None, false);

        public static ObjectLook Get(ObjectTint tint, bool underlined)
        {
            int k = (int)tint * 2 + (underlined ? 1 : 0);
            ObjectLook l;
            if (!all.TryGetValue(k, out l)) all[k] = l = new ObjectLook { Tint = tint, Underlined = underlined };
            return l;
        }

        /// <summary>The look of an object (null: plain).</summary>
        public static ObjectLook Of(ObjectInfo o)
        {
            if (o == null || o.Free) return Plain;
            return Get(TintOf(o), o.IsTrapOrTrigger);
        }

        /// <summary>A creature's attitude (the mobile record's word at 0Dh, bits 14-15: include/object.h OBJ_ATTITUDE, the same in both games); −1 for anything else.</summary>
        public static int AttitudeOf(ObjectInfo o)
        {
            if (o == null || !o.IsCritter || !o.IsMobile || o.Record == null || o.Record.Length < Uw1Schemas.MobileObject.Size) return -1;
            return Uw1Schemas.MobileObject["Attitude"].Get(o.Record) & 3;
        }

        public static ObjectTint TintOf(ObjectInfo o)
        {
            int a = AttitudeOf(o);
            if (a >= 0) return a == 0 ? ObjectTint.Hostile : a == 1 ? ObjectTint.Upset : a == 2 ? ObjectTint.Mellow : ObjectTint.Friendly;
            if (o.Enchanted && !o.IsCritter) return ObjectTint.Enchanted;
            return ObjectTint.None;
        }
    }
}
