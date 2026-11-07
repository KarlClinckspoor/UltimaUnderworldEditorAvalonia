using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>
    /// The Dialogue tab's colours: keywords, strings, numbers, string numbers, comments, the
    /// script's functions, the game's built-ins and its imported variables. A line is coloured
    /// on its own (ConversationText.ScanLine): nothing in the language spans lines. The colours
    /// are picked for the light or dark look each time a line is drawn.
    /// </summary>
    public sealed class DialogueColorizer : DocumentColorizingTransformer
    {
        public static IBrush KeywordBrush { get { return ColourScheme.Pick("#7FB6E0", "#2F6DA8"); } }
        public static IBrush StringBrush { get { return ColourScheme.Pick("#D99A7E", "#A3412A"); } }
        public static IBrush NumberBrush { get { return ColourScheme.Pick("#A8C99A", "#2B7A31"); } }
        public static IBrush StringIdBrush { get { return ColourScheme.Pick("#8E8B85", "#6E6A62"); } }
        public static IBrush CommentBrush { get { return ColourScheme.Pick("#7C8A74", "#66705C"); } }
        public static IBrush FunctionBrush { get { return ColourScheme.Pick("#D9A441", "#8A6210"); } }
        public static IBrush BuiltinBrush { get { return ColourScheme.Pick("#4EC9B0", "#1E7A6A"); } }
        public static IBrush VariableBrush { get { return ColourScheme.Pick("#C895F2", "#7A3DB8"); } }
        public static IBrush LabelBrush { get { return ColourScheme.Pick("#B8A68A", "#6E5A3A"); } }

        /// <summary>The built-in functions and imported variables of the shown conversation (and every built-in the game has).</summary>
        public HashSet<string> Builtins = new HashSet<string>(ConversationBuiltins.Names, StringComparer.Ordinal);
        public HashSet<string> Variables = new HashSet<string>(ImportedVariables.All.Select(v => v.Name), StringComparer.Ordinal);

        public void SetConversation(Conversation c)
        {
            Builtins = new HashSet<string>(ConversationBuiltins.Names, StringComparer.Ordinal);
            Variables = new HashSet<string>(ImportedVariables.All.Select(v => v.Name), StringComparer.Ordinal);
            if (c == null) return;
            foreach (var i in c.Imports) (i.IsFunction ? Builtins : Variables).Add(i.Name);
        }

        /// <summary>What a token is drawn as (for tests and the hovers): keyword, string, number, stringid, address, comment, function, builtin, variable, label or name.</summary>
        public string Classify(List<SourceSpan> line, int i)
        {
            var t = line[i];
            switch (t.Kind)
            {
                case SpanKind.Keyword: return "keyword";
                case SpanKind.String: return "string";
                case SpanKind.Number: return "number";
                case SpanKind.StringId: return "stringid";
                case SpanKind.Address: return "address";
                case SpanKind.Comment: return "comment";
                case SpanKind.Name:
                    bool call = i + 1 < line.Count && line[i + 1].Kind == SpanKind.Punct && line[i + 1].Text == "(";
                    if (Builtins.Contains(t.Text) && (call || !Variables.Contains(t.Text))) return "builtin";
                    if (call) return "function";
                    if (Variables.Contains(t.Text)) return "variable";
                    if (i == 0 && line.Count == 2 && line[1].Text == ":") return "label";
                    return "name";
                default: return "punct";
            }
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            string text = CurrentContext.Document.GetText(line);
            var spans = ConversationText.ScanLine(text);
            for (int i = 0; i < spans.Count; i++)
            {
                IBrush brush = null;
                bool bold = false, italic = false;
                switch (Classify(spans, i))
                {
                    case "keyword": brush = KeywordBrush; break;
                    case "string": brush = StringBrush; break;
                    case "number": case "address": brush = NumberBrush; break;
                    case "stringid": brush = StringIdBrush; break;
                    case "comment": brush = CommentBrush; italic = true; break;
                    case "builtin": brush = BuiltinBrush; break;
                    case "variable": brush = VariableBrush; break;
                    case "label": brush = LabelBrush; break;
                    case "function": brush = FunctionBrush; bold = i == 0 && spans[i].Start == 0; break;
                }
                if (brush == null) continue;
                int from = line.Offset + spans[i].Start, to = line.Offset + spans[i].End;
                ChangeLinePart(from, to, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(brush);
                    if (bold || italic)
                    {
                        var tf = el.TextRunProperties.Typeface;
                        el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, italic ? FontStyle.Italic : tf.Style, bold ? FontWeight.SemiBold : tf.Weight));
                    }
                });
            }
        }
    }

    /// <summary>An error or warning of the live check, kept on its place in the text as the text changes.</summary>
    public sealed class SourceProblem : TextSegment
    {
        public bool Warning;
        public string Message;
        public int Line, Column;
    }

    /// <summary>The wavy underlines of the live check's errors (red) and warnings (amber).</summary>
    public sealed class ProblemRenderer : IBackgroundRenderer
    {
        TextSegmentCollection<SourceProblem> problems;
        TextDocument document;

        public KnownLayer Layer { get { return KnownLayer.Selection; } }

        public IEnumerable<SourceProblem> All { get { return problems ?? Enumerable.Empty<SourceProblem>(); } }

        public void Set(TextDocument doc, IEnumerable<SourceProblem> list)
        {
            if (doc != document || problems == null) { document = doc; problems = new TextSegmentCollection<SourceProblem>(doc); }
            problems.Clear();
            foreach (var p in list) problems.Add(p);
        }

        /// <summary>The problems at an offset (on it, or touching it).</summary>
        public List<SourceProblem> At(int offset)
        {
            if (problems == null) return new List<SourceProblem>();
            return problems.FindSegmentsContaining(offset).ToList();
        }

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (problems == null || problems.Count == 0 || !textView.VisualLinesValid) return;
            var visual = textView.VisualLines;
            if (visual.Count == 0) return;
            int first = visual.First().FirstDocumentLine.Offset, last = visual.Last().LastDocumentLine.EndOffset;
            var error = new Pen(ColourScheme.Pick("#E07060", "#C0392B"), 1.2);
            var warn = new Pen(ColourScheme.Pick("#D9A441", "#9A6A10"), 1.2);
            foreach (var p in problems.FindOverlappingSegments(first, last - first))
            {
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, p))
                {
                    double y = r.Bottom - 1.5, x = r.Left, end = Math.Max(r.Right, r.Left + 4);
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(x, y), false);
                        bool up = true;
                        for (x += 2; x <= end; x += 2, up = !up) ctx.LineTo(new Point(x, up ? y - 2 : y));
                        ctx.EndFigure(false);
                    }
                    dc.DrawGeometry(null, p.Warning ? warn : error, g);
                }
            }
        }
    }
}
