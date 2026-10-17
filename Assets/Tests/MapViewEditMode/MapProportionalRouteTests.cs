using NUnit.Framework;
using RoguelikeMap.Generation;

namespace SpotlightGameJam.Tests
{
    public sealed class MapProportionalRouteTests
    {
        [Test]
        public void UniformFitCoversBothGroupsOfPorts()
        {
            var source = new RouteFitBounds(0, 0, 36, 38);
            var target = new RouteFitBounds(225, 520, 265, 590);
            var firstNode = new RouteFitBounds(-63, -63, 63, 63);
            var lastNode = new RouteFitBounds(217, 497, 343, 623);
            Assert.That(MapProportionalRouteFit.TryFit(source, target, firstNode, lastNode, 1,
                out double scale, out double x, out double y), Is.True);
            AssertCovered(source, firstNode, scale, x, y);
            AssertCovered(target, lastNode, scale, x, y);
        }

        [Test]
        public void ImpossibleFitDoesNotStretchOrRotate()
        {
            var source = new RouteFitBounds(0, 0, 10, 10);
            var target = new RouteFitBounds(0, 100, 10, 110);
            Assert.That(MapProportionalRouteFit.TryFit(source, target,
                new RouteFitBounds(-5, -5, 5, 5), new RouteFitBounds(995, 95, 1005, 105), 1,
                out _, out _, out _), Is.False);
        }

        static void AssertCovered(RouteFitBounds ports, RouteFitBounds node, double scale, double x, double y)
        {
            Assert.That(scale, Is.GreaterThan(0));
            Assert.That(x + ports.MinX * scale, Is.GreaterThanOrEqualTo(node.MinX - 0.000001));
            Assert.That(x + ports.MaxX * scale, Is.LessThanOrEqualTo(node.MaxX + 0.000001));
            Assert.That(y + ports.MinY * scale, Is.GreaterThanOrEqualTo(node.MinY - 0.000001));
            Assert.That(y + ports.MaxY * scale, Is.LessThanOrEqualTo(node.MaxY + 0.000001));
        }
    }
}
