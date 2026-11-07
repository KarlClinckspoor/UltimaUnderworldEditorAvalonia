using System;
using System.Collections.Generic;
using System.Text;

namespace UwEditor.Core.Conversations
{
    public enum SpanKind { Keyword, Name, Number, String, StringId, Address, Comment, Punct, Bad }

    /// <summary>A token of the conversation language in a text: where it is and what it is.</summary>
    public struct SourceSpan
    {
        public SpanKind Kind;
        /// <summary>From the start of what was scanned (the line, or the whole text).</summary>
        public int Start, Length;
        public string Text;
        public int End { get { return Start + Length; } }
        public override string ToString() { return Kind + " " + Text; }
    }

    /// <summary>A function's place in the text, found by its header line ("name(" at the start of a line).</summary>
    public sealed class FunctionRange
    {
        public string Name;
        /// <summary>The header line (from 1), the offset of its name, and where the next function's header starts (or the text ends).</summary>
        public int Line, Start, End;
        /// <summary>The view's "// never called" comment is on the header line.</summary>
        public bool NeverCalled;
        public bool Holds(int offset) { return offset >= Start && offset < End; }
    }

    /// <summary>
    /// The conversation language (docs/conversation-language.md) as text for an editor: the
    /// tokens of a line (for colours, hovers and the word at the caret), the functions, where a
    /// name is defined and where it is used. Nothing in the language spans lines (a string
    /// ends at its line, a comment runs to the end of its line), so a line can be scanned on
    /// its own; the rules are the lexer's (ConversationSource.Lex).
    /// </summary>
    public static class ConversationText
    {
        static readonly string[] puncts = { "==", "!=", "<=", ">=", "&&", "||", "(", ")", "{", "}", "[", "]", ",", ";", ":", "=", "<", ">", "+", "-", "*", "/", "%", "!", "&" };

        static bool NameStart(char c) { return char.IsLetter(c) || c == '_'; }
        static bool NameChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        /// <summary>The tokens of one line (no line break in it), comments included.</summary>
        public static List<SourceSpan> ScanLine(string s, int from = 0, int to = -1)
        {
            var list = new List<SourceSpan>();
            if (s == null) return list;
            if (to < 0 || to > s.Length) to = s.Length;
            int i = from;
            while (i < to)
            {
                char ch = s[i];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') { i++; continue; }
                int b = i;
                SpanKind kind;
                if (ch == '/' && i + 1 < to && s[i + 1] == '/') { i = to; kind = SpanKind.Comment; }
                else if (NameStart(ch))
                {
                    while (i < to && NameChar(s[i])) i++;
                    kind = ConversationSource.Keywords.Contains(s.Substring(b, i - b)) ? SpanKind.Keyword : SpanKind.Name;
                }
                else if (char.IsDigit(ch))
                {
                    bool hex = ch == '0' && i + 1 < to && (s[i + 1] == 'x' || s[i + 1] == 'X');
                    if (hex) i += 2;
                    while (i < to && (hex ? Uri.IsHexDigit(s[i]) : char.IsDigit(s[i]))) i++;
                    kind = SpanKind.Number;
                    if (i < to && NameStart(s[i])) { while (i < to && NameChar(s[i])) i++; kind = SpanKind.Bad; }
                }
                else if (ch == '#')
                {
                    i++;
                    while (i < to && char.IsDigit(s[i])) i++;
                    kind = i - b > 1 ? SpanKind.StringId : SpanKind.Bad;
                }
                else if (ch == '@')
                {
                    i++;
                    while (i < to && Uri.IsHexDigit(s[i])) i++;
                    kind = i - b > 1 && i - b <= 5 ? SpanKind.Address : SpanKind.Bad;
                }
                else if (ch == '"')
                {
                    i++;
                    while (i < to && s[i] != '"' && s[i] != '\n')
                    {
                        if (s[i] == '\\' && i + 1 < to && (s[i + 1] == '"' || s[i + 1] == '\\' || s[i + 1] == 'n')) i += 2;
                        else i++;
                    }
                    if (i < to && s[i] == '"') i++;
                    kind = SpanKind.String;
                }
                else
                {
                    string p = null;
                    foreach (var x in puncts) if (i + x.Length <= to && string.CompareOrdinal(s, i, x, 0, x.Length) == 0) { p = x; break; }
                    i += p == null ? 1 : p.Length;
                    kind = p == null ? SpanKind.Bad : SpanKind.Punct;
                }
                list.Add(new SourceSpan { Kind = kind, Start = b, Length = i - b, Text = s.Substring(b, i - b) });
            }
            return list;
        }

        /// <summary>The tokens of a whole text, with offsets from its start.</summary>
        public static List<SourceSpan> Scan(string text)
        {
            var list = new List<SourceSpan>();
            text = text ?? "";
            int at = 0;
            while (at <= text.Length)
            {
                int nl = text.IndexOf('\n', at);
                int end = nl < 0 ? text.Length : nl;
                list.AddRange(ScanLine(text, at, end));
                if (nl < 0) break;
                at = nl + 1;
            }
            return list;
        }

