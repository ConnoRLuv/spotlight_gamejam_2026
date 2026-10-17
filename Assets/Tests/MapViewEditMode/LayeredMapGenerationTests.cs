using System;
using System.Linq;
using NUnit.Framework;
using RoguelikeMap.Config;
using RoguelikeMap.Core;
using RoguelikeMap.Generation;
using RoguelikeMap.Generation.Steps;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;

namespace SpotlightGameJam.Tests
{
    public sealed class LayeredMapGenerationTests
    {
        static MapGenerationConfig CreateCurrentConfig()
        {
            var config = MapGenerationConfig.CreateDefault();
            config.Rows = 14; // 常规层含起点，额外挂上 Boss 后总计 15 层。
            config.Columns = 5;
            config.PathCount = 4;
            config.ConstrainBossApproach = true;
            config.FixedRows[1] = new FixedRowEntry { Row = 13, TypeKey = NodeTypeKeys.Shop };
            return config;
        }

        [Test]
        public void ThousandSeedsRespectTopologyRoomsAndDeterminism()
        {
            var config = CreateCurrentConfig();
            var catalog = NodeTypeCatalog.CreateDefault();
            var generator = MapGenerator.CreateLayered();
            for (uint seed = 1; seed <= 1000; seed++)
            {
                var result = generator.Generate(config, catalog, seed);
                Assert.That(result.Success, Is.True, "seed=" + seed + " " + result.DiagnosticsText);
                var graph = result.Graph;
                Assert.That(graph.Rows, Is.EqualTo(15), "总共 15 层，包含起点和 Boss");
                Assert.That(graph.Columns, Is.EqualTo(5));
                Assert.That(graph.AssetLayout, Is.Null);
                Assert.That(graph.NodesInRow(0).Count(), Is.EqualTo(1));
                Assert.That(graph.NodesInRow(14).Single().TypeKey, Is.EqualTo(NodeTypeKeys.Boss));
                Assert.That(result.DiagnosticsText, Does.Contain("4 条路径"));
                Assert.That(graph.CountPaths(config.MaxPathCount + 1), Is.InRange((long)config.MinPathCount, (long)config.MaxPathCount));
                AssertEdges(graph, seed);
                var context = new MapGenerationContext { Config = config, Graph = graph, Catalog = catalog };
                foreach (var node in graph.Nodes)
                {
                    Assert.That(node.IsAssigned, Is.True);
                    Assert.That(MapRuleSet.CreateDefault().Evaluate(node, node.TypeKey, context).Allowed, Is.True);
                    if (node.Row == 1) Assert.That(node.TypeKey, Is.EqualTo(NodeTypeKeys.Monster));
                    if (node.Row == 13) Assert.That(node.TypeKey, Is.EqualTo(NodeTypeKeys.Shop));
                }
                if (seed <= 20)
                {
                    var again = generator.Generate(config, catalog, seed).Graph;
                    Assert.That(Fingerprint(again), Is.EqualTo(Fingerprint(graph)));
                    var restored = RoguelikeMap.Serialization.MapSerializer.FromDto(RoguelikeMap.Serialization.MapSerializer.ToDto(graph));
                    Assert.That(Fingerprint(restored), Is.EqualTo(Fingerprint(graph)));
                }
            }
        }

        [Test]
        public void ImpossibleRoomWeightsFailInsteadOfInventingRooms()
        {
            var config = CreateCurrentConfig();
            config.Weights.Clear();
            config.MaxGenerationAttempts = 2;
            var result = MapGenerator.CreateLayered().Generate(config, NodeTypeCatalog.CreateDefault(), 1);
            Assert.That(result.Success, Is.False);
            Assert.That(result.DiagnosticsText, Does.Contain("没有符合权重和规则的房间类型"));
        }

        static string Fingerprint(MapGraph graph) => string.Join(";", graph.Nodes.Select(n =>
            n.Id + ":" + n.Row + ":" + n.Column + ":" + n.TypeKey + ":" + string.Join(",", n.NextIds)));

        [Test]
        public void FourCarvedPathsNeverCrossBeforeAnyRepair()
        {
            for (uint seed = 1; seed <= 1000; seed++)
            {
                var config = CreateCurrentConfig();
                var context = new MapGenerationContext
                {
                    Config = config, Graph = new MapGraph { Rows = config.Rows, Columns = config.Columns },
                    Random = new XorShiftRandom(seed)
                };
                new CarvePathsStep().Execute(context);
                AssertEdges(context.Graph, seed);
                Assert.That(context.Graph.CountPaths(100000), Is.GreaterThanOrEqualTo(1), "seed=" + seed);
                Assert.That(context.Diagnostics.Single(), Does.Contain("4 条路径"));
            }
        }

        public static void AssertEdges(MapGraph graph, uint seed)
        {
            foreach (var from in graph.Nodes)
            {
                Assert.That(from.Column, Is.InRange(0, graph.Columns - 1));
                foreach (int id in from.NextIds)
                {
                    var to = graph.Get(id);
                    Assert.That(to.Row, Is.EqualTo(from.Row + 1), "seed=" + seed);
                    Assert.That(Math.Abs(to.Column - from.Column), Is.LessThanOrEqualTo(1), "seed=" + seed);
                    foreach (var right in graph.NodesInRow(from.Row).Where(n => n.Column > from.Column))
                        foreach (int rightId in right.NextIds)
                            Assert.That(to.Column, Is.LessThanOrEqualTo(graph.Get(rightId).Column),
                                "seed=" + seed + " crossing at row=" + from.Row);
                }
            }
        }
    }
}
