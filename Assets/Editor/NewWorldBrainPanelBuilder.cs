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
    /// <summary>在 BattleScene 中创建新世界脑机的重排面板并接入 BattleHUD；重复执行只补齐缺失部分。</summary>
    public static class NewWorldBrainPanelBuilder
    {
        private static Font font;
        private static readonly Color Ink = new Color(0.17f, 0.19f, 0.21f);

        /// <summary>在已接入卡牌与 HUD 的 BattleScene 上增量创建重排面板；不重建其他战斗 UI。</summary>
        [MenuItem("Spotlight/Panels/Create NewWorld Brain Panel")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BattleScene.unity" || scene.isDirty)
                throw new InvalidOperationException("请打开并保存 BattleScene 后创建重排面板。");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            if (!font || !catalog) throw new InvalidOperationException("缺少中文字体或卡面目录。");
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            var hud = references ? references.GetComponent<BattleHUD>() : null;
            if (!references || !hud) throw new InvalidOperationException("BattleScene 缺少 HUD。");
            var canvas = references.GetComponent<Canvas>();

            var existing = FindPanel(canvas.transform);
            if (existing)
            {
                if (!existing.GetComponent<CanvasGroup>()) existing.gameObject.AddComponent<CanvasGroup>();
                existing.Hide(0);
                var panelFields = new SerializedObject(existing);
                foreach (var field in new[] { "ordinaryPoolRoot", "ordinaryOrderRoot", "cyberneticPoolRoot", "cyberneticOrderRoot" })
                {
                    var property = panelFields.FindProperty(field);
                    var area = property.objectReferenceValue as RectTransform;
                    if (!area || !area.GetComponent<GridLayoutGroup>()) continue;
                    // 默认隐藏的面板不能用不含 inactive 的父级查询，否则会重复包装同一个内容区。
                    property.objectReferenceValue = area.parent && area.parent.GetComponent<ScrollRect>()
                        ? NormalizeScrollArea(area) : MakeScrollable(area);
                }
                panelFields.ApplyModifiedPropertiesWithoutUndo();
                var serializedExisting = new SerializedObject(hud);
                serializedExisting.FindProperty("brainPanel").objectReferenceValue = existing;
                serializedExisting.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Debug.Log("新世界重排面板已存在，仅补接 HUD 引用。");
                return;
            }

            var overlay = new GameObject("NewWorldBrainPanel", typeof(RectTransform), typeof(Image), typeof(NewWorldBrainPanel));
            overlay.layer = 5;
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.SetParent(canvas.transform, false);
            overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);

            var window = Rect(overlay.transform, "Window", new Rect(0, 0, 1000, 720));
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.gameObject.AddComponent<Image>().color = new Color(0.87f, 0.88f, 0.89f);

            var title = TextAt(window, "Title", new Rect(20, 6, 960, 42), 22);
            title.text = "观看并调整抽牌堆";
            var hint = TextAt(window, "Hint", new Rect(20, 52, 960, 30), 16);

            var ordinaryColumn = Rect(window, "OrdinaryColumn", new Rect(20, 90, 470, 520));
            var cyberneticColumn = Rect(window, "CyberneticColumn", new Rect(510, 90, 470, 520));

            var ordinaryLabel = TextAt(ordinaryColumn, "Label", new Rect(0, 0, 470, 30), 18);
            ordinaryLabel.text = "普通牌";
            var ordinaryPool = Area(ordinaryColumn, "Pool", new Rect(0, 34, 470, 240));
            var ordinaryOrder = Area(ordinaryColumn, "Order", new Rect(0, 286, 470, 234));

            var cyberneticLabel = TextAt(cyberneticColumn, "Label", new Rect(0, 0, 470, 30), 18);
            cyberneticLabel.text = "义体牌";
            var cyberneticPool = Area(cyberneticColumn, "Pool", new Rect(0, 34, 470, 240));
            var cyberneticOrder = Area(cyberneticColumn, "Order", new Rect(0, 286, 470, 234));

            var confirm = MakeButton(window, "Confirm", new Rect(360, 628, 160, 48), "确认", 18);
            var reset = MakeButton(window, "Reset", new Rect(540, 628, 160, 48), "重置", 18);
            var cancel = MakeButton(window, "Cancel", new Rect(720, 628, 160, 48), "取消", 18);

            overlay.GetComponent<NewWorldBrainPanel>().Configure(catalog, title, hint,
                ordinaryPool, ordinaryOrder, cyberneticPool, cyberneticOrder, confirm, cancel, reset);

            var serialized = new SerializedObject(hud);
            serialized.FindProperty("brainPanel").objectReferenceValue = overlay.GetComponent<NewWorldBrainPanel>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            overlay.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("新世界脑机重排面板已接入 BattleHUD。");
        }

        /// <summary>在画布下查找已创建的重排面板，重复执行时复用。</summary>
        private static NewWorldBrainPanel FindPanel(Transform canvas)
        {
            foreach (var panel in canvas.GetComponentsInChildren<NewWorldBrainPanel>(true)) return panel;
            return null;
        }

        /// <summary>创建带网格布局的卡牌区域，容纳待排序与新顺序两组卡面。</summary>
        private static RectTransform Area(Transform parent, string name, Rect bounds)
        {
            var root = Rect(parent, name, bounds);
            root.gameObject.AddComponent<Image>().color = new Color(0.82f, 0.84f, 0.85f, 0.35f);
            var grid = root.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 175); grid.spacing = new Vector2(8, 8);
            grid.padding = new RectOffset(8, 8, 8, 8); grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            return MakeScrollable(root);
        }

        /// <summary>把旧网格增量升级为裁剪滚动区域，牌堆较大时不会溢出到其他列或按钮上。</summary>
        private static RectTransform MakeScrollable(RectTransform viewport)
        {
            var content = Rect(viewport, "Cards", new Rect(0, 0, viewport.rect.width, 0));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var grid = viewport.GetComponent<GridLayoutGroup>();
            EditorUtility.CopySerialized(grid, content.gameObject.AddComponent<GridLayoutGroup>());
            UnityEngine.Object.DestroyImmediate(grid);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            return content;
        }

        /// <summary>收敛旧构建器误生成的嵌套滚动层，保留最外层视口和真实卡牌内容。</summary>
        private static RectTransform NormalizeScrollArea(RectTransform content)
        {
            var parent = content.parent;
            while (parent && parent.GetComponent<ScrollRect>() && parent.parent && parent.parent.GetComponent<ScrollRect>())
            {
                var outer = parent.parent;
                content.SetParent(outer, false);
                outer.GetComponent<ScrollRect>().content = content;
                UnityEngine.Object.DestroyImmediate(parent.gameObject);
                parent = outer;
            }
            return content;
        }

        /// <summary>创建采用左上角坐标的矩形，不依赖当前 GameView 分辨率。</summary>
        private static RectTransform Rect(Transform parent, string name, Rect bounds)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
            return rect;
        }

        /// <summary>复用项目中文字体创建说明文字。</summary>
        private static Text TextAt(Transform parent, string name, Rect bounds, int size)
        {
            var text = Rect(parent, name, bounds).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = Ink; text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleCenter; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }

        /// <summary>创建低饱和占位按钮，业务监听由面板生命周期管理。</summary>
        private static Button MakeButton(Transform parent, string name, Rect bounds, string label, int size)
        {
            var rect = Rect(parent, name, bounds);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.63f, 0.66f, 0.68f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            TextAt(rect, "Label", new Rect(0, 0, bounds.width, bounds.height), size).text = label;
            return button;
        }
    }
}
