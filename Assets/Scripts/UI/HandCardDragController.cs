using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>手牌的拖放展示适配器；复用通用拖拽，只报告有效放置，不支付任何战斗费用。</summary>
    [DisallowMultipleComponent]
    public sealed class HandCardDragController : MonoBehaviour
    {
        private ReorderableDragList dragList;
        private RectTransform modal, dragLayer, hand;
        private HandDragBackdrop backdrop;
        private Func<CardView, RectTransform> targetForCard;
        private Action<CardInstance> play;
        private Action starting;
        private RectTransform target;
        private CanvasGroup backgroundInput;
        private bool savedBlocks, savedNavigation;
        private EventSystem inputSystem;
        private GameObject savedSelection;
        private Canvas canvas;
        private Canvas modalCanvas, floatingCanvas;
        private Func<CardInstance, IEnumerable<RectTransform>> highlightRoots;
        private readonly List<HighlightState> highlights = new List<HighlightState>();
        private readonly List<Canvas> createdCanvases = new List<Canvas>();
        private readonly List<GraphicRaycaster> createdRaycasters = new List<GraphicRaycaster>();
        /// <summary>保存临时覆盖前的 Canvas 设置，取消和关闭均逐项恢复。</summary>
        private sealed class HighlightState
        {
            public Canvas Canvas;
            public bool Override, Enabled;
            public int Order, Layer;
            public GraphicRaycaster Raycaster;
            public bool RaycasterEnabled;
        }
        public bool IsDragging => dragList && dragList.IsDragging;

        /// <summary>绑定手牌列表与既有战斗入口；可重复调用，不重复注册事件。</summary>
        public void Configure(RectTransform cards, ScrollRect scroll, RectTransform handArea,
            Func<CardView, RectTransform> validTarget, Action<CardInstance> onDrop, Action onStarting,
            Func<CardInstance, IEnumerable<RectTransform>> highlightedObjects = null)
        {
            CancelDrag(); hand = handArea; targetForCard = validTarget; play = onDrop; starting = onStarting;
            highlightRoots = highlightedObjects;
            // 停用过程中的子面板回调可能先于根 HUD 的 OnDisable，缓存画布并允许查找失活父级。
            if (!canvas)
            {
                var owner = cards.GetComponentInParent<Canvas>(true);
                canvas = owner && owner.rootCanvas ? owner.rootCanvas : owner;
            }
            if (!modal)
            {
                // Canvas 先于 Graphic 创建，确保 Graphic 缓存和射线注册都指向黑幕自己的画布。
                modal = new GameObject("HandCardDragModal", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster),
                    typeof(HandDragBackdrop)).GetComponent<RectTransform>();
                modal.gameObject.layer = gameObject.layer; modal.SetParent(canvas.transform, false); Stretch(modal);
                backdrop = modal.GetComponent<HandDragBackdrop>(); backdrop.color = new Color(0, 0, 0, .75f); backdrop.raycastTarget = true;
                modalCanvas = modal.GetComponent<Canvas>();
                dragLayer = new GameObject("DragLayer", typeof(RectTransform)).GetComponent<RectTransform>();
                dragLayer.gameObject.layer = gameObject.layer; dragLayer.SetParent(modal, false); Stretch(dragLayer);
                floatingCanvas = dragLayer.gameObject.AddComponent<Canvas>();
                modal.gameObject.SetActive(false);
            }
            if (!dragList)
            {
                dragList = cards.GetComponent<ReorderableDragList>();
                if (!dragList) dragList = cards.gameObject.AddComponent<ReorderableDragList>();
                dragList.CanBeginDrag = CanBegin;
                dragList.DragStarting += Begin;
                dragList.DragFinished += Finish;
            }
            dragList.Configure(scroll, dragLayer, true); dragList.ReorderEnabled = false;
        }

        /// <summary>手牌每次刷新时挂接同一个独立拖拽入口，保留原有单击和长按预览。</summary>
        public void Attach(CardView view) { dragList.Attach((RectTransform)view.transform); }

        /// <summary>仅可交互的真实手牌允许开始；目标由 HUD 的战斗状态确定。</summary>
        private bool CanBegin(ReorderableDragItem entry)
        {
            var view = entry.GetComponent<CardView>(); var button = entry.GetComponent<Button>();
            target = isActiveAndEnabled && view && button && button.IsInteractable() ? targetForCard?.Invoke(view) : null;
            return target;
        }

        /// <summary>完整黑幕之上只显示原角色、资源及手牌；保留立绘自身的透明轮廓。</summary>
        private void Begin(ReorderableDragItem entry)
        {
            starting?.Invoke();
            inputSystem = EventSystem.current;
            if (inputSystem)
            {
                savedSelection = inputSystem.currentSelectedGameObject; savedNavigation = inputSystem.sendNavigationEvents;
                inputSystem.SetSelectedGameObject(null); inputSystem.sendNavigationEvents = false;
            }
            backgroundInput = hand.GetComponentInParent<CanvasGroup>();
            if (backgroundInput)
            {
                // 保留按钮的正常视觉状态；黑幕接收鼠标，导航由 EventSystem 单独暂停。
                savedBlocks = backgroundInput.blocksRaycasts; backgroundInput.blocksRaycasts = false;
            }
            modal.SetAsLastSibling(); modal.gameObject.SetActive(true);
            // Unity 激活嵌套画布时会刷新排序覆盖，必须在激活后设置，不能给失活画布提前赋值。
            SetOverlayOrder(modalCanvas, canvas.sortingOrder + 1);
            SetOverlayOrder(floatingCanvas, canvas.sortingOrder + 3);
            Highlight(hand); Highlight(target);
            var view = entry.GetComponent<CardView>();
            if (highlightRoots != null && view && view.Instance != null)
                foreach (var root in highlightRoots(view.Instance)) Highlight(root);
        }

        /// <summary>独立画布按统一排序层显示，黑幕、目标和浮动卡牌依次排列。</summary>
        private void SetOverlayOrder(Canvas layer, int order)
        {
            layer.overrideSorting = true; layer.sortingLayerID = canvas.sortingLayerID; layer.sortingOrder = order;
            layer.worldCamera = canvas.worldCamera;
        }

        /// <summary>只提高原对象的 Canvas，不复制人物、不改变层级或尺寸；运行时创建的组件重复使用。</summary>
        private void Highlight(RectTransform root)
        {
            if (!root || root == canvas.transform || highlights.Exists(value => value.Canvas && value.Canvas.transform == root)) return;
            var layer = root.GetComponent<Canvas>();
            if (!layer)
            {
                layer = root.gameObject.AddComponent<Canvas>(); layer.worldCamera = canvas.worldCamera;
                createdCanvases.Add(layer);
                // 新增子画布需要自己的射线器，归位后手牌和敌人点击才继续可用。
                var addedInput = root.gameObject.AddComponent<GraphicRaycaster>(); createdRaycasters.Add(addedInput);
            }
            var raycaster = root.GetComponent<GraphicRaycaster>();
            highlights.Add(new HighlightState { Canvas = layer, Override = layer.overrideSorting, Enabled = layer.enabled,
                Order = layer.sortingOrder, Layer = layer.sortingLayerID, Raycaster = raycaster,
                RaycasterEnabled = raycaster && raycaster.enabled });
            // 高亮只提升视觉，不能让这些新画布的输入穿透黑幕。
            if (raycaster) raycaster.enabled = false;
            layer.enabled = true; layer.overrideSorting = true;
            layer.sortingLayerID = canvas.sortingLayerID; layer.sortingOrder = canvas.sortingOrder + 2;
        }

        /// <summary>通用组件已归位后才释放遮罩并报告放置，业务回调可安全销毁或重建整手牌。</summary>
        private void Finish(ReorderableDragItem entry, Vector2 position, bool released)
        {
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bool valid = released && target && RectTransformUtility.RectangleContainsScreenPoint(target, position, camera);
            var view = entry ? entry.GetComponent<CardView>() : null; var card = view ? view.Instance : null;
            RestoreInput();
            if (valid && card != null && isActiveAndEnabled) play?.Invoke(card);
        }

        /// <summary>取消、停用及战斗重绑均不结算，立即解除黑幕与原 UI 的输入锁。</summary>
        public void CancelDrag() { if (dragList) dragList.CancelDrag(); RestoreInput(); }

        /// <summary>恢复保存的输入状态，不覆盖其他面板已经设置的锁定策略。</summary>
        private void RestoreInput()
        {
            target = null;
            // 立即恢复排序而不在激活/停用回调中重新挂接物体；缓存的 Canvas 不遗留前景覆盖。
            foreach (var value in highlights)
                if (value.Canvas)
                {
                    value.Canvas.overrideSorting = value.Override; value.Canvas.sortingOrder = value.Order;
                    value.Canvas.sortingLayerID = value.Layer; value.Canvas.enabled = value.Enabled;
                    if (value.Raycaster) value.Raycaster.enabled = value.RaycasterEnabled;
                }
            highlights.Clear();
            if (backgroundInput)
            {
                backgroundInput.blocksRaycasts = savedBlocks;
                backgroundInput = null;
            }
            if (modal) modal.gameObject.SetActive(false);
            if (inputSystem)
            {
                inputSystem.sendNavigationEvents = savedNavigation;
                if (savedSelection && savedSelection.activeInHierarchy) inputSystem.SetSelectedGameObject(savedSelection);
            }
            savedSelection = null; inputSystem = null;
        }

        /// <summary>跟随父画布铺满，尺寸变化时无需拉伸人物或卡面。</summary>
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

        private void Update()
        {
            if (IsDragging && Input.GetKeyDown(KeyCode.Escape)) CancelDrag();
        }
        private void OnApplicationFocus(bool focus) { if (!focus) CancelDrag(); }
        private void OnDisable() { CancelDrag(); }
        private void OnDestroy()
        {
            CancelDrag();
            if (dragList)
            {
                dragList.CanBeginDrag = null; dragList.DragStarting -= Begin; dragList.DragFinished -= Finish;
            }
            if (modal) Destroy(modal.gameObject);
            foreach (var raycaster in createdRaycasters) if (raycaster) Destroy(raycaster);
            createdRaycasters.Clear();
            foreach (var layer in createdCanvases) if (layer) Destroy(layer);
            createdCanvases.Clear();
        }
    }
}
