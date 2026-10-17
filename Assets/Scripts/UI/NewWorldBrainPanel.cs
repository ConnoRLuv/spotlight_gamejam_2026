using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 新世界脑机的重排面板：分普通/义体两栏展示抽牌堆，点击卡牌依次加入新顺序，确认后提交。
    /// </summary>
    /// <remarks>
    /// 只承载展示与交互，不结算费用或移动牌区；确认时把顺序封装进 CardPlayRequest 交由
    /// BattleController 原子校验并执行，取消与 Esc 均不消耗资源。面板由编辑器构建工具注入控件。
    /// </remarks>
    public sealed class NewWorldBrainPanel : BasePanel
    {
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private Text title;
        [SerializeField] private Text hint;
        [SerializeField] private RectTransform ordinaryPoolRoot;
        [SerializeField] private RectTransform ordinaryOrderRoot;
        [SerializeField] private RectTransform cyberneticPoolRoot;
        [SerializeField] private RectTransform cyberneticOrderRoot;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button resetButton;

        private readonly List<CardInstance> ordinaryPool = new List<CardInstance>();
        private readonly List<CardInstance> cyberneticPool = new List<CardInstance>();
        private readonly List<CardInstance> ordinaryOrder = new List<CardInstance>();
        private readonly List<CardInstance> cyberneticOrder = new List<CardInstance>();
        private readonly List<CardView> views = new List<CardView>();
        private BattleController battle;
        private CardInstance pending;
        private Action<CardPlayResult> onFinished;
        private bool wired;

        public bool IsOpen => IsVisible;

        /// <summary>由构建工具注入展示控件与按钮，不绑定任何具体战斗。</summary>
        public void Configure(CardPrefabCatalog cardCatalog, Text heading, Text tip,
            RectTransform ordinaryPool, RectTransform ordinaryOrder, RectTransform cyberneticPool, RectTransform cyberneticOrder,
            Button confirm, Button cancel, Button reset)
        {
            UnwireButtons();
            catalog = cardCatalog; title = heading; hint = tip;
            ordinaryPoolRoot = ordinaryPool; ordinaryOrderRoot = ordinaryOrder;
            cyberneticPoolRoot = cyberneticPool; cyberneticOrderRoot = cyberneticOrder;
            confirmButton = confirm; cancelButton = cancel; resetButton = reset;
            WireButtons();
        }

        protected override void OnEnable() { base.OnEnable(); WireButtons(); }
        protected override void OnDisable()
        {
            var callback = onFinished;
            UnwireButtons(); ClearViews(); ClearRequest(); base.OnDisable();
            // 外部停用也必须解除调用方的等待；正常 Hide 已提前清空回调，不会重复通知。
            callback?.Invoke(new CardPlayResult(CardPlayFailure.InvalidPhase, "面板已关闭。"));
        }
        private void Update()
        {
            if (!IsVisible) return;
            if (battle == null || battle.Context.IsFinished || Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        /// <summary>打开面板并载入两套抽牌堆的当前顺序；关闭旧内容但不动真实牌区。</summary>
        public void Open(BattleController controller, CardInstance card, Action<CardPlayResult> finished)
        {
            if (controller == null || card == null || controller.Context.IsFinished) return;
            Close(0);
            battle = controller; pending = card; onFinished = finished;
            ordinaryPool.Clear(); ordinaryPool.AddRange(controller.Context.Ordinary.GetDrawPileOrder());
            cyberneticPool.Clear(); cyberneticPool.AddRange(controller.Context.Cybernetic.GetDrawPileOrder());
            ordinaryOrder.Clear(); cyberneticOrder.Clear();
            if (title) title.text = "观看并调整抽牌堆";
            Refresh();
            Show();
        }

        /// <summary>显示待确认的重排交互；过渡时间以秒计，不结算真实牌区。</summary>
        public override void Show(float fadeSeconds = 0.15f)
        {
            ValidateDuration(fadeSeconds);
            if (battle == null || pending == null || battle.Context.IsFinished) return;
            base.Show(fadeSeconds);
        }

        /// <summary>隐藏开始即释放交互和待提交引用，淡出结束后销毁临时卡面。</summary>
        public override void Hide(float fadeSeconds = 0.15f)
        {
            ValidateDuration(fadeSeconds);
            ClearRequest(); base.Hide(fadeSeconds);
            if (!gameObject.activeSelf) ClearViews();
        }

        /// <summary>关闭不提交请求；生命周期清理可传零秒，通常使用 0.15 秒淡出。</summary>
        public void Close(float fadeSeconds = 0.15f) => Hide(fadeSeconds);

        /// <summary>释放请求与副本顺序，不修改战斗中的真实牌区。</summary>
        private void ClearRequest()
        {
            battle = null; pending = null; onFinished = null;
            ordinaryPool.Clear(); cyberneticPool.Clear(); ordinaryOrder.Clear(); cyberneticOrder.Clear();
        }

        /// <summary>重建两栏展示；全部卡牌进入新顺序后允许确认。</summary>
        private void Refresh()
        {
            ClearViews();
            bool complete = ordinaryPool.Count + cyberneticPool.Count == 0;
            if (hint) hint.text = complete
                ? "已排好全部卡牌，点击确认生效。"
                : "点击卡牌依次加入新顺序（左到右为下一张先抽到）。";
            RenderPile(ordinaryPoolRoot, ordinaryOrderRoot, ordinaryPool, ordinaryOrder, 0);
            RenderPile(cyberneticPoolRoot, cyberneticOrderRoot, cyberneticPool, cyberneticOrder, 1);
            if (confirmButton) confirmButton.interactable = complete;
        }

        /// <summary>分别渲染待排区和已排区，卡牌点击只移动展示副本。</summary>
        private void RenderPile(RectTransform poolRoot, RectTransform orderRoot, List<CardInstance> pool, List<CardInstance> order, int pile)
        {
            foreach (var card in pool) AddView(poolRoot, card, () => MoveToOrder(pile, card));
            foreach (var card in order) AddView(orderRoot, card, () => MoveToPool(pile, card));
        }

        /// <summary>复用卡面目录创建交互预览，点击不走手牌直接出牌入口。</summary>
        private void AddView(RectTransform root, CardInstance card, Action onClick)
        {
            var prefab = catalog ? catalog.Find(card.Data) : null;
            if (!prefab || !root) return;
            var view = Instantiate(prefab, root);
            view.Bind(card);
            view.SetInteractable(true);
            view.Selected += _ => onClick();
            views.Add(view);
        }

        /// <summary>把一个预览候选追加到新抽取顺序。</summary>
        private void MoveToOrder(int pile, CardInstance card)
        {
            var pool = pile == 0 ? ordinaryPool : cyberneticPool;
            var order = pile == 0 ? ordinaryOrder : cyberneticOrder;
            if (!pool.Remove(card)) return;
            order.Add(card); Refresh();
        }

        /// <summary>把已排候选退回待排区，允许重新安排位置。</summary>
        private void MoveToPool(int pile, CardInstance card)
        {
            var pool = pile == 0 ? ordinaryPool : cyberneticPool;
            var order = pile == 0 ? ordinaryOrder : cyberneticOrder;
            if (!order.Remove(card)) return;
            pool.Add(card); Refresh();
        }

        /// <summary>恢复打开时的原始抽牌顺序，不把曾经的点击顺序当作初始顺序。</summary>
        private void ResetOrder()
        {
            if (battle == null) return;
            ordinaryPool.Clear(); ordinaryPool.AddRange(battle.Context.Ordinary.GetDrawPileOrder()); ordinaryOrder.Clear();
            cyberneticPool.Clear(); cyberneticPool.AddRange(battle.Context.Cybernetic.GetDrawPileOrder()); cyberneticOrder.Clear();
            Refresh();
        }

        /// <summary>通过现有原子出牌服务校验并结算完整顺序，关闭后只回调一次。</summary>
        private void Confirm()
        {
            if (battle == null || pending == null || ordinaryPool.Count + cyberneticPool.Count != 0) { Refresh(); return; }
            var request = new CardPlayRequest(pending, reorder: ordinaryOrder.Concat(cyberneticOrder).ToArray());
            var callback = onFinished;
            onFinished = null;
            // 成功结算会触发 HUD 刷新并可能关闭本面板，必须在提交前保存单次完成回调。
            var result = battle.TryPlayRequest(request);
            Close();
            callback?.Invoke(result);
        }

        /// <summary>取消只清理预览；费用、耐久和次数均不结算。</summary>
        private void Cancel()
        {
            var callback = onFinished;
            Close();
            callback?.Invoke(new CardPlayResult(CardPlayFailure.InvalidPhase, "已取消。"));
        }

        /// <summary>幂等连接按钮，避免反复打开导致多次确认或取消。</summary>
        private void WireButtons()
        {
            if (wired) return;
            wired = true;
            if (confirmButton) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton) cancelButton.onClick.AddListener(Cancel);
            if (resetButton) resetButton.onClick.AddListener(ResetOrder);
        }

        /// <summary>成对移除按钮监听，供停用和重新配置使用。</summary>
        private void UnwireButtons()
        {
            if (!wired) return;
            wired = false;
            if (confirmButton) confirmButton.onClick.RemoveListener(Confirm);
            if (cancelButton) cancelButton.onClick.RemoveListener(Cancel);
            if (resetButton) resetButton.onClick.RemoveListener(ResetOrder);
        }

        /// <summary>释放预览卡面及其点击委托；回收前先停用，避免延迟销毁期间接收输入。</summary>
        private void ClearViews()
        {
            foreach (var view in views)
            {
                if (!view) continue;
                view.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject);
            }
            views.Clear();
        }
    }
}
