using System.Linq;
using NUnit.Framework;
using RoguelikeMap.Unity;
using RoguelikeMap.Generation;
using RoguelikeMap.Serialization;
using RoguelikeMap.Validation;
using UnityEngine;
using UnityEditor;

namespace SpotlightGameJam.Tests
{
    public sealed class MapPresetPoolTests
    {
        [Test]
        public void ReplacingPrefabSnapshotInvalidatesParsedData()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var copy = Object.Instantiate(asset.PreGeneratedMaps[0]);
            try
            {
                var before = copy.Data;
                var updated = JsonUtility.FromJson<MapPresetDto>(JsonUtility.ToJson(before));
                updated.Version = 99;
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("_snapshot").stringValue = JsonUtility.ToJson(updated);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.Data.Version, Is.EqualTo(99), "重新烘焙后不能继续返回旧缓存。");
            }
            finally { Object.DestroyImmediate(copy.gameObject); }
        }

        [Test]
        public void Map01ContainsSixBasisPathsCoveringEveryEdge()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var preset = asset.PreGeneratedMaps[0];
            Assert.That(preset.Data.Paths.Count, Is.EqualTo(6));
            Assert.That(preset.Data.Graph.AssetLayout.Edges.Count - preset.Data.Graph.Nodes.Count + 2, Is.EqualTo(6), "应增加实际分支，不能只从原图已有组合走法中挑六条。");
            var graph = MapSerializer.FromDto(preset.Data.Graph);
            var covered = new System.Collections.Generic.HashSet<string>();
            foreach (var path in preset.Data.Paths)
            {
                Assert.That(path.NodeIds.Count, Is.EqualTo(15));
                Assert.That(path.NodeIds.First(), Is.EqualTo(graph.StartNodeId));
                Assert.That(graph.Get(path.NodeIds.Last()).TypeKey, Is.EqualTo(asset.Config.BossTypeKey));
                for (int i = 0; i < 14; i++)
                {
                    Assert.That(graph.HasEdge(path.NodeIds[i], path.NodeIds[i + 1]), Is.True);
                    covered.Add(path.NodeIds[i] + "/" + path.NodeIds[i + 1]);
                }
            }
            Assert.That(preset.Data.Paths.Select(p => string.Join(",", p.NodeIds)).Distinct().Count(), Is.EqualTo(6));
            CollectionAssert.AreEquivalent(graph.AssetLayout.Edges.Select(e => e.FromId + "/" + e.ToId), covered);
        }

        [Test]
        public void Map01PlacesRoomPrefabsUnderNodeAndCoincidesNamedAnchors()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var prefab = asset.PreGeneratedMaps[0];
            var nodes = prefab.Nodes.ToDictionary(n => n.NodeId, n => n.PlacementRoot.transform);
            foreach (var node in prefab.Nodes)
            {
                Assert.That(nodes[node.NodeId].Find("top"), Is.Not.Null, "房间图标应放到具有四向标记的 Node 容器下。");
                if (node.View != null) Assert.That(node.View.transform.localPosition, Is.EqualTo(Vector3.zero));
            }
            foreach (var edge in prefab.Edges)
            {
                var placement = prefab.Data.Graph.AssetLayout.FindEdge(edge.FromId, edge.ToId);
                string source = AnchorName(placement.SourceSide), target = AnchorName(placement.TargetSide);
                var a = edge.View.transform.Find(source); var b = edge.View.transform.Find(target);
                Assert.That(a, Is.Not.Null); Assert.That(b, Is.Not.Null);
                Assert.That(Vector3.Distance(a.position, nodes[edge.FromId].Find(source).position), Is.LessThan(0.02f));
                Assert.That(Vector3.Distance(b.position, nodes[edge.ToId].Find(target).position), Is.LessThan(0.02f));
            }
        }

        private static string AnchorName(RoguelikeMap.Model.MapSocketSide side) => side == RoguelikeMap.Model.MapSocketSide.Up ? "top" :
            side == RoguelikeMap.Model.MapSocketSide.Down ? "bottom" : side == RoguelikeMap.Model.MapSocketSide.Left ? "left" : "right";

        [Test]
        public void CurrentMapUsesTenStoredMapsInsteadOfLiveGeneration()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var serialized = new SerializedObject(asset);
            var enabled = serialized.FindProperty("_usePreGeneratedMaps");
            Assert.That(enabled, Is.Not.Null, "运行时应读取离线地图池。");
            Assert.That(enabled.boolValue, Is.True);
            var maps = serialized.FindProperty("_preGeneratedMaps");
            Assert.That(maps.arraySize, Is.EqualTo(10));
            Assert.That(Enumerable.Range(0, 10).Select(i => maps.GetArrayElementAtIndex(i).objectReferenceValue).Distinct().Count(), Is.EqualTo(10));
        }

        [Test]
        public void EveryPrefabHasFiveUsedColumnsFourCoveringPathsAndValidPixels()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var signatures = new System.Collections.Generic.HashSet<string>();
            for (uint i = 1; i <= 10; i++)
            {
                var result = asset.LoadPreGeneratedMap(i);
                Assert.That(result.Success, Is.True, result.DiagnosticsText);
                var graph = result.Graph;
                var prefab = asset.PreGeneratedMaps[(int)i - 1];
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(prefab), Is.True);
                Assert.That(asset.FindPreGeneratedMapPrefab(graph), Is.SameAs(prefab));
                Assert.That(graph.Rows, Is.EqualTo(15));
                Assert.That(graph.Columns, Is.EqualTo(5));
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, graph.Nodes.Select(n => n.Column).Distinct());
                Assert.That(MapAssetGeometry.Validate(graph, asset.RouteProfile, out string error), Is.True, prefab.name + ": " + error);
                var generator = MapGenerator.CreateLayered(asset.RouteProfile);
                foreach (var node in graph.Nodes) node.IsExemptFromRules = node.Id == graph.StartNodeId || node.Row == 14;
                var context = new MapGenerationContext { Graph = graph, Config = asset.Config, Catalog = asset.BuildCatalog(), Rules = generator.Rules };
                var report = new MapValidationReport();
                foreach (var validator in generator.Validators) validator.Validate(graph, context, report);
                Assert.That(report.IsValid, Is.True, prefab.name + ": " + report.Summarize());
                Assert.That(signatures.Add(string.Join(";", graph.Nodes.Select(n => n.Row + "," + n.Column + ":" + string.Join(",", n.NextIds)))), Is.True, "十张地图的连接布局必须不同。");
                Assert.That(prefab.Data.Paths.Count, Is.EqualTo(prefab.Data.PathCount > 0 ? prefab.Data.PathCount : asset.Config.PathCount));
                var covered = new System.Collections.Generic.HashSet<string>();
                var paths = new System.Collections.Generic.HashSet<string>();
                foreach (var path in prefab.Data.Paths)
                {
                    Assert.That(path.NodeIds.Count, Is.EqualTo(15));
                    Assert.That(path.NodeIds[0], Is.EqualTo(graph.StartNodeId));
                    Assert.That(graph.Get(path.NodeIds[14]).TypeKey, Is.EqualTo(asset.Config.BossTypeKey));
                    Assert.That(paths.Add(string.Join(",", path.NodeIds)), Is.True);
                    for (int j = 0; j < 14; j++)
                    {
                        Assert.That(graph.HasEdge(path.NodeIds[j], path.NodeIds[j + 1]), Is.True);
                        covered.Add(path.NodeIds[j] + "/" + path.NodeIds[j + 1]);
                    }
                }
                CollectionAssert.AreEquivalent(graph.AssetLayout.Edges.Select(e => e.FromId + "/" + e.ToId), covered);
                Assert.That(prefab.Nodes.Count, Is.EqualTo(graph.NodeCount));
                Assert.That(prefab.Edges.Count, Is.EqualTo(graph.AssetLayout.Edges.Count));
                foreach (var node in prefab.Nodes)
                {
                    var rect = node.PlacementRoot != null ? node.PlacementRoot : (RectTransform)node.View.transform;
                    var expected = graph.AssetLayout.FindNode(node.NodeId);
                    if (prefab.UseConnectionAnchors)
                    {
                        Assert.That(node.PlacementRoot, Is.Not.Null);
                        if (node.View != null) Assert.That(((RectTransform)node.View.transform).anchoredPosition, Is.EqualTo(Vector2.zero));
                    }
                    else Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(expected.X, expected.Y)));
                }
                foreach (var edge in prefab.Edges)
                {
                    var rect = (RectTransform)edge.View.transform;
                    var expected = graph.AssetLayout.FindEdge(edge.FromId, edge.ToId);
                    if (!prefab.UseConnectionAnchors)
                        Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(expected.X, expected.Y)));
                    Assert.That(rect.rect.size, Is.EqualTo(edge.View.Image.sprite.rect.size));
                    Assert.That(rect.localScale.x, Is.GreaterThan(0));
                    Assert.That(rect.localScale.x, Is.EqualTo(rect.localScale.y));
                    if (!prefab.UseConnectionAnchors) Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(rect.localRotation, Is.EqualTo(Quaternion.identity));
                }
            }
        }

        [Test]
        public void LoadingAgainCreatesIndependentGraphAndLayout()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var first = asset.LoadPreGeneratedMap(1).Graph;
            var second = asset.LoadPreGeneratedMap(1).Graph;
            first.Nodes[1].TypeKey = "changed";
            first.AssetLayout.Nodes[1].X = -9999;
            Assert.That(second.Nodes[1].TypeKey, Is.Not.EqualTo("changed"));
            Assert.That(second.AssetLayout.Nodes[1].X, Is.Not.EqualTo(-9999));
            Assert.That(asset.FindPreGeneratedMapPrefab(second), Is.SameAs(asset.PreGeneratedMaps[0]));
        }

        [Test]
        public void ChangedSettingsRejectStoredMapWithoutLiveGenerationFallback()
        {
            var source = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var asset = Object.Instantiate(source);
            try
            {
                asset.Config.Columns = 4;
                var result = asset.LoadPreGeneratedMap(1);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Graph, Is.Null);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void RandomSelectionCanUseAllTenPrefabs()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var previous = Random.state;
            try
            {
                Random.InitState(20261017);
                var prefabs = new System.Collections.Generic.HashSet<MapPresetPrefab>();
                for (int i = 0; i < 200; i++) prefabs.Add(asset.FindPreGeneratedMapPrefab(asset.LoadPreGeneratedMap().Graph));
                Assert.That(prefabs.Count, Is.EqualTo(10));
            }
            finally { Random.state = previous; }
        }
    }
}
