using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量接入战斗设置面板，复用现有美术预制体，不重建战斗布局。</summary>
    public static class SettingsPanelBuilder
    {
        /// <summary>创建或复用唯一设置遮罩并显式接线，重复执行不增加窗口和按钮监听。</summary>
        [MenuItem("Spotlight/UI/Wire Battle Settings Panel")]
        public static void WireBattleScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在非运行状态接线。");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHUD>(true)).Single();
            var references = hud.GetComponent<BattleSceneReferences>();
            if (!references.settingsButton) throw new InvalidOperationException("缺少 SettingsButton 引用。");
            var controller = hud.GetComponent<SettingsPanelController>();
            if (!controller) controller = Undo.AddComponent<SettingsPanelController>(hud.gameObject);
            var modal = hud.transform.Find("SettingsModal") as RectTransform;
            if (!modal)
            {
                var created = new GameObject("SettingsModal", typeof(RectTransform), typeof(CanvasGroup), typeof(BasePanel));
                Undo.RegisterCreatedObjectUndo(created, "创建设置面板");
                created.layer = hud.gameObject.layer; modal = created.GetComponent<RectTransform>(); modal.SetParent(hud.transform, false);
            }
            Stretch(modal);
            var blackout = modal.Find("Blackout") as RectTransform;
            if (!blackout)
            {
                var created = new GameObject("Blackout", typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(created, "创建设置黑幕");
                created.layer = hud.gameObject.layer; blackout = created.GetComponent<RectTransform>(); blackout.SetParent(modal, false);
            }
            Stretch(blackout); blackout.SetAsFirstSibling();
            var shade = blackout.GetComponent<Image>();
            Undo.RecordObject(shade, "设置黑幕颜色"); shade.color = new Color(0, 0, 0, .7f); shade.raycastTarget = true;
            var window = modal.Find("SettingPanel") as RectTransform;
            if (!window)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SettingPanel.prefab");
                if (!prefab) throw new InvalidOperationException("缺少 SettingPanel 预制体。");
                var created = (GameObject)PrefabUtility.InstantiatePrefab(prefab, modal);
                Undo.RegisterCreatedObjectUndo(created, "创建设置美术实例"); window = created.GetComponent<RectTransform>();
            }
            // 实例使用统一 BasePanel 显隐，移除美术自带的测试按键动画，避免同时控制透明度和交互。
            var tween = window.GetComponent<PopupTween>();
            if (tween) Undo.DestroyObjectImmediate(tween);
            var windowGroup = window.GetComponent<CanvasGroup>();
            Undo.RecordObject(windowGroup, "恢复设置美术可见性");
            windowGroup.alpha = 1; windowGroup.interactable = windowGroup.blocksRaycasts = true;
            Undo.RecordObject(window, "居中设置面板");
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero; window.localScale = Vector3.one;
            window.sizeDelta = new Vector2(737, 489);
            var layout = hud.transform.Find("BattleLayout_16x9");
            var input = layout.GetComponent<CanvasGroup>();
            if (!input) input = Undo.AddComponent<CanvasGroup>(layout.gameObject);
            Set(controller, "openButton", references.settingsButton);
            Set(controller, "continueButton", window.Find("Button_1").GetComponent<Button>());
            Set(controller, "returnTitleButton", window.Find("Button_2").GetComponent<Button>());
            Set(controller, "volumeSlider", EnsureVolumeSlider(window));
            Set(controller, "panel", modal.GetComponent<BasePanel>());
            Set(controller, "battleHud", hud); Set(controller, "backgroundInput", input);
            Undo.RecordObject(references, "连接设置控制器"); references.settingsPanel = controller;
            EditorUtility.SetDirty(references); modal.GetComponent<BasePanel>().Hide(0);
            EnsureTitleScene();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("设置面板已接入：SettingsButton 打开，继续游戏或 Esc 关闭；黑幕遮挡底层输入。");
        }

        /// <summary>在现有音量栏右侧增量创建滑条，不拉伸原有背景和按钮美术。</summary>
        private static Slider EnsureVolumeSlider(RectTransform window)
        {
            var root = Child(window, "VolumeSlider");
            Undo.RecordObject(root, "放置音量滑条");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = new Vector2(86, 122); root.sizeDelta = new Vector2(356, 44);
            var hitArea = root.GetComponent<Image>();
            if (!hitArea) hitArea = Undo.AddComponent<Image>(root.gameObject);
            Undo.RecordObject(hitArea, "设置音量点击区域"); hitArea.color = Color.clear; hitArea.raycastTarget = true;
            var background = Child(root, "Track");
            Place(background, Vector2.zero, Vector2.one, new Vector2(14, 17), new Vector2(-14, -17));
            SetImage(background, new Color(.16f, .18f, .3f), false);
            var fillArea = Child(root, "FillArea");
            Place(fillArea, Vector2.zero, Vector2.one, new Vector2(14, 17), new Vector2(-14, -17));
            var fill = Child(fillArea, "Fill"); Stretch(fill);
            SetImage(fill, new Color(.56f, .46f, .96f), false);
            var handleArea = Child(root, "HandleArea");
            Place(handleArea, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
            var handle = Child(handleArea, "Handle");
            Undo.RecordObject(handle, "设置音量滑块尺寸");
            handle.anchorMin = new Vector2(.5f, 0); handle.anchorMax = new Vector2(.5f, 1); handle.pivot = new Vector2(.5f, .5f);
            // Slider 会沿垂直轴保持拉伸，44 高的父区域减去 14，实际滑块高度为 30。
            handle.anchoredPosition = Vector2.zero; handle.sizeDelta = new Vector2(22, -14);
            var handleImage = SetImage(handle, new Color(.78f, .72f, 1), true);
            var slider = root.GetComponent<Slider>();
            if (!slider) slider = Undo.AddComponent<Slider>(root.gameObject);
            Undo.RecordObject(slider, "设置音量范围");
            slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight; slider.fillRect = fill; slider.handleRect = handle;
            slider.targetGraphic = handleImage; slider.SetValueWithoutNotify(1);
            EditorUtility.SetDirty(slider); return slider;
        }

        /// <summary>确保返回目标可用于实际构建，只在末尾追加标题场景，保留既有场景顺序。</summary>
        private static void EnsureTitleScene()
        {
            const string path = "Assets/Scenes/StartupScene.unity";
            var scenes = EditorBuildSettings.scenes.ToList();
            var title = scenes.FirstOrDefault(entry => entry.path == path);
            if (title == null) scenes.Add(new EditorBuildSettingsScene(path, true));
            else title.enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>复用同名子节点，避免重复构建滑条层级。</summary>
        private static RectTransform Child(Transform parent, string name)
        {
            var child = parent.Find(name) as RectTransform;
            if (child) return child;
            var created = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, "创建音量控件");
            created.layer = parent.gameObject.layer;
            child = created.GetComponent<RectTransform>(); child.SetParent(parent, false); return child;
        }

        /// <summary>按父区域设置滑条内部的留边。</summary>
        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            Undo.RecordObject(rect, "调整音量控件布局");
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }

        /// <summary>配置纯色轨道和滑块，只有实际输入区域拦截射线。</summary>
        private static Image SetImage(RectTransform rect, Color color, bool raycast)
        {
            var graphic = rect.GetComponent<Image>();
            if (!graphic) graphic = Undo.AddComponent<Image>(rect.gameObject);
            Undo.RecordObject(graphic, "设置音量控件颜色"); graphic.color = color; graphic.raycastTarget = raycast;
            return graphic;
        }

        /// <summary>铺满父级区域，黑幕随画布尺寸适配。</summary>
        private static void Stretch(RectTransform rect)
        {
            Undo.RecordObject(rect, "适配设置遮罩");
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        }

        /// <summary>通过序列化接口接线，保留运行时组件的封装和撤销记录。</summary>
        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedProperties(); EditorUtility.SetDirty(target);
        }
    }
}
