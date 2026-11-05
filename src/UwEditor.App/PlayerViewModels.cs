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
        static IBrush Red { get { return ColourScheme.Pick("#E0605A", "#B03A2A"); } }
        static IBrush Orange { get { return ColourScheme.Pick("#D9A441", "#9A6A10"); } }
        static IBrush Grey { get { return ColourScheme.Pick("#8E8B85", "#6E6A62"); } }
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

    /// <summary>A line of the inventory tree: a slot (its object, or none), an object without a slot, or an object inside another.</summary>
    public sealed class ItemNode : Observable
    {
        /// <summary>The object, or null for an empty slot.</summary>
        public InventoryItem Item { get; set; }
        /// <summary>The slot a top line stands for, or -1.</summary>
        public int Slot { get; set; } = -1;
        /// <summary>The slot's name on a top line, or "".</summary>
        public string SlotLabel { get; set; } = "";
        public bool IsEmpty { get { return Item == null; } }
        /// <summary>An object at the top of the inventory in no slot: the game shows it nowhere.</summary>
        public bool NoSlot { get { return Item != null && Item.Parent == null && Slot < 0; } }
        public ObservableCollection<ItemNode> Children { get; } = new ObservableCollection<ItemNode>();
        Bitmap icon;
        public Bitmap Icon { get { return icon; } set { Set(ref icon, value); } }
        string text;
        public string Text { get { return text; } set { Set(ref text, value); } }
        bool expanded = true;
        internal Action<ItemNode> Toggled;
        public bool IsExpanded { get { return expanded; } set { if (Set(ref expanded, value)) Toggled?.Invoke(this); } }
        bool dropTarget;
        /// <summary>The line an object being dragged would land on.</summary>
        public bool IsDropTarget { get { return dropTarget; } set { Set(ref dropTarget, value); } }
    }

    /// <summary>A place an object can go: a slot, a container, or the first free pack slot.</summary>
    public sealed class ItemPlace
    {
        public int Slot = -1;
        public InventoryItem Container;
        public string Label { get; set; }
        public bool FirstFree { get { return Slot < 0 && Container == null; } }
        public bool Same(ItemPlace p) { return p != null && p.Slot == Slot && p.Container == Container; }
        public override string ToString() { return Label; }
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
        /// <summary>UW1's story bits are shown (PlayerSheet.HasStoryBits).</summary>
        public bool HasStoryBits { get { return Sheet.HasStoryBits; } }
        public bool TalismansCanBurn { get { return Sheet.TalismansCanBurn; } set { Field(() => Sheet.TalismansCanBurn = value, "Talismans can be destroyed", nameof(TalismansCanBurn)); } }
        public bool GaramonBuried { get { return Sheet.GaramonBuried; } set { Field(() => Sheet.GaramonBuried = value, "Garamon buried", nameof(GaramonBuried)); } }
        public decimal? TalismansLeft { get { return Sheet.TalismansLeft; } set { Field(() => Sheet.TalismansLeft = (int)(value ?? 0), "Talismans left", nameof(TalismansLeft)); } }
        public bool OrbDestroyed { get { return Sheet.OrbDestroyed; } set { Field(() => Sheet.OrbDestroyed = value, "Orb destroyed", nameof(OrbDestroyed)); } }
        public bool CupFound { get { return Sheet.CupFound; } set { Field(() => Sheet.CupFound = value, "Cup of Wonder found", nameof(CupFound)); } }
        public bool KeyOfTruthGiven { get { return Sheet.KeyOfTruthGiven; } set { Field(() => Sheet.KeyOfTruthGiven = value, "Key of Truth given", nameof(KeyOfTruthGiven)); } }
        public decimal? MoonstoneLevel { get { return Sheet.MoonstoneLevel; } set { Field(() => Sheet.MoonstoneLevel = (int)(value ?? 0), "Moonstone level", nameof(MoonstoneLevel)); } }
        public decimal? SilverTreeLevel { get { return Sheet.SilverTreeLevel; } set { Field(() => Sheet.SilverTreeLevel = (int)(value ?? 0), "Silver tree level", nameof(SilverTreeLevel)); } }
        public bool ArmageddonCast { get { return Sheet.ArmageddonCast; } set { Field(() => Sheet.ArmageddonCast = value, "Armageddon cast", nameof(ArmageddonCast)); } }
        public bool MazeSpell { get { return Sheet.MazeSpell; } set { Field(() => Sheet.MazeSpell = value, "Maze spell", nameof(MazeSpell)); } }
        public decimal? Dreams { get { return Sheet.Dreams; } set { Field(() => Sheet.Dreams = (int)(value ?? 0), "Dreams", nameof(Dreams)); Raise(nameof(DreamsText)); } }
        /// <summary>The dream bits set, in words.</summary>
        public string DreamsText
        {
            get
            {
                if (!Sheet.HasStoryBits) return "";
                int d = Sheet.Dreams;
                var said = new List<string>();
                if ((d & 1) != 0) said.Add("first dream seen");
                if ((d & 2) != 0) said.Add("second dream seen");
                if ((d & 4) != 0) said.Add("Tybal's death dream to come");
                if ((d & 8) != 0) said.Add("talisman dream to come");
                var random = Enumerable.Range(4, 6).Where(b => (d & (1 << b)) != 0).ToList();
                if (random.Count > 0) said.Add("random dreams seen: " + string.Join(", ", random));
                return said.Count == 0 ? "No dream bits are set." : string.Join("; ", said) + ".";
            }
        }
        public string Derived
        {
            get
            {
                return string.Format("As the game computes them: maximum vitality {0}, maximum mana {1}, carrying capacity {2}. The experience has earned level {3} and {4} skill points.",
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
                nameof(Vitality), nameof(MaxVitality), nameof(Mana), nameof(MaxMana), nameof(Hunger), nameof(Fatigue), nameof(Level), nameof(Experience), nameof(SkillPoints), nameof(MaxWeight), nameof(Derived),
                nameof(TalismansCanBurn), nameof(GaramonBuried), nameof(TalismansLeft), nameof(OrbDestroyed), nameof(CupFound), nameof(KeyOfTruthGiven),
                nameof(MoonstoneLevel), nameof(SilverTreeLevel), nameof(ArmageddonCast), nameof(MazeSpell), nameof(Dreams), nameof(DreamsText) })
                Raise(n);
            foreach (var s in Skills) s.Refresh();
        }

        public void Check()
        {
            Warnings.Clear();
            var w = Sheet.Check();
            foreach (var x in w.OrderBy(x => x.Level)) Warnings.Add(new WarningRow { Warning = x });
            int det = w.Count(x => x.Level == WarningLevel.Detected), un = w.Count(x => x.Level == WarningLevel.Unreachable);
            WarningSummary = det > 0 ? "The game will notice " + det + (det == 1 ? " value" : " values") + " it calls cheating."
                : un > 0 ? un + " value" + (un == 1 ? "" : "s") + " that normal play cannot produce (the game does not check)."
                : w.Count > 0 ? "Nothing the game would notice; " + w.Count + " note" + (w.Count == 1 ? "." : "s.")
                : "Nothing the game would notice.";
            Raise(nameof(Derived));
        }

        // ---- inventory

        /// <summary>The slots as the tree lists them: top to bottom as on the paper doll, then the pack.</summary>
        public static readonly int[] SlotOrder = { 5, 6, 0, 1, 2, 7, 8, 9, 10, 3, 4, 11, 12, 13, 14, 15, 16, 17, 18 };
        static readonly string[] SlotLabels =
        {
            "Head", "Torso", "Gloves", "Legs", "Feet", "Shoulder 1", "Shoulder 2", "Hand 1", "Hand 2", "Ring 1", "Ring 2",
            "Pack 1", "Pack 2", "Pack 3", "Pack 4", "Pack 5", "Pack 6", "Pack 7", "Pack 8",
        };
        public static string SlotLabel(int slot) { return slot >= 0 && slot < SlotLabels.Length ? SlotLabels[slot] : "No slot"; }

        readonly HashSet<InventoryItem> collapsed = new HashSet<InventoryItem>();
        ItemPlace addTarget, moveTarget;
        List<ItemPlace> addTargets = new List<ItemPlace>(), moveTargets = new List<ItemPlace>();
        bool placing;

        public void BuildTree()
        {
            var inv = Sheet.Inventory;
            Items.Clear();
            foreach (int s in SlotOrder)
            {
                var it = inv.InSlot(s);
                var n = it != null ? Node(it) : new ItemNode { Text = "empty" };
                n.Slot = s;
                n.SlotLabel = SlotLabel(s);
                Items.Add(n);
            }
            foreach (var t in inv.Top.Where(t => t.Slot < 0))
            {
                var n = Node(t);
                n.SlotLabel = "No slot";
                Items.Add(n);
            }
            BuildAddTargets();
        }

        ItemNode Node(InventoryItem it)
        {
            var n = new ItemNode { Item = it, Icon = icon?.Invoke(it.ItemId), IsExpanded = !collapsed.Contains(it) };
            n.Text = ItemText(it);
            n.Toggled = x => { if (x.IsExpanded) collapsed.Remove(x.Item); else collapsed.Add(x.Item); };
            foreach (var c in it.Contents) n.Children.Add(Node(c));
            return n;
        }

        public string ItemText(InventoryItem it)
        {
            string name = Session.Names.Get(it.ItemId) ?? "item";
            string q = it.Quantity.HasValue && it.Quantity.Value > 1 ? " × " + it.Quantity.Value : "";
            return string.Format("0x{0:X3}  {1}{2}{3}", it.ItemId, name, q, it.Enchanted ? " (enchanted)" : "");
        }

        string Short(InventoryItem it) { return string.Format("0x{0:X3} {1}", it.ItemId, Session.Names.Get(it.ItemId) ?? "item"); }

        /// <summary>Where an object is: its slot, or the container it is in.</summary>
        string Where(InventoryItem it) { return it.Parent == null ? SlotLabel(it.Slot) : "in " + Short(it.Parent); }

        IEnumerable<ItemPlace> Containers(InventoryItem except)
        {
            foreach (var c in Sheet.Inventory.All())
                if (!c.IsQuantity && (except == null || !PlayerInventory.IsWithin(c, except)))
                    yield return new ItemPlace { Container = c, Label = "Inside " + Short(c) + " (" + Where(c) + ")" };
        }

        /// <summary>Where new objects go: the first free pack slot, an empty slot, or a container. It stays until changed.</summary>
        public List<ItemPlace> AddTargets { get { return addTargets; } }
        public ItemPlace AddTarget
        {
            get { return addTarget; }
            set { if (value != null && Set(ref addTarget, value)) Raise(nameof(AddTargetText)); }
        }
        public string AddTargetText
        {
            get { return addTarget == null || addTarget.FirstFree ? "Click a container or an empty slot in the list to add there instead." : "New objects keep going there until you pick another place."; }
        }

        void BuildAddTargets()
        {
            var inv = Sheet.Inventory;
            var l = new List<ItemPlace> { new ItemPlace { Label = "The pack (first free slot)" } };
            foreach (int s in SlotOrder) if (inv.InSlot(s) == null) l.Add(new ItemPlace { Slot = s, Label = SlotLabel(s) + " (empty)" });
            l.AddRange(Containers(null));
            var keep = addTarget;
            addTargets = l;
            addTarget = l.FirstOrDefault(p => p.Same(keep)) ?? l[0];
            Raise(nameof(AddTargets));
            Raise(nameof(AddTarget));
            Raise(nameof(AddTargetText));
        }

        /// <summary>The places the selected object can move to: any slot (its object makes way), or a container not inside it.</summary>
        public List<ItemPlace> MoveTargets { get { return moveTargets; } }
        public ItemPlace MoveTarget
        {
            get { return moveTarget; }
            set
            {
                if (placing || value == null || It == null || value.Same(moveTarget) || (value.FirstFree)) return;
                MoveTo(It, value.Container, value.Slot, null);
            }
        }

        void BuildMoveTargets()
        {
            placing = true;
            var l = new List<ItemPlace>();
            ItemPlace here = null;
            if (It != null)
            {
                var inv = Sheet.Inventory;
                foreach (int s in SlotOrder)
                {
                    var o = inv.InSlot(s);
                    var p = new ItemPlace { Slot = s, Label = SlotLabel(s) + (o == null ? " (empty)" : o == It ? "" : " (swap with " + Short(o) + ")") };
                    l.Add(p);
                    if (It.Parent == null && It.Slot == s) here = p;
                }
                foreach (var p in Containers(It))
                {
                    l.Add(p);
                    if (p.Container == It.Parent) here = p;
                }
                if (here == null) { here = new ItemPlace { Label = SlotLabel(-1) }; l.Insert(0, here); }
            }
            moveTargets = l;
            moveTarget = here;
            Raise(nameof(MoveTargets));
            Raise(nameof(MoveTarget));
            placing = false;
        }

        public ItemNode Selected
        {
            get { return selected; }
            set
            {
                if (Set(ref selected, value))
                {
                    foreach (var n in new[] { nameof(HasItem), nameof(ItemId), nameof(ItemName), nameof(Quality), nameof(Owner), nameof(Link), nameof(IsQuantity), nameof(Enchanted), nameof(Invisible), nameof(ItemFlags), nameof(LinkMeaning) })
                        Raise(n);
                    bool quiet = placing;
                    BuildMoveTargets();
                    // a container or an empty slot picked in the list becomes where new objects go
                    var p = value == null || quiet ? null
                        : value.Item == null ? addTargets.FirstOrDefault(x => x.Slot == value.Slot)
                        : !value.Item.IsQuantity ? addTargets.FirstOrDefault(x => x.Container == value.Item) : null;
                    if (p != null) AddTarget = p;
                }
            }
        }

        public bool HasItem { get { return It != null; } }
        public InventoryItem It { get { return selected == null ? null : selected.Item; } }

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
        public bool IsQuantity
        {
            get { return It != null && It.IsQuantity; }
            set { ItemField(i => i.IsQuantity = value, "Link kind", nameof(IsQuantity), nameof(Link), nameof(LinkMeaning)); BuildAddTargets(); BuildMoveTargets(); }
        }
        public bool Enchanted { get { return It != null && It.Enchanted; } set { ItemField(i => i.Enchanted = value, "Enchanted", nameof(Enchanted)); } }
        public bool Invisible { get { return It != null && It.Invisible; } set { ItemField(i => i.Invisible = value, "Invisible", nameof(Invisible)); } }
        public decimal? ItemFlags { get { return It?.Flags; } set { ItemField(i => i.Flags = (int)(value ?? 0), "Flags", nameof(ItemFlags)); } }
        public string LinkMeaning
        {
            get
            {
                if (It == null) return "";
                if (!It.IsQuantity) return "A container: the link is its contents (" + It.Contents.Count + " objects).";
                return It.Link < 0x200 ? "Quantity " + It.Link + "." : "Special " + (It.Link - 0x200) + " (a spell, a text, a key's lock: link − 0x200).";
            }
        }

        public decimal? NewItem { get { return newItem; } set { if (Set(ref newItem, value)) Raise(nameof(NewItemName)); } }
        public string NewItemName { get { return Session.Names.Get((int)(newItem ?? 0)) ?? ""; } }

        /// <summary>Adds the new item id where AddTarget says.</summary>
        public ItemNode Add()
        {
            int id = (int)(newItem ?? 0);
            bool isContainer = (ItemCategories.Of(id, Session.Kind) & ItemCategory.Container) != 0;
            return Added(c => Sheet.Inventory.Add(id, c, isContainer), string.Format("0x{0:X3} {1}", id, NewItemName));
        }

        /// <summary>Adds an object chosen in the add window (an enchanted weapon, a wand with its spell) where AddTarget says.</summary>
        public ItemNode AddSpec(UwEditor.Core.Editing.ObjectSpec spec)
        {
            return Added(c => Sheet.Inventory.Add(spec, c), spec.Describe(Session.Names));
        }

        /// <summary>Where AddTarget sends new objects, in words.</summary>
        public string AddTargetWhere
        {
            get
            {
                var t = addTarget ?? addTargets[0];
                return t.FirstFree ? "the first free pack slot" : t.Slot >= 0 ? "the " + SlotLabel(t.Slot) + " slot" : "inside " + Short(t.Container);
            }
        }

        ItemNode Added(Func<InventoryItem, InventoryItem> add, string what)
        {
            var target = addTarget ?? addTargets[0];
            try
            {
                if (target.Slot >= 0 && Sheet.Inventory.InSlot(target.Slot) != null)
                    throw new InvalidOperationException(SlotLabel(target.Slot) + " is no longer empty; pick another place.");
                var it = add(target.Container);
                if (target.Slot >= 0) Sheet.Inventory.MoveToSlot(it, target.Slot);
                if (target.Container != null) collapsed.Remove(target.Container);
                Status = "Added " + what + (target.Container != null ? " inside " + Short(target.Container) : " to " + SlotLabel(it.Slot)) + " (not saved).";
                return Changed(it);
            }
            catch (Exception ex) { Status = ex.Message; return null; }
        }

        /// <summary>Rebuilds the tree after a change and selects the object, keeping where new objects go.</summary>
        ItemNode Changed(InventoryItem it)
        {
            var keep = addTarget;
            BuildTree();
            Check();
            Edited?.Invoke();
            placing = true;
            Selected = Find(Items, it);
            placing = false;
            AddTarget = addTargets.FirstOrDefault(p => p.Same(keep)) ?? addTargets[0];
            return selected;
        }

        /// <summary>Moves an object into a container (before another of its objects), or to a slot.</summary>
        public ItemNode MoveTo(InventoryItem it, InventoryItem container, int slot, InventoryItem before)
        {
            try
            {
                if (container != null)
                {
                    Sheet.Inventory.MoveInto(it, container, before);
                    collapsed.Remove(container);
                    Status = "Moved " + Short(it) + " inside " + Short(container) + " (not saved).";
                }
                else
                {
                    var other = Sheet.Inventory.InSlot(slot);
                    Sheet.Inventory.MoveToSlot(it, slot);
                    Status = "Moved " + Short(it) + " to " + SlotLabel(slot) + (other != null && other != it ? "; " + Short(other) + " went to " + SlotLabel(other.Slot) : "") + " (not saved).";
                }
                return Changed(it);
            }
            catch (Exception ex) { Status = ex.Message; BuildMoveTargets(); return null; }
        }

        /// <summary>
        /// Whether a dragged object can land on a line: an empty slot, a container (not inside the
        /// dragged one), an object in a container (it goes before it) or an object in a slot (they swap).
        /// </summary>
        public bool CanDrop(ItemNode dragged, ItemNode onto)
        {
            if (dragged == null || dragged.Item == null || onto == null || onto == dragged) return false;
            if (onto.Item == null) return true;
            if (PlayerInventory.IsWithin(onto.Item, dragged.Item)) return false;
            return !onto.Item.IsQuantity || onto.Item.Parent != null || onto.Slot >= 0;
        }

        public ItemNode Drop(ItemNode dragged, ItemNode onto)
        {
            if (!CanDrop(dragged, onto)) return null;
            var it = dragged.Item;
            if (onto.Item == null) return MoveTo(it, null, onto.Slot, null);
            if (!onto.Item.IsQuantity) return MoveTo(it, onto.Item, -1, null);
            if (onto.Item.Parent != null) return MoveTo(it, onto.Item.Parent, -1, onto.Item);
            return MoveTo(it, null, onto.Slot, null);
        }

        public void Delete()
        {
            if (It == null) return;
            var it = It;
            Sheet.Inventory.Remove(it);
            Selected = null;
            BuildTree();
            Status = string.Format("Removed 0x{0:X3} and everything inside it (not saved).", it.ItemId);
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
