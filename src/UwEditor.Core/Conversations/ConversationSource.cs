using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UwEditor.Core.Conversations
{
    /// <summary>A mistake in a conversation's source, at a line and column (both from 1).</summary>
    public sealed class SourceError
    {
        public int Line, Column;
        public string Message;
        public SourceError(int line, int column, string message) { Line = line; Column = column; Message = message; }
        public override string ToString() { return "Line " + Line + ", column " + Column + ": " + Message; }
    }

    /// <summary>
    /// The conversation language (docs/conversation-language.md) read into a syntax tree: the
    /// lexer and the parser. Names are not looked up here; the compiler
    /// (ConversationCompiler.cs) does that.
    /// </summary>
    public static class ConversationSource
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "else", "while", "break", "continue", "return", "goto", "exit", "end", "say", "respond", "menu",
            "fmenu", "switch", "case", "default", "int", "true", "streq", "ptr", "word", "save_reg",
        };

        /// <summary>Reads the source; the errors (at most one per function, as the parser skips to the next function after one) go into errors.</summary>
        public static SourceProgram Parse(string text, List<SourceError> errors)
        {
            var toks = Lex(text ?? "", errors);
            var p = new Parser(toks, errors);
            var prog = p.Program();
            prog.Tokens = toks;
            return prog;
        }

        // ----- lexer -----

        internal enum T { Name, Number, String, StringId, Address, Punct, End }

        internal sealed class Tok
        {
            public T Kind;
            public string Text;
            public int Value;
            public int Line, Col;
            public override string ToString() { return Kind + " " + Text; }
        }

        static readonly string[] puncts = { "==", "!=", "<=", ">=", "&&", "||", "(", ")", "{", "}", "[", "]", ",", ";", ":", "=", "<", ">", "+", "-", "*", "/", "%", "!", "&" };

        internal static List<Tok> Lex(string s, List<SourceError> errors)
        {
            var list = new List<Tok>();
            int i = 0, line = 1, lineStart = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == '\n') { i++; line++; lineStart = i; continue; }
                if (ch == ' ' || ch == '\t' || ch == '\r') { i++; continue; }
                int col = i - lineStart + 1;
                if (ch == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (char.IsLetter(ch) || ch == '_')
                {
                    int b = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    list.Add(new Tok { Kind = T.Name, Text = s.Substring(b, i - b), Line = line, Col = col });
                    continue;
                }
                if (char.IsDigit(ch))
                {
                    int b = i;
                    bool hex = ch == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X');
                    if (hex) i += 2;
                    while (i < s.Length && (hex ? Uri.IsHexDigit(s[i]) : char.IsDigit(s[i]))) i++;
                    if (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_'))
                    {
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                        errors.Add(new SourceError(line, col, "'" + s.Substring(b, i - b) + "' is not a number or a name (a name cannot start with a digit)."));
                        continue;
                    }
                    string t = s.Substring(b, i - b);
                    long v;
                    bool ok = hex ? long.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                                  : long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out v);
                    if (!ok || v > 0xFFFF) { errors.Add(new SourceError(line, col, t + " does not fit in a word (0 to 65535, or -32768 with a minus).")); v = 0; }
                    list.Add(new Tok { Kind = T.Number, Text = t, Value = (int)v, Line = line, Col = col });
                    continue;
                }
                if (ch == '#')
                {
                    int b = ++i;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                    int v;
                    if (i == b || !int.TryParse(s.Substring(b, i - b), NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > 0xFFFF)
                    {
                        errors.Add(new SourceError(line, col, "'#' must be followed by a string's number."));
                        continue;
                    }
                    list.Add(new Tok { Kind = T.StringId, Text = "#" + v, Value = v, Line = line, Col = col });
                    continue;
                }
                if (ch == '@')
                {
                    int b = ++i;
                    while (i < s.Length && Uri.IsHexDigit(s[i])) i++;
                    if (i == b || i - b > 4) { errors.Add(new SourceError(line, col, "'@' must be followed by a code address (up to four hex digits).")); continue; }
                    list.Add(new Tok { Kind = T.Address, Text = s.Substring(b - 1, i - b + 1), Value = int.Parse(s.Substring(b, i - b), NumberStyles.HexNumber, CultureInfo.InvariantCulture), Line = line, Col = col });
                    continue;
                }
                if (ch == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    bool closed = false;
                    while (i < s.Length)
                    {
                        char c = s[i];
                        if (c == '"') { closed = true; i++; break; }
                        if (c == '\n') break;
                        if (c == '\\' && i + 1 < s.Length)
                        {
                            char n = s[i + 1];
                            // \" \\ \n are the view's escapes (Decompiler.Quote); any other backslash stays as it is
                            if (n == '"' || n == '\\') { sb.Append(n); i += 2; continue; }
                            if (n == 'n') { sb.Append('\n'); i += 2; continue; }
                        }
                        sb.Append(c);
                        i++;
                    }
                    if (!closed) errors.Add(new SourceError(line, col, "the string is not closed with '\"' on its line."));
                    list.Add(new Tok { Kind = T.String, Text = sb.ToString(), Line = line, Col = col });
                    continue;
                }
                string p = null;
                foreach (var x in puncts) if (string.CompareOrdinal(s, i, x, 0, x.Length) == 0) { p = x; break; }
                if (p == null)
                {
                    errors.Add(new SourceError(line, col, "'" + ch + "' is not part of the language."));
                    i++;
                    continue;
                }
                list.Add(new Tok { Kind = T.Punct, Text = p, Line = line, Col = col });
                i += p.Length;
            }
            list.Add(new Tok { Kind = T.End, Text = "end of the text", Line = line, Col = i - lineStart + 1 });
            return list;
        }

        sealed class ParseFailed : Exception { }

        sealed class Parser
        {
            readonly List<Tok> t;
            readonly List<SourceError> errors;
            int p;

            public Parser(List<Tok> toks, List<SourceError> errors) { t = toks; this.errors = errors; }

            Tok Peek(int k = 0) { return t[Math.Min(p + k, t.Count - 1)]; }
            Tok Next() { var x = t[p]; if (p < t.Count - 1) p++; return x; }
            bool Is(string punct, int k = 0) { var x = Peek(k); return x.Kind == T.Punct && x.Text == punct; }
            bool IsWord(string w, int k = 0) { var x = Peek(k); return x.Kind == T.Name && x.Text == w; }

            Exception Fail(Tok at, string message)
            {
                errors.Add(new SourceError(at.Line, at.Col, message));
                return new ParseFailed();
            }

            static string Show(Tok x)
            {
                switch (x.Kind)
                {
                    case T.End: return "the end of the text";
                    case T.String: return "a string";
                    default: return "'" + x.Text + "'";
                }
            }

            Tok Expect(string punct, string what = null)
            {
                if (!Is(punct)) throw Fail(Peek(), "expected '" + punct + "'" + (what != null ? " " + what : "") + ", not " + Show(Peek()) + ".");
                return Next();
            }

            Tok ExpectName(string what)
            {
                var x = Peek();
                if (x.Kind != T.Name) throw Fail(x, "expected " + what + ", not " + Show(x) + ".");
                if (Keywords.Contains(x.Text)) throw Fail(x, "'" + x.Text + "' is a keyword; it cannot be " + what + ".");
                return Next();
            }

            public SourceProgram Program()
            {
                var prog = new SourceProgram();
                while (Peek().Kind != T.End)
                {
                    int start = p;
                    try { prog.Functions.Add(Function()); }
                    catch (ParseFailed)
                    {
                        // skip to the next function: a name at the start of a line followed by '(' after a '}' at depth 0
                        p = Math.Max(p, start + 1);
                        Recover();
                    }
                }
                return prog;
            }

            /// <summary>After an error: on to the next line that starts with "name(" in the first column.</summary>
            void Recover()
            {
                while (Peek().Kind != T.End)
                {
                    var x = Peek();
                    if (x.Kind == T.Name && x.Col == 1 && Is("(", 1) && !Keywords.Contains(x.Text)) return;
                    Next();
                }
            }

            SourceFunction Function()
            {
                var f = new SourceFunction { FirstToken = p };
                var name = Peek();
                if (name.Kind == T.Name && Keywords.Contains(name.Text)) throw Fail(name, "a function starts with its name; '" + name.Text + "' is a keyword (is a statement outside a function, or a '}' too many?).");
                if (name.Kind != T.Name) throw Fail(name, "expected a function (its name, then '('), not " + Show(name) + ".");
                Next();
                f.Name = name.Text; f.Line = name.Line; f.Col = name.Col;
                Expect("(", "after the function's name");
                if (!Is(")"))
                {
                    while (true)
                    {
                        var a = ExpectName("an argument's name");
                        f.Params.Add(new SourceVar { Name = a.Text, Line = a.Line, Col = a.Col });
                        if (Is(",")) { Next(); continue; }
                        break;
                    }
                }
                Expect(")", "after the arguments");
                Expect("{", "to start the function's body");
                while (IsWord("int"))
                {
                    Next();
                    while (true)
                    {
                        var v = ExpectName("a local's name");
                        var sv = new SourceVar { Name = v.Text, Line = v.Line, Col = v.Col, Size = 1 };
                        if (Is("["))
                        {
                            Next();
                            var n = Peek();
                            if (n.Kind != T.Number || n.Value < 1) throw Fail(n, "an array's size is a number from 1.");
                            Next();
                            sv.Size = n.Value; sv.Array = true;
                            Expect("]", "after the array's size");
                        }
                        f.Locals.Add(sv);
                        if (Is(",")) { Next(); continue; }
                        break;
                    }
                    Expect(";", "after the locals");
                }
                f.Body = Statements(() => Is("}"));
                f.CloseLine = Peek().Line; f.CloseCol = Peek().Col;
                Expect("}", "to end the function");
                f.LastToken = p - 1;
                return f;
            }

            List<SStmt> Statements(Func<bool> stop)
            {
                var list = new List<SStmt>();
                while (!stop())
                {
                    if (Peek().Kind == T.End) throw Fail(Peek(), "the text ends inside a block: a '}' is missing.");
                    list.Add(Statement());
                }
                return list;
            }

            List<SStmt> Block()
            {
                Expect("{", "to start a block");
                var b = Statements(() => Is("}"));
                Expect("}", "to end the block");
                return b;
            }

            T2 At<T2>(T2 node, Tok tok) where T2 : SNodeBase { node.Line = tok.Line; node.Col = tok.Col; return node; }

            SStmt Statement()
            {
                var x = Peek();
                if (x.Kind == T.Name)
                {
                    switch (x.Text)
                    {
                        case "if": return If();
                        case "while":
                            {
                                Next();
                                Expect("(", "after while");
                                SExpr cond = null;
                                if (IsWord("true") && Is(")", 1)) Next();
                                else cond = Expr();
                                Expect(")", "after the loop's condition");
                                return At(new WhileS { Cond = cond, Body = Block() }, x);
                            }
                        case "break": Next(); Expect(";", "after break"); return At(new JumpS { What = JumpKind.Break }, x);
                        case "continue": Next(); Expect(";", "after continue"); return At(new JumpS { What = JumpKind.Continue }, x);
                        case "exit": Next(); Expect(";", "after exit"); return At(new JumpS { What = JumpKind.Exit }, x);
                        case "end": Next(); Expect(";", "after end"); return At(new JumpS { What = JumpKind.End }, x);
                        case "goto":
                            {
                                Next();
                                var l = ExpectName("a label's name");
                                Expect(";", "after the goto");
                                return At(new JumpS { What = JumpKind.Goto, Label = l.Text, LabelLine = l.Line, LabelCol = l.Col }, x);
                            }
                        case "return":
                            {
                                Next();
                                SExpr e = null;
                                if (!Is(";")) e = Expr();
                                Expect(";", "after return");
                                return At(new ReturnS { Value = e }, x);
                            }
                        case "say":
                        case "respond":
                            {
                                Next();
                                var e = Expr();
                                Expect(";", "after the text");
                                return At(new SayS { Respond = x.Text == "respond", Text = e }, x);
                            }
                        case "menu":
                        case "fmenu":
                            return Menu(null);
                        case "switch": return Switch();
                        case "word":
                            {
                                Next();
                                var n = Peek();
                                if (n.Kind != T.Number) throw Fail(n, "word takes a number (0x0544).");
                                Next();
                                Expect(";", "after the word");
                                return At(new WordS { Value = n.Value }, x);
                            }
                        case "save_reg":
                            {
                                Next();
                                var e = Expr();
                                Expect(";", "after save_reg's value");
                                return At(new SaveRegS { Value = e }, x);
                            }
                        case "else": throw Fail(x, "'else' without an 'if' before it.");
                        case "case": case "default": throw Fail(x, "'" + x.Text + "' outside a switch or menu.");
                        case "int": throw Fail(x, "locals are declared on the function's first line, before any statement.");
                    }
                    if (!Keywords.Contains(x.Text) && Is(":", 1))
                    {
                        Next(); Next();
                        return At(new LabelS { Name = x.Text }, x);
                    }
                }
                if (x.Kind == T.Punct && x.Text == "}") throw Fail(x, "unexpected '}'.");
                if (x.Kind == T.String) throw Fail(x, "a string on its own is not a statement (say it, or is this a menu choice outside a menu?).");
                var target = Expr();
                if (Is("="))
                {
                    var eq = Next();
                    if (IsWord("menu") || IsWord("fmenu")) return Menu(target);
                    var v = Expr();
                    Expect(";", "after the assignment");
                    return At(new AssignS { Target = target, Value = v }, x);
                }
                if (target is CallE)
                {
                    Expect(";", "after the call");
                    return At(new CallS { Call = (CallE)target }, x);
                }
                if (Is("==")) throw Fail(Peek(), "'==' compares; to store a value write '='.");
                throw Fail(Peek(), "expected '=' or ';' after " + Describe(target) + ", not " + Show(Peek()) + " (a statement is an assignment, a call, or a keyword's statement).");
            }

            static string Describe(SExpr e)
            {
                if (e is NameE n) return "'" + n.Name + "'";
                return "the expression";
            }

            SStmt If()
            {
                var x = Next();
                Expect("(", "after if");
                var cond = Expr();
                Expect(")", "after the condition");
                if (IsWord("goto"))
                {
                    var g = Next();
                    var l = ExpectName("a label's name");
                    Expect(";", "after the goto");
                    return At(new JumpS { What = JumpKind.Goto, When = cond, Label = l.Text, LabelLine = l.Line, LabelCol = l.Col }, x);
                }
                var node = At(new IfS { Cond = cond, Then = Block() }, x);
                if (IsWord("else"))
                {
                    Next();
                    if (IsWord("if")) node.Else = new List<SStmt> { If() };
                    else node.Else = Block();
                }
                return node;
            }

            SStmt Menu(SExpr answer)
            {
                var x = Next();
                var m = At(new MenuS { Filtered = x.Text == "fmenu", Answer = answer }, x);
                if (answer != null) { m.Line = answer.Line; m.Col = answer.Col; }
                Expect("{", "after " + x.Text);
                while (!Is("}"))
                {
                    var y = Peek();
                    if (IsWord("default"))
                    {
                        Next();
                        Expect(":", "after default");
                        if (m.Default != null) throw Fail(y, "the menu has two defaults.");
                        m.Default = ChoiceBody();
                        m.DefaultLine = y.Line; m.DefaultCol = y.Col;
                        continue;
                    }
                    if (y.Kind != T.String && y.Kind != T.StringId) throw Fail(y, "expected a choice (a string), 'default:' or '}' in the menu, not " + Show(y) + ".");
                    if (m.Default != null) throw Fail(y, "the choices come before the menu's default.");
                    var ch = new MenuChoice { Text = (StringE)Primary(), Line = y.Line, Col = y.Col };
                    if (IsWord("if"))
                    {
                        var iff = Next();
                        if (!m.Filtered) throw Fail(iff, "only an fmenu's choices have conditions.");
                        Expect("(", "after if");
                        ch.Flag = Expr();
                        Expect(")", "after the choice's condition");
                    }
                    if (Is(";")) { Next(); }
                    else
                    {
                        Expect(":", "or ';' after the choice");
                        ch.Body = ChoiceBody();
                    }
                    m.Choices.Add(ch);
                }
                Expect("}", "to end the menu");
                if (answer != null) Expect(";", "after the menu");
                return m;
            }

            /// <summary>A choice's or a default's statements: up to the next choice, default or the menu's end.</summary>
            List<SStmt> ChoiceBody()
            {
                var list = new List<SStmt>();
                while (true)
                {
                    var y = Peek();
                    if (y.Kind == T.String || y.Kind == T.StringId || IsWord("default") || Is("}") || y.Kind == T.End) break;
                    if (Is("{")) { list.AddRange(Block()); continue; }
                    list.Add(Statement());
                }
                return list;
            }

            SStmt Switch()
            {
                var x = Next();
                Expect("(", "after switch");
                var e = Expr();
                Expect(")", "after the switch's value");
                Expect("{", "to start the switch");
                var s = At(new SwitchS { Value = e }, x);
                while (!Is("}"))
                {
                    var y = Peek();
                    if (IsWord("case"))
                    {
                        Next();
                        bool neg = false;
                        if (Is("-")) { Next(); neg = true; }
                        var n = Peek();
                        if (n.Kind != T.Number) throw Fail(n, "case takes a number.");
                        Next();
                        Expect(":", "after the case's number");
                        if (s.Default != null) throw Fail(y, "the cases come before the switch's default.");
                        int v = neg ? (-n.Value) & 0xFFFF : n.Value;
                        s.Cases.Add(new SwitchCase { Value = v, Line = y.Line, Col = y.Col, Body = CaseBody() });
                        continue;
                    }
                    if (IsWord("default"))
                    {
                        Next();
                        Expect(":", "after default");
                        if (s.Default != null) throw Fail(y, "the switch has two defaults.");
                        s.Default = CaseBody();
                        continue;
                    }
                    throw Fail(y, "expected 'case', 'default' or '}' in the switch, not " + Show(y) + ".");
                }
                Expect("}", "to end the switch");
                return s;
            }

            List<SStmt> CaseBody()
            {
                var list = new List<SStmt>();
                while (!IsWord("case") && !IsWord("default") && !Is("}"))
                {
                    if (Peek().Kind == T.End) throw Fail(Peek(), "the text ends inside a switch: a '}' is missing.");
                    if (Is("{")) { list.AddRange(Block()); continue; }
                    list.Add(Statement());
                }
                return list;
            }

            // ----- expressions -----

            static int Level(string op)
            {
                switch (op)
                {
                    case "||": return 2;
                    case "&&": return 3;
                    case "==": case "!=": return 4;
                    case "<": case "<=": case ">": case ">=": return 5;
                    case "+": case "-": return 6;
                    case "*": case "/": case "%": return 7;
                }
                return 0;
            }

            public SExpr Expr(int min = 2)
            {
                var a = Unary();
                while (true)
                {
                    var x = Peek();
                    if (x.Kind != T.Punct) break;
                    int l = Level(x.Text);
                    if (l == 0 || l < min) break;
                    Next();
                    var b = Expr(l + 1);
                    a = new BinaryE { Op = x.Text, A = a, B = b, Line = a.Line, Col = a.Col, OpLine = x.Line, OpCol = x.Col };
                }
                return a;
            }

            SExpr Unary()
            {
                var x = Peek();
                if (x.Kind == T.Punct && (x.Text == "-" || x.Text == "!" || x.Text == "*" || x.Text == "&"))
                {
                    Next();
                    var e = Unary();
                    return At(new UnaryE { Op = x.Text, A = e }, x);
                }
                return Postfix();
            }

            SExpr Postfix()
            {
                var e = Primary();
                while (Is("["))
                {
                    var x = Next();
                    var i = Expr();
                    Expect("]", "after the index");
                    e = new IndexE { Base = e, Index = i, Line = e.Line, Col = e.Col };
                }
                return e;
            }

            SExpr Primary()
            {
                var x = Peek();
                switch (x.Kind)
                {
                    case T.Number: Next(); return At(new NumberE { Value = x.Value }, x);
                    case T.String:
                        {
                            Next();
                            var s = At(new StringE { Text = x.Text }, x);
                            if (Peek().Kind == T.StringId) s.Id = Next().Value;
                            return s;
                        }
                    case T.StringId: Next(); return At(new StringE { Id = x.Value }, x);
                    case T.Address:
                        {
                            Next();
                            var c = At(new CallE { Name = x.Text, Address = x.Value }, x);
                            Args(c);
                            return c;
                        }
                    case T.Punct:
                        if (x.Text == "(")
                        {
                            Next();
                            var e = Expr();
                            Expect(")", "to close the parenthesis");
                            e.Paren = true;
                            return e;
                        }
                        break;
                    case T.Name:
                        {
                            if (x.Text == "true") { Next(); return At(new NumberE { Value = 1, True = true }, x); }
                            if (x.Text == "streq")
                            {
                                Next();
                                Expect("(", "after streq");
                                var a = Expr();
                                Expect(",", "between streq's strings");
                                var b = Expr();
                                Expect(")", "after streq's strings");
                                return At(new BinaryE { Op = "streq", A = a, B = b, OpLine = x.Line, OpCol = x.Col }, x);
                            }
                            if (x.Text == "ptr")
                            {
                                Next();
                                Expect("(", "after ptr");
                                var a = Expr();
                                Expect(")", "after ptr's value");
                                return At(new PtrE { A = a }, x);
                            }
                            if (Keywords.Contains(x.Text)) throw Fail(x, "'" + x.Text + "' is a keyword and cannot be used in an expression.");
                            Next();
                            if (Is("("))
                            {
                                var c = At(new CallE { Name = x.Text, Address = -1 }, x);
                                Args(c);
                                return c;
                            }
                            return At(new NameE { Name = x.Text }, x);
                        }
                }
                throw Fail(x, "expected a value (a number, a string, a name, a call or '('), not " + Show(x) + ".");
            }

            void Args(CallE c)
            {
                Expect("(", "after the function's name");
                if (!Is(")"))
                {
                    while (true)
                    {
                        c.Args.Add(Expr());
                        if (Is(",")) { Next(); continue; }
                        break;
                    }
                }
                Expect(")", "after the arguments");
            }
        }
    }

    // ----- the syntax tree -----

    public sealed class SourceProgram
    {
        public readonly List<SourceFunction> Functions = new List<SourceFunction>();
        internal List<ConversationSource.Tok> Tokens;

        /// <summary>A function's tokens as one string (kind and text, no positions or comments): equal when the function's text is the same but for spacing and comments.</summary>
        internal string Shape(SourceFunction f) { return Shape(f.FirstToken, f.LastToken); }

        internal string Shape(int first, int last)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = first; i <= last && i < Tokens.Count; i++)
                sb.Append((int)Tokens[i].Kind).Append(':').Append(Tokens[i].Kind == ConversationSource.T.Number || Tokens[i].Kind == ConversationSource.T.StringId ? Tokens[i].Value.ToString() : Tokens[i].Text).Append('\u0001');
            return sb.ToString();
        }
    }

    /// <summary>A function's place in the source found by its braces alone (name, '(' ... ')', '{' ... the matching '}'), whether or not it parses.</summary>
    internal sealed class SourceChunk
    {
        public string Name;
        public int First, Last, Line, Col, EndLine, EndCol;

        public static List<SourceChunk> Split(List<ConversationSource.Tok> t)
        {
            var list = new List<SourceChunk>();
            int i = 0;
            Func<int, string, bool> punct = (k, x) => k < t.Count && t[k].Kind == ConversationSource.T.Punct && t[k].Text == x;
            while (i < t.Count && t[i].Kind != ConversationSource.T.End)
            {
                if (t[i].Kind != ConversationSource.T.Name || !punct(i + 1, "(")) { i++; continue; }
                int k = i + 2;
                while (k < t.Count && t[k].Kind != ConversationSource.T.End && !punct(k, ")") && !punct(k, "{") && !punct(k, "}")) k++;
                if (!punct(k, ")") || !punct(k + 1, "{")) { i++; continue; }
                int depth = 0, e = k + 1;
                for (; e < t.Count && t[e].Kind != ConversationSource.T.End; e++)
                {
                    if (punct(e, "{")) depth++;
                    else if (punct(e, "}") && --depth == 0) break;
                }
                if (e >= t.Count || t[e].Kind == ConversationSource.T.End) break;
                list.Add(new SourceChunk { Name = t[i].Text, First = i, Last = e, Line = t[i].Line, Col = t[i].Col, EndLine = t[e].Line, EndCol = t[e].Col });
                i = e + 1;
            }
            return list;
        }

        public bool Holds(int line, int col)
        {
            if (line < Line || line > EndLine) return false;
            if (line == Line && col < Col) return false;
            if (line == EndLine && col > EndCol) return false;
            return true;
        }
    }

    public sealed class SourceVar : SNodeBase
    {
        public string Name;
        public int Size = 1;
        public bool Array;
    }

    public sealed class SourceFunction : SNodeBase
    {
        public string Name;
        public int CloseLine, CloseCol;
        public readonly List<SourceVar> Params = new List<SourceVar>();
        public readonly List<SourceVar> Locals = new List<SourceVar>();
        public List<SStmt> Body = new List<SStmt>();
        /// <summary>The function's tokens (for telling whether it changed): indexes into the lexer's list.</summary>
        internal int FirstToken, LastToken;
    }

    public abstract class SNodeBase { public int Line, Col; }

    public abstract class SExpr : SNodeBase { public bool Paren; }
    public sealed class NumberE : SExpr { public int Value; public bool True; }
    /// <summary>A string: its text (null for a bare #n) and the number written after it (-1: none).</summary>
    public sealed class StringE : SExpr { public string Text; public int Id = -1; }
    public sealed class NameE : SExpr { public string Name; }
    public sealed class IndexE : SExpr { public SExpr Base, Index; }
    public sealed class UnaryE : SExpr { public string Op; public SExpr A; }
    public sealed class BinaryE : SExpr { public string Op; public SExpr A, B; public int OpLine, OpCol; }
    public sealed class PtrE : SExpr { public SExpr A; }
    /// <summary>A call by name, or of a code address (@FFFF: Address set, Name the text).</summary>
    public sealed class CallE : SExpr { public string Name; public int Address = -1; public readonly List<SExpr> Args = new List<SExpr>(); }

    public abstract class SStmt : SNodeBase { }
    public sealed class AssignS : SStmt { public SExpr Target, Value; }
    public sealed class CallS : SStmt { public CallE Call; }
    public sealed class SayS : SStmt { public bool Respond; public SExpr Text; }
    public sealed class IfS : SStmt { public SExpr Cond; public List<SStmt> Then, Else; }
    public sealed class WhileS : SStmt { public SExpr Cond; public List<SStmt> Body; }
    public enum JumpKind { Break, Continue, Goto, Exit, End }
    /// <summary>break, continue, goto (When: "if (e) goto L;"), exit, end.</summary>
    public sealed class JumpS : SStmt { public JumpKind What; public string Label; public int LabelLine, LabelCol; public SExpr When; }
    public sealed class ReturnS : SStmt { public SExpr Value; }
    public sealed class LabelS : SStmt { public string Name; }
    public sealed class WordS : SStmt { public int Value; }
    public sealed class SaveRegS : SStmt { public SExpr Value; }
    public sealed class MenuChoice : SNodeBase { public StringE Text; public SExpr Flag; public List<SStmt> Body; }
    public sealed class MenuS : SStmt
    {
        public bool Filtered;
        /// <summary>x in "x = menu { ... };", else null.</summary>
        public SExpr Answer;
        public readonly List<MenuChoice> Choices = new List<MenuChoice>();
        public List<SStmt> Default;
        public int DefaultLine, DefaultCol;
    }
    public sealed class SwitchCase : SNodeBase { public int Value; public List<SStmt> Body; }
    public sealed class SwitchS : SStmt
    {
        public SExpr Value;
        public readonly List<SwitchCase> Cases = new List<SwitchCase>();
        public List<SStmt> Default;
    }
}
