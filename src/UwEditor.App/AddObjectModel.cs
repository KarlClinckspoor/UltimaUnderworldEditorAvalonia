using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A line of the add window's tree: a kind of item (a group) or an item.</summary>
    public sealed class ItemChoice : Observable
    {
        public int Item { get; set; } = -1;
        public string Label { get; set; }
        public Bitmap Icon { get; set; }
        public bool IsGroup { get { return Item < 0; } }
        public ObservableCollection<ItemChoice> Children { get; } = new ObservableCollection<ItemChoice>();
        bool expanded;
        public bool IsExpanded { get { return expanded; } set { Set(ref expanded, value); } }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// The add-object window's state: the item, and a form that depends on what kind of item it
    /// is (ObjectSpec.FormOf). A boulder only has quality, owner and quantity; a weapon or a
    /// piece of armour also an enchantment, with what it really does; a wand its spell and
    /// charges; a book its text; a key its lock; a critter its hit points, attitude and
    /// conversation. Build() gives the ObjectSpec the level editor or the inventory adds.
    /// </summary>
    public sealed class AddObjectModel : Observable
    {
        public GameSession Session { get; private set; }
        public bool ForInventory { get; private set; }
        readonly List<ItemChoice> all;
        /// <summary>The items that match the search, in id order.</summary>
        public ObservableCollection<ItemChoice> Items { get; } = new ObservableCollection<ItemChoice>();
        /// <summary>The same items by kind, with the object list's categories, for the tree.</summary>
        public ObservableCollection<ItemChoice> Groups { get; } = new ObservableCollection<ItemChoice>();
        readonly Dictionary<int, string> groupOf = new Dictionary<int, string>();
        static readonly KeyValuePair<ItemCategory, string>[] Kinds = ItemCategories.Choices.Where(c => c.Key != ItemCategory.All && c.Key != ItemCategory.Weapon).ToArray();
        public List<Choice> Races { get; private set; }
        public List<EnchantmentChoice> EnchantmentList { get; } = new List<EnchantmentChoice>();
        public List<EnchantmentChoice> SpellList { get; private set; }
        public static readonly string[] AttitudeNames = { "0: hostile", "1: upset", "2: mellow", "3: friendly" };
        public string[] Attitudes { get { return AttitudeNames; } }

        public AddObjectModel(GameSession s, Func<int, Bitmap> icon, bool forInventory)
        {
            Session = s;
            ForInventory = forInventory;
            all = new List<ItemChoice>();
            for (int i = 0; i < 0x200; i++)
            {
                if (forInventory && !Carryable(i)) continue;
                string raw = s.Names.Raw(i);
                if (string.IsNullOrWhiteSpace(raw) && !(i >= 0x180 && i < 0x1C0)) continue;
                string n = s.Names.Get(i);
                string proper = NamedItems.Of(s.Kind, i);
                all.Add(new ItemChoice { Item = i, Label = string.Format("0x{0:X3}  {1}{2}", i, n, proper != null ? " (" + proper + ")" : ""), Icon = icon?.Invoke(i) });
                var cat = ItemCategories.Of(i, s.Kind);
                var kind = Kinds.FirstOrDefault(k => (k.Key & cat) != 0);
                groupOf[i] = kind.Value ?? "Other";
            }
            Races = UwEditor.Core.Editing.Races.Of(s);
            SpellList = Enchantments.Spells(s.Strings);
            ApplyFilter();
        }

        bool Carryable(int item)
        {
            var c = ItemCategories.Of(item, Session.Kind);
            if ((c & (ItemCategory.Creature | ItemCategory.Door | ItemCategory.Furniture | ItemCategory.Decal | ItemCategory.Switch | ItemCategory.Trap | ItemCategory.Trigger | ItemCategory.Scenery)) != 0) return false;
            return item < 0x140 && item != 0x10F && item != 0x120;
        }

        string filter = "";
        public string Filter { get { return filter; } set { if (Set(ref filter, value ?? "")) ApplyFilter(); } }

        void ApplyFilter()
        {
            string f = filter.Trim().ToLowerInvariant();
            int id;
            bool hex = f.StartsWith("0x") && int.TryParse(f.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out id);
            Items.Clear();
            foreach (var c in all)
                if (f.Length == 0 || c.Label.ToLowerInvariant().Contains(f)) Items.Add(c);
            var open = new HashSet<string>(Groups.Where(g => g.IsExpanded).Select(g => g.Label.Split('(')[0].Trim()));
            Groups.Clear();
            foreach (var name in Kinds.Select(k => k.Value).Concat(new[] { "Other" }))
            {
                var members = Items.Where(c => groupOf[c.Item] == name).ToList();
                if (members.Count == 0) continue;
                var g = new ItemChoice { Label = string.Format("{0}  ({1})", name, members.Count) };
                foreach (var m in members) g.Children.Add(m);
                // a search opens every group it finds something in; otherwise keep what was open
                g.IsExpanded = f.Length > 0 || open.Contains(name);
                Groups.Add(g);
            }
        }

        /// <summary>Opens the group holding an item (for selecting it from code).</summary>
        public void Reveal(ItemChoice c)
        {
            foreach (var g in Groups) if (g.Children.Contains(c)) g.IsExpanded = true;
        }

        ItemChoice selected;
        public ItemChoice Selected
        {
            get { return selected; }
            set
            {
                if (value != null && value.IsGroup) { value.IsExpanded = !value.IsExpanded; return; }
                if (Set(ref selected, value)) { if (value != null) Reveal(value); SetItem(value == null ? -1 : value.Item); }
            }
        }

        int item = -1;
        public int Item { get { return item; } }
        public ItemForm Form { get; private set; }
        public string FormName
        {
            get
            {
                if (item < 0) return "Choose an item";
                if (item >= 0x180 && item < 0x1C0) return Triggers.IsTrigger(item) ? "Trigger" : "Trap";
                switch (Form)
                {
                    case ItemForm.Enchantable: return "Weapon, armour or jewellery: can be enchanted";
                    case ItemForm.Wand: return "Wand: holds a spell with charges";
                    case ItemForm.Text: return "Book or scroll: holds a text";
                    case ItemForm.Key: return "Key: opens a lock";
                    case ItemForm.Lock: return "Lock: goes inside a door or chest";
                    case ItemForm.Container: return "Container: holds objects";
                    case ItemForm.Creature: return "Creature";
                    default: return "Object";
                }
            }
        }

        public void SetItem(int id)
        {
            item = id;
            if (id < 0) { Raise(nameof(FormName)); RaiseForm(); return; }
            Form = ObjectSpec.FormOf(id, Session.Kind);
            var d = ObjectSpec.Default(id, Session.Kind);
            quality = d.Quality; owner = d.Owner; quantity = 1;
            invisible = false;
            EnchantmentList.Clear();
            EnchantmentList.Add(new EnchantmentChoice { Name = "(none)", Link = 0, Group = "" });
            if (Form == ItemForm.Enchantable)
            {
                EnchantmentList.AddRange(Enchantments.WeaponOrArmour(Session.Kind, id, Session.Strings));
                EnchantmentList.AddRange(SpellList);
            }
            enchantment = 0;
            spell = 0; charges = 8;
            textIndex = 0; lockNumber = 1; locked = true; relockable = false; pickDifficulty = 3;
            hp = 0; attitude = 2; whoami = 0;
            var c = ItemCategories.Of(id, Session.Kind);
            Raise(nameof(FormName));
            RaiseForm();
        }

        void RaiseForm()
        {
            foreach (var n in new[] { nameof(EnchantmentNames), nameof(SpellNames), nameof(HasItem), nameof(IsBasic), nameof(IsEnchantable), nameof(IsWand), nameof(IsText), nameof(IsKey), nameof(IsLock), nameof(IsCreature), nameof(IsTrap),
                nameof(HasQuantity), nameof(CanOwn), nameof(Quality), nameof(Owner), nameof(Quantity), nameof(Invisible), nameof(EnchantmentIndex), nameof(EnchantmentEffect), nameof(ShowUw2Warning),
                nameof(SpellIndex), nameof(Charges), nameof(TextIndex), nameof(TextPreview), nameof(LockNumber), nameof(KeyText), nameof(Locked), nameof(Relockable), nameof(PickDifficulty),
                nameof(Hp), nameof(AttitudeIndex), nameof(Whoami), nameof(WhoamiName), nameof(TrapHelp), nameof(OwnerIndex), nameof(Summary), nameof(QualityMeaning) })
                Raise(n);
        }

        public bool HasItem { get { return item >= 0; } }
        public bool IsTrap { get { return item >= 0x180 && item < 0x1C0; } }
        public bool IsBasic { get { return HasItem && !IsTrap; } }
        public bool IsEnchantable { get { return HasItem && Form == ItemForm.Enchantable; } }
        public bool IsWand { get { return HasItem && Form == ItemForm.Wand; } }
        public bool IsText { get { return HasItem && Form == ItemForm.Text; } }
        public bool IsKey { get { return HasItem && Form == ItemForm.Key; } }
        public bool IsLock { get { return HasItem && Form == ItemForm.Lock; } }
        public bool IsCreature { get { return HasItem && Form == ItemForm.Creature; } }
        public bool HasQuantity { get { return HasItem && (Form == ItemForm.Basic || (Form == ItemForm.Enchantable && enchantment == 0)) && !IsTrap; } }
        /// <summary>The owner is a race only for ordinary objects (a key's owner is its lock, a critter's its home).</summary>
        public bool CanOwn { get { return HasItem && Form != ItemForm.Key && Form != ItemForm.Creature && Form != ItemForm.Lock && !IsTrap && UwEditor.Core.Editing.Races.CanBeOwned(Session, item); } }

        int quality = 0x28, owner, quantity = 1;
        bool invisible;
        public decimal? Quality { get { return quality; } set { Set(ref quality, Clamp(value, 0, 63)); Raise(nameof(Summary)); } }
        public decimal? Owner { get { return owner; } set { if (Set(ref owner, Clamp(value, 0, 63))) { Raise(nameof(OwnerIndex)); Raise(nameof(Summary)); } } }
        public int OwnerIndex
        {
            get { return Races.FindIndex(r => r.Value == owner); }
            set { if (value >= 0 && value < Races.Count) Owner = Races[value].Value; }
        }
        public decimal? Quantity { get { return quantity; } set { Set(ref quantity, Clamp(value, 1, 0x1FF)); Raise(nameof(Summary)); } }
        public bool Invisible { get { return invisible; } set { Set(ref invisible, value); } }
        public string QualityMeaning
        {
            get
            {
                if (IsTrap) return "For the quality and owner, see the description below.";
                if (Form == ItemForm.Wand) return "The wand's own quality is its condition; the charges are the spell object's.";
                if (Form == ItemForm.Creature) return "A critter's quality and owner are its home (set from the square it is put on).";
                return "Quality is the condition, 0-63 (the shipped objects use 40, 0x28).";
            }
        }

        // enchantments
        int enchantment;
        public List<string> EnchantmentNames
        {
            get { return EnchantmentList.Select(e => e.Group.Length == 0 ? e.Name : e.Name + "  — " + e.Group.ToLowerInvariant()).ToList(); }
        }
        public int EnchantmentIndex
        {
            get { return enchantment; }
            set { if (value >= 0 && Set(ref enchantment, value)) { Raise(nameof(EnchantmentEffect)); Raise(nameof(HasQuantity)); Raise(nameof(Summary)); } }
        }
        public EnchantmentChoice Enchantment { get { return enchantment > 0 && enchantment < EnchantmentList.Count ? EnchantmentList[enchantment] : null; } }
        public string EnchantmentEffect
        {
            get
            {
                var e = Enchantment;
                if (e == null) return "No enchantment: the link is a quantity of 1.";
                string where = e.Flag11 ? string.Format("Stored as spell {0} (flag 11 set, link 0x{1:X3}).", e.Link & 0x3F, e.Link)
                    : string.Format("Stored as class 12 effect {0} (link 0x{1:X3}; the name is STRINGS.PAK block 6, 0x{2:X3}).", e.Link & 0xF, e.Link, 0x1C0 + (Enchantments.IsArmourLike(item) ? 0x10 : 0) + (e.Link & 0xF));
                return (string.IsNullOrEmpty(e.Effect) ? "" : "What it does: " + e.Effect + "\n") + where + Note(e);
            }
        }
        /// <summary>What the game does with this enchantment on this kind of item (COMBAT.C DoPlayerWeapon, PLAYDATA.C, USEITEMS.C checkSpell).</summary>
        string Note(EnchantmentChoice e)
        {
            bool weapon = (ItemCategories.Of(item, Session.Kind) & ItemCategory.Weapon) != 0;
            if (e.Flag11)
                return "\n" + (weapon ? "A swing ignores it: only weapon enchantments change a blow. " : "Wearing it does nothing: only effects without flag 11 apply while worn. ")
                    + "The game casts the spell when the object is used (double-clicked), at most every 0x2FD ticks, and never runs out: only a spell object inside a wand has charges.";
            if (weapon && (e.Link & 0x1F0) >> 4 != 12)
                return "\nOn a weapon this does nothing: a held weapon is not worn, so only class 12 enchantments count, and only when it strikes.";
            return "";
        }

        public bool ShowUw2Warning { get { return Session.IsUw2 && (IsEnchantable || IsWand); } }
        public string Uw2Warning { get { return Enchantments.Uw2Warning; } }

        // wands
        int spell, charges = 8;
        public List<string> SpellNames { get { return SpellList.Select(s => s.Name + "  — " + s.Group.ToLowerInvariant()).ToList(); } }
        public int SpellIndex { get { return spell; } set { if (value >= 0) Set(ref spell, value); Raise(nameof(Summary)); } }
        public decimal? Charges { get { return charges; } set { Set(ref charges, Clamp(value, 0, 63)); } }

        // texts
        int textIndex;
        public decimal? TextIndex { get { return textIndex; } set { if (Set(ref textIndex, Clamp(value, 0, 0x1FF))) Raise(nameof(TextPreview)); } }
        public string TextPreview { get { var t = Session.Strings.Get(3, textIndex); return string.IsNullOrWhiteSpace(t) ? "(no text with this number in block 3)" : t.Trim(); } }

        // keys and locks
        int lockNumber = 1, pickDifficulty = 3;
        bool locked = true, relockable;
        public decimal? LockNumber { get { return lockNumber; } set { if (Set(ref lockNumber, Clamp(value, 0, IsKey ? 63 : 0x1FF))) { Raise(nameof(KeyText)); Raise(nameof(Summary)); } } }
        public string KeyText { get { var t = Session.Strings.Get(5, 0x64 + lockNumber); return string.IsNullOrWhiteSpace(t) ? "(no description: block 5, " + (0x64 + lockNumber) + ")" : t.Trim(); } }
        public bool Locked { get { return locked; } set { Set(ref locked, value); } }
        public bool Relockable { get { return relockable; } set { Set(ref relockable, value); } }
        public decimal? PickDifficulty { get { return pickDifficulty; } set { Set(ref pickDifficulty, Clamp(value, 0, 15)); } }

        // creatures
        int hp, attitude = 2, whoami;
        public decimal? Hp { get { return hp; } set { Set(ref hp, Clamp(value, 0, 255)); } }
        public int AttitudeIndex { get { return attitude; } set { if (value >= 0) Set(ref attitude, value & 3); } }
        public decimal? Whoami { get { return whoami; } set { if (Set(ref whoami, Clamp(value, 0, 255))) Raise(nameof(WhoamiName)); } }
        public string WhoamiName
        {
            get
            {
                if (whoami == 0) return "0: no conversation of its own (the generic one for its kind)";
                var n = Session.Strings.Get(7, whoami + 16);
                return string.IsNullOrWhiteSpace(n) ? "no name in block 7" : "talks as " + n.Trim();
            }
        }

        // traps
        public string TrapHelp
        {
            get
            {
                if (!IsTrap) return "";
                var o = new ObjectInfo { Item = item, Record = new byte[0x1B], Quality = quality, Owner = owner, IsQuantity = true, Name = Session.Names.Get(item) };
                return (Triggers.Explain(Session, o) ?? "") + (Triggers.IsTrigger(item) ? "\n\nA new trigger aims at its own square; choose another target afterwards with the button in the properties panel." : "");
            }
        }

        public string Summary { get { return item < 0 ? "" : Build().Describe(Session.Names); } }

        static int Clamp(decimal? v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, (int)(v ?? lo))); }

        /// <summary>The object as chosen.</summary>
        public ObjectSpec Build()
        {
            if (item < 0) throw new InvalidOperationException("Choose an item first.");
            var s = ObjectSpec.Default(item, Session.Kind);
            s.Quality = quality;
            s.Invisible = invisible;
            if (CanOwn) s.Owner = owner;
            switch (Form)
            {
                case ItemForm.Basic:
                    if (!IsTrap) { s.IsQuantity = true; s.Link = quantity; }
                    break;
                case ItemForm.Enchantable:
                    var e = Enchantment;
                    if (e == null) { s.IsQuantity = true; s.Link = quantity; }
                    else
                    {
                        s.IsQuantity = true; s.Enchanted = true; s.Link = e.Link;
                        if (e.Flag11) s.Flags |= 4;
                    }
                    break;
                case ItemForm.Wand:
                    s.IsQuantity = false; s.Link = 0;
                    if (spell >= 0 && spell < SpellList.Count)
                        s.Contents.Add(new ObjectSpec { Item = 0x120, IsQuantity = true, Link = SpellList[spell].Link, Flags = 4, Quality = charges });
                    break;
                case ItemForm.Text:
                    s.IsQuantity = true; s.Link = 0x200 + textIndex;
                    break;
                case ItemForm.Key:
                    s.IsQuantity = true; s.Link = 1; s.Owner = lockNumber & 0x3F;
                    break;
                case ItemForm.Lock:
                    s.IsQuantity = true; s.Link = 0x200 + (lockNumber & 0x1FF);
                    s.Flags = (locked ? 1 : 0) | (relockable ? 2 : 0);
                    s.Z = pickDifficulty;
                    break;
                case ItemForm.Creature:
                    s.Hp = hp; s.Attitude = attitude; s.Whoami = whoami;
                    break;
            }
            if (IsTrap) { s.IsQuantity = true; s.Link = 0; s.Invisible = true; s.Flags = Triggers.IsTrigger(item) ? 4 | 2 : 0; }
            return s;
        }
    }
}
