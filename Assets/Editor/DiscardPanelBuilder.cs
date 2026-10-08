using System;
using System.IO;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>创建可复用弃牌面板，并将悬停入口接到现有 BattleScene，不重建已有界面。</summary>
    public static class DiscardPanelBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/DiscardPanel.prefab";
        private static Font font;

        /// <summary>生成并接入对应的界面资产，配置场景中的组件引用。</summary>
        [MenuItem("Spotlight/Panels/Create Discard Panel And Wire Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (scene.path != "Assets/Scenes/BattleScene.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            if (!font || !catalog || !references || !references.GetComponent<BattleHUD>())
                throw new InvalidOperationException("缺少卡牌目录、中文字体或 BattleHUD。");
            Directory.CreateDirectory("Assets/Prefabs/UI");
            AssetDatabase.Refresh();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) prefab = CreatePrefab(catalog);
            var frame = references.transform.Find("BattleLayout_16x9");
            var panel = frame.GetComponentInChildren<DiscardPanel>(true);
            if (!panel)
            {
                panel = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, frame)).GetComponent<DiscardPanel>();
                var rect = panel.GetComponent<RectTransform>();
                // 放在弃牌堆正上方，归一化锚点沿用场景的 16:9 内容区。
                rect.anchorMin = new Vector2(1120f / 1928, 1 - 945f / 1088);
                rect.anchorMax = new Vector2(1870f / 1928, 1 - 455f / 1088);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            panel.Hide();
            var hover = references.discardPileButton.GetComponent<DiscardPileHover>();
            if (!hover) hover = references.discardPileButton.gameObject.AddComponent<DiscardPileHover>();
            hover.Configure(panel);
            EditorUtility.SetDirty(hover);
            var hud = new SerializedObject(references.GetComponent<BattleHUD>());
            hud.FindProperty("discardPanel").objectReferenceValue = panel;
            hud.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("弃牌面板预制体及 BattleScene 悬停入口已创建。");
        }

        /// <summary>创建弃牌面板预制体，设置滚动列表、空状态及脚本引用。</summary>
        private static GameObject CreatePrefab(CardPrefabCatalog catalog)
        {
            var root = Rect(null, "DiscardPanel"); root.sizeDelta = new Vector2(750, 490);
            try
            {
                var image = root.gameObject.AddComponent<Image>(); image.color = new Color(0.91f, 0.92f, 0.92f, 0.98f);
                var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
                var panel = root.gameObject.AddComponent<DiscardPanel>();
                var title = Label(root, "Title", "弃牌堆", 20);
                Stretch(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -48), new Vector2(-16, -8));
                var viewport = Rect(root, "Viewport");
                Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(20, 46), new Vector2(-30, -56));
                viewport.gameObject.AddComponent<RectMask2D>();
                viewport.gameObject.AddComponent<Image>().color = new Color(0.84f, 0.86f, 0.86f, 0.3f);
                var content = Rect(viewport, "Cards");
                content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(180, 210); grid.spacing = new Vector2(18, 18);
                grid.padding = new RectOffset(8, 8, 8, 8); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = root.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
                scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;

                var track = Rect(root, "Scrollbar");
                Stretch(track, new Vector2(1, 0), Vector2.one, new Vector2(-23, 46), new Vector2(-11, -56));
                track.gameObject.AddComponent<Image>().color = new Color(0.78f, 0.8f, 0.8f);
                var handle = Rect(track, "Handle");
                Stretch(handle, Vector2.zero, Vector2.one, Vector2.one, -Vector2.one);
                var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(0.48f, 0.52f, 0.53f);
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

                var empty = Label(viewport, "EmptyState", "暂无弃牌", 20);
                Stretch(empty.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var footer = Label(root, "Hint", "仅查看，不能出牌 · 鼠标滚轮浏览", 14);
                Stretch(footer.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(16, 8), new Vector2(-16, 36));
                panel.Configure(catalog, content, title, empty.gameObject, scroll);
                root.gameObject.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }
        /// <summary>创建指定父节点下的 UI 矩形容器。</summary>
        private static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = 5;
            if (parent) rect.SetParent(parent, false); return rect;
        }
        /// <summary>将矩形锚点扩展到整个父容器，并清零边缘偏移。</summary>
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
        /// <summary>创建指定位置的文本标签，供场景占位布局使用。</summary>
        private static Text Label(Transform parent, string name, string value, int size)
        {
            var text = Rect(parent, name).gameObject.AddComponent<Text>(); text.font = font; text.text = value;
            text.fontSize = size; text.color = new Color(0.18f, 0.21f, 0.23f); text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; return text;
        }
    }
}
