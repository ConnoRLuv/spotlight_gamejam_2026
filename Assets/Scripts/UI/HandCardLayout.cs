using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>单页手牌展示：按数量压缩中心间距，用独立补间处理增减牌、悬停和归位。</summary>
    [DisallowMultipleComponent]
    public sealed class HandCardLayout : MonoBehaviour
    {
        // 缩小框内留白，使卡面更饱满；顶部仍额外保留完整悬停抬升距离。
        [SerializeField, Min(0)] private float padding = 3;
        [SerializeField, Min(0)] private float hoverLift = 16;
        [SerializeField, Min(0)] private float neighborPush = 12;
        [SerializeField, Min(0)] private float maximumGap = 40;
        [SerializeField, Min(.01f)] private float transitionSeconds = .22f;
        [SerializeField, Min(.01f)] private float hoverSeconds = .14f;
        private readonly List<Entry> cards = new List<Entry>();
        private RectTransform root;
        private ScrollRect scroll;
        private CanvasGroup backgroundInput;
        private Entry hovered, returning;
        private PointerEventData pointer;
        private Vector2 lastSize;
        private bool suspended;

        /// <summary>逻辑顺序独立于悬停时的绘制顺序；每张牌只持有自己的动画。</summary>
        private sealed class Entry
        {
            public CardView View;
            public RectTransform Rect;
            public Vector2 Base, Target, DragPosition;
            public float Scale, TargetScale;
            public bool Fresh = true;
            public Tween Motion;
        }

        /// <summary>接管手牌的排布，保留滚动组件供通用拖拽读取视口，但关闭两个滚动方向。</summary>
        public void Configure(ScrollRect value)
        {
            root = (RectTransform)transform; scroll = value;
            backgroundInput = scroll ? scroll.GetComponentInParent<CanvasGroup>() : null;
            var layout = GetComponent<LayoutGroup>(); if (layout) layout.enabled = false;
            var fitter = GetComponent<ContentSizeFitter>(); if (fitter) fitter.enabled = false;
            if (scroll)
            {
                scroll.StopMovement(); scroll.horizontal = scroll.vertical = false; scroll.inertia = false;
                if (scroll.horizontalScrollbar) scroll.horizontalScrollbar.gameObject.SetActive(false);
                if (scroll.verticalScrollbar) scroll.verticalScrollbar.gameObject.SetActive(false);
            }
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.pivot = new Vector2(.5f, .5f);
            root.offsetMin = root.offsetMax = Vector2.zero;
        }

        /// <summary>同步当前页的真实卡牌，不销毁仍在手中的展示对象。</summary>
        public void SetCards(IReadOnlyList<CardView> views)
        {
            var next = new List<Entry>(views.Count);
            foreach (var view in views)
            {
                var entry = cards.Find(value => value.View == view);
                if (entry == null)
                {
                    entry = new Entry { View = view, Rect = (RectTransform)view.transform };
                    entry.Rect.anchorMin = entry.Rect.anchorMax = entry.Rect.pivot = new Vector2(.5f, .5f);
                    var hover = view.GetComponent<HandCardHover>();
                    if (!hover) hover = view.gameObject.AddComponent<HandCardHover>();
                    hover.Bind(this, view);
                }
                next.Add(entry);
            }
            foreach (var removed in cards.Where(value => !next.Contains(value))) Stop(removed);
            cards.Clear(); cards.AddRange(next);
            if (returning != null && !cards.Contains(returning)) returning = null;
            if (hovered != null && !cards.Contains(hovered)) { hovered = null; pointer = null; }
            RestoreOrder(); RefreshLayout();
        }

        /// <summary>按可用高度等比显示卡面，中心间距随牌数收紧；两侧及顶部预留悬停空间。</summary>
        public void RefreshLayout()
        {
            if (!root || suspended || !isActiveAndEnabled) return;
            lastSize = root.rect.size;
            float height = Mathf.Max(1, lastSize.y - padding * 2 - hoverLift);
            float width = Mathf.Max(1, lastSize.x - (padding + neighborPush) * 2);
            float widest = 0;
            foreach (var card in cards)
            {
                if (!card.Rect) continue;
                card.Scale = Mathf.Min(height / Mathf.Max(1, card.Rect.rect.height), width / Mathf.Max(1, card.Rect.rect.width));
                widest = Mathf.Max(widest, card.Rect.rect.width * card.Scale);
            }
            float gap = maximumGap / (1 + Mathf.Max(0, cards.Count - 1) * .16f);
            float step = cards.Count > 1 ? Mathf.Min(widest + gap, Mathf.Max(0, width - widest) / (cards.Count - 1)) : 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i]; if (!card.Rect) continue;
                card.Base = new Vector2((i - (cards.Count - 1) * .5f) * step,
                    -lastSize.y * .5f + padding + card.Rect.rect.height * card.Scale * .5f);
            }
            AnimateLayout(transitionSeconds);
        }

        /// <summary>根据固定槽位识别悬停，避免卡牌抬起后反复触发移入移出造成抖动。</summary>
        public void TrackPointer(PointerEventData value)
        {
            if (!CanHover()) return;
            pointer = value; ResolveHover();
        }

        /// <summary>卡牌停用时撤销它拥有的悬停，不把装饰状态保留到下一页。</summary>
        public void ReleaseHover(CardView view)
        {
            if (hovered == null || hovered.View != view) return;
            pointer = null; ChangeHover(null);
        }

        /// <summary>拖拽开始前停止所有位置补间并恢复逻辑层级，避免动画争夺被抓住的卡牌。</summary>
        public void BeginDrag(CardView view)
        {
            suspended = true; pointer = null; hovered = null; RestoreOrder();
            foreach (var card in cards) Stop(card);
            var selected = cards.Find(value => value.View == view);
            if (selected != null) selected.DragPosition = selected.Rect.anchoredPosition;
        }

        /// <summary>通用拖拽归位后恢复手牌坐标，再平滑收回原槽位；延迟归位由下一帧继续处理。</summary>
        public void EndDrag(CardView view)
        {
            suspended = false; pointer = null;
            returning = cards.Find(value => value.View == view);
            RestoreReturningCard(); RefreshLayout();
        }

        /// <summary>处理停用期间由通用拖拽延迟恢复的卡牌。</summary>
        private void RestoreReturningCard()
        {
            if (returning == null || !returning.Rect || returning.Rect.parent != transform) return;
            returning.Rect.anchoredPosition = returning.DragPosition; returning = null;
        }

        /// <summary>悬停只在手牌可操作区域开放，设置面板和拖拽遮罩显示时不抢占绘制顺序。</summary>
        private bool CanHover() => isActiveAndEnabled && !suspended &&
            (!backgroundInput || backgroundInput.interactable && backgroundInput.blocksRaycasts);

        /// <summary>沿固定中心点选最近的一张牌，在整行范围之外清除悬停。</summary>
        private void ResolveHover()
        {
            if (pointer == null || cards.Count == 0 || !root) { ChangeHover(null); return; }
            var canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pointer.position, camera, out local)) { ChangeHover(null); return; }
            var first = cards[0]; var last = cards[cards.Count - 1];
            float lower = -lastSize.y * .5f + padding - 3;
            float upper = lastSize.y * .5f - padding + 3;
            if (local.y < lower || local.y > upper || local.x < first.Base.x - first.Rect.rect.width * first.Scale * .5f ||
                local.x > last.Base.x + last.Rect.rect.width * last.Scale * .5f) { ChangeHover(null); return; }
            Entry nearest = null; float distance = float.MaxValue;
            foreach (var card in cards)
            {
                float next = Mathf.Abs(local.x - card.Base.x);
                if (next < distance) { distance = next; nearest = card; }
            }
            ChangeHover(nearest);
        }

        /// <summary>把当前牌绘制在其他手牌前面，退出时恢复逻辑顺序。</summary>
        private void ChangeHover(Entry next)
        {
            if (hovered == next) return;
            hovered = next;
            if (!isActiveAndEnabled || suspended) return;
            RestoreOrder(); AnimateLayout(hoverSeconds);
        }

        /// <summary>仅改变绘制层级，不调整模型中的手牌顺序。</summary>
        private void RestoreOrder()
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Rect && cards[i].Rect.parent == transform) cards[i].Rect.SetSiblingIndex(i);
            if (hovered != null && hovered.Rect && hovered.Rect.parent == transform) hovered.Rect.SetAsLastSibling();
        }

        /// <summary>邻近两张牌逐级让位，其余牌保持原槽位；补间使用真实时间。</summary>
        private void AnimateLayout(float duration)
        {
            int index = hovered == null ? -1 : cards.IndexOf(hovered);
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i]; if (!card.Rect || card.Rect.parent != transform) continue;
                Vector2 target = card.Base;
                int distance = Mathf.Abs(i - index);
                if (index >= 0 && distance == 0) target.y += hoverLift;
                // 当前页不超过七张时足够宽松，只抬起悬停牌，不让两侧卡牌退开。
                else if (cards.Count > 7 && index >= 0 && distance <= 2) target.x += Mathf.Sign(i - index) * neighborPush / distance;
                if (card.Motion != null && card.Motion.IsActive() && Vector2.Distance(card.Target, target) < .01f && Mathf.Abs(card.TargetScale - card.Scale) < .0001f) continue;
                Stop(card); card.Target = target; card.TargetScale = card.Scale;
                if (card.Fresh)
                {
                    card.Rect.anchoredPosition = target; card.Rect.localScale = Vector3.one * card.Scale * .94f; card.Fresh = false;
                }
                if (Vector2.Distance(card.Rect.anchoredPosition, target) < .01f && Mathf.Abs(card.Rect.localScale.x - card.Scale) < .0001f) continue;
                card.Motion = DOTween.Sequence().Join(card.Rect.DOAnchorPos(target, duration)).Join(card.Rect.DOScale(card.Scale, duration))
                    .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(card.View.gameObject).OnComplete(() => card.Motion = null);
            }
        }

        /// <summary>只终止本布局持有的动画，不打断卡面其他动效。</summary>
        private static void Stop(Entry card) { var motion = card.Motion; card.Motion = null; motion?.Kill(); }

        private void LateUpdate()
        {
            if (suspended) return;
            if (returning != null && returning.Rect && returning.Rect.parent == transform) { RestoreReturningCard(); RefreshLayout(); }
            if (root && Vector2.Distance(lastSize, root.rect.size) > .1f) RefreshLayout();
            if (!CanHover()) { pointer = null; ChangeHover(null); }
            else if (pointer != null) ResolveHover();
        }
        private void OnDisable()
        {
            pointer = null; hovered = null; suspended = false;
            foreach (var card in cards) Stop(card);
        }
        private void OnDestroy() { foreach (var card in cards) Stop(card); }
        private void OnApplicationFocus(bool focus) { if (!focus) { pointer = null; ChangeHover(null); } }
    }
}
