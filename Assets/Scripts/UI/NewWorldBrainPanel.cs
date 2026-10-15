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
    public sealed class NewWorldBrainPanel : MonoBehaviour
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

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>由构建工具注入展示控件与按钮，不绑定任何具体战斗。</summary>
        public void Configure(CardPrefabCatalog cardCatalog, Text heading, Text tip,
            RectTransform ordinaryPool, RectTransform ordinaryOrder, RectTransform cyberneticPool, RectTransform cyberneticOrder,
            Button confirm, Button cancel, Button reset)
        {
            catalog = cardCatalog; title = heading; hint = tip;
            ordinaryPoolRoot = ordinaryPool; ordinaryOrderRoot = ordinaryOrder;
            cyberneticPoolRoot = cyberneticPool; cyberneticOrderRoot = cyberneticOrder;
            confirmButton = confirm; cancelButton = cancel; resetButton = reset;
            WireButtons();
        }

        private void OnEnable() { WireButtons(); }
        private void OnDisable() { UnwireButtons(); }

        /// <summary>打开面板并载入两套抽牌堆的当前顺序；关闭旧内容但不动真实牌区。</summary>
        public void Open(BattleController controller, CardInstance card, Action<CardPlayResult> finished)
        {
            if (controller == null || card == null) return;
            battle = controller; pending = card; onFinished = finished;
            ordinaryPool.Clear(); ordinaryPool.AddRange(controller.Context.Ordinary.GetDrawPileOrder());
            cyberneticPool.Clear(); cyberneticPool.AddRange(controller.Context.Cybernetic.GetDrawPileOrder());
            ordinaryOrder.Clear(); cyberneticOrder.Clear();
            if (title) title.text = "观看并调整抽牌堆";
            gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>关闭面板并清理卡面，不提交任何请求。</summary>
        public void Close()
        {
            gameObject.SetActive(false);
            ClearViews();
            battle = null; pending = null; onFinished = null;
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

        private void RenderPile(RectTransform poolRoot, RectTransform orderRoot, List<CardInstance> pool, List<CardInstance> order, int pile)
        {
            foreach (var card in pool) AddView(poolRoot, card, () => MoveToOrder(pile, card));
            foreach (var card in order) AddView(orderRoot, card, () => MoveToPool(pile, card));
        }

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

        private void MoveToOrder(int pile, CardInstance card)
        {
            var pool = pile == 0 ? ordinaryPool : cyberneticPool;
            var order = pile == 0 ? ordinaryOrder : cyberneticOrder;
            if (!pool.Remove(card)) return;
            order.Add(card); Refresh();
        }

        private void MoveToPool(int pile, CardInstance card)
        {
            var pool = pile == 0 ? ordinaryPool : cyberneticPool;
            var order = pile == 0 ? ordinaryOrder : cyberneticOrder;
            if (!order.Remove(card)) return;
            pool.Add(card); Refresh();
        }

        private void ResetOrder()
        {
            ordinaryPool.AddRange(ordinaryOrder); ordinaryOrder.Clear();
            cyberneticPool.AddRange(cyberneticOrder); cyberneticOrder.Clear();
            Refresh();
        }

        private void Confirm()
        {
            if (battle == null || pending == null || ordinaryPool.Count + cyberneticPool.Count != 0) { Refresh(); return; }
            var request = new CardPlayRequest(pending, reorder: ordinaryOrder.Concat(cyberneticOrder).ToArray());
            var result = battle.TryPlayRequest(request);
            var callback = onFinished;
            Close();
            callback?.Invoke(result);
        }

        private void Cancel()
        {
            var callback = onFinished;
            Close();
            callback?.Invoke(new CardPlayResult(CardPlayFailure.InvalidPhase, "已取消。"));
        }

        private void WireButtons()
        {
            if (wired) return;
            wired = true;
            if (confirmButton) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton) cancelButton.onClick.AddListener(Cancel);
            if (resetButton) resetButton.onClick.AddListener(ResetOrder);
        }

        private void UnwireButtons()
        {
            if (!wired) return;
            wired = false;
            if (confirmButton) confirmButton.onClick.RemoveListener(Confirm);
            if (cancelButton) cancelButton.onClick.RemoveListener(Cancel);
            if (resetButton) resetButton.onClick.RemoveListener(ResetOrder);
        }

        private void ClearViews()
        {
            foreach (var view in views)
            {
                if (!view) continue;
                view.Selected -= _ => { };
                view.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject);
            }
            views.Clear();
        }
    }
}