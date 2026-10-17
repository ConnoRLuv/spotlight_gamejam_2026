using NUnit.Framework;
using RoguelikeMap.Unity;
using RoguelikeMap.Model;
using RoguelikeMap.Runtime;
using RoguelikeMap.Generation;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证实际地图美术的端点与节点连接，避免斜线按图片中央端点错误旋转。</summary>
    public sealed class MapRouteLayoutTests
    {
        /// <summary>原尺寸模式下每种房间图标使用自身 Sprite 尺寸，不再统一压缩成 130×130。</summary>
        [Test]
        public void NativeNodesUseTheirOwnSpriteDimensions()
        {
            var root = new GameObject("Native icons test", typeof(RectTransform), typeof(MapGeneratorBehaviour));
            try
            {
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
                var view = root.AddComponent<MapGraphView>();
                var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
                var fields = new SerializedObject(view);
                fields.FindProperty("_configAsset").objectReferenceValue = asset;
                fields.FindProperty("_style.RouteLayout").enumValueIndex = (int)MapRouteLayout.NativeSize;
                fields.FindProperty("_style.BackgroundWidthRatio").floatValue = 0;
                fields.FindProperty("_style.DisplayScale").floatValue = 1;
                fields.ApplyModifiedPropertiesWithoutUndo();
                var graph = new MapGraph { Columns = 5, Rows = asset.Visuals.Count };
                int row = 0;
                foreach (var visual in asset.Visuals) if (visual.Icon) graph.AddNode(row++, 2, visual.TypeKey);
                view.Render(graph, asset.BuildCatalog(), new MapRunState(graph));
                foreach (var node in root.GetComponentsInChildren<MapNodeView>())
                {
                    var image = node.GetComponent<Image>();
                    Assert.That(((RectTransform)node.transform).rect.size, Is.EqualTo(image.sprite.rect.size), node.Node.TypeKey);
                    Assert.That(node.transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(node.transform.parent.localScale, Is.EqualTo(Vector3.one));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>地图底板占背景实际宽度的 65%；窗口改变后仍保持比例，不能退回固定倍率。</summary>
        [TestCase(1280, 720)]
        [TestCase(1024, 768)]
        [TestCase(1920, 1080)]
        [TestCase(3840, 2160)]
        public void BoardWidthMatchesReferenceBackgroundRatio(float width, float height)
        {
            var root = new GameObject("Reference ratio map", typeof(RectTransform), typeof(MapGeneratorBehaviour));
            try
            {
                var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(width, height);
                var view = root.AddComponent<MapGraphView>();
                var so = new SerializedObject(view);
                var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
                so.FindProperty("_configAsset").objectReferenceValue = asset;
                so.FindProperty("_style.RouteLayout").enumValueIndex = (int)MapRouteLayout.NativeSize;
                so.FindProperty("_style.ColumnSpacing").floatValue = 230;
                so.FindProperty("_style.RowSpacing").floatValue = 500;
                so.FindProperty("_style.BackgroundWidthRatio").floatValue = 0.65f;
                so.ApplyModifiedPropertiesWithoutUndo();
                var graph = new MapGraph { Columns = 5, Rows = 2 };
                graph.AddNode(0, 0, NodeTypeKeys.Monster); graph.AddNode(1, 4, NodeTypeKeys.Monster);
                view.Render(graph, asset.BuildCatalog(), new MapRunState(graph));
                for (int resize = 0; resize < 2; resize++)
                {
                    var board = root.GetComponentInChildren<RawImage>().rectTransform;
                    Assert.That(board.GetComponent<RawImage>().uvRect, Is.EqualTo(new Rect(0, 0, 1, 1)), "底纹必须完整采样一次，不能循环重复。");
                    float iconHeight = asset.GetVisual(NodeTypeKeys.Monster).Icon.rect.height;
                    Assert.That(board.rect.height, Is.EqualTo(500 + iconHeight + asset.BackdropPadding * 2).Within(0.01f), "单张底纹必须铺满原尺寸节点和整段地图。");
                    float backgroundWidth = Mathf.Min(rect.rect.width, rect.rect.height * asset.BackgroundSprite.rect.width / asset.BackgroundSprite.rect.height);
                    Assert.That(board.rect.width * board.parent.localScale.x / backgroundWidth, Is.EqualTo(0.65f).Within(0.0001f));
                    Assert.That(board.parent.localScale.x, Is.EqualTo(board.parent.localScale.y));
                    rect.sizeDelta *= 0.8f;
                    // 编辑模式没有运行时生命周期，直接调用布局更新，避免 SendMessage 的引擎运行状态断言。
                    typeof(MapGraphView).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>原尺寸与整体缩小后，剪枝地图的两端节点均可定位，节点自身尺寸不变。</summary>
        [TestCase(1f)]
        [TestCase(0.2f)]
        [TestCase(0.5f)]
        public void AsymmetricOccupiedColumnsRemainReachableAtDisplayScale(float displayScale)
        {
            var root = new GameObject("Asymmetric map", typeof(RectTransform), typeof(MapGeneratorBehaviour));
            try
            {
                var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(600, 800);
                var view = root.AddComponent<MapGraphView>();
                var so = new SerializedObject(view);
                var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
                so.FindProperty("_configAsset").objectReferenceValue = asset;
                so.FindProperty("_style.RouteLayout").enumValueIndex = (int)MapRouteLayout.NativeSize;
                so.FindProperty("_style.ColumnSpacing").floatValue = 440;
                so.FindProperty("_style.RowSpacing").floatValue = 750;
                Assert.That(so.FindProperty("_style.DisplayScale"), Is.Not.Null, "需要独立的地图整体显示倍率。");
                so.FindProperty("_style.DisplayScale").floatValue = displayScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                var graph = new MapGraph { Columns = 5, Rows = 2 };
                var left = graph.AddNode(0, 2, NodeTypeKeys.Monster);
                var right = graph.AddNode(1, 4, NodeTypeKeys.Monster);
                view.Render(graph, asset.BuildCatalog(), new MapRunState(graph));
                foreach (var node in new[] { left, right })
                {
                    view.ScrollToNode(node.Id);
                    var face = root.GetComponentsInChildren<MapNodeView>().Single(v => v.Node == node);
                    float x = rect.InverseTransformPoint(face.transform.position).x;
                    Assert.That(Mathf.Abs(x), Is.LessThanOrEqualTo(300), "剪枝后的外侧节点不能超出视口。");
                    Assert.That(face.transform.parent.localScale, Is.EqualTo(new Vector3(displayScale, displayScale, 1)));
                    Assert.That(face.transform.localScale, Is.EqualTo(Vector3.one));
                }
                var faces = root.GetComponentsInChildren<MapNodeView>();
                var a = faces.Single(v => v.Node == left).transform;
                var b = faces.Single(v => v.Node == right).transform;
                float actualDistance = Vector3.Distance(a.position, b.position);
                Assert.That(actualDistance, Is.EqualTo(Vector2.Distance(view.GetPosition(left), view.GetPosition(right)) * displayScale).Within(0.01f),
                    "节点间距必须与路线一起等比缩小。");
            }
            finally { Object.DestroyImmediate(root); }
        }
        /// <summary>六方向均从父节点接到子节点；原生摆放保留电路图的水平、垂直线段。</summary>
        [TestCase(MapRouteDirection.Up, 0, 500)]
        [TestCase(MapRouteDirection.UpRight, 230, 500)]
        [TestCase(MapRouteDirection.UpLeft, -230, 500)]
        [TestCase(MapRouteDirection.Down, 0, -500)]
        [TestCase(MapRouteDirection.DownRight, 230, -500)]
        [TestCase(MapRouteDirection.DownLeft, -230, -500)]
        public void NativeArtKeepsOriginalSizeAndConnectsBothNodes(MapRouteDirection direction, float x, float y)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var route = asset.GetRoute(direction);
            Assert.That(route.Sprite, Is.Not.Null);
            var root = new GameObject("Route geometry test", typeof(RectTransform), typeof(Image), typeof(MapEdgeView));
            try
            {
                var rect = root.GetComponent<RectTransform>();
                rect.pivot = route.SourceEnd;
                var edge = root.GetComponent<MapEdgeView>();
                edge.SetSprite(route.Sprite, Color.white); edge.Configure(route.TargetEnd, 1, 0);
                Assert.That(route.SourcePorts.Length, Is.GreaterThan(0), "原图起始线头必须已校准。");
                Assert.That(route.TargetPorts.Length, Is.GreaterThan(0), "原图终止线头必须已校准。");
                var from = new Vector2(43, 71); var to = from + new Vector2(x, y);
                edge.Place(from, to, MapRouteLayout.NativeSize, 14);
                Assert.That(rect.rect.size, Is.EqualTo(route.Sprite.rect.size), "原图矩形必须保持原始像素尺寸。");
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one), "不能通过 Transform 缩放原图。");
                Assert.That(Quaternion.Angle(rect.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                // 实际地图仅向上连接。向下图片保留旧枚举兼容性，仅检查不缩放、不补线。
                if (direction == MapRouteDirection.Up || direction == MapRouteDirection.UpRight || direction == MapRouteDirection.UpLeft)
                {
                    foreach (var port in route.SourcePorts) AssertPortCovered(rect, port, from);
                    foreach (var port in route.TargetPorts) AssertPortCovered(rect, port, to);
                }
                edge.Place(from, to, MapRouteLayout.NativeSize, 14);
                Assert.That(root.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(1), "每条边只能使用一张完整美术，不能生成补线。");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(MapRouteDirection.Up, 0)]
        [TestCase(MapRouteDirection.UpRight, 280)]
        [TestCase(MapRouteDirection.UpLeft, -280)]
        public void ProportionalRoutesKeepOrientationAndConnectBothNodeCores(MapRouteDirection direction, float x)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var route = asset.GetRoute(direction);
            var parent = new GameObject("Scaled route parent", typeof(RectTransform));
            var owner = new GameObject("Proportional route", typeof(RectTransform), typeof(Image), typeof(MapEdgeView));
            owner.transform.SetParent(parent.transform, false);
            parent.transform.localScale = new Vector3(0.4f, 0.4f, 1);
            try
            {
                var edge = owner.GetComponent<MapEdgeView>();
                edge.SetSprite(route.Sprite, Color.white);
                var from = new Vector2(43, 71);
                var to = from + new Vector2(x, 560);
                Assert.That(edge.PlaceProportional(from, to, route, new Vector2(130, 130), new Vector2(130, 130)), Is.True);
                var rect = (RectTransform)owner.transform;
                Assert.That(rect.rect.size, Is.EqualTo(route.Sprite.rect.size));
                Assert.That(rect.localScale.x, Is.EqualTo(rect.localScale.y).Within(0.0001f));
                Assert.That(Quaternion.Angle(rect.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                foreach (var ports in new[] { route.SourcePorts, route.TargetPorts })
                foreach (var port in ports)
                {
                    var actual = (Vector2)parent.transform.InverseTransformPoint(rect.TransformPoint(Vector2.Scale(port - rect.pivot, rect.rect.size)));
                    var node = ports == route.SourcePorts ? from : to;
                    Assert.That(Mathf.Abs(actual.x - node.x), Is.LessThanOrEqualTo(63.01f));
                    Assert.That(Mathf.Abs(actual.y - node.y), Is.LessThanOrEqualTo(63.01f));
                }
            }
            finally { Object.DestroyImmediate(parent); }
        }

        /// <summary>真实线头必须直接进入节点矩形内部，由节点覆盖，不需要补充线段。</summary>
        private static void AssertPortCovered(RectTransform rect, Vector2 port, Vector2 node)
        {
            var actual = (Vector2)rect.TransformPoint(Vector2.Scale(port - rect.pivot, rect.rect.size));
            Assert.That(Mathf.Abs(actual.x - node.x), Is.LessThanOrEqualTo(65.01f));
            Assert.That(Mathf.Abs(actual.y - node.y), Is.LessThanOrEqualTo(65.01f));
        }

        /// <summary>使用实际配置验证多种种子，包括首领入口，禁止跨越两列及生成失败。</summary>
        [Test]
        public void FiveColumnMapsOnlyConnectAdjacentColumns()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            Assert.That(asset.Config.Columns, Is.EqualTo(5));
            Assert.That(asset.Config.ConstrainBossApproach, Is.True);
            Assert.That(asset.Config.Clone().ConstrainBossApproach, Is.True);
            for (uint seed = 1; seed <= 64; seed++)
            {
                var result = asset.CreateGenerator().Generate(asset.Config, asset.BuildCatalog(), seed);
                Assert.That(result.Success, Is.True, "seed=" + seed + " " + result.DiagnosticsText);
                foreach (var from in result.Graph.Nodes)
                foreach (int target in from.NextIds)
                {
                    var to = result.Graph.Get(target);
                    Assert.That(Mathf.Abs(to.Column - from.Column), Is.LessThanOrEqualTo(1), "seed=" + seed);
                    Assert.That(to.Row - from.Row, Is.EqualTo(1));
                }
            }
        }
    }
}
