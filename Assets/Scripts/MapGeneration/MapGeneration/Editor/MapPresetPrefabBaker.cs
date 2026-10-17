using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Serialization;
using RoguelikeMap.Unity;
using RoguelikeMap.Validation;
using UnityEditor;
using UnityEngine;

namespace RoguelikeMap.EditorTools
{
    /// <summary>仅编辑器使用：复核十份离线布局，再用已有节点和线路预制体保存整张地图。</summary>
    public static class MapPresetPrefabBaker
    {
        public const string OutputFolder = "Assets/Prefabs/Map/Presets";

        public static void BakeMap01(string jsonPath)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请在编辑模式烘焙。");
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var data = JsonUtility.FromJson<MapPresetDto>(File.ReadAllText(jsonPath));
            data.PathCount = data.Paths.Count;
            Validate(data, asset);
            var prefab = BakeOne(data, asset, 1);
            var pool = asset.PreGeneratedMaps.ToList();
            pool[0] = prefab;
            asset.ConfigurePreGeneratedMaps(pool);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        public static void BakeFromFolder(string sourceFolder)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请在编辑模式烘焙。");
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var data = Enumerable.Range(1, 10).Select(i => JsonUtility.FromJson<MapPresetDto>(
                File.ReadAllText(Path.Combine(sourceFolder, "Map_" + i.ToString("D2") + ".json")))).ToArray();
            foreach (var map in data) Validate(map, asset);
            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();
            var prefabs = new List<MapPresetPrefab>();
            for (int i = 0; i < data.Length; i++) prefabs.Add(BakeOne(data[i], asset, i + 1));
            asset.ConfigurePreGeneratedMaps(prefabs);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        public static void Validate(MapPresetDto data, MapGenerationConfigAsset asset)
        {
            var graph = MapSerializer.FromDto(data.Graph);
            if (graph.Rows != 15 || graph.Columns != 5 || graph.Nodes.Select(n => n.Column).Distinct().Count() != 5)
                throw new InvalidOperationException("地图必须是 15 层，且五列都有节点。");
            foreach (var node in graph.Nodes)
                node.IsExemptFromRules = node.Id == graph.StartNodeId || node.Row == graph.Rows - 1;
            var generator = MapGenerator.CreateLayered(asset.RouteProfile);
            var context = new MapGenerationContext { Graph = graph, Config = asset.Config, Catalog = asset.BuildCatalog(), Rules = generator.Rules };
            var report = new MapValidationReport();
            foreach (var validator in generator.Validators) validator.Validate(graph, context, report);
            if (!report.IsValid) throw new InvalidOperationException(report.Summarize());
            int expectedPaths = data.PathCount > 0 ? data.PathCount : asset.Config.PathCount;
            if (data.Paths.Count != expectedPaths) throw new InvalidOperationException("完整路径数量与该地图配置不一致。");
            var covered = new HashSet<string>();
            var distinct = new HashSet<string>();
            foreach (var path in data.Paths)
            {
                if (path.NodeIds.Count != 15 || path.NodeIds[0] != graph.StartNodeId ||
                    graph.Get(path.NodeIds[14]).TypeKey != asset.Config.BossTypeKey || !distinct.Add(string.Join(",", path.NodeIds)))
                    throw new InvalidOperationException("路径必须不同，并从起点逐层到达 Boss。");
                for (int i = 0; i < 14; i++)
                {
                    if (!graph.HasEdge(path.NodeIds[i], path.NodeIds[i + 1])) throw new InvalidOperationException("路径有缺失连线。");
                    covered.Add(path.NodeIds[i] + "/" + path.NodeIds[i + 1]);
                }
            }
            if (!covered.SetEquals(graph.AssetLayout.Edges.Select(e => e.FromId + "/" + e.ToId)))
                throw new InvalidOperationException("四条路径必须覆盖整张地图的所有连线。");
            foreach (var node in graph.Nodes)
            {
                var p = graph.AssetLayout.FindNode(node.Id);
                if (p.X != (node.Column - 2) * asset.RouteProfile.ColumnSpacing || p.Y != node.Row * asset.RouteProfile.RowSpacing)
                    throw new InvalidOperationException("节点没有对齐网格。");
                if (!node.IsExemptFromRules && !asset.Config.FixedRows.Any(r => r.Row == node.Row) &&
                    !asset.Config.Weights.Any(w => w.TypeKey == node.TypeKey && w.Evaluate(node.Row) > 0))
                    throw new InvalidOperationException("房间不满足层数权重约束。");
            }
        }

        private static MapPresetPrefab BakeOne(MapPresetDto data, MapGenerationConfigAsset asset, int number)
        {
            var graph = MapSerializer.FromDto(data.Graph);
            data.Graph.ConfigHash = asset.GenerationHash();
            data.SettingsHash = MapPresetFingerprint.Compute(asset.Config, asset.RouteProfile, data.PathCount > 0 ? data.PathCount : asset.Config.PathCount);
            var root = new GameObject("Map_" + number.ToString("D2"), typeof(RectTransform), typeof(MapPresetPrefab));
            try
            {
                var edgesRoot = Container("Routes", root.transform);
                var nodesRoot = Container("Nodes", root.transform);
                var nodes = new List<MapPresetNodeBinding>();
                var edges = new List<MapPresetEdgeBinding>();
                foreach (var node in graph.Nodes)
                {
                    var visual = asset.GetVisual(node.TypeKey);
                    if (visual == null || visual.CustomPrefab == null) throw new InvalidOperationException("缺少节点预制体：" + node.TypeKey);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(visual.CustomPrefab, nodesRoot);
                    go.name = "Node_" + node.Id + "_" + node.TypeKey;
                    var rect = (RectTransform)go.transform;
                    var position = graph.AssetLayout.FindNode(node.Id);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                    rect.anchoredPosition = new Vector2(position.X, position.Y);
                    rect.sizeDelta = visual.Icon.rect.size;
                    rect.localScale = Vector3.one;
                    rect.localRotation = Quaternion.identity;
                    var view = go.GetComponent<MapNodeView>() ?? go.AddComponent<MapNodeView>();
                    nodes.Add(new MapPresetNodeBinding { NodeId = node.Id, View = view });
                }
                foreach (var placement in graph.AssetLayout.Edges)
                {
                    var art = asset.FindConstraintSprite(placement.FootprintId);
                    var route = asset.Routes.First(r => r.Sprite == art && r.Prefab != null);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(route.Prefab, edgesRoot);
                    go.name = "Edge_" + placement.FromId + "_" + placement.ToId + "_Preset";
                    var view = go.GetComponent<MapEdgeView>() ?? go.AddComponent<MapEdgeView>();
                    view.SetSprite(art, Color.white);
                    view.PlaceBaked(placement.X, placement.Y, asset.RouteProfile.FindTemplate(placement.TemplateId));
                    edges.Add(new MapPresetEdgeBinding { FromId = placement.FromId, ToId = placement.ToId, View = view });
                }
                root.GetComponent<MapPresetPrefab>().Configure(data, nodes, edges);
                ((RectTransform)root.transform).sizeDelta = Vector2.zero;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, OutputFolder + "/" + root.name + ".prefab");
                return saved.GetComponent<MapPresetPrefab>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static RectTransform Container(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }
    }
}
