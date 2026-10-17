using System;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量接线 MapScene 的探索桥，不重建地图 UI、不修改构建顺序。</summary>
    public static class AdventureMapBridgeBuilder
    {
        /// <summary>保留已有地图对象与配置，仅添加一个幂等的会话桥并接入生成器、视图。</summary>
        [MenuItem("Spotlight/Exploration/Wire Map Session Bridge")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式接线。");
            const string path = "Assets/Scenes/MapScene.unity";
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("MapScene 有未保存改动，请先保存。");
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                MapGraphView view = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    view = root.GetComponentInChildren<MapGraphView>(true);
                    if (view) break;
                }
                if (!view) throw new InvalidOperationException("MapScene 缺少 MapGraphView。");
                var generator = view.Generator;
                if (!generator) generator = view.GetComponentInParent<MapGeneratorBehaviour>();
                if (!generator) throw new InvalidOperationException("请为地图视图指定生成器。");
                var bridge = generator.GetComponent<AdventureMapBridge>();
                if (!bridge) bridge = generator.gameObject.AddComponent<AdventureMapBridge>();
                bridge.Configure(generator, view);
                EditorUtility.SetDirty(bridge);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
