using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>牌堆面板的排序模式：管理顺序草稿与黑色模态遮罩，实际拖拽由通用组件处理。</summary>
    public abstract partial class PilePreviewPanel
    {
        private List<CardInstance> ordinaryDraft, cyberneticDraft;
        private Action<bool> reorderFinished;
        private ReorderableDragList dragList;
        private RectTransform modalRoot, dragLayer;
        private Transform savedParent;
        private int savedSibling;
        private Vector2 savedMin, savedMax, savedPivot, savedSize, savedPosition;
        private Vector3 savedScale;
        private CanvasGroup backgroundInput;
        private bool savedInteractable, savedBlocksRaycasts;
        private bool modalPlaced;
        public bool IsReordering => reorderFinished != null;
        private List<CardInstance> CurrentDraft => cybernetic ? cyberneticDraft : ordinaryDraft;

        /// <summary>已付费的效果显式打开排序模式；两个页签使用独立草稿，关闭时提交，面板不结算费用。</summary>
        public void OpenReorder(BattleContext battle, Action<bool> finished)
        {
            if (!IsDrawPile) throw new InvalidOperationException("仅抽牌堆支持排序。");
            if (battle == null || finished == null || battle.IsFinished || battle.State != BattleState.PlayerAction) return;
            Hide(0); Bind(battle); ClearViews();
            ordinaryDraft = battle.Ordinary.GetDrawPileOrder().ToList();
            cyberneticDraft = battle.Cybernetic.GetDrawPileOrder().ToList();
            reorderFinished = finished; cybernetic = false;
            PlaceModal(); Show();
        }

        /// <summary>为当前卡面接入可复用拖拽组件，普通只读浏览不会接管 ScrollRect 的拖动。</summary>
        private void ConfigureDragItems()
        {
            if (!cardsRoot) return;
            if (!IsReordering)
            {
                if (dragList) dragList.Configure(scroll, null, false);
                foreach (var view in views)
                {
                    var item = view ? view.GetComponent<ReorderableDragItem>() : null;
                    if (item) item.enabled = false;
                }
                return;
            }
            if (!dragList)
            {
                dragList = cardsRoot.GetComponent<ReorderableDragList>();
                if (!dragList) dragList = cardsRoot.gameObject.AddComponent<ReorderableDragList>();
                dragList.Reordered += OnDraftReordered;
            }
            dragList.Configure(scroll, dragLayer, true);
            foreach (var view in views) if (view) dragList.Attach((RectTransform)view.transform);
        }

        /// <summary>接收布局索引变化，只调整当前页草稿，拖动期间不修改真实牌堆。</summary>
        private void OnDraftReordered(int from, int to)
        {
            if (!IsReordering || from < 0 || to < 0 || from >= CurrentDraft.Count || to >= CurrentDraft.Count) return;
            var card = CurrentDraft[from]; CurrentDraft.RemoveAt(from); CurrentDraft.Insert(to, card);
            var view = views[from]; views.RemoveAt(from); views.Insert(to, view);
            displayed = Array.AsReadOnly(CurrentDraft.ToArray());
        }

        /// <summary>两堆都必须仍与打开时的实例集合一致，外部抽弃牌不能导致部分提交。</summary>
        private bool ReorderStillValid() => context != null && !context.IsFinished &&
            DraftMatches(ordinaryDraft, context.Ordinary) && DraftMatches(cyberneticDraft, context.Cybernetic);

        /// <summary>校验完整、无重复的实例排列，不以卡牌名称或配置相同代替实例身份。</summary>
        private static bool DraftMatches(List<CardInstance> draft, CardDeck deck) => draft != null &&
            draft.Count == deck.DrawPile.Count && draft.Distinct().Count() == draft.Count && draft.All(deck.DrawPile.Contains);

        /// <summary>关闭按钮及 Esc 提交两套草稿；先校验双方再应用，费用与耐久不会再次结算。</summary>
        private void CompleteReorder()
        {
            if (dragList) dragList.CancelDrag();
            bool success = ReorderStillValid();
            if (success)
            {
                context.Ordinary.ReorderDrawPile(ordinaryDraft);
                context.Cybernetic.ReorderDrawPile(cyberneticDraft);
            }
            var callback = reorderFinished;
            ClearReorderState(); Hide(.15f);
            callback?.Invoke(success);
        }

        /// <summary>战斗切换、外部隐藏与停用只取消未提交草稿，已成功使用脑机的费用不重复处理。</summary>
        private void CancelReorder()
        {
            if (!IsReordering) return;
            var callback = reorderFinished; ClearReorderState(); callback?.Invoke(false);
        }

        /// <summary>先清空完成回调再清理输入，防止淡出、停用和战斗回调重复完成。</summary>
        private void ClearReorderState()
        {
            reorderFinished = null;
            if (dragList) dragList.Configure(scroll, null, false);
            ordinaryDraft = null; cyberneticDraft = null;
            foreach (var view in views)
            {
                var item = view ? view.GetComponent<ReorderableDragItem>() : null;
                if (item) item.enabled = false;
            }
        }

        /// <summary>将现有抽牌面板临时居中到顶层画布，黑幕吞掉其他区域输入；拖动浮层位于滚动裁剪之外。</summary>
        private void PlaceModal()
        {
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var rect = (RectTransform)transform;
            if (!modalRoot)
            {
                modalRoot = new GameObject("DrawPileReorderModal", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                modalRoot.gameObject.layer = gameObject.layer; modalRoot.SetParent(canvas.transform, false);
                StretchModal(modalRoot);
                var backdrop = modalRoot.GetComponent<Image>(); backdrop.color = new Color(0, 0, 0, .7f); backdrop.raycastTarget = true;
                dragLayer = new GameObject("DragLayer", typeof(RectTransform)).GetComponent<RectTransform>();
                dragLayer.gameObject.layer = gameObject.layer; dragLayer.SetParent(modalRoot, false); StretchModal(dragLayer);
            }
            savedParent = rect.parent; savedSibling = rect.GetSiblingIndex();
            savedMin = rect.anchorMin; savedMax = rect.anchorMax; savedPivot = rect.pivot;
            savedSize = rect.sizeDelta; savedPosition = rect.anchoredPosition; savedScale = rect.localScale;
            var size = rect.rect.size;
            var canvasSize = ((RectTransform)canvas.transform).rect.size;
            float fit = Mathf.Min(1, canvasSize.x * .9f / size.x, canvasSize.y * .9f / size.y);
            modalRoot.gameObject.SetActive(true); modalRoot.GetComponent<Image>().raycastTarget = true; modalRoot.SetAsLastSibling();
            rect.SetParent(modalRoot, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size * fit; rect.anchoredPosition = Vector2.zero; rect.localScale = Vector3.one;
            backgroundInput = savedParent ? savedParent.GetComponent<CanvasGroup>() : null;
            if (backgroundInput)
            {
                savedInteractable = backgroundInput.interactable; savedBlocksRaycasts = backgroundInput.blocksRaycasts;
                backgroundInput.interactable = backgroundInput.blocksRaycasts = false;
            }
            dragLayer.SetAsLastSibling(); modalPlaced = true;
        }

        /// <summary>淡出结束或立即关闭后恢复原场景位置，牌堆只读预览继续使用原来的布局。</summary>
        private void RestoreModalPlacement()
        {
            if (!modalPlaced) return;
            modalPlaced = false;
            if (backgroundInput)
            {
                backgroundInput.interactable = savedInteractable; backgroundInput.blocksRaycasts = savedBlocksRaycasts;
                backgroundInput = null;
            }
            var rect = (RectTransform)transform;
            if (savedParent)
            {
                rect.SetParent(savedParent, false); rect.SetSiblingIndex(savedSibling);
                rect.anchorMin = savedMin; rect.anchorMax = savedMax; rect.pivot = savedPivot;
                rect.sizeDelta = savedSize; rect.anchoredPosition = savedPosition; rect.localScale = savedScale;
            }
            if (modalRoot) modalRoot.gameObject.SetActive(false);
        }

        /// <summary>Unity 不允许在停用回调中重新挂接当前物体；先释放遮罩射线，再在停用过程结束后恢复层级。</summary>
        private void RestoreModalAfterDisable()
        {
            if (!modalPlaced) return;
            if (modalRoot) modalRoot.GetComponent<Image>().raycastTarget = false;
            DOVirtual.DelayedCall(0, () =>
            {
                if (this && !gameObject.activeInHierarchy) RestoreModalPlacement();
            }, true);
        }

        /// <summary>让临时遮罩与浮层覆盖整个父画布，跟随分辨率变化。</summary>
        private static void StretchModal(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
