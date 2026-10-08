using System;
using System.Collections.Generic;
using System.IO;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>
    /// 根据战斗线框图生成可编辑的 uGUI 场景，并提供引用校验和预览导出。
    /// 创建入口只生成新文件，避免重新执行菜单时覆盖成员手工调整的场景。
    /// </summary>
    public static class BattleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/BattleScene.unity";
        private const string FontPath = "Assets/Fonts/NotoSansCJKsc-Regular.otf";
        // 布局坐标来自参考图，左上角为原点；归一化锚点使各面板随画布等比例缩放。
        private const float ReferenceWidth = 1928f;
        private const float ReferenceHeight = 1088f;
        private static readonly Color LightGray = Gray(0.75f);
        private static readonly Color DarkGray = Gray(0.32f);

        /// <summary>创建并保存 BattleScene；已有文件或未保存场景时停止，保护现场。</summary>
        [MenuItem("Spotlight/Battle Scene/Create From Wireframe")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("BattleScene 已存在，请直接编辑该场景。");
            for (var index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再创建 BattleScene。");

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) throw new InvalidOperationException("未导入 Noto 中文字体。");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.orthographic = true;
            camera.orthographicSize = 5;
            var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var canvasObject = new GameObject("BattleCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(BattleSceneReferences));
            canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>();
            // 相机画布同时支持正常 Game View、UI 鼠标事件和相机离屏预览。
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var frame = CreateRect(canvasObject.transform, "BattleLayout_16x9", new Rect(0, 0, ReferenceWidth, ReferenceHeight));
            var fitter = frame.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            // 不同宽高比保留完整战斗布局，外围由白色相机背景补齐。
            var background = frame.gameObject.AddComponent<Image>();
            background.color = Color.white;
            background.raycastTarget = false;
            var references = canvasObject.GetComponent<BattleSceneReferences>();

            var header = Panel(frame, "TopBar", new Rect(4, 4, 1920, 95), LightGray);
            references.healthText = Label(Panel(header.transform, "Health", new Rect(55, 22, 112, 55), DarkGray, true),
                "Label", "生命值", 18, Color.white, font);
            references.goldText = Label(Panel(header.transform, "Gold", new Rect(186, 22, 112, 55), DarkGray, true),
                "Label", "钱", 18, Color.white, font);
            references.mapProgressText = Label(Panel(header.transform, "MapProgress", new Rect(1253, 22, 548, 55), DarkGray, true),
                "Label", "地图进度", 18, Color.white, font);
            references.settingsButton = Button(header.transform, "SettingsButton", new Rect(1820, 22, 55, 55),
                "设置", 24, Gray(0.4f), font, true);

            var playerArea = Group(frame, "PlayerArea");
            references.playerHealthText = Label(Panel(playerArea, "HealthBar", new Rect(386, 291, 222, 26), LightGray),
                "Label", "血条", 18, Color.black, font);
            references.playerPortrait = Panel(playerArea, "PlayerPortrait", new Rect(386, 341, 222, 344), Color.black);
            Label(references.playerPortrait, "Label", "玩家", 52, Color.white, font);
            references.sanityText = Label(Panel(playerArea, "SanityBar", new Rect(386, 710, 222, 27), LightGray),
                "Label", "理智值", 18, Color.black, font);

            var durability = Panel(playerArea, "CyberneticDurabilityArea", new Rect(184, 341, 163, 344), Gray(0.4f));
            references.cyberneticDurabilityArea = durability.rectTransform;
            // 两行图标与耐久条是线框图占位，后续可替换为部位 prefab。
            Panel(durability.transform, "SlotIcon_01", new Rect(15, 40, 47, 47), Color.black, true);
            Panel(durability.transform, "DurabilityBar_01", new Rect(76, 52, 71, 19), Color.black, true);
            Panel(durability.transform, "SlotIcon_02", new Rect(15, 105, 47, 47), Color.black, true);
            Panel(durability.transform, "DurabilityBar_02", new Rect(76, 117, 71, 19), Color.black, true);
            LabelInRect(durability.transform, "Description", new Rect(8, 166, 147, 70), "义体\n耐久度显示区域", 18, Color.white, font);

            var enemyArea = Group(frame, "EnemyArea");
            references.enemyHealthText = Label(Panel(enemyArea, "HealthBar", new Rect(1323, 291, 222, 26), LightGray),
                "Label", "血条", 18, Color.black, font);
            references.enemyPortrait = Panel(enemyArea, "EnemyPortrait", new Rect(1323, 341, 222, 344), Color.black);
            Label(references.enemyPortrait, "Label", "敌对npc", 38, Color.white, font);
            references.enemyNameText = Label(Panel(enemyArea, "EnemyName", new Rect(1323, 710, 222, 27), LightGray),
                "Label", "npc名字", 18, Color.black, font);
            var tooltip = Panel(enemyArea, "EnemyTooltip", new Rect(1584, 342, 202, 149), LightGray);
            Label(tooltip, "Description", "鼠标移到npc上\n有描述（待定）", 18, Color.black, font);
            references.enemyTooltip = tooltip.gameObject;
            references.enemyPortrait.raycastTarget = true;
            references.enemyPortrait.gameObject.AddComponent<BattleEnemyTooltip>().Configure(tooltip.gameObject);

            var bottom = Group(frame, "HandAndTurnArea");
            references.actionPointsText = Label(Panel(bottom, "ActionPoints", new Rect(181, 845, 183, 78), DarkGray),
                "Label", "行动点", 28, Color.white, font);
            references.handArea = Panel(bottom, "HandArea", new Rect(462, 865, 1007, 223), Color.black).rectTransform;
            Label(references.handArea.GetComponent<Image>(), "Placeholder", "卡牌放置区", 52, Color.white, font);
            // 真正的卡牌实例放在 Cards 下，替换内容时无需改动手牌区背景。
            Group(references.handArea, "Cards");
            references.endTurnButton = Button(bottom, "EndTurnButton", new Rect(1565, 845, 233, 78),
                "结束回合", 28, DarkGray, font);
            references.drawPileButton = Button(bottom, "DrawPileButton", new Rect(55, 952, 80, 81),
                "抽牌堆", 18, Gray(0.59f), font);
            references.discardPileButton = Button(bottom, "DiscardPileButton", new Rect(1795, 952, 80, 81),
                "弃牌堆", 18, Gray(0.59f), font);

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            // 细边框最后绘制但不拦截点击。
            Panel(frame, "BorderTop", new Rect(4, 3, 1920, 1), DarkGray);
            Panel(frame, "BorderBottom", new Rect(4, 1084, 1920, 1), DarkGray);
            Panel(frame, "BorderLeft", new Rect(4, 3, 1, 1082), DarkGray);
            Panel(frame, "BorderRight", new Rect(1923, 3, 1, 1082), DarkGray);

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("保存 BattleScene 失败。");
            Selection.activeGameObject = canvasObject;
            Debug.Log("BattleScene 已按线框图生成。按钮保留绑定入口，悬停说明已可用。");
            ValidateScene();
        }

        /// <summary>检查场景的基础对象、字体和 UI 引用，发现缺项即报告错误。</summary>
        [MenuItem("Spotlight/Battle Scene/Validate Current Scene")]
        public static void ValidateScene()
        {
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            if (!references) throw new InvalidOperationException("当前场景缺少 BattleSceneReferences。");
            if (!Camera.main || !UnityEngine.Object.FindObjectOfType<EventSystem>())
                throw new InvalidOperationException("缺少主相机或 EventSystem。");
            foreach (var field in typeof(BattleSceneReferences).GetFields())
                if (field.GetValue(references) is UnityEngine.Object value && !value || field.GetValue(references) == null)
                    throw new InvalidOperationException("缺少 UI 引用：" + field.Name);

            var objects = SceneManager.GetActiveScene().GetRootGameObjects();
            var count = 0;
            foreach (var root in objects)
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    throw new InvalidOperationException("存在丢失脚本：" + transform.name);
                count++;
            }
            foreach (var text in references.GetComponentsInChildren<Text>(true))
            {
                if (!text.font) throw new InvalidOperationException("文本缺少字体：" + text.name);
                foreach (var character in text.text)
                    if (!char.IsWhiteSpace(character) && !text.font.HasCharacter(character))
                        throw new InvalidOperationException("字体缺字：" + character);
            }
            Debug.Log("BattleScene 校验通过：" + count + " 个对象，UI 引用完整、无丢失脚本、中文字体完整。");
        }

        /// <summary>导出固定分辨率预览，供成员比对线框图；输出位于非资源目录 Logs。</summary>
        [MenuItem("Spotlight/Battle Scene/Export Preview")]
        public static void ExportPreview()
        {
            ValidateScene();
            var camera = Camera.main;
            var previous = camera.targetTexture;
            var previousActive = RenderTexture.active;
            // 同时检查标准、较小、4:3 和超宽窗口，确保锚点布局不会裁掉角色或操作区。
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720),
                new Vector2Int(1024, 768), new Vector2Int(2560, 1080) })
            {
                var target = new RenderTexture(size.x, size.y, 24);
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    image.Apply();
                    Directory.CreateDirectory("Logs/BattleScene");
                    var path = "Logs/BattleScene/BattleScene-" + size.x + "x" + size.y + ".png";
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Debug.Log("BattleScene 预览已导出：" + path);
                }
                finally
                {
                    camera.targetTexture = previous;
                    RenderTexture.active = previousActive;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(image);
                    Canvas.ForceUpdateCanvases();
                }
            }
        }

        /// <summary>
        /// 在 Play Mode 中验证实际 UI 射线命中和敌人说明的显示、隐藏。
        /// 仅发送悬停事件，不触发按钮的业务操作。
        /// </summary>
        [MenuItem("Spotlight/Battle Scene/Validate Play Mode Interaction")]
        public static void ValidateInteraction()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("请在 Play Mode 中验证交互。");
            ValidateScene();
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            Canvas.ForceUpdateCanvases();
            if (references.enemyTooltip.activeSelf) throw new InvalidOperationException("启动时应隐藏敌人说明。");
            foreach (var target in new[] { references.enemyPortrait.gameObject, references.settingsButton.gameObject,
                references.endTurnButton.gameObject, references.drawPileButton.gameObject, references.discardPileButton.gameObject })
            {
                var rect = target.GetComponent<RectTransform>();
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(Camera.main, rect.TransformPoint(rect.rect.center))
                };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                if (hits.Count == 0 || hits[0].gameObject != target)
                    throw new InvalidOperationException("UI 射线未命中：" + target.name);
            }
            var hover = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(references.enemyPortrait.gameObject, hover, ExecuteEvents.pointerEnterHandler);
            if (!references.enemyTooltip.activeSelf) throw new InvalidOperationException("悬停后未显示敌人说明。");
            ExecuteEvents.Execute(references.enemyPortrait.gameObject, hover, ExecuteEvents.pointerExitHandler);
            if (references.enemyTooltip.activeSelf) throw new InvalidOperationException("移出后未隐藏敌人说明。");
            Debug.Log("BattleScene Play Mode 校验通过：5 个交互区域射线命中，敌人说明进入显示、离开隐藏。");
        }

        /// <summary>按指定亮度创建不透明灰色占位颜色。</summary>
        private static Color Gray(float value) { return new Color(value, value, value, 1); }

        /// <summary>创建铺满父节点的分组，不增加背景或鼠标拦截。</summary>
        private static RectTransform Group(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>
        /// 以左上角像素坐标定位 UI；局部面板内部使用局部坐标，其余使用整幅参考图坐标。
        /// </summary>
        private static RectTransform CreateRect(Transform parent, string name, Rect bounds, bool local = false)
        {
            var rect = Group(parent, name);
            var parentRect = parent as RectTransform;
            var width = ReferenceWidth;
            var height = ReferenceHeight;
            // 父节点尺寸可能尚未由 Canvas 更新，用记录的参考尺寸取代布局时序依赖。
            if (local)
            {
                var anchor = parentRect.anchorMax - parentRect.anchorMin;
                width = anchor.x * ReferenceWidth;
                height = anchor.y * ReferenceHeight;
            }
            rect.anchorMin = new Vector2(bounds.xMin / width, 1 - bounds.yMax / height);
            rect.anchorMax = new Vector2(bounds.xMax / width, 1 - bounds.yMin / height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>创建具有背景颜色的面板，并设置参考图中的位置和尺寸。</summary>
        private static Image Panel(Transform parent, string name, Rect bounds, Color color, bool local = false)
        {
            var image = CreateRect(parent, name, bounds, local).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>创建指定位置的文本标签，供场景占位布局使用。</summary>
        private static Text Label(Image panel, string name, string value, int size, Color color, Font font)
        {
            return CreateText(Group(panel.transform, name), value, size, color, font);
        }

        /// <summary>在已有矩形容器中创建文本标签。</summary>
        private static Text LabelInRect(Transform parent, string name, Rect bounds, string value, int size, Color color, Font font)
        {
            return CreateText(CreateRect(parent, name, bounds, true), value, size, color, font);
        }

        /// <summary>创建文本组件并设置字体、颜色及对齐方式。</summary>
        private static Text CreateText(RectTransform rect, string value, int size, Color color, Font font)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>创建带标签的按钮，供场景交互入口使用。</summary>
        private static Button Button(Transform parent, string name, Rect bounds, string label, int size, Color color, Font font, bool local = false)
        {
            var panel = Panel(parent, name, bounds, color, local);
            panel.raycastTarget = true;
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label(panel, "Label", label, size, Color.white, font).color = size == 18 ? Color.black : Color.white;
            return button;
        }
    }
}
