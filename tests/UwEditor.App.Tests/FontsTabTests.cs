using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Graphics;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Underworld.Mods;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Graphics ▸ Fonts: editing, the accent table, exports checked by a real font engine (Skia), saving and Make a mod.</summary>
    public class FontsTabTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from, params string[] dirs)
            {
                foreach (var d in dirs)
                {
                    string src = Path.Combine(from, d);
                    if (!Directory.Exists(src)) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        [AvaloniaFact]
        public void The_fonts_tab_edits_a_glyph_maps_a_letter_and_saves_a_font_a_mod_carries()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                Ui.Pump();
                gw.ShowTab(3);
                Ui.Pump();
                var p = gw.FontsPanel;
                Assert.NotNull(p);
                Assert.Equal(6, p.Fonts.ByName.Count);
                Assert.Equal(128, p.Cells.Count);
                p.SelectFont("FONT5X6P.SYS");
                Assert.Equal("FONT5X6P.SYS", p.Font.Name);
                Assert.Contains("'$'", p.FreeCodesText);

                // a code a string uses is refused, a free one takes the letter
                Assert.False(p.SetLetter('a', 'é'));
                Assert.False(p.SetLetter('#', 'é'));             // used by a debug string
                Assert.True(p.SetLetter('$', 'é'));
                Assert.Equal('é', w.ViewModel.Session.Accents.LetterOf('$'));
                Assert.Equal("é", w.AccentSettings().Map['$']);  // kept in the settings for this folder

                // make é at '$' from e with an acute
                p.SelectCode('$');
                p.MakeAccented('e', AccentMark.Acute);
                Assert.True(p.HasChanges);
                Assert.Equal(p.Font.Glyphs['e'].Rows.Skip(1), p.Font.Glyphs['$'].Rows.Skip(1));
                p.Canvas.Paint(0, 0, true);
                p.Undo();
                p.SetWidth(5);
                p.CopyGlyph();
                p.SelectCode(1);
                p.PasteGlyph();
                Assert.True(p.Font.Glyphs[1].SameAs(p.Font.Glyphs['$']));
                p.Undo();

                p.PreviewText = "Café";
                Assert.Contains("pixels wide", p.PreviewInfo);
                Assert.DoesNotContain("No code", p.PreviewInfo);
                p.PreviewText = "Pão";
                Assert.Contains("No code for: ã", p.PreviewInfo);

                // a string with é: the scroll font has it now, FONTBIG (block 1's menus) still shows '$'
                var st = w.Strings();
                st.Set(1, 0, "Café");
                var problems = p.RunChecks();
                Assert.Contains(problems, x => x.Contains("FONTBIG.SYS") && x.Contains("still the shipped '$'"));
                Assert.DoesNotContain(problems, x => x.Contains("FONT5X6P.SYS"));

                // the free-code choices: debug-only symbols join the list
                p.SetFreeChoices(true, false);
                Assert.Contains("'#'", p.FreeCodesText);
                Assert.True(p.SetLetter('#', 'ç'));

                // saving: all 128 glyphs, .ORIG kept, a mod carries the font
                var done = p.Save();
                Assert.Equal(new[] { "FONT5X6P.SYS" }, done);
                string path = Path.Combine(copy.Folder, "DATA", "FONT5X6P.SYS");
                var saved = UwFont.Load(path);
                Assert.Equal(128, saved.Count);
                Assert.True(saved.Glyphs['$'].SameAs(p.Font.Glyphs['$']));
                Assert.True(File.Exists(path + ".ORIG"));
                Assert.False(p.HasChanges);
                var changes = ModMaker.FromOriginals(copy.Folder, GameKind.UW1);
                Assert.Contains(changes.Changed, c => c.Path == "DATA/FONT5X6P.SYS");

                gw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void An_import_and_a_header_change_are_each_one_undo_step()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                Ui.Pump();
                gw.ShowTab(3);
                Ui.Pump();
                var p = gw.FontsPanel;
                // another font's BDF (another height), imported into the scroll font
                p.SelectFont("FONTBIG.SYS");
                string bdf = Path.Combine(copy.Folder, "big.bdf");
                File.WriteAllText(bdf, FontBdf.Export(p.Font, w.ViewModel.Session.Accents));
                p.SelectFont("FONT5X6P.SYS");
                var before = p.Font.ToBytes();
                p.ImportBdf(bdf);
                Assert.False(p.Font.ToBytes().SequenceEqual(before));
                p.Undo();
                Assert.Equal(before, p.Font.ToBytes());
                // the space width: one step, header and all
                int space = p.Font.SpaceWidth;
                p.Font.SpaceWidth = space;   // unchanged: no step
                p.SetSpaceWidth(space + 2);
                Assert.Equal(space + 2, p.Font.SpaceWidth);
                p.Undo();
                Assert.Equal(space, p.Font.SpaceWidth);
                Assert.Equal(before, p.Font.ToBytes());
                gw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void A_red_warning_says_the_executable_must_change_when_the_stock_codes_run_out()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                Ui.Pump();
                gw.ShowTab(3);
                Ui.Pump();
                var p = gw.FontsPanel;
                Assert.False(p.WarningShown);
                Assert.True(p.SetLetter('$', 'é'));
                Assert.False(p.WarningShown);
                // Portuguese needs more letters than UW1's four free symbols
                string left = p.FillPreset("Portuguese");
                Assert.NotEqual("", left);
                Assert.True(p.WarningShown);
                Assert.Contains("EXECUTABLE WOULD HAVE TO BE CHANGED", p.WarningText);
                Assert.Contains("cmp al,7Ah to cmp al,7Fh", p.WarningText);
                Assert.Contains("Exhumed", p.WarningText);
                // a code the stock game draws as '?'
                p.SetFreeChoices(false, true);
                Assert.True(p.SetLetter(1, 'õ'));
                Assert.Contains("õ", p.WarningText);
                Assert.Contains(p.RunChecks(), x => x.Contains("executable would have to be changed"));
                gw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Exports_read_back_and_the_truetype_font_draws_each_glyph_pixel_for_pixel_in_skia()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                Ui.Pump();
                gw.ShowTab(3);
                Ui.Pump();
                var p = gw.FontsPanel;
                Assert.True(p.SetLetter('$', 'é'));
                foreach (var name in new[] { "FONT5X6P.SYS", "FONTBIG.SYS", "FONTCHAR.SYS" })
                {
                    p.SelectFont(name);
                    var before = p.Font.Clone();
                    string png = Path.Combine(copy.Folder, name + ".png"), bdf = Path.Combine(copy.Folder, name + ".bdf"), ttf = Path.Combine(copy.Folder, name + ".ttf");
                    p.ExportPng(png); p.ExportBdf(bdf); p.ExportTtf(ttf);
                    Assert.Equal("", p.ImportBdf(bdf));
                    Assert.Equal(before.ToBytes(), p.Font.ToBytes());
                    Assert.Equal("", p.ImportPng(png));
                    var full = before.Clone(); full.FillTo128();
                    Assert.Equal(full.ToBytes(), p.Font.ToBytes());

                    using (var data = SKData.CreateCopy(File.ReadAllBytes(ttf)))
                    using (var face = SKTypeface.FromData(data))
                    {
                        Assert.NotNull(face);
                        Assert.Equal("UW1 " + Path.GetFileNameWithoutExtension(name), face.FamilyName);
                        Assert.NotEqual(0, face.GetGlyph('é'));
                        Assert.NotEqual(0, face.GetGlyph('A'));
                        int descent = FontBdf.Descent(before), ascent = before.Height - descent;
                        // at a size of one pixel per font pixel, without smoothing, each glyph is its bitmap
                        foreach (char ch in "AgeQ!é")
                        {
                            int code = ch == 'é' ? '$' : ch;
                            using (var bmp = new SKBitmap(before.Columns + 4, before.Height + 4))
                            using (var canvas = new SKCanvas(bmp))
                            using (var font = new SKFont(face, before.Height) { Edging = SKFontEdging.Alias, Hinting = SKFontHinting.None, Subpixel = false })
                            using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
                            {
                                canvas.Clear(SKColors.White);
                                canvas.DrawText(ch.ToString(), 2, 2 + ascent, font, paint);
                                for (int y = 0; y < before.Height; y++)
                                    for (int x = 0; x < before.Columns; x++)
                                        Assert.True(before.Pixel(code, x, y) == (bmp.GetPixel(2 + x, 2 + y).Red < 128), name + " '" + ch + "' pixel " + x + "," + y);
                                Assert.Equal(before.Glyphs[code].Width, (int)Math.Round(font.MeasureText(ch.ToString())));
                            }
                        }
                    }
                }
                gw.Close();
                w.Close();
            }
        }
    }
}
