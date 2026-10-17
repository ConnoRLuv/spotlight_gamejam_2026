using NUnit.Framework;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEditor;
using System.Linq;

namespace SpotlightGameJam.Tests
{
    public sealed class MapRouteSocketSelectionTests
    {
        [Test]
        public void GenuineEndpointsCoverAllFiveSocketPairs()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var templates = asset.RouteProfile.Templates.Where(MapRouteSocketSelector.HasUsableEndpoints).ToArray();
            Assert.That(templates.Length, Is.EqualTo(6), "六张现有原图各有一种真实接法。");
            Assert.That(templates.Select(t => t.SourceSide + ":" + t.TargetSide).Distinct().Count(), Is.EqualTo(5));
        }
        [Test]
        public void LongPaintedBorderDoesNotBecomeASocket()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var paintedBorder = asset.RouteProfile.Templates.Single(t => t.FootprintId.EndsWith(":分支（上左）") && t.SourceSide == MapSocketSide.Up);
            var realOpening = asset.RouteProfile.Templates.Single(t => t.FootprintId.EndsWith(":分支（上左）") && t.SourceSide == MapSocketSide.Left);
            Assert.That(MapRouteSocketSelector.HasUsableEndpoints(paintedBorder), Is.False);
            Assert.That(MapRouteSocketSelector.HasUsableEndpoints(realOpening), Is.True);
        }
        [TestCase(-1, MapSocketSide.Left)]
        [TestCase(1, MapSocketSide.Right)]
        public void ActualArtCanExitEitherSideOfNode(int delta, MapSocketSide side)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            Assert.That(MapRouteSocketSelector.TrySelect(asset.RouteProfile, delta,
                new RouteFitBounds(-63, -63, 63, 63), new RouteFitBounds(delta * 280 - 63, 497, delta * 280 + 63, 623),
                0, 0, out var fit, asset.BuildRouteEndpointCoverage()), Is.True);
            Assert.That(fit.Template.SourceSide, Is.EqualTo(side));
            Assert.That(fit.Template.TargetSide, Is.EqualTo(MapSocketSide.Down));
            AssertCovered(fit.SourcePorts, fit, 0, 0);
            AssertCovered(fit.TargetPorts, fit, delta * 280, 560);
        }

        [TestCase(-1, MapSocketSide.Left, MapSocketSide.Right)]
        [TestCase(1, MapSocketSide.Right, MapSocketSide.Left)]
        public void OccupiedSideSelectsArtEnteringUpperNodeFromOppositeSide(int delta, MapSocketSide occupied, MapSocketSide entry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            Assert.That(MapRouteSocketSelector.TrySelect(asset.RouteProfile, delta,
                new RouteFitBounds(-63, -63, 63, 63), new RouteFitBounds(delta * 280 - 63, 497, delta * 280 + 63, 623),
                1 << (int)occupied, 0, out var fit, asset.BuildRouteEndpointCoverage()), Is.True);
            Assert.That(fit.Template.SourceSide, Is.EqualTo(MapSocketSide.Up));
            Assert.That(fit.Template.TargetSide, Is.EqualTo(entry));
            AssertCovered(fit.SourcePorts, fit, 0, 0);
            AssertCovered(fit.TargetPorts, fit, delta * 280, 560);
        }

        static void AssertCovered(RouteFitBounds ports, MapRouteSocketFit fit, double x, double y)
        {
            Assert.That(fit.X + fit.Scale * ports.MinX, Is.GreaterThanOrEqualTo(x - 63.000001));
            Assert.That(fit.X + fit.Scale * ports.MaxX, Is.LessThanOrEqualTo(x + 63.000001));
            Assert.That(fit.Y + fit.Scale * ports.MinY, Is.GreaterThanOrEqualTo(y - 63.000001));
            Assert.That(fit.Y + fit.Scale * ports.MaxY, Is.LessThanOrEqualTo(y + 63.000001));
        }
    }
}
