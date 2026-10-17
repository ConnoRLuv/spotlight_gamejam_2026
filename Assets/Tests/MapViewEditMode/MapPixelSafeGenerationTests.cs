using NUnit.Framework;
using RoguelikeMap.Generation;
using RoguelikeMap.Unity;
using UnityEditor;

namespace SpotlightGameJam.Tests
{
    public sealed class MapPixelSafeGenerationTests
    {
        [TestCase(3918945954u)]
        [TestCase(1u)]
        [TestCase(12345u)]
        public void CurrentMapHasPixelValidatedAlignedLayout(uint seed)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var result = asset.CreateGenerator().Generate(asset.Config, asset.BuildCatalog(), seed);
            Assert.That(result.Success, Is.True, result.DiagnosticsText);
            Assert.That(result.Graph.Rows, Is.EqualTo(15));
            Assert.That(result.Graph.Columns, Is.EqualTo(5));
            Assert.That(result.Graph.AssetLayout, Is.Not.Null, "像素避让必须参与铺路，不能等显示时再独立选择图片。");
            Assert.That(MapAssetGeometry.Validate(result.Graph, asset.RouteProfile, out string error), Is.True, error);
            Assert.That(result.DiagnosticsText, Does.Contain("铺出 4 条路径"));
            foreach (var node in result.Graph.Nodes)
            {
                var p = result.Graph.AssetLayout.FindNode(node.Id);
                Assert.That(p.X, Is.EqualTo((node.Column - 2) * asset.RouteProfile.ColumnSpacing));
                Assert.That(p.Y, Is.EqualTo(node.Row * asset.RouteProfile.RowSpacing));
            }
        }
    }
}
