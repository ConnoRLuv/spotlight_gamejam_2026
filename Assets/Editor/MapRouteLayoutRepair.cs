using System;
using RoguelikeMap.EditorTools;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotlightGameJam.Editor
{
    /// <summary>校准路线端点并配置五列相邻连接布局，保留节点美术、房间类型配置和其他场景内容。</summary>
    public static class MapRouteLayoutRepair
    {
        /// <summary>校准图片端点并采用五列单图连接布局；每条连线保持原始尺寸与朝向。</summary>
        [MenuItem("Spotlight/Map/Repair Route Connections")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            if (!asset) throw new InvalidOperationException("缺少地图配置。");
            var sources = new Vector2[6]; var targets = new Vector2[6];
            var sourcePorts = new Vector2[6][]; var targetPorts = new Vector2[6][];
            // 先完整检测再写入，避免某张图片缺失时只修正一半方向。
            for (int i = 0; i < 6; i++)
            {
                var route = asset.GetRoute((MapRouteDirection)i);
                if (route == null || !route.Sprite) throw new InvalidOperationException("路线图片缺失：" + i);
                bool up = i < 3;
                if (i == 0 || i == 3)
                {
                    sources[i] = new Vector2(0.5f, up ? 0 : 1);
                    targets[i] = new Vector2(0.5f, up ? 1 : 0);
                }
                else if (!MapArtPrefabBuilder.TryDetectRouteEndpoints(route.Sprite, up, out sources[i], out targets[i]))
                    throw new InvalidOperationException("无法检测路线端点：" + route.Sprite.name);
                sourcePorts[i] = MapArtPrefabBuilder.DetectRoutePorts(route.Sprite, sources[i]);
                targetPorts[i] = MapArtPrefabBuilder.DetectRoutePorts(route.Sprite, targets[i]);
            }
            const string path = "Assets/Scenes/MapScene.unity";
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("MapScene 有未保存修改，请先保存。");
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                MapGraphView view = null;
                foreach (var root in scene.GetRootGameObjects())
                { view = root.GetComponentInChildren<MapGraphView>(true); if (view) break; }
                if (!view) throw new InvalidOperationException("MapScene 缺少地图视图。");
                var assetFields = new SerializedObject(asset);
                // 参考图为五列；首领前逐层收拢到中间三列，保留所有路径且避免跨越两列。
                assetFields.FindProperty("_config.Columns").intValue = 5;
                assetFields.FindProperty("_config.ConstrainBossApproach").boolValue = true;
                var routes = assetFields.FindProperty("_routes");
                for (int index = 0; index < routes.arraySize; index++)
                {
                    var route = routes.GetArrayElementAtIndex(index);
                    int direction = route.FindPropertyRelative("Direction").enumValueIndex;
                    route.FindPropertyRelative("SourceEnd").vector2Value = sources[direction];
                    route.FindPropertyRelative("TargetEnd").vector2Value = targets[direction];
                    SetPorts(route.FindPropertyRelative("SourcePorts"), sourcePorts[direction]);
                    SetPorts(route.FindPropertyRelative("TargetPorts"), targetPorts[direction]);
                }
                assetFields.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                var fields = new SerializedObject(view);
                fields.FindProperty("_style.RouteLayout").enumValueIndex = (int)MapRouteLayout.NativeSize;
                // 竖线长 432，左右斜线端点差约 230×540；此间距让线头直接落入 130×130 的节点内。
                // 不能再根据图片外框加大间距，否则需要额外补线才能连接。
                fields.FindProperty("_style.ColumnSpacing").floatValue = 230;
                fields.FindProperty("_style.RowSpacing").floatValue = 500;
                fields.FindProperty("_style.RouteLengthScale").floatValue = 1;
                fields.FindProperty("_style.RouteTrim").floatValue = 0;
                // 图片本身已有浅青色线条，避免锁定状态乘上深色后变成粗黑线。
                fields.FindProperty("_style.EdgeLockedTint").colorValue = Color.white;
                fields.FindProperty("_style.EdgeAvailableTint").colorValue = Color.white;
                fields.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>通过序列化接口保存接线口，确保退出运行模式、重新导入后仍保持一致。</summary>
        private static void SetPorts(SerializedProperty property, Vector2[] ports)
        {
            property.arraySize = ports.Length;
            for (int i = 0; i < ports.Length; i++) property.GetArrayElementAtIndex(i).vector2Value = ports[i];
        }
    }
}
