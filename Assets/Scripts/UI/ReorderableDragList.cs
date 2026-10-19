using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>可复用的布局拖拽排序：占位、浮层、边缘滚动与取消；不依赖任何游戏规则。</summary>
    [DisallowMultipleComponent]
    public sealed class ReorderableDragList : MonoBehaviour
    {
        private ScrollRect scroll;
        private RectTransform dragLayer, placeholder;
        private RectTransform dragged;
        private Canvas canvas;
        private CanvasGroup dragGroup;
        private bool oldBlocksRaycasts, oldVertical, oldHorizontal;
        private Vector3 oldScale;
        private Quaternion oldRotation;
        private Vector3 pointerOffset;
        private Vector2 pointerPosition;
        private Camera pointerCamera;
        private int fromIndex, pointerId;
        private readonly Vector3[] corners = new Vector3[4];
        public bool AllowDrag { get; private set; }
        public ReorderableDragItem DraggedItem { get; private set; }
        public bool IsDragging => DraggedItem;
        public event Action<int, int> Reordered;
        // 固定占位模式复用同一浮动/归位逻辑，放置后的业务行为由调用方负责。
        public bool ReorderEnabled { get; set; } = true;
        public Func<ReorderableDragItem, bool> CanBeginDrag { get; set; }
        public event Action<ReorderableDragItem> DragStarting;
        public event Action<ReorderableDragItem, Vector2, bool> DragFinished;

        /// <summary>指定滚动容器、浮层和交互开关，重新配置前取消未完成拖动。</summary>
        public void Configure(ScrollRect scroller, RectTransform layer, bool allowDrag)
        {
            CancelDrag(); scroll = scroller; dragLayer = layer; AllowDrag = allowDrag;
            canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>给一个直接子节点挂接通用拖拽入口，不绑定按钮或卡牌点击事件。</summary>
        public void Attach(RectTransform item)
        {
            var entry = item.GetComponent<ReorderableDragItem>();
            if (!entry) entry = item.gameObject.AddComponent<ReorderableDragItem>();
            entry.Bind(this); entry.enabled = AllowDrag;
        }

        /// <summary>创建占位并把原对象移出滚动裁剪，保持原尺寸和抓取位置。</summary>
        public void BeginDrag(ReorderableDragItem item, PointerEventData data)
        {
            if (!AllowDrag || !isActiveAndEnabled || !dragLayer || IsDragging || data.button != PointerEventData.InputButton.Left ||
                item.transform.parent != transform || CanBeginDrag != null && !CanBeginDrag(item)) return;
            DraggedItem = item; dragged = (RectTransform)item.transform;
            // 调用方先启用浮层，再移动卡牌，避免临时父物体失活中断指针事件。
            DragStarting?.Invoke(item);
            fromIndex = dragged.GetSiblingIndex(); pointerId = data.pointerId;
            oldScale = dragged.localScale; oldRotation = dragged.localRotation;
            pointerCamera = data.pressEventCamera ? data.pressEventCamera : canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var hold = item.GetComponent<CardHoldPreview>(); if (hold) hold.CancelHold();
            data.eligibleForClick = false;
            placeholder = new GameObject("DragPlaceholder", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            placeholder.gameObject.layer = gameObject.layer; placeholder.SetParent(transform, false);
            placeholder.sizeDelta = dragged.rect.size; placeholder.SetSiblingIndex(fromIndex);
            placeholder.localScale = oldScale;
            var layout = placeholder.GetComponent<LayoutElement>();
            layout.preferredWidth = dragged.rect.width; layout.preferredHeight = dragged.rect.height;
            dragGroup = dragged.GetComponent<CanvasGroup>(); if (!dragGroup) dragGroup = dragged.gameObject.AddComponent<CanvasGroup>();
            oldBlocksRaycasts = dragGroup.blocksRaycasts;
            // 只穿透射线；禁用 interactable 会触发按钮禁用色，导致浮动卡面意外变暗。
            dragGroup.blocksRaycasts = false;
            Vector3 world;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(dragLayer, data.position, pointerCamera, out world);
            pointerOffset = dragged.position - world;
            dragged.SetParent(dragLayer, true); dragged.SetAsLastSibling();
            if (scroll)
            {
                oldVertical = scroll.vertical; oldHorizontal = scroll.horizontal; scroll.StopMovement();
                scroll.vertical = scroll.horizontal = false;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            Drag(item, data);
        }

        /// <summary>移动浮动对象并选择最近的布局位置，包含当前占位以避免静止时来回抖动。</summary>
        public void Drag(ReorderableDragItem item, PointerEventData data)
        {
            if (DraggedItem != item || pointerId != data.pointerId) return;
            pointerPosition = data.position; data.eligibleForClick = false;
            Vector3 world;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(dragLayer, data.position, pointerCamera, out world)) dragged.position = world + pointerOffset;
            if (ReorderEnabled) MovePlaceholder();
        }

        /// <summary>按匹配指针完成排序，其他触点不能结束本次拖拽。</summary>
        public void EndDrag(ReorderableDragItem item, PointerEventData data)
        {
            if (DraggedItem != item || pointerId != data.pointerId) return;
            pointerPosition = data.position; data.eligibleForClick = false; FinishDrag(true);
        }

        /// <summary>列表隐藏、切页或战斗切换时恢复本次拖动前的位置，不发布排序事件。</summary>
        public void CancelDrag() { FinishDrag(false); }

        /// <summary>完成或撤销一次拖动；先恢复对象及输入，再通知业务层，允许回调安全重建列表。</summary>
        private void FinishDrag(bool commit)
        {
            if (!DraggedItem) return;
            int toIndex = placeholder ? placeholder.GetSiblingIndex() : fromIndex;
            var entry = DraggedItem; var item = dragged; var oldPlaceholder = placeholder;
            int originalIndex = fromIndex; var scale = oldScale; var rotation = oldRotation;
            // 整个画布停用时，Unity 禁止在回调中移动正在停用的子物体；此时撤销拖动并延后恢复层级。
            bool hierarchyInactive = !gameObject.activeInHierarchy || !item || !item.gameObject.activeInHierarchy;
            bool apply = commit && !hierarchyInactive;
            DraggedItem = null; dragged = null; placeholder = null;
            if (dragGroup) dragGroup.blocksRaycasts = oldBlocksRaycasts;
            dragGroup = null;
            if (scroll) { scroll.vertical = oldVertical; scroll.horizontal = oldHorizontal; scroll.StopMovement(); }
            if (hierarchyInactive)
            {
                DOVirtual.DelayedCall(0, () => RestoreDraggedItem(item, oldPlaceholder, originalIndex, scale, rotation), true);
                DragFinished?.Invoke(entry, pointerPosition, false);
                return;
            }
            RestoreDraggedItem(item, oldPlaceholder, apply ? toIndex : originalIndex, scale, rotation);
            if (apply && originalIndex != toIndex) Reordered?.Invoke(originalIndex, toIndex);
            DragFinished?.Invoke(entry, pointerPosition, apply);
        }

        /// <summary>在正常指针流程或停用过程结束后安全恢复卡牌及占位，销毁中的列表不会遗留浮层对象。</summary>
        private void RestoreDraggedItem(RectTransform item, RectTransform oldPlaceholder, int index, Vector3 scale, Quaternion rotation)
        {
            if (oldPlaceholder)
            {
                oldPlaceholder.gameObject.SetActive(false); oldPlaceholder.SetParent(null, false);
                Destroy(oldPlaceholder.gameObject);
            }
            if (!this) { if (item) Destroy(item.gameObject); return; }
            if (item)
            {
                item.SetParent(transform, false); item.SetSiblingIndex(index);
                item.localScale = scale; item.localRotation = rotation;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        /// <summary>选择离指针最近的槽位，支持网格及横纵布局，不依赖固定卡牌尺寸。</summary>
        private void MovePlaceholder()
        {
            if (!placeholder) return;
            int nearest = placeholder.GetSiblingIndex(); float distance = float.MaxValue;
            for (int i = 0; i < transform.childCount; i++)
            {
                var rect = transform.GetChild(i) as RectTransform;
                if (!rect || !rect.gameObject.activeSelf) continue;
                rect.GetWorldCorners(corners);
                var center = RectTransformUtility.WorldToScreenPoint(pointerCamera, (corners[0] + corners[2]) * .5f);
                float next = (center - pointerPosition).sqrMagnitude;
                if (next < distance) { distance = next; nearest = i; }
            }
            if (nearest == placeholder.GetSiblingIndex()) return;
            placeholder.SetSiblingIndex(nearest); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        private void Update()
        {
            if (!ReorderEnabled || !IsDragging || !scroll || !oldVertical || !scroll.viewport) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(scroll.viewport, pointerPosition, pointerCamera, out local)) return;
            var rect = scroll.viewport.rect;
            float edge = Mathf.Min(50, rect.height * .2f), speed = 0;
            if (local.y > rect.yMax - edge) speed = Mathf.Clamp01((local.y - rect.yMax + edge) / edge);
            else if (local.y < rect.yMin + edge) speed = -Mathf.Clamp01((rect.yMin + edge - local.y) / edge);
            if (speed == 0 || scroll.content.rect.height <= rect.height) return;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + speed * Time.unscaledDeltaTime * 1.3f);
            MovePlaceholder();
        }
        private void OnDisable() { CancelDrag(); }
        private void OnDestroy() { CancelDrag(); }
    }
}
