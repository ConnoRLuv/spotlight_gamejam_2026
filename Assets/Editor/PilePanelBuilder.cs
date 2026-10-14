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
    /// <summary>增量升级只读牌堆预制体及场景引用，不重建其他 UI 或修改卡牌效果。</summary>
    public static class PilePanelBuilder
    {
        private static Font font;
        private const string Folder = "Assets/Prefabs/UI/";

        /// <summary>保留已有资产 GUID，补齐页签、关闭按钮及抽牌面板，并移除旧悬停组件。</summary>
        [MenuItem("Spotlight/Panels/Create Pile Panels And Wire Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            if (!font || !catalog) throw new InvalidOperationException("缺少中文字体或卡面目录。");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("BattleScene 有未保存修改，请先保存。");
                var references = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleSceneReferences>(true)).Single();
                var frame = references.transform.Find("BattleLayout_16x9");
                if (!frame || !references.GetComponent<BattleHUD>()) throw new InvalidOperationException("BattleScene 缺少战斗布局或 HUD。");
                // 所有前置检查通过后才写入资产，拒绝脏场景时不会留下部分修改。
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
                var discardPrefab = UpgradePrefab<DiscardPanel>("DiscardPanel", catalog);
                var drawPrefab = UpgradePrefab<DrawPilePanel>("DrawPilePanel", catalog);
                var discard = EnsureInstance<DiscardPanel>(discardPrefab, frame);
                var draw = EnsureInstance<DrawPilePanel>(drawPrefab, frame);
                foreach (var component in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)))
                    if (component && component.GetType().Name == "DiscardPileHover") UnityEngine.Object.DestroyImmediate(component);
                var hud = new SerializedObject(references.GetComponent<BattleHUD>());
                hud.FindProperty("discardPanel").objectReferenceValue = discard;
                hud.FindProperty("drawPilePanel").objectReferenceValue = draw;
                hud.ApplyModifiedPropertiesWithoutUndo();
                // 两个牌堆按钮保留数量展示，不添加任何开窗事件。
                discard.Hide(0); draw.Hide(0);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>只实例化缺失面板；已有面板位置和场景覆盖保留。</summary>
        private static T EnsureInstance<T>(GameObject prefab, Transform frame) where T : PilePreviewPanel
        {
            var existing = frame.GetComponentInChildren<T>(true);
            if (existing) return existing;
            var panel = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, frame)).GetComponent<T>();
            var rect = panel.GetComponent<RectTransform>();
            // 抽牌靠近左下角抽牌按钮，弃牌靠近右下角；两者使用相同的大面板尺寸。
            bool draw = panel is DrawPilePanel;
            rect.anchorMin = new Vector2((draw ? 155f : 870f) / 1928, (draw ? 55f : 143f) / 1088);
            rect.anchorMax = new Vector2((draw ? 1155f : 1870f) / 1928, (draw ? 695f : 783f) / 1088);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return panel;
        }

        /// <summary>更新同名节点而非重建预制体，保留脚本、控件身份及外部引用。</summary>
        private static GameObject UpgradePrefab<T>(string name, CardPrefabCatalog catalog) where T : PilePreviewPanel
        {
            string path = Folder + name + ".prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                root.SetActive(false); root.layer = 5;
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 640);
                var background = Get<Image>(root); background.color = new Color(.91f, .92f, .92f, .98f); background.raycastTarget = true;
                var group = Get<CanvasGroup>(root); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
                var panel = Get<T>(root);
                var heading = Label(root.transform, "Title", name == "DrawPilePanel" ? "抽牌堆" : "弃牌堆", 24);
                Stretch(heading.rectTransform, new Vector2(0,1), Vector2.one, new Vector2(16,-46), new Vector2(-105,-8));
                var ordinary = Tab(root.transform, "OrdinaryTab", "普通牌", 16);
                var cyber = Tab(root.transform, "CyberneticTab", "义体牌", 252);
                var close = Get<Button>(Rect(root.transform, "CloseButton").gameObject);
                Stretch(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-96,-44), new Vector2(-16,-10));
                close.targetGraphic = Get<Image>(close.gameObject); close.targetGraphic.color = new Color(.69f,.73f,.73f);
                var closeText = Label(close.transform, "Label", "关闭", 20); Fill(closeText.rectTransform);

                var viewport = Rect(root.transform,"Viewport");
                Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(20,46), new Vector2(-30,-102));
                Get<RectMask2D>(viewport.gameObject);
                var viewportImage = Get<Image>(viewport.gameObject); viewportImage.color = new Color(.84f,.86f,.86f,.3f); viewportImage.raycastTarget = true;
                var content = Rect(viewport,"Cards");
                content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1); content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
                var grid = Get<GridLayoutGroup>(content.gameObject); grid.cellSize = new Vector2(240,280); grid.spacing = new Vector2(18,18);
                grid.padding = new RectOffset(8,8,8,8); grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
                Get<ContentSizeFitter>(content.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = Get<ScrollRect>(root); scroll.viewport = viewport; scroll.content = content;
                scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
                var track = Rect(root.transform,"Scrollbar"); Stretch(track,new Vector2(1,0),Vector2.one,new Vector2(-23,46),new Vector2(-11,-102));
                Get<Image>(track.gameObject).color = new Color(.78f,.8f,.8f);
                var handle = Rect(track,"Handle"); Fill(handle);
                var handleImage = Get<Image>(handle.gameObject); handleImage.color = new Color(.48f,.52f,.53f);
                var scrollbar = Get<Scrollbar>(track.gameObject); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
                scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                var empty = Label(viewport,"EmptyState", "该牌堆暂无卡牌",24); Fill(empty.rectTransform);
                var hint = Label(root.transform,"Hint", "仅查看 · 鼠标滚轮浏览",18);
                Stretch(hint.rectTransform,Vector2.zero,new Vector2(1,0),new Vector2(16,8),new Vector2(-16,36));
                panel.Configure(catalog,content,heading,empty.gameObject,scroll);
                panel.ConfigureNavigation(ordinary,cyber,close,hint);
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { if (exists) PrefabUtility.UnloadPrefabContents(root); else UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>获取或补充缺失组件，不删除已有组件。</summary>
        private static T Get<T>(GameObject owner) where T : Component => owner.GetComponent<T>() ?? owner.AddComponent<T>();
        /// <summary>获取或创建同名 UI 子节点，保留已有节点身份。</summary>
        private static RectTransform Rect(Transform parent,string name)
        {
            var child = parent.Find(name);
            if (child) return child.GetComponent<RectTransform>();
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer=5; rect.SetParent(parent,false); return rect;
        }
        /// <summary>设置比例锚点和边距，保证根节点缩放后控件仍在面板内。</summary>
        private static void Stretch(RectTransform rect,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
        { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=low; rect.offsetMax=high; }
        /// <summary>将控件铺满父节点。</summary>
        private static void Fill(RectTransform rect) => Stretch(rect,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        /// <summary>创建中文文本，关闭文字射线以免遮挡按钮和滚动。</summary>
        private static Text Label(Transform parent,string name,string value,int size)
        {
            var text=Get<Text>(Rect(parent,name).gameObject); text.font=font; text.text=value; text.fontSize=size;
            text.color=new Color(.18f,.21f,.23f); text.alignment=TextAnchor.MiddleCenter; text.raycastTarget=false;
            text.verticalOverflow=VerticalWrapMode.Overflow; return text;
        }
        /// <summary>创建可切换页签，业务监听由面板运行时统一绑定。</summary>
        private static Button Tab(Transform parent,string name,string value,float left)
        {
            var rect=Rect(parent,name); Stretch(rect,new Vector2(0,1),new Vector2(0,1),new Vector2(left,-92),new Vector2(left+220,-54));
            var image=Get<Image>(rect.gameObject); image.color=new Color(.75f,.79f,.79f); image.raycastTarget=true;
            var button=Get<Button>(rect.gameObject); button.targetGraphic=image;
            Fill(Label(rect,"Label",value,22).rectTransform); return button;
        }
    }
}
