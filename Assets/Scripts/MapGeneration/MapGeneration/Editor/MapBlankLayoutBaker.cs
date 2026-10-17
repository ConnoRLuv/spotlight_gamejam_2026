using System;
using System.IO;
using System.Linq;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Serialization;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEngine;

namespace RoguelikeMap.EditorTools
{
    /// <summary>将现有十张地图转为空布局，保持所有节点中心、路线变换和拓扑。</summary>
    public static class MapBlankLayoutBaker
    {
        public static void ConvertAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            var nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/Nodes/Node.prefab");
            Directory.CreateDirectory("Logs/MapBlankLayouts/Before");
            foreach (var source in asset.PreGeneratedMaps)
            {
                string path = AssetDatabase.GetAssetPath(source);
                string backup = "Logs/MapBlankLayouts/Before/" + source.name + ".prefab.txt";
                if (!File.Exists(backup)) File.Copy(path, backup);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var preset = root.GetComponent<MapPresetPrefab>();
                    var data = JsonUtility.FromJson<MapPresetDto>(JsonUtility.ToJson(preset.Data));
                    foreach (var binding in preset.Nodes)
                    {
                        var node = data.Graph.Nodes.Single(n => n.Id == binding.NodeId);
                        var container = binding.PlacementRoot;
                        if (container == null)
                        {
                            var icon = (RectTransform)binding.View.transform;
                            var position = icon.anchoredPosition;
                            container = (RectTransform)((GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, icon.parent)).transform;
                            container.name = "Node_" + node.Id;
                            container.anchorMin = container.anchorMax = container.pivot = new Vector2(.5f, .5f);
                            container.sizeDelta = new Vector2(asset.RouteProfile.NodeWidth, asset.RouteProfile.NodeHeight);
                            container.anchoredPosition = position;
                            container.localScale = Vector3.one;
                            container.localRotation = Quaternion.identity;
                            icon.SetParent(container, false);
                            icon.anchoredPosition = Vector2.zero;
                            binding.PlacementRoot = container;
                        }
                        bool fixedNode = node.Id == data.Graph.StartNodeId || node.Row == data.Graph.Rows - 1;
                        if (fixedNode) continue;
                        node.TypeKey = NodeTypeKeys.Unassigned;
                        if (binding.View != null) UnityEngine.Object.DestroyImmediate(binding.View.gameObject);
                        binding.View = null;
                    }
                    int paths = data.PathCount > 0 ? data.PathCount : asset.Config.PathCount;
                    data.SettingsHash = MapPresetFingerprint.ComputeLayout(asset.Config, asset.RouteProfile, paths);
                    var check = MapRoomDistributor.Assign(data.Graph, asset.Config, asset.BuildCatalog(), 123u);
                    if (!check.Success) throw new InvalidOperationException(source.name + ": " + check.DiagnosticsText);
                    preset.ConfigureEmptyLayout(data);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }
}
