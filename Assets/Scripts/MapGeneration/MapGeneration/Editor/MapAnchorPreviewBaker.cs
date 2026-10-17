using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEngine;

namespace RoguelikeMap.EditorTools
{
    /// <summary>将指定地图的 Node 与连线同名接点对齐；不修改共享节点和路线资产。</summary>
    public static class MapAnchorPreviewBaker
    {
        [Serializable] public sealed class Solution { public List<NodePose> Nodes; public List<EdgePose> Edges; public List<AnchorPose> Anchors; public float Width, Height; }
        [Serializable] public sealed class AnchorPose { public string Name; public float X, Y; }
        [Serializable] public sealed class NodePose { public int NodeId; public float X, Y; }
        [Serializable] public sealed class EdgePose { public int FromId, ToId; public float X, Y, Scale; public string TemplateId; }

        public static void Apply(string solutionPath, int mapNumber = 1)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            if (mapNumber < 1 || mapNumber > 10) throw new ArgumentOutOfRangeException(nameof(mapNumber));
            var solution = JsonUtility.FromJson<Solution>(File.ReadAllText(solutionPath));
            var config = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Nodes/Node.prefab");
            string path = "Assets/Prefabs/Map/Presets/Map_" + mapNumber.ToString("00") + ".prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var preset = contents.GetComponent<MapPresetPrefab>();
                foreach (var binding in preset.Nodes)
                {
                    var pose = solution.Nodes.Single(n => n.NodeId == binding.NodeId);
                    var art = binding.View != null ? (RectTransform)binding.View.transform : null;
                    var root = binding.PlacementRoot;
                    if (root == null)
                    {
                        if (art == null) throw new InvalidOperationException("空节点缺少 Node 容器：" + binding.NodeId);
                        root = (RectTransform)((GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, art.parent)).transform;
                        root.name = "Node_" + binding.NodeId;
                        art.SetParent(root, false);
                    }
                    root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
                    root.sizeDelta = solution.Width > 0 && solution.Height > 0 ? new Vector2(solution.Width, solution.Height) :
                        ((RectTransform)nodePrefab.transform).rect.size;
                    root.anchoredPosition = new Vector2(pose.X, pose.Y);
                    root.localScale = Vector3.one;
                    root.localRotation = Quaternion.identity;
                    if (solution.Anchors != null)
                        foreach (var anchor in solution.Anchors)
                        {
                            var side = (MapSocketSide)Enum.Parse(typeof(MapSocketSide), anchor.Name);
                            var marker = root.Find(Name(side));
                            if (marker == null) throw new InvalidOperationException("Node 缺少连接标记：" + anchor.Name);
                            marker.localPosition = new Vector3(anchor.X, anchor.Y, 0);
                        }
                    binding.PlacementRoot = root;
                    // 空布局只保存 Node；具体房间由运行时添加为其居中子物体。
                    if (art == null) continue;
                    art.SetParent(root, false);
                    art.anchoredPosition = Vector2.zero;
                    // 小于接入容器的图标也要盖住四向接点，保持原比例；较大房间图标保留原倍率。
                    float artScale = Mathf.Max(1f, (root.rect.width + 4) / art.rect.width, (root.rect.height + 4) / art.rect.height);
                    art.localScale = new Vector3(artScale, artScale, 1);
                    art.localRotation = Quaternion.identity;
                    var node = preset.Data.Graph.Nodes.Single(n => n.Id == binding.NodeId);
                    art.name = config.GetVisual(node.TypeKey).CustomPrefab.name;
                }
                foreach (var binding in preset.Edges)
                {
                    var pose = solution.Edges.Single(e => e.FromId == binding.FromId && e.ToId == binding.ToId);
                    var template = config.RouteProfile.FindTemplate(pose.TemplateId);
                    if (pose.TemplateId != preset.Data.Graph.AssetLayout.FindEdge(binding.FromId, binding.ToId).TemplateId)
                        throw new InvalidOperationException("对齐方案不能更改地图拓扑和线型。");
                    var rect = (RectTransform)binding.View.transform;
                    rect.pivot = Vector2.zero;
                    rect.anchoredPosition = new Vector2(pose.X, pose.Y);
                    rect.localScale = new Vector3(pose.Scale, pose.Scale, 1);
                    rect.localRotation = Quaternion.identity;
                    SetMarker(rect, template.SourceSide, template.Source);
                    SetMarker(rect, template.TargetSide, template.Target);
                }
                preset.EnableConnectionAnchors();
                Validate(preset, config);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public static void Validate(MapPresetPrefab preset, MapGenerationConfigAsset config)
        {
            foreach (var binding in preset.Nodes)
                if (binding.PlacementRoot == null || (binding.View != null &&
                    (binding.View.transform.parent != binding.PlacementRoot || binding.View.transform.localPosition.sqrMagnitude > .0001f)))
                    throw new InvalidOperationException("房间图标必须居中放在 Node 下。");
            foreach (var binding in preset.Edges)
            {
                var placement = preset.Data.Graph.AssetLayout.FindEdge(binding.FromId, binding.ToId);
                var template = config.RouteProfile.FindTemplate(placement.TemplateId);
                var from = preset.Nodes.Single(n => n.NodeId == binding.FromId).PlacementRoot;
                var to = preset.Nodes.Single(n => n.NodeId == binding.ToId).PlacementRoot;
                string a = Name(template.SourceSide), b = Name(template.TargetSide);
                if (Vector3.Distance(binding.View.transform.Find(a).position, from.Find(a).position) > .02f ||
                    Vector3.Distance(binding.View.transform.Find(b).position, to.Find(b).position) > .02f)
                    throw new InvalidOperationException("同名连接标记未重合：" + binding.View.name);
            }
        }

        private static void SetMarker(RectTransform route, MapSocketSide side, MapRoutePort port)
        {
            string name = Name(side);
            var marker = route.Find(name) as RectTransform;
            // 左出线的旧实例误命名为 right，只在当前地图实例中修正。
            if (marker == null && side == MapSocketSide.Left && route.Find("right") != null)
                marker = (RectTransform)route.Find("right");
            if (marker == null)
            {
                marker = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                marker.SetParent(route, false);
            }
            marker.name = name;
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.pivot = new Vector2(.5f, .5f);
            marker.sizeDelta = Vector2.one;
            marker.localScale = Vector3.one;
            marker.anchoredPosition = new Vector2((port.MinX + port.MaxX) * .5f, (port.MinY + port.MaxY) * .5f);
        }

        private static string Name(MapSocketSide side) => side == MapSocketSide.Up ? "top" :
            side == MapSocketSide.Down ? "bottom" : side == MapSocketSide.Left ? "left" : "right";
    }
}
