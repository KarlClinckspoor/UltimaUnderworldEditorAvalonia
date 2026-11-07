using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>A conversation in the list.</summary>
    public sealed class ConversationRow : Observable
    {
        string detail, title;
        bool changed;
        public int Slot { get; set; }
        public string Name { get; set; }
        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Detail { get { return detail; } set { Set(ref detail, value); } }
        public bool Changed { get { return changed; } set { Set(ref changed, value); } }
        public string Search { get; set; }
    }

    /// <summary>A line of the readable view.</summary>
    public sealed class DialogueLine
    {
        static IBrush SayBrush { get { return ColourScheme.Pick("#E8E2D4", "#2B2A26"); } }
        static IBrush RespondBrush { get { return ColourScheme.Pick("#C9934C", "#8A5A1C"); } }
        static IBrush MenuBrush { get { return ColourScheme.Pick("#7FB6E0", "#2F6DA8"); } }
        static IBrush LabelBrush { get { return ColourScheme.Pick("#8E8B85", "#6E6A62"); } }
        static IBrush FuncBrush { get { return ColourScheme.Pick("#D9A441", "#8A6210"); } }
        static IBrush CodeBrush { get { return ColourScheme.Pick("#A9B1A0", "#4A5544"); } }
        public PseudoLine Line { get; set; }
        public string Text { get { return new string(' ', 4 * Line.Indent) + Line.Text; } }
        public string Address { get { return Line.Kind == PseudoKind.Comment && Line.Text.Length == 0 ? "" : Line.Address.ToString("X4"); } }
        public IBrush Brush
        {
            get
            {
                switch (Line.Kind)
                {
                    case PseudoKind.Say: return SayBrush;
                    case PseudoKind.Respond: return RespondBrush;
                    case PseudoKind.Menu: return MenuBrush;
                    case PseudoKind.Label: case PseudoKind.Comment: return LabelBrush;
                    case PseudoKind.Function: return FuncBrush;
                    default: return CodeBrush;
                }
            }
        }
        public Avalonia.Media.FontWeight Weight { get { return Line.Kind == PseudoKind.Function ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal; } }
    }

    /// <summary>One string of the conversation's block, editable.</summary>
    public sealed class StringRow : Observable
    {
        string text, uses, error;
        bool changed;
        public int Index { get; set; }
        public string Number { get { return Index.ToString(); } }
        /// <summary>The text with real line breaks, as the box edits it.</summary>
        public string Text
        {
            get { return text; }
            set { if (Set(ref text, value ?? "")) Edited?.Invoke(this); }
        }
        public string Uses { get { return uses; } set { if (Set(ref uses, value)) Raise(nameof(HasUses)); } }
        public bool HasUses { get { return !string.IsNullOrEmpty(uses); } }
        public bool Changed { get { return changed; } set { Set(ref changed, value); } }
        public string Error { get { return error; } set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
        public bool HasError { get { return !string.IsNullOrEmpty(error); } }
        public event Action<StringRow> Edited;
        internal void SetQuietly(string t) { text = t ?? ""; Raise(nameof(Text)); }
    }

    /// <summary>An error or warning of the Dialogue tab's live check, in the list under the editor.</summary>
    public sealed class ProblemRow
    {
        public SourceError Error { get; set; }
        public bool Warning { get; set; }
        public string Where { get { return "Line " + Error.Line + ", col " + Error.Column; } }
        public string Kind { get { return Warning ? "Warning" : "Error"; } }
        public string Message { get { return Error.Message; } }
        public IBrush Brush { get { return Warning ? ColourScheme.Pick("#D9A441", "#9A6A10") : ColourScheme.Pick("#E07060", "#B03A2A"); } }
        public override string ToString() { return Kind + " at line " + Error.Line + ", column " + Error.Column + ": " + Error.Message; }
    }

    public sealed class UserRow
    {
        public ConversationUser User { get; set; }
        public string Text { get; set; }
    }

    public sealed class ConversationViewModel : Observable
    {
        string filter = "", status = "", header = "", subheader = "", script = "", scriptError = "";
        ConversationRow selected;
        int tab;
        bool scriptChanged, dialogueChanged, canUndoApply, canRedoApply;
        string problemSummary = "";

        public ObservableCollection<ConversationRow> All { get; } = new ObservableCollection<ConversationRow>();
        public ObservableCollection<ConversationRow> Rows { get; } = new ObservableCollection<ConversationRow>();
        public ObservableCollection<DialogueLine> Lines { get; } = new ObservableCollection<DialogueLine>();
        public ObservableCollection<StringRow> Strings { get; } = new ObservableCollection<StringRow>();
        public ObservableCollection<UserRow> Users { get; } = new ObservableCollection<UserRow>();
        /// <summary>The Dialogue tab's errors and warnings, from the last check of the text.</summary>
        public ObservableCollection<ProblemRow> Problems { get; } = new ObservableCollection<ProblemRow>();

        public string Filter { get { return filter; } set { if (Set(ref filter, value ?? "")) ApplyFilter(); } }
        public ConversationRow Selected { get { return selected; } set { if (Set(ref selected, value)) { SelectedChanged?.Invoke(); Raise(nameof(HasSelection)); } } }
        public bool HasSelection { get { return selected != null; } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public string Header { get { return header; } set { Set(ref header, value); } }
        public string Subheader { get { return subheader; } set { Set(ref subheader, value); } }
        public int Tab { get { return tab; } set { if (Set(ref tab, value)) TabChanged?.Invoke(); } }
        public string Script { get { return script; } set { if (Set(ref script, value ?? "")) ScriptChanged = true; } }
        public bool ScriptChanged { get { return scriptChanged; } set { Set(ref scriptChanged, value); } }
        public string ScriptError { get { return scriptError; } set { if (Set(ref scriptError, value)) Raise(nameof(HasScriptError)); } }
        public bool HasScriptError { get { return !string.IsNullOrEmpty(scriptError); } }
        /// <summary>The Dialogue tab's text differs from the conversation as applied.</summary>
        public bool DialogueChanged { get { return dialogueChanged; } set { Set(ref dialogueChanged, value); } }
        public bool CanUndoApply { get { return canUndoApply; } set { Set(ref canUndoApply, value); } }
        public bool CanRedoApply { get { return canRedoApply; } set { Set(ref canRedoApply, value); } }
        /// <summary>The live check's verdict, above the problems list.</summary>
        public string ProblemSummary { get { return problemSummary; } set { Set(ref problemSummary, value); } }

        public event Action SelectedChanged, TabChanged;

        internal void SetScriptQuietly(string s) { script = s ?? ""; Raise(nameof(Script)); ScriptChanged = false; ScriptError = ""; }

        void ApplyFilter()
        {
            var keep = selected;
            Rows.Clear();
            string f = filter.Trim();
            foreach (var r in All)
                if (f.Length == 0 || r.Search.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) Rows.Add(r);
            if (keep != null && Rows.Contains(keep)) Selected = keep;
        }

        public void Fill(IEnumerable<ConversationRow> rows)
        {
            All.Clear();
            foreach (var r in rows) All.Add(r);
            ApplyFilter();
        }
    }

    /// <summary>One global in the variables window.</summary>
    public sealed class GlobalRow : Observable
    {
        string value, error;
        public GlobalVariable Variable { get; set; }
        public string Kind { get { return Variable.KindName; } }
        public string Number { get { return Variable.Kind == GlobalKind.Private ? "conv " + Variable.Slot + " · " + Variable.Number : Variable.Kind == GlobalKind.Imported ? "" : Variable.Number.ToString(); } }
        public string Name { get { return Variable.Name ?? ""; } }
        public string Value
        {
            get { return value; }
            set { if (Set(ref this.value, value ?? "")) Edited?.Invoke(this); }
        }
        public bool Editable { get { return Variable.Editable; } }
        public bool ReadOnly { get { return !Variable.Editable; } }
        public bool ShowValue { get { return Variable.Editable || Variable.Value.HasValue; } }
        public string ReadBy { get; set; }
        public string WrittenBy { get; set; }
        public string Error { get { return error; } set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
        public bool HasError { get { return !string.IsNullOrEmpty(error); } }
        public string Tip { get { return Variable.Description; } }
        public event Action<GlobalRow> Edited;
        internal void SetQuietly(string v) { value = v ?? ""; Raise(nameof(Value)); }
    }

    public sealed class UseRow
    {
        public GlobalUse Use { get; set; }
        public string Text { get; set; }
    }

    public sealed class GlobalsViewModel : Observable
    {
        string filter = "", status = "", detail = "";
        int kindIndex;
        bool onlyUsed = true, onlySet;
        GlobalRow selected;

        public static readonly string[] KindNames = { "All kinds", "Quests", "Game variables", "X clocks", "Skills", "Private (per conversation)", "Talker and player" };
        public string[] Kinds { get { return KindNames; } }
        public ObservableCollection<GlobalRow> Rows { get; } = new ObservableCollection<GlobalRow>();
        public ObservableCollection<UseRow> Uses { get; } = new ObservableCollection<UseRow>();
        public List<GlobalRow> All { get; } = new List<GlobalRow>();
        public string Filter { get { return filter; } set { if (Set(ref filter, value ?? "")) Apply(); } }
        public int KindIndex { get { return kindIndex; } set { if (Set(ref kindIndex, value)) Apply(); } }
        public bool OnlyUsed { get { return onlyUsed; } set { if (Set(ref onlyUsed, value)) Apply(); } }
        public bool OnlySet { get { return onlySet; } set { if (Set(ref onlySet, value)) Apply(); } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public string Detail { get { return detail; } set { Set(ref detail, value); } }
        public GlobalRow Selected { get { return selected; } set { if (Set(ref selected, value)) SelectedChanged?.Invoke(); } }
        public event Action SelectedChanged;

        public void Apply()
        {
            Rows.Clear();
            string f = filter.Trim();
            foreach (var r in All)
            {
                var v = r.Variable;
                if (kindIndex > 0 && (int)v.Kind != kindIndex - 1) continue;
                if (onlyUsed && v.Uses.Count == 0) continue;
                if (onlySet && (!v.Value.HasValue || v.Value.Value == 0)) continue;
                if (f.Length > 0 && (r.Kind + " " + r.Number + " " + r.Name + " " + v.Description + " " + r.ReadBy + " " + r.WrittenBy).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Rows.Add(r);
            }
        }
    }
}
