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
    /// <summary>增量升级共用选牌窗口，保留候选根节点及已有场景引用，不重建战斗界面。</summary>
    public static class CardChoicePanelBuilder
    {
        /// <summary>升级当前 BattleScene 的动态标题、说明及裁剪滚动网格并保存。</summary>
        [MenuItem("Spotlight/Panels/Upgrade Card Choice Panel")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BattleScene.unity" || scene.isDirty)
                throw new InvalidOperationException("请打开并保存 BattleScene 后升级选牌窗口。");
            var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHUD>(true)).Single();
            Upgrade(hud); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        /// <summary>复用现有选牌节点，替换溢出的横排布局；场景生成工具也调用同一入口。</summary>
        public static void Upgrade(BattleHUD hud)
        {
            var fields = new SerializedObject(hud);
            var overlay = fields.FindProperty("choicePanel").objectReferenceValue as GameObject;
            var content = fields.FindProperty("choicesRoot").objectReferenceValue as RectTransform;
            var cancel = fields.FindProperty("cancelChoiceButton").objectReferenceValue as Button;
            if (!overlay || !content || !cancel) throw new InvalidOperationException("选牌界面缺少原有控件。");
            var window = overlay.transform.Find("ChoiceWindow") as RectTransform;
            if (!window) throw new InvalidOperationException("选牌窗口不存在。");
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero; window.sizeDelta = new Vector2(1000,760);
            var title = window.Find("Title").GetComponent<Text>();
            title.fontSize = 24; title.text = "选择一张卡牌";
            Bounds(title.rectTransform, new Vector2(0,1), Vector2.one, new Vector2(24,-50), new Vector2(-24,-8));
            var hint = Get<Text>(Child(window,"ChoiceHint").gameObject); hint.font = title.font; hint.fontSize = 18;
            hint.color = title.color; hint.alignment = TextAnchor.MiddleCenter; hint.raycastTarget = false;
            hint.text = "选择目标后确认，取消不扣费。";
            Bounds(hint.rectTransform,new Vector2(0,1),Vector2.one,new Vector2(24,-100),new Vector2(-24,-50));
            var scrollRoot = Child(window,"ChoiceScroll");
            Bounds(scrollRoot,Vector2.zero,Vector2.one,new Vector2(24,78),new Vector2(-24,-110));
            var viewport = Child(scrollRoot,"Viewport");
            Bounds(viewport,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-20,0));
            var image = Get<Image>(viewport.gameObject); image.color = new Color(.82f,.84f,.85f,.25f); image.raycastTarget = true;
            Get<RectMask2D>(viewport.gameObject);
            var oldLayout = content.GetComponent<HorizontalLayoutGroup>(); if (oldLayout) UnityEngine.Object.DestroyImmediate(oldLayout);
            content.SetParent(viewport,false); content.localScale = Vector3.one;
            content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1);
            content.anchoredPosition = content.sizeDelta = Vector2.zero;
            var grid = Get<GridLayoutGroup>(content.gameObject); grid.cellSize = new Vector2(210,245); grid.spacing = new Vector2(18,18);
            grid.padding = new RectOffset(8,8,8,8); grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            var fitter = Get<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = Get<ScrollRect>(scrollRoot.gameObject); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
            var track = Child(scrollRoot,"Scrollbar");
            Bounds(track,new Vector2(1,0),Vector2.one,new Vector2(-12,0),Vector2.zero);
            Get<Image>(track.gameObject).color = new Color(.75f,.78f,.79f);
            var handle = Child(track,"Handle"); Bounds(handle,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            var handleImage = Get<Image>(handle.gameObject); handleImage.color = new Color(.47f,.52f,.54f);
            var scrollbar = Get<Scrollbar>(track.gameObject); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            Bounds(cancel.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(-110,18),new Vector2(110,58));
            var cancelText = cancel.GetComponentInChildren<Text>(true); cancelText.fontSize = 20;
            Bounds(cancelText.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            fields.FindProperty("choiceTitle").objectReferenceValue = title;
            fields.FindProperty("choiceHint").objectReferenceValue = hint;
            fields.FindProperty("choiceScroll").objectReferenceValue = scroll;
            fields.ApplyModifiedPropertiesWithoutUndo(); overlay.SetActive(false);
        }
        /// <summary>获取组件或补齐缺失组件，不替换已有引用。</summary>
        private static T Get<T>(GameObject owner) where T : Component => owner.GetComponent<T>() ?? owner.AddComponent<T>();
        /// <summary>复用同名 UI 节点，保留升级前的对象身份。</summary>
        private static RectTransform Child(Transform parent,string name)
        {
            var existing = parent.Find(name); if (existing) return existing.GetComponent<RectTransform>();
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent,false); return rect;
        }
        /// <summary>统一设置锚点和边距，尺寸随窗口调整。</summary>
        private static void Bounds(RectTransform rect,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
    }
}
