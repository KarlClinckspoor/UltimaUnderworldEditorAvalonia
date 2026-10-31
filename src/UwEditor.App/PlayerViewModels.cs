using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Graphics;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    public static class Pictures
    {
        public static Bitmap ToBitmap(IndexedImage img)
        {
            if (img == null) return null;
            var r = new RgbaImage(img.Width, img.Height);
            var px = img.ToRgba();
            Array.Copy(px, r.Pixels, px.Length);
            return MainViewModel.ToBitmap(r);
        }
    }

    public sealed class WarningRow
    {
        static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E0605A")), Orange = new SolidColorBrush(Color.Parse("#D9A441")), Grey = new SolidColorBrush(Color.Parse("#8E8B85"));
        public PlayerWarning Warning { get; set; }
        public string Label { get { return Warning.Level == WarningLevel.Detected ? "GAME DETECTS" : Warning.Level == WarningLevel.Unreachable ? "NOT REACHABLE" : "NOTE"; } }
        public string Message { get { return Warning.Message; } }
        public IBrush Brush { get { return Warning.Level == WarningLevel.Detected ? Red : Warning.Level == WarningLevel.Unreachable ? Orange : Grey; } }
    }

    /// <summary>A skill's row: its name and value.</summary>
    public sealed class SkillRow : Observable
    {
        readonly PlayerViewModel owner;
        public int Index { get; }
        public string Name { get; }
        public SkillRow(PlayerViewModel owner, int index, string name) { this.owner = owner; Index = index; Name = name; }
        public decimal? Value
        {
            get { return owner.Sheet.Skill(Index); }
            set { owner.Apply(() => owner.Sheet.SetSkill(Index, (int)(value ?? 0)), Name); Raise(); }
        }
        internal void Refresh() { Raise(nameof(Value)); }
    }

    /// <summary>A line of the inventory tree.</summary>
    public sealed class ItemNode : Observable
    {
        public InventoryItem Item { get; set; }
        public ObservableCollection<ItemNode> Children { get; } = new ObservableCollection<ItemNode>();
        public Bitmap Icon { get; set; }
        string text;
        public string Text { get { return text; } set { Set(ref text, value); } }
    }

    public sealed class PlayerViewModel : Observable
    {
        public PlayerSheet Sheet { get; }
        public GameSession Session { get; }
        readonly Func<int, Bitmap> icon;
        readonly Func<int, Bitmap> portrait;
        string status = "", warningSummary = "";
        ItemNode selected;
        decimal? newItem = 0xA0;

        public ObservableCollection<WarningRow> Warnings { get; } = new ObservableCollection<WarningRow>();
        public List<SkillRow> Skills { get; }
        public ObservableCollection<ItemNode> Items { get; } = new ObservableCollection<ItemNode>();
        public string[] ClassNames { get { return PlayerSheet.ClassNames; } }
        public string[] Sexes { get; } = { "Male", "Female" };
        public string[] Hands { get; }
        public List<string> SlotChoices { get; } = new[] { "(no slot)" }.Concat(PlayerInventory.SlotNames.Select((n, i) => i + ": " + n)).ToList();

        public event Action Edited;

        public PlayerViewModel(PlayerSheet sheet, GameSession session, Func<int, Bitmap> icon, Func<int, Bitmap> portrait)
        {
            Sheet = sheet; Session = session; this.icon = icon; this.portrait = portrait;
            Hands = PlayerSheet.HandNames(session.DataFolder, session.Strings);
            Skills = Enumerable.Range(0, 20).Select(i => new SkillRow(this, i, UltimateEditor.Games.PlayerDat.SkillNames[i])).ToList();
            BuildTree();
            Check();
        }

        public string Status { get { return status; } set { Set(ref status, value); } }
        public string WarningSummary { get { return warningSummary; } set { Set(ref warningSummary, value); } }

        /// <summary>Runs a change; a refused value goes to the status line.</summary>
        internal void Apply(Action a, string what)
        {
            try { a(); Status = what + " changed (not saved)."; }
            catch (Exception ex) { Status = what + ": " + ex.Message; }
            Check();
            Edited?.Invoke();
        }

        void Field(Action a, string what, string name) { Apply(a, what); Raise(name); }

        public string Name { get { return Sheet.Name; } set { Field(() => Sheet.Name = value, "Name", nameof(Name)); } }
        public int ClassIndex { get { return Sheet.Class; } set { if (value >= 0) Field(() => Sheet.Class = value, "Class", nameof(ClassIndex)); } }
        public int SexIndex { get { return Sheet.Female ? 1 : 0; } set { if (value >= 0) { Field(() => Sheet.Female = value == 1, "Sex", nameof(SexIndex)); Raise(nameof(Portrait)); } } }
        public int HandIndex { get { return Sheet.HandBit ? 1 : 0; } set { if (value >= 0) Field(() => Sheet.HandBit = value == 1, "Hand", nameof(HandIndex)); } }
        public decimal? Body { get { return Sheet.Body; } set { Field(() => Sheet.Body = (int)(value ?? 0), "Body", nameof(Body)); Raise(nameof(Portrait)); } }
        public Bitmap Portrait { get { return portrait?.Invoke(Sheet.Portrait); } }
        public decimal? Strength { get { return Sheet.Strength; } set { Field(() => Sheet.Strength = (int)(value ?? 0), "Strength", nameof(Strength)); } }
        public decimal? Dexterity { get { return Sheet.Dexterity; } set { Field(() => Sheet.Dexterity = (int)(value ?? 0), "Dexterity", nameof(Dexterity)); } }
        public decimal? Intelligence { get { return Sheet.Intelligence; } set { Field(() => Sheet.Intelligence = (int)(value ?? 0), "Intelligence", nameof(Intelligence)); } }
        public decimal? Vitality { get { return Sheet.Vitality; } set { Field(() => Sheet.Vitality = (int)(value ?? 0), "Vitality", nameof(Vitality)); } }
        public decimal? MaxVitality { get { return Sheet.MaxVitality; } set { Field(() => Sheet.MaxVitality = (int)(value ?? 0), "Maximum vitality", nameof(MaxVitality)); } }
        public decimal? Mana { get { return Sheet.Mana; } set { Field(() => Sheet.Mana = (int)(value ?? 0), "Mana", nameof(Mana)); } }
        public decimal? MaxMana { get { return Sheet.MaxMana; } set { Field(() => Sheet.MaxMana = (int)(value ?? 0), "Maximum mana", nameof(MaxMana)); } }
        public decimal? Hunger { get { return Sheet.Hunger; } set { Field(() => Sheet.Hunger = (int)(value ?? 0), "Hunger", nameof(Hunger)); } }
        public decimal? Fatigue { get { return Sheet.Fatigue; } set { Field(() => Sheet.Fatigue = (int)(value ?? 0), "Fatigue", nameof(Fatigue)); } }
        public decimal? Level { get { return Sheet.Level; } set { Field(() => Sheet.Level = (int)(value ?? 0), "Level", nameof(Level)); } }
        public decimal? Experience { get { return Sheet.Experience; } set { Field(() => Sheet.Experience = (long)(value ?? 0), "Experience", nameof(Experience)); } }
        public decimal? SkillPoints { get { return Sheet.SkillPoints; } set { Field(() => Sheet.SkillPoints = (int)(value ?? 0), "Skill points", nameof(SkillPoints)); } }
        public decimal? MaxWeight { get { return Sheet.MaxWeight; } set { Field(() => Sheet.MaxWeight = (int)(value ?? 0), "Carrying capacity", nameof(MaxWeight)); } }
        public string Derived
        {
            get
            {
                return string.Format("The game derives: maximum vitality {0}, maximum mana {1}, carrying capacity {2}; the experience has earned level {3} and {4} skill points.",
                    Sheet.ExpectedMaxVitality, Sheet.ExpectedMaxMana, Sheet.ExpectedMaxWeight, Sheet.ExpectedLevel, Sheet.ExpectedPointsEarned);
            }
        }

        public void Recompute()
        {
            Apply(() => Sheet.Recompute(), "Maxima");
            RaiseAll();
        }

        public void RaiseAll()
        {
            foreach (var n in new[] { nameof(Name), nameof(ClassIndex), nameof(SexIndex), nameof(HandIndex), nameof(Body), nameof(Portrait), nameof(Strength), nameof(Dexterity), nameof(Intelligence),
                nameof(Vitality), nameof(MaxVitality), nameof(Mana), nameof(MaxMana), nameof(Hunger), nameof(Fatigue), nameof(Level), nameof(Experience), nameof(SkillPoints), nameof(MaxWeight), nameof(Derived) })
                Raise(n);
            foreach (var s in Skills) s.Refresh();
        }

        public void Check()
        {
            Warnings.Clear();
            var w = Sheet.Check();
            foreach (var x in w.OrderBy(x => x.Level)) Warnings.Add(new WarningRow { Warning = x });
            int det = w.Count(x => x.Level == WarningLevel.Detected), un = w.Count(x => x.Level == WarningLevel.Unreachable);
            WarningSummary = det > 0 ? "The game will notice: " + det + (det == 1 ? " value" : " values") + " it calls cheating."
                : un > 0 ? un + " value" + (un == 1 ? "" : "s") + " normal play cannot produce (the game does not check)."
                : w.Count > 0 ? "Nothing the game would notice; " + w.Count + " note" + (w.Count == 1 ? "." : "s.")
                : "Nothing the game would notice.";
            Raise(nameof(Derived));
        }

        // ---- inventory

        public void BuildTree()
        {
            Items.Clear();
            foreach (var t in Sheet.Inventory.Top.OrderBy(t => t.Slot < 0 ? 99 : t.Slot)) Items.Add(Node(t));
        }

        ItemNode Node(InventoryItem it)
        {
            var n = new ItemNode { Item = it, Icon = icon?.Invoke(it.ItemId) };
            n.Text = ItemText(it);
            foreach (var c in it.Contents) n.Children.Add(Node(c));
            return n;
        }

        public string ItemText(InventoryItem it)
        {
            string name = Session.Names.Get(it.ItemId) ?? "item";
            string q = it.Quantity.HasValue && it.Quantity.Value > 1 ? " × " + it.Quantity.Value : "";
            string slot = it.Slot >= 0 ? "   [" + PlayerInventory.SlotNames[it.Slot] + "]" : it.Parent == null ? "   [no slot]" : "";
            return string.Format("0x{0:X3}  {1}{2}{3}{4}", it.ItemId, name, q, it.Enchanted ? " (enchanted)" : "", slot);
        }

        public ItemNode Selected
        {
            get { return selected; }
            set
            {
                if (Set(ref selected, value))
                    foreach (var n in new[] { nameof(HasItem), nameof(ItemId), nameof(ItemName), nameof(Quality), nameof(Owner), nameof(Link), nameof(IsQuantity), nameof(Enchanted), nameof(Invisible), nameof(ItemFlags), nameof(SlotIndex), nameof(IsTop), nameof(LinkMeaning) })
                        Raise(n);
            }
        }

        public bool HasItem { get { return selected != null; } }
        public bool IsTop { get { return selected != null && selected.Item.Parent == null; } }
        InventoryItem It { get { return selected == null ? null : selected.Item; } }

        void ItemField(Action<InventoryItem> a, string what, params string[] names)
        {
            if (It == null) return;
            try { a(It); Sheet.Inventory.Changed = true; Status = what + " changed (not saved)."; }
            catch (Exception ex) { Status = what + ": " + ex.Message; }
            selected.Text = ItemText(It);
            selected.Icon = icon?.Invoke(It.ItemId);
            foreach (var n in names) Raise(n);
            Check();
            Edited?.Invoke();
        }

        public decimal? ItemId { get { return It?.ItemId; } set { ItemField(i => i.ItemId = (int)(value ?? 0), "Item", nameof(ItemId), nameof(ItemName)); } }
        public string ItemName { get { return It == null ? "" : Session.Names.Get(It.ItemId) ?? ""; } }
        public decimal? Quality { get { return It?.Quality; } set { ItemField(i => i.Quality = (int)(value ?? 0), "Quality", nameof(Quality)); } }
        public decimal? Owner { get { return It?.Owner; } set { ItemField(i => i.Owner = (int)(value ?? 0), "Owner", nameof(Owner)); } }
        public decimal? Link { get { return It?.Link; } set { ItemField(i => { if (!i.IsQuantity) throw new InvalidOperationException("A container's link is its contents."); i.Link = (int)(value ?? 0); }, "Quantity", nameof(Link), nameof(LinkMeaning)); } }
        public bool IsQuantity { get { return It != null && It.IsQuantity; } set { ItemField(i => i.IsQuantity = value, "Link kind", nameof(IsQuantity), nameof(Link), nameof(LinkMeaning)); } }
        public bool Enchanted { get { return It != null && It.Enchanted; } set { ItemField(i => i.Enchanted = value, "Enchanted", nameof(Enchanted)); } }
        public bool Invisible { get { return It != null && It.Invisible; } set { ItemField(i => i.Invisible = value, "Invisible", nameof(Invisible)); } }
        public decimal? ItemFlags { get { return It?.Flags; } set { ItemField(i => i.Flags = (int)(value ?? 0), "Flags", nameof(ItemFlags)); } }
        public int SlotIndex
        {
            get { return It == null ? 0 : It.Slot + 1; }
            set { if (value >= 0 && It != null && It.Parent == null) { ItemField(i => Sheet.Inventory.SetSlot(i, value - 1), "Slot", nameof(SlotIndex)); RefreshTexts(Items); } }
        }
        public string LinkMeaning
        {
            get
            {
                if (It == null) return "";
                if (!It.IsQuantity) return "A container: the link is its contents (" + It.Contents.Count + " objects).";
                return It.Link < 0x200 ? "Quantity " + It.Link + "." : "Special " + (It.Link - 0x200) + " (a spell, a text, a key's lock: link − 0x200).";
            }
        }

        void RefreshTexts(IEnumerable<ItemNode> nodes)
        {
            foreach (var n in nodes) { n.Text = ItemText(n.Item); RefreshTexts(n.Children); }
        }

        public decimal? NewItem { get { return newItem; } set { if (Set(ref newItem, value)) Raise(nameof(NewItemName)); } }
        public string NewItemName { get { return Session.Names.Get((int)(newItem ?? 0)) ?? ""; } }

        /// <summary>Adds the new item id: into the selected container, or to the pack.</summary>
        public ItemNode Add(bool intoSelected)
        {
            int id = (int)(newItem ?? 0);
            var container = intoSelected && It != null ? It : null;
            if (container != null && container.IsQuantity) container = container.Parent;
            try
            {
                bool isContainer = (ItemCategories.Of(id, Session.Kind) & ItemCategory.Container) != 0;
                var it = Sheet.Inventory.Add(id, container, isContainer);
                BuildTree();
                Status = string.Format("Added 0x{0:X3} {1} (not saved).", id, NewItemName);
                Check();
                Edited?.Invoke();
                var n = Find(Items, it);
                Selected = n;
                return n;
            }
            catch (Exception ex) { Status = ex.Message; return null; }
        }

        public void Delete()
        {
            if (It == null) return;
            var it = It;
            Sheet.Inventory.Remove(it);
            Selected = null;
            BuildTree();
            Status = string.Format("Removed 0x{0:X3} and what was in it (not saved).", it.ItemId);
            Check();
            Edited?.Invoke();
        }

        public ItemNode Find(IEnumerable<ItemNode> nodes, InventoryItem it)
        {
            foreach (var n in nodes)
            {
                if (n.Item == it) return n;
                var c = Find(n.Children, it);
                if (c != null) return c;
            }
            return null;
        }
    }
}
