using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The zone graph's force-directed layout.</summary>
    public class ForceLayoutTests
    {
        static double Dist(double[] x, double[] y, int a, int b) { return Math.Sqrt((x[a] - x[b]) * (x[a] - x[b]) + (y[a] - y[b]) * (y[a] - y[b])); }

        static (double[] X, double[] Y) Run(int n, List<(int A, int B)> links, ForceOptions o, int steps = 600)
        {
            var rnd = new Random(3);
            var x = Enumerable.Range(0, n).Select(i => 1000 + rnd.NextDouble() * 400).ToArray();
            var y = Enumerable.Range(0, n).Select(i => 800 + rnd.NextDouble() * 400).ToArray();
            var st = new ForceState();
            for (int s = 0; s < steps; s++) ForceLayout.Step(x, y, links, null, o, 3600, 2400, st);
            return (x, y);
        }

        [Fact]
        public void Linked_nodes_end_near_each_other_and_apart_from_the_rest()
        {
            // two triangles joined by nothing
            var links = new List<(int, int)> { (0, 1), (1, 2), (2, 0), (3, 4), (4, 5), (5, 3) };
            var (x, y) = Run(6, links, new ForceOptions());
            Assert.All(x.Concat(y), v => Assert.False(double.IsNaN(v)));
            double inside = new[] { Dist(x, y, 0, 1), Dist(x, y, 1, 2), Dist(x, y, 3, 4) }.Max();
            double across = new[] { Dist(x, y, 0, 3), Dist(x, y, 1, 4), Dist(x, y, 2, 5) }.Min();
            Assert.True(inside < across, inside + " vs " + across);
            // the layout settles: the steps shrink to nothing
            var st = new ForceState();
            double last = 0;
            for (int s = 0; s < 400; s++) last = ForceLayout.Step(x, y, links, null, new ForceOptions(), 3600, 2400, st);
            Assert.True(last < 1, "still moving " + last);
        }

        [Fact]
        public void More_attraction_draws_links_shorter_and_more_repulsion_spreads_the_nodes()
        {
            var links = new List<(int, int)> { (0, 1), (1, 2), (2, 3), (3, 4), (4, 0) };
            Func<double, double, double> mean = (a, r) =>
            {
                var (x, y) = Run(5, links, new ForceOptions { Attraction = a, Repulsion = r });
                return links.Average(l => Dist(x, y, l.Item1, l.Item2));
            };
            double usual = mean(1, 1);
            Assert.True(mean(3, 1) < usual);
            Assert.True(mean(1, 3) > usual);
        }

        [Fact]
        public void A_pinned_node_stays_and_nodes_on_one_spot_come_apart()
        {
            var x = new double[] { 500, 500, 500 }; var y = new double[] { 500, 500, 500 };
            var pinned = new[] { true, false, false };
            for (int s = 0; s < 50; s++) ForceLayout.Step(x, y, new List<(int, int)>(), pinned, new ForceOptions(), 3600, 2400);
            Assert.Equal(500, x[0]); Assert.Equal(500, y[0]);
            Assert.True(Dist(x, y, 1, 2) > 50);
            Assert.True(Dist(x, y, 0, 1) > 50);
        }

        [Fact]
        public void Each_level_has_its_own_outline_colour()
        {
            var c = Enumerable.Range(1, 9).Select(ForceLayout.LevelColor).ToList();
            Assert.Equal(9, c.Distinct().Count());
        }
    }
}
