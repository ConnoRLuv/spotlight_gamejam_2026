using System.Linq;
using NUnit.Framework;
using RoguelikeMap.Model;
using RoguelikeMap.Generation;
using RoguelikeMap.Serialization;
using RoguelikeMap.Validation;
using RoguelikeMap.Unity;
using UnityEditor;

namespace SpotlightGameJam.Tests
{
    public sealed class MapBlankLayoutTests
    {
        [Test]
        public void RoomsVaryWhileEveryLayoutAndItsRulesRemainValid()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            for (uint map = 1; map <= 10; map++)
            {
                var variants = new System.Collections.Generic.HashSet<string>();
                for (uint seed = 1; seed <= 100; seed++)
                {
                    var result = asset.LoadPreGeneratedMap(map, seed);
                    Assert.That(result.Success, Is.True, result.DiagnosticsText);
                    var graph = result.Graph;
                    Assert.That(asset.FindPreGeneratedMapPrefab(graph), Is.SameAs(asset.PreGeneratedMaps[(int)map - 1]));
                    variants.Add(string.Join(",", graph.Nodes.Select(n => n.TypeKey)));
                    var generator = MapGenerator.CreateLayered(asset.RouteProfile);
                    var context = new MapGenerationContext { Graph = graph, Config = asset.Config, Catalog = asset.BuildCatalog(), Rules = generator.Rules };
                    var report = new MapValidationReport();
                    foreach (var validator in generator.Validators) validator.Validate(graph, context, report);
                    Assert.That(report.IsValid, Is.True, report.Summarize());
                    var restored = MapSerializer.FromDto(MapSerializer.ToDto(graph));
                    CollectionAssert.AreEqual(graph.Nodes.Select(n => n.TypeKey), restored.Nodes.Select(n => n.TypeKey));
                    Assert.That(asset.FindPreGeneratedMapPrefab(restored), Is.SameAs(asset.PreGeneratedMaps[(int)map - 1]));
                }
                Assert.That(variants.Count, Is.GreaterThan(1), "同一张布局应有不同房间分布。");
                CollectionAssert.AreEqual(asset.LoadPreGeneratedMap(map, 123u).Graph.Nodes.Select(n => n.TypeKey),
                    asset.LoadPreGeneratedMap(map, 123u).Graph.Nodes.Select(n => n.TypeKey));
            }
        }

        [Test]
        public void AllTenLayoutsContainOnlyStartBossAndEmptyNodeContainers()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            foreach (var preset in asset.PreGeneratedMaps)
            {
                foreach (var binding in preset.Nodes)
                {
                    var node = preset.Data.Graph.Nodes.Single(n => n.Id == binding.NodeId);
                    Assert.That(binding.PlacementRoot, Is.Not.Null);
                    if (node.Row == 0 || node.Row == 14) Assert.That(binding.View, Is.Not.Null);
                    else
                    {
                        Assert.That(node.TypeKey, Is.EqualTo(NodeTypeKeys.Unassigned));
                        Assert.That(binding.View, Is.Null, "中间节点不应预先带房间图标。");
                        Assert.That(binding.PlacementRoot.GetComponentsInChildren<MapNodeView>(true), Is.Empty);
                    }
                }
            }
        }
    }
}