        /// <summary>The token at an offset (one that starts there or holds it; a token ending there when nothing else does), or null.</summary>
        public static SourceSpan? SpanAt(string text, int offset)
        {
            text = text ?? "";
            if (offset < 0 || offset > text.Length) return null;
            int ls = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            int nl = text.IndexOf('\n', offset);
            SourceSpan? before = null;
            foreach (var sp in ScanLine(text, ls, nl < 0 ? text.Length : nl))
            {
                if (offset >= sp.Start && offset < sp.End) return sp;
                if (sp.End == offset) before = sp;
            }
            return before;
        }

        /// <summary>A string token's text, its escapes read (\" \\ \n, as the lexer does).</summary>
        public static string Unquote(string token)
        {
            if (string.IsNullOrEmpty(token) || token[0] != '"') return token;
            var sb = new StringBuilder();
            int end = token.Length > 1 && token[token.Length - 1] == '"' ? token.Length - 1 : token.Length;
            for (int i = 1; i < end; i++)
            {
                char c = token[i];
                if (c == '\\' && i + 1 < end)
                {
                    char n = token[i + 1];
                    if (n == '"' || n == '\\') { sb.Append(n); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The 1-based line an offset is on.</summary>
        public static int LineOf(string text, int offset)
        {
            int line = 1;
            for (int i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>
        /// The functions of a text, by their header lines: a line whose first token, in the first
        /// column, is a name (not a keyword) followed by '(' (the parser's recovery rule). A
        /// function runs to the next header.
        /// </summary>
        public static List<FunctionRange> Functions(string text)
        {
            var list = new List<FunctionRange>();
            text = text ?? "";
            int at = 0, line = 1;
            while (at <= text.Length)
            {
                int nl = text.IndexOf('\n', at);
                int end = nl < 0 ? text.Length : nl;
                if (at < end && NameStart(text[at]))
                {
                    var toks = ScanLine(text, at, end);
                    if (toks.Count >= 2 && toks[0].Kind == SpanKind.Name && toks[1].Kind == SpanKind.Punct && toks[1].Text == "(")
                    {
                        if (list.Count > 0) list[list.Count - 1].End = at;
                        list.Add(new FunctionRange
                        {
                            Name = toks[0].Text, Line = line, Start = at, End = text.Length,
                            NeverCalled = toks.Exists(t => t.Kind == SpanKind.Comment && t.Text.Contains("never called")),
                        });
                    }
                }
                if (nl < 0) break;
                at = nl + 1;
                line++;
            }
            return list;
        }

        /// <summary>The function holding an offset, or null.</summary>
        public static FunctionRange FunctionAt(string text, int offset)
        {
            foreach (var f in Functions(text)) if (f.Holds(offset) || offset == f.End && f.End == (text ?? "").Length) return f;
            return null;
        }

        /// <summary>The offsets of the name tokens equal to name (not in strings or comments), in a function only when one is given.</summary>
        public static List<int> Uses(string text, string name, FunctionRange inFunction = null)
        {
            var list = new List<int>();
            text = text ?? "";
            if (string.IsNullOrEmpty(name)) return list;
            int from = inFunction?.Start ?? 0, to = inFunction?.End ?? text.Length;
            int at = from;
            while (at < to)
            {
                int nl = text.IndexOf('\n', at);
                int end = nl < 0 || nl > to ? to : nl;
                foreach (var t in ScanLine(text, at, end))
                    if (t.Kind == SpanKind.Name && t.Text == name) list.Add(t.Start);
                if (nl < 0 || nl >= to) break;
                at = nl + 1;
            }
            return list;
        }

        /// <summary>
        /// Where name is defined, seen from offset: a local or argument of the function there (its
        /// declaration), a label of that function (name followed by ':' at the start of a line), a
        /// function (its header). Returns the name's offset, or -1.
        /// </summary>
        public static int DefinitionOf(string text, string name, int offset)
        {
            text = text ?? "";
            var fns = Functions(text);
            var here = fns.Find(f => f.Holds(offset));
            if (here != null)
            {
                int at = here.Start;
                bool header = true;
                while (at < here.End)
                {
                    int nl = text.IndexOf('\n', at);
                    int end = nl < 0 || nl > here.End ? here.End : nl;
                    var toks = ScanLine(text, at, end);
                    if (header)
                    {
                        // the arguments, between the header's parentheses
                        for (int i = 2; i < toks.Count && toks[i].Text != ")"; i++)
                            if (toks[i].Kind == SpanKind.Name && toks[i].Text == name) return toks[i].Start;
                        header = false;
                    }
                    else if (toks.Count > 0 && toks[0].Kind == SpanKind.Keyword && toks[0].Text == "int")
                    {
                        foreach (var t in toks) if (t.Kind == SpanKind.Name && t.Text == name) return t.Start;
                    }
                    else if (toks.Count >= 2 && toks[0].Kind == SpanKind.Name && toks[0].Text == name && toks[1].Text == ":") return toks[0].Start;
                    if (nl < 0) break;
                    at = nl + 1;
                }
            }
            var fn = fns.Find(f => f.Name == name);
            return fn != null ? fn.Start : -1;
        }
    }
}
