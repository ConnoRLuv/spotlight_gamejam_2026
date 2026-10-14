using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>牌堆只读浏览的共用实现：页签、滚动、空状态及战斗绑定，不结算卡牌效果。</summary>
    public abstract class PilePreviewPanel : BasePanel
    {
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform cardsRoot;
        [SerializeField] private Text title;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private Button ordinaryTab;
        [SerializeField] private Button cyberneticTab;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text orderHint;
        private BattleContext context;
        private BattleContext subscribedContext;
        private bool cybernetic;
        private float viewportWidth = -1;
        private readonly List<CardView> views = new List<CardView>();
        private IReadOnlyList<CardInstance> displayed = Array.Empty<CardInstance>();
        public IReadOnlyList<CardInstance> DisplayedCards => displayed;
        public bool IsCyberneticPage => cybernetic;
        protected abstract bool IsDrawPile { get; }

        /// <summary>构建工具显式注入展示控件；保留旧弃牌面板的配置签名。</summary>
        public void Configure(CardPrefabCatalog cardCatalog, RectTransform content, Text heading, GameObject empty, ScrollRect scroller)
        { catalog = cardCatalog; cardsRoot = content; title = heading; emptyState = empty; scroll = scroller; }

        /// <summary>绑定两个页签、关闭按钮和顺序说明，不绑定任何出牌或移牌事件。</summary>
        public void ConfigureNavigation(Button ordinary, Button cyber, Button close, Text hint)
        {
            RemoveListeners(); ordinaryTab = ordinary; cyberneticTab = cyber; closeButton = close; orderHint = hint;
            if (isActiveAndEnabled) AddListeners();
        }

        /// <summary>新战斗绑定时关闭并清除旧内容，不因绑定而揭示牌堆。</summary>
        public void Bind(BattleContext value)
        {
            if (context == value) return;
            Unsubscribe(); Hide(0); ClearViews(); context = value; cybernetic = false;
        }

        protected override void OnEnable() { base.OnEnable(); AddListeners(); Subscribe(); }
        protected override void OnDisable() { RemoveListeners(); Unsubscribe(); base.OnDisable(); }
        protected override void OnDestroy() { Unsubscribe(); base.OnDestroy(); }
        private void Update() { if (IsVisible && Input.GetKeyDown(KeyCode.Escape)) Close(); }
        private void LateUpdate()
        {
            if (!scroll || !scroll.viewport || !cardsRoot) return;
            float width = scroll.viewport.rect.width;
            if (Mathf.Abs(width - viewportWidth) < 0.1f) return;
            viewportWidth = width;
            var grid = cardsRoot.GetComponent<GridLayoutGroup>();
            if (grid) grid.constraintCount = Mathf.Clamp(Mathf.FloorToInt((width - grid.padding.horizontal + grid.spacing.x) /
                (grid.cellSize.x + grid.spacing.x)), 1, 3);
        }

        /// <summary>只由显式调用打开；费用必须由效果调用方提前结算，本类不修改资源。</summary>
        public override void Show(float fadeSeconds = 0.15f)
        {
            ValidateDuration(fadeSeconds);
            if (context == null || context.IsFinished) return;
            if (!IsVisible) cybernetic = false;
            RefreshCards(); base.Show(fadeSeconds); Subscribe();
        }

        /// <summary>淡出开始便解除战斗订阅，不让隐藏面板保留旧战斗监听。</summary>
        public override void Hide(float fadeSeconds = 0f)
        { ValidateDuration(fadeSeconds); Unsubscribe(); base.Hide(fadeSeconds); }

        /// <summary>关闭按钮及 Esc 共用入口，开始淡出时立即停止拦截鼠标。</summary>
        public void Close() => Hide(0.15f);
        /// <summary>切换到普通牌，不移动真实卡牌或消耗资源。</summary>
        public void ShowOrdinary() { cybernetic = false; if (IsVisible) RefreshCards(); }
        /// <summary>切换到义体牌，不移动真实卡牌或消耗资源。</summary>
        public void ShowCybernetic() { cybernetic = true; if (IsVisible) RefreshCards(); }

        /// <summary>绑定运行时控件事件，移除与添加保持成对。</summary>
        private void AddListeners()
        {
            if (ordinaryTab) ordinaryTab.onClick.AddListener(ShowOrdinary);
            if (cyberneticTab) cyberneticTab.onClick.AddListener(ShowCybernetic);
            if (closeButton) closeButton.onClick.AddListener(Close);
        }
        /// <summary>解除控件事件，避免重新启用后重复响应。</summary>
        private void RemoveListeners()
        {
            if (ordinaryTab) ordinaryTab.onClick.RemoveListener(ShowOrdinary);
            if (cyberneticTab) cyberneticTab.onClick.RemoveListener(ShowCybernetic);
            if (closeButton) closeButton.onClick.RemoveListener(Close);
        }
        /// <summary>只在显示期间订阅战斗，隐藏状态不会持续生成预览。</summary>
        private void Subscribe()
        {
            if (context == null || subscribedContext == context) return;
            subscribedContext = context; subscribedContext.Changed += OnBattleChanged;
            subscribedContext.StateChanged += OnBattleStateChanged;
        }
        /// <summary>解除旧战斗引用，防止旧遭遇更新当前面板。</summary>
        private void Unsubscribe()
        {
            if (subscribedContext != null)
            {
                subscribedContext.Changed -= OnBattleChanged;
                subscribedContext.StateChanged -= OnBattleStateChanged;
            }
            subscribedContext = null;
        }
        /// <summary>战斗结束立即关闭；其余变化只刷新当前页，不改变滚动相同内容。</summary>
        private void OnBattleChanged(BattleContext value)
        { if (value.IsFinished) Hide(0); else if (IsVisible) RefreshCards(); }
        /// <summary>胜负阶段立即关闭，即使调用方尚未发布普通数据刷新通知。</summary>
        private void OnBattleStateChanged(BattleState state)
        { if (state == BattleState.Victory || state == BattleState.Defeat) Hide(0); }
        /// <summary>获取当前页真实牌区，只读取，不触发重洗或抽牌。</summary>
        private CardZone Zone(bool cyber)
        {
            var deck = cyber ? context.Cybernetic : context.Ordinary;
            return IsDrawPile ? deck.DrawPile : deck.DiscardPile;
        }

        /// <summary>尾部为下一张抽取或最近弃置，反向快照用于展示，原列表顺序保持不变。</summary>
        public void RefreshCards()
        {
            if (context == null) return;
            var cards = Zone(cybernetic).Cards.Reverse().ToArray();
            int ordinaryCount = Zone(false).Count, cyberCount = Zone(true).Count;
            if (title) title.text = (IsDrawPile ? "抽牌堆" : "弃牌堆") + " · 普通 " + ordinaryCount + " / 义体 " + cyberCount;
            if (ordinaryTab) ordinaryTab.GetComponentInChildren<Text>().text = "普通牌（" + ordinaryCount + "）";
            if (cyberneticTab) cyberneticTab.GetComponentInChildren<Text>().text = "义体牌（" + cyberCount + "）";
            if (ordinaryTab) ordinaryTab.interactable = cybernetic;
            if (cyberneticTab) cyberneticTab.interactable = !cybernetic;
            if (orderHint) orderHint.text = IsDrawPile ? "堆顶 → 按抽取顺序展示 · 仅查看" : "最近弃置 → 较早弃置 · 仅查看";
            if (emptyState) emptyState.SetActive(cards.Length == 0);
            if (displayed.SequenceEqual(cards))
            {
                // 牌区顺序未变时仍可能修复或消耗耐久；更新卡面但保留滚动和对象。
                foreach (var view in views) if (view) view.Refresh();
                return;
            }
            ClearViews(); displayed = Array.AsReadOnly(cards);
            foreach (var card in cards)
            {
                var prefab = catalog ? catalog.Find(card.Data) : null;
                if (!prefab) continue;
                var view = Instantiate(prefab, cardsRoot); view.Bind(card);
                view.GetComponent<Button>().transition = Selectable.Transition.None;
                view.SetInteractable(false);
                // 卡面不参与射线；滚动事件由 viewport 接收，也不能触发选牌或拖拽。
                foreach (var graphic in view.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
                views.Add(view);
            }
            if (scroll) scroll.verticalNormalizedPosition = 1;
            if (cardsRoot) LayoutRebuilder.MarkLayoutForRebuild(cardsRoot);
        }
        /// <summary>先停用旧展示再销毁，防止延迟销毁期间出现重复卡面。</summary>
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
