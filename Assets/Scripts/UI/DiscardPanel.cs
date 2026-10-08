using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>只读展示当前普通与义体弃牌；重新洗入抽牌堆的牌会从展示中移除。</summary>
    public sealed class DiscardPanel : BasePanel, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform cardsRoot;
        [SerializeField] private Text title;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private ScrollRect scroll;
        private BattleContext context;
        private BattleContext subscribedContext;
        private readonly List<CardView> views = new List<CardView>();
        private IReadOnlyList<CardInstance> displayed = Array.Empty<CardInstance>();
        public IReadOnlyList<CardInstance> DisplayedCards => displayed;
        public event Action<bool> PointerPresenceChanged;

        /// <summary>编辑器创建预制体时绑定控件；不持有场景中的战斗对象。</summary>
        public void Configure(CardPrefabCatalog cardCatalog, RectTransform content, Text heading, GameObject empty, ScrollRect scroller)
        { catalog = cardCatalog; cardsRoot = content; title = heading; emptyState = empty; scroll = scroller; }

        /// <summary>由 HUD 注入当前战斗，替换战斗时解除旧订阅和显示内容。</summary>
        public void Bind(BattleContext value)
        {
            if (context == value) return;
            Unsubscribe(); context = value;
            Hide(); ClearViews();
            if (isActiveAndEnabled) Subscribe();
        }
        protected override void OnEnable() { base.OnEnable(); Subscribe(); }
        protected override void OnDisable()
        {
            Unsubscribe(); base.OnDisable();
            PointerPresenceChanged?.Invoke(false);
        }
        protected override void OnDestroy() { Unsubscribe(); base.OnDestroy(); }
        /// <summary>订阅当前战斗的状态变化，使弃牌展示及时更新。</summary>
        private void Subscribe()
        {
            if (context == null || subscribedContext == context) return;
            subscribedContext = context; subscribedContext.Changed += OnBattleChanged;
        }
        /// <summary>解除战斗状态订阅，避免重复回调及旧战斗引用。</summary>
        private void Unsubscribe()
        {
            if (subscribedContext != null) subscribedContext.Changed -= OnBattleChanged;
            subscribedContext = null;
        }
        /// <summary>响应战斗状态变化，更新当前界面展示。</summary>
        private void OnBattleChanged(BattleContext value) { if (IsVisible) RefreshCards(); }

        /// <summary>显示前读取真实牌区；没有绑定战斗时不显示空的业务窗口。</summary>
        public override void Show(float fadeSeconds = 0f)
        {
            ValidateDuration(fadeSeconds);
            if (context == null) return;
            RefreshCards(); base.Show(fadeSeconds);
        }
        /// <summary>通知悬停入口鼠标已进入弃牌面板，使浏览期间保持显示。</summary>
        public void OnPointerEnter(PointerEventData eventData) { PointerPresenceChanged?.Invoke(true); }
        /// <summary>通知悬停入口鼠标已离开弃牌面板，交由入口决定是否关闭。</summary>
        public void OnPointerExit(PointerEventData eventData) { PointerPresenceChanged?.Invoke(false); }

        /// <summary>只展示当前弃牌区，普通牌在前、义体牌在后；浏览不扣费、不移动牌。</summary>
        public void RefreshCards()
        {
            if (context == null) return;
            var cards = context.Ordinary.DiscardPile.Cards.Concat(context.Cybernetic.DiscardPile.Cards).ToArray();
            title.text = "弃牌堆 · 普通 " + context.Ordinary.DiscardPile.Count + " / 义体 " + context.Cybernetic.DiscardPile.Count;
            emptyState.SetActive(cards.Length == 0);
            // 数值或 AP 更新不应重复生成同一批预览对象，保留当前滚动位置。
            if (displayed.SequenceEqual(cards)) return;
            ClearViews(); displayed = Array.AsReadOnly(cards);
            foreach (var card in cards)
            {
                var view = Instantiate(catalog.Find(card.Data), cardsRoot);
                view.Bind(card);
                // 弃牌只能查看；关闭禁用态变色，保持预制体的低饱和度主题。
                view.GetComponent<Button>().transition = Selectable.Transition.None;
                view.SetInteractable(false); views.Add(view);
            }
            if (scroll) scroll.verticalNormalizedPosition = 1;
            LayoutRebuilder.MarkLayoutForRebuild(cardsRoot);
        }
        /// <summary>停用并销毁现有卡牌展示对象，清空展示集合。</summary>
        private void ClearViews()
        {
            foreach (var view in views)
            {
                if (!view) continue;
                view.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject);
            }
            views.Clear(); displayed = Array.Empty<CardInstance>();
        }
    }
}
