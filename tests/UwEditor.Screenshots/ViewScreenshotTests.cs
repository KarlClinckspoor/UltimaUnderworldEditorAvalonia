using System;
using System.IO;
using System.Numerics;
using UwEditor.Core;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.Screenshots
{
    /// <summary>The editor with its 3D view, on the virtual screen. Pictures are compared with
    /// baselines (recorded on the first run, from your own game files); facts come from the
    /// editor's --state-file.</summary>
    [Collection("desktop")]
    public class ViewScreenshotTests
    {
        readonly Desktop desktop;
        public ViewScreenshotTests(Desktop d) { desktop = d; }

        string Game(string name)
        {
            if (desktop.Missing != null) Assert.Skip(desktop.Missing);
            string g = GameData.Game(name);
            if (g == null) Assert.Skip("no " + name + " game files (set UW_GAMEDATA)");
            return g;
        }

        static string BaselineDir { get { return Path.Combine(Snapshots.RepoRoot, "tests", "UwEditor.Screenshots", "baselines"); } }

        void Expect(string name, RawImage image)
        {
            var r = Snapshots.Check(BaselineDir, name, image, new MagickCodec(desktop), maxFraction: 0.02);
            if (r.Outcome == SnapshotOutcome.Recorded) Assert.Skip(r.Message);
            Assert.True(r.Outcome == SnapshotOutcome.Matched, r.Message);
        }

        /// <summary>A drawn level has many colours; a blank or failed GL view has one or two.</summary>
        static void AssertDrawn(Editor e, RawImage window)
        {
            Assert.Null(e.State.glError);
            Assert.True(e.ViewPart(window).DistinctColours() > 200, "the 3D view shows a level");
        }

        [Fact]
        public void Uw1_opens_over_the_start_square()
        {
            using (var e = new Editor(desktop, Game("UW")))
            {
                var s = e.State;
                Assert.Equal(1, s.level);
                Assert.Equal(32.5f, s.target[0], 2);
                Assert.Equal(2.5f, s.target[2], 2);
                var shot = e.Capture();
                AssertDrawn(e, shot);
                Expect("gl-uw1-start", shot);
            }
        }

        [Fact]
        public void Uw2_castle_draws_its_furniture_as_models()
        {
            using (var e = new Editor(desktop, Game("UW2")))
            {
                var shot = e.Capture();
                AssertDrawn(e, shot);
                Expect("gl-uw2-start", shot);
            }
        }

        [Fact]
        public void A_selected_wall_lever_is_framed_head_on()
        {
            using (var e = new Editor(desktop, Game("UW"), "--select", "623"))
            {
                var s = e.State;
                Assert.Equal(623, s.selectedObject);
                Assert.Equal(180f, s.yaw, 1);   // the lever faces south: seen looking south
                var shot = e.Capture();
                AssertDrawn(e, shot);
                Expect("gl-uw1-lever", shot);
            }
        }

        [Fact]
        public void Clicking_the_sack_beside_the_start_selects_it()
        {
            string g = Game("UW");
            using (var e = new Editor(desktop, g))
            {
                // where the sack (#942, tile 33, 3) is on the screen, from the camera the editor reports
                var s = e.State;
                var cam = new OrbitCamera { Target = new Vector3(s.target[0], s.target[1], s.target[2]), Yaw = s.yaw, Pitch = s.pitch, Distance = s.distance };
                var level = GameSession.Open(g).LoadLevel(1);
                var sack = level.Objects[942];
                var p = new Vector3((float)sack.WorldX, (float)sack.WorldZ + 0.08f, (float)sack.WorldY);
                var c = Vector4.Transform(new Vector4(p, 1), cam.View * cam.Projection((float)(s.view[2] / s.view[3])));
                double x = (c.X / c.W * 0.5 + 0.5) * s.view[2], y = (0.5 - c.Y / c.W * 0.5) * s.view[3];
                e.ClickView(x, y);
                Assert.Equal(942, e.State.selectedObject);
                Expect("gl-uw1-sack-selected", e.Capture());
            }
        }

        [Fact]
        public void Keys_turn_the_camera_and_T_looks_straight_down()
        {
            using (var e = new Editor(desktop, Game("UW")))
            {
                var before = e.Capture();
                e.ClickView(5, 5);            // gives the view the keyboard (and selects whatever is in that corner)
                e.Key("e");
                Assert.Equal(15f, e.State.yaw, 1);
                e.Key("t");
                Assert.Equal(89f, e.State.pitch, 1);
                var after = e.Capture();
                int changed = 0;
                for (int i = 0; i < after.Rgba.Length; i += 4) if (after.Rgba[i] != before.Rgba[i] || after.Rgba[i + 1] != before.Rgba[i + 1]) changed++;
                Assert.True(changed > after.Width * after.Height / 5, "the view changed");
                Assert.True(e.ViewPart(after).DistinctColours() > 200);
                Expect("gl-uw1-topdown", after);
            }
        }

        [Fact]
        public void The_grid_and_its_labels_show_and_hide_with_H()
        {
            using (var e = new Editor(desktop, Game("UW")))
            {
                Assert.True(e.State.grid);
                Expect("gl-uw1-grid-on", e.Capture());
                e.ClickView(5, 5);
                e.Key("h");
                Assert.False(e.State.grid);
                Expect("gl-uw1-grid-off", e.Capture());
            }
        }
    }
}
