using NUnit.Framework;
using RoguelikeMap.Unity;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Serialization;
using RoguelikeMap.Runtime;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>真实地图资产的接点约束接入测试；几何和固定种子验证在此类继续补充。</summary>
    public sealed class MapAssetConstraintTests
    {
        /// <summary>原图图标的完整矩形都需避让；不能仍按原先 130×130 的接入内核检查无关节点。</summary>
        [Test]
        public void NativeIconOuterAreaIsReservedForUnrelatedEdges()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var profile = JsonUtility.FromJson<MapRouteProfile>(JsonUtility.ToJson(asset.RouteProfile));
            int x = (int)asset.Visuals.Where(v => v.Icon).Max(v => v.Icon.rect.width) / 2 - 1;
            profile.Footprints.Add(new MapRouteFootprint { Id = "native-clearance-test", Width = 1, Height = 1, Runs = new[] { 0, 0, 1 } });
            Assert.That(MapAssetGeometry.HitsNode(profile, new MapEdgePlacement { FootprintId = "native-clearance-test", X = x }, new MapNodePlacement()), Is.True);
        }

        /// <summary>使用真实配置验证大量种子，分支覆盖后半段，底部直连优先且入出线共用接点。</summary>
        [Test]
        public void ThousandSeedsRespectPortsPixelsAndBottomPriority()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var generator = MapGenerator.CreateAssetConstrained(asset.RouteProfile);
            int centerCorridors = 0, longBranches = 0, branchCount = 0;
            var mergeDepths = new HashSet<int>();
            for (uint seed = 1; seed <= 1000; seed++)
            {
                var result = generator.Generate(asset.Config, asset.BuildCatalog(), seed);
                Assert.That(result.Success, Is.True, "seed=" + seed + " " + result.DiagnosticsText);
                var graph = result.Graph;
                Assert.That(MapAssetGeometry.Validate(graph, asset.RouteProfile, out string error), Is.True, "seed=" + seed + " " + error);
                Assert.That(graph.Columns, Is.EqualTo(5));
                Assert.That(graph.CountPaths(asset.Config.MaxPathCount + 1), Is.InRange((long)asset.Config.MinPathCount, (long)asset.Config.MaxPathCount));
                var central = graph.Nodes.Where(n => n.Column == graph.Columns / 2).OrderBy(n => n.Row).ToArray();
                if (central.Length == graph.Rows && central.Zip(central.Skip(1), (a, b) => graph.HasEdge(a.Id, b.Id)).All(v => v)) centerCorridors++;
                Assert.That(graph.Nodes.Any(n => n.Row >= graph.Rows / 2 && n.NextIds.Count > 1), Is.True,
                    "seed=" + seed + " 后半段必须仍有可选分支。");
                int straightRows = 0, previousColumn = -1;
                for (int row = 1; row < graph.Rows - 2; row++)
                {
                    // 并行支路和左右转向都打断直线段，只统计同列、单一路线连续推进的层数。
                    var layer = graph.NodesInRow(row).ToArray();
                    if (layer.Length == 1 && layer[0].NextIds.Count <= 1)
                    { straightRows = layer[0].Column == previousColumn ? straightRows + 1 : 1; previousColumn = layer[0].Column; }
                    else { straightRows = 0; previousColumn = -1; }
                    Assert.That(straightRows, Is.LessThanOrEqualTo(3), "seed=" + seed + " 连续过多层退化为同列直线。");
                }
                foreach (var node in graph.Nodes)
                {
                    if (node.NextIds.Count > 1)
                    {
                        int depth = FirstMergeDepth(graph, node);
                        Assert.That(depth, Is.GreaterThanOrEqualTo(3), "seed=" + seed + " 支路需经过至少两个独立节点。");
                        branchCount++; mergeDepths.Add(depth); if (depth >= 5) longBranches++;
                    }
                    // 两条支路至少分别经过两个节点，不能在刚分开的下一层立即回到同一节点。
                    var secondLayer = new HashSet<int>();
                    foreach (int child in node.NextIds)
                        foreach (int target in graph.Get(child).NextIds)
                            Assert.That(secondLayer.Add(target), Is.True, "seed=" + seed + " row=" + node.Row + " 分支过早合流。");
                    Assert.That(node.PrevIds.Count, Is.LessThanOrEqualTo(3));
                    var used = new HashSet<MapSocketSide>();
                    var incoming = graph.AssetLayout.Edges.Where(e => e.ToId == node.Id).ToArray();
                    foreach (var edge in incoming) Assert.That(used.Add(edge.TargetSide), Is.True);
                    foreach (var edge in graph.AssetLayout.Edges.Where(e => e.FromId == node.Id)) Assert.That(used.Add(edge.SourceSide), Is.True);
                    var direct = incoming.FirstOrDefault(e => graph.Get(e.FromId).Column == node.Column);
                    if (direct == null) continue;
                    Assert.That(direct.TargetSide, Is.EqualTo(MapSocketSide.Down));
                    Assert.That(direct.SourceSide, Is.EqualTo(MapSocketSide.Up));
                    Assert.That(incoming[0], Is.SameAs(direct), "seed=" + seed + " row=" + node.Row + " 同列正下方连入必须先占用下侧。");
                }
            }
            // 随机外观用固定种子集合统计，既允许偶尔保留直连，也防止恢复为每张图必有中轴线。
            Assert.That(centerCorridors, Is.LessThanOrEqualTo(100), "中间贯通直线仍占据过多地图。");
            Assert.That(longBranches, Is.GreaterThanOrEqualTo(branchCount * 0.3f), "支路仍过于集中在最短允许距离合流。");
            Assert.That(mergeDepths.Count, Is.GreaterThanOrEqualTo(4), "合流深度缺少变化。");
            TestContext.WriteLine("贯通中路=" + centerCorridors + "/1000；较长支路=" + longBranches + "/" + branchCount);
        }

        /// <summary>从最终可见拓扑逐层寻找任意两条支路的首次相交，不读取生成器的内部偏好。</summary>
        private static int FirstMergeDepth(MapGraph graph, MapNode fork)
        {
            var fronts = fork.NextIds.Select(id => new HashSet<int> { id }).ToArray();
            for (int depth = 2; depth < graph.Rows - fork.Row; depth++)
            {
                fronts = fronts.Select(f => new HashSet<int>(f.SelectMany(id => graph.Get(id).NextIds))).ToArray();
                for (int i = 0; i < fronts.Length; i++) for (int j = i + 1; j < fronts.Length; j++)
                    if (fronts[i].Overlaps(fronts[j])) return depth;
            }
            return -1;
        }

        /// <summary>所有五种接法都要能用原图匹配实际节点，而非仅在配置中存在名称。</summary>
        [Test]
        public void FivePhysicalConnectionsHaveUsableAssets()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var profile = asset.RouteProfile;
            var types = new HashSet<string>();
            foreach (var t in profile.Templates)
            {
                var placements = MapAssetGeometry.GetPlacements(profile, t, new MapNodePlacement(),
                    new MapNodePlacement { NodeId = 1, X = t.ColumnDelta * profile.ColumnSpacing, Y = profile.RowSpacing });
                if (placements.Count > 0) types.Add(t.SourceSide + ":" + t.TargetSide);
            }
            CollectionAssert.AreEquivalent(new[] { "Up:Down", "Right:Down", "Left:Down", "Up:Left", "Up:Right" }, types);
        }

        /// <summary>直接读取原 PNG，烘焙掩码不得遗漏抗锯齿边缘等任何非零透明度像素。</summary>
        [Test]
        public void BakedMasksIncludeEveryNonTransparentPngPixel()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            foreach (var footprint in asset.RouteProfile.Footprints)
            {
                var pixels = ReadOriginalPixels(asset.FindConstraintSprite(footprint.Id));
                var encoded = new bool[pixels.Length];
                for (int i = 0; i < footprint.Runs.Length; i += 3)
                    for (int x = footprint.Runs[i + 1]; x < footprint.Runs[i + 2]; x++) encoded[footprint.Runs[i] * footprint.Width + x] = true;
                int mismatch = -1;
                for (int i = 0; i < pixels.Length; i++) if ((pixels[i].a > 0) != encoded[i]) { mismatch = i; break; }
                Assert.That(mismatch, Is.EqualTo(-1), footprint.Id + " 原始透明像素与烘焙占用不一致");
            }
        }

        /// <summary>独立读取原图逐像素检查实际摆放，不复用生产碰撞函数，包含一像素相邻接触。</summary>
        [TestCase(1u)]
        [TestCase(17u)]
        [TestCase(91u)]
        public void RawPngPixelsDoNotOverlapOutsideSharedNodes(uint seed)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var result = MapGenerator.CreateAssetConstrained(asset.RouteProfile).Generate(asset.Config, asset.BuildCatalog(), seed);
            Assert.That(result.Success, Is.True, result.DiagnosticsText);
            var layout = result.Graph.AssetLayout;
            var originals = asset.RouteProfile.Footprints.ToDictionary(f => f.Id, f => ReadOriginalPixels(asset.FindConstraintSprite(f.Id)));
            var occupied = new Dictionary<long, List<int>>();
            for (int index = 0; index < layout.Edges.Count; index++)
            {
                var edge = layout.Edges[index]; var mask = asset.RouteProfile.FindFootprint(edge.FootprintId);
                var pixels = originals[edge.FootprintId];
                for (int p = 0; p < pixels.Length; p++)
                {
                    if (pixels[p].a == 0) continue;
                    int x = edge.X + p % mask.Width, y = edge.Y + p / mask.Width;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!occupied.TryGetValue(PixelKey(x + dx, y + dy), out var owners)) continue;
                        foreach (int owner in owners)
                        {
                            if (owner == index) continue;
                            var old = layout.Edges[owner];
                            int common = edge.FromId == old.FromId || edge.FromId == old.ToId ? edge.FromId :
                                edge.ToId == old.FromId || edge.ToId == old.ToId ? edge.ToId : -1;
                            var node = layout.FindNode(common);
                            if (node == null || !InsideNode(node, x, y, asset.RouteProfile) || !InsideNode(node, x + dx, y + dy, asset.RouteProfile))
                                Assert.Fail("seed=" + seed + " 原图像素在节点外接触：" + index + "/" + owner + " (" + x + "," + y + ")");
                        }
                    }
                    long key = PixelKey(x, y);
                    if (!occupied.TryGetValue(key, out var at)) { at = new List<int>(); occupied.Add(key, at); }
                    at.Add(index);
                }
            }
        }

        /// <summary>直接载入源文件，避免运行时纹理压缩或 Read/Write 导入开关影响验证。</summary>
        private static Color32[] ReadOriginalPixels(Sprite sprite)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture))); return texture.GetPixels32(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        /// <summary>使用确定性的二维像素身份。</summary>
        private static long PixelKey(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>独立判断原生像素是否落在共同端点的完整节点矩形内。</summary>
        private static bool InsideNode(MapNodePlacement node, int x, int y, MapRouteProfile profile) =>
            Math.Abs(x - node.X) <= profile.NodeWidth / 2 && Math.Abs(y - node.Y) <= profile.NodeHeight / 2;

        /// <summary>透明外框可以相交；可见像素重叠、相邻接触必须拒绝，共同端点内部可以接合。</summary>
        [Test]
        public void CollisionUsesVisiblePixelsAndOnlyExemptsSharedNode()
        {
            var profile = new MapRouteProfile();
            profile.Footprints.Add(new MapRouteFootprint { Id = "a", Width = 20, Height = 20, Runs = new[] { 0, 0, 3 } });
            profile.Footprints.Add(new MapRouteFootprint { Id = "b", Width = 20, Height = 20, Runs = new[] { 10, 10, 15 } });
            var a = new MapEdgePlacement { FootprintId = "a" }; var b = new MapEdgePlacement { FootprintId = "b" };
            Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, null), Is.False);
            b.FootprintId = "a"; Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, null), Is.True);
            b.X = 3; Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, null), Is.True);
            b.X = 4; Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, null), Is.False);
            b.X = 0; Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, new MapNodePlacement()), Is.False);
            Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, new MapNodePlacement { X = 200 }), Is.True);
            Assert.That(MapAssetGeometry.HitsNode(profile, a, new MapNodePlacement()), Is.True);
        }

        /// <summary>即使四条入线分别占不同方向，仍必须拒绝超过三条的入线。</summary>
        [Test]
        public void FourIncomingEdgesAreRejected()
        {
            var graph = new MapGraph { Columns = 5, Rows = 2, AssetLayout = new MapAssetLayout() };
            var target = graph.AddNode(1, 2, NodeTypeKeys.Monster);
            for (int col = 0; col < 4; col++) graph.AddEdge(graph.AddNode(0, col, NodeTypeKeys.Monster).Id, target.Id);
            foreach (var node in graph.Nodes) graph.AssetLayout.Nodes.Add(new MapNodePlacement { NodeId = node.Id });
            Assert.That(MapAssetGeometry.Validate(graph, new MapRouteProfile(), out string error), Is.False);
            StringAssert.Contains("入边超过三条", error);
        }

        /// <summary>共同节点边界外的相邻像素不得豁免，交换两条边也必须得到同一结果。</summary>
        [TestCase(65, 66)]
        [TestCase(66, 65)]
        public void SharedNodeBoundaryDoesNotExemptOutsidePixels(int aX, int bX)
        {
            var profile = new MapRouteProfile();
            profile.Footprints.Add(new MapRouteFootprint { Id = "pixel", Width = 1, Height = 1, Runs = new[] { 0, 0, 1 } });
            var a = new MapEdgePlacement { FootprintId = "pixel", X = aX };
            var b = new MapEdgePlacement { FootprintId = "pixel", X = bX };
            Assert.That(MapAssetGeometry.EdgesConflict(profile, a, b, new MapNodePlacement()), Is.True);
        }

        /// <summary>旧版只有种子的存档必须仍用旧拓扑恢复，不能静默切成新资产布局。</summary>
        [Test]
        public void LegacySeedSnapshotDoesNotMigrateToAssetPipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var original = MapGenerator.CreateDefault().Generate(asset.Config, asset.BuildCatalog(), 17).Graph;
            var oldState = new MapRunState(original);
            oldState.MoveTo(oldState.GetOptions()[0].Id);
            var snapshot = MapSerializer.CaptureRun(original, oldState);
            Assert.That(snapshot.Graph, Is.Null);
            var root = new GameObject("Legacy map restore");
            try
            {
                var behaviour = root.AddComponent<MapGeneratorBehaviour>();
                var fields = new SerializedObject(behaviour); fields.FindProperty("_configAsset").objectReferenceValue = asset;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(behaviour.TryRestore(snapshot), Is.True);
                Assert.That(behaviour.Graph.AssetLayout, Is.Null);
                Assert.That(behaviour.RunState.CurrentNodeId, Is.EqualTo(oldState.CurrentNodeId));
                Assert.That(JsonUtility.ToJson(MapSerializer.ToDto(behaviour.Graph)), Is.EqualTo(JsonUtility.ToJson(MapSerializer.ToDto(original))));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>同种子布局可复现，快照保留节点身份与接点，且 DTO 与运行图不共享可变布局。</summary>
        [Test]
        public void LayoutIsDeterministicAndSnapshotsAreIndependent()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var a = MapGenerator.CreateAssetConstrained(asset.RouteProfile).Generate(asset.Config, asset.BuildCatalog(), 19).Graph;
            var b = MapGenerator.CreateAssetConstrained(asset.RouteProfile).Generate(asset.Config, asset.BuildCatalog(), 19).Graph;
            Assert.That(JsonUtility.ToJson(a.AssetLayout), Is.EqualTo(JsonUtility.ToJson(b.AssetLayout)));
            var dto = MapSerializer.ToDto(a); var restored = MapSerializer.FromDto(dto);
            Assert.That(MapAssetGeometry.Validate(restored, asset.RouteProfile, out string error), Is.True, error);
            dto.AssetLayout.Nodes[0].X += 1000;
            Assert.That(a.AssetLayout.Nodes[0].X, Is.EqualTo(restored.AssetLayout.Nodes[0].X));
            Assert.That(a.Nodes.Select(n => n.Id), Is.EqualTo(restored.Nodes.Select(n => n.Id)));
        }
        /// <summary>当前场景使用分层路径；原美术约束算法仍单独验证。</summary>
        [Test]
        public void CurrentMapUsesLayeredPaths()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var fields = new SerializedObject(asset);
            var enabled = fields.FindProperty("_useAssetConstraints");
            Assert.That(enabled, Is.Not.Null, "缺少资产约束配置入口。");
            Assert.That(enabled.boolValue, Is.False, "当前地图应使用四条逐层路径。");
            Assert.That(asset.Config.Rows, Is.EqualTo(14), "额外挂上 Boss 后共 15 层。");
            Assert.That(asset.Config.Columns, Is.EqualTo(5));
            Assert.That(asset.Config.PathCount, Is.EqualTo(4));
            Assert.That(asset.Visuals.All(v => v.CustomPrefab != null), Is.True, "复用已有节点预制体。");
            Assert.That(asset.Routes.All(r => r.Prefab != null), Is.True, "复用已有路线预制体。");
            var result = asset.CreateGenerator().Generate(asset.Config, asset.BuildCatalog(), 12345);
            Assert.That(result.Success, Is.True, result.DiagnosticsText);
            Assert.That(result.Graph.AssetLayout, Is.Not.Null);
            Assert.That(MapAssetGeometry.Validate(result.Graph, asset.RouteProfile, out string layoutError), Is.True, layoutError);
            Assert.That(result.Graph.Rows, Is.EqualTo(15));
            Assert.That(result.Graph.NodesInRow(14).Single().TypeKey, Is.EqualTo(NodeTypeKeys.Boss));
        }
    }
}
