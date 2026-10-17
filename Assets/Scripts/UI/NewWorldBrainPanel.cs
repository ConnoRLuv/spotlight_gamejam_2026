using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 新世界脑机的重排面板：每次使用只移动一张牌——先选择一张牌，再决定把它放到哪个位置。
    /// </summary>
    /// <remarks>
    /// 只承载展示与交互，不结算费用或移动牌区；确认时把最终顺序封装进 CardPlayRequest 交由
    /// BattleController 原子校验并执行，取消与 Esc 均不消耗资源。面板由编辑器构建工具注入控件。
    /// </remarks>
    public sealed class NewWorldBrainPanel : MonoBehaviour
    {
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private Text title;
        [SerializeField] private Text hint;
        [SerializeField] private RectTransform ordinaryCardsRoot;
        [SerializeField] private Button ordinaryEndButton;
        [SerializeField] private RectTransform cyberneticCardsRoot;
        [SerializeField] private Button cyberneticEndButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button resetButton;

        private readonly List<CardInstance> ordinary = new List<CardInstance>();
        private readonly List<CardInstance> cybernetic = new List<CardInstance>();
        private readonly List<CardInstance> ordinaryOriginal = new List<CardInstance>();
        private readonly List<CardInstance> cyberneticOriginal = new List<CardInstance>();
        private readonly List<CardView> views = new List<CardView>();
        private BattleController battle;
        private CardInstance pending;
        private CardInstance selected;
        private int selectedPile = -1;
        private bool moved;
        private Action<CardPlayResult> onFinished;
        private bool wired;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>由构建工具注入展示控件与按钮，不绑定任何具体战斗。</summary>
        public void Configure(CardPrefabCatalog cardCatalog, Text heading, Text tip,
            RectTransform ordinaryCards, Button ordinaryEnd, RectTransform cyberneticCards, Button cyberneticEnd,
            Button confirm, Button cancel, Button reset)
        {
            catalog = cardCatalog; title = heading; hint = tip;
            ordinaryCardsRoot = ordinaryCards; ordinaryEndButton = ordinaryEnd;
            cyberneticCardsRoot = cyberneticCards; cyberneticEndButton = cyberneticEnd;
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
            ordinary.Clear(); ordinary.AddRange(controller.Context.Ordinary.GetDrawPileOrder());
            cybernetic.Clear(); cybernetic.AddRange(controller.Context.Cybernetic.GetDrawPileOrder());
            ordinaryOriginal.Clear(); ordinaryOriginal.AddRange(ordinary);
            cyberneticOriginal.Clear(); cyberneticOriginal.AddRange(cybernetic);
            selected = null; selectedPile = -1; moved = false;
            if (title) title.text = "调整一张牌的位置";
            gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>关闭面板并清理卡面，不提交任何请求。</summary>
        public void Close()
        {
            gameObject.SetActive(false);
            ClearViews();
            battle = null; pending = null; onFinished = null;
            selected = null; selectedPile = -1; moved = false;
        }

        private List<CardInstance> Pile(int pile) => pile == 0 ? ordinary : cybernetic;

        /// <summary>重建两栏展示；选中一张牌并放置一次后允许确认。</summary>
        private void Refresh()
        {
            ClearViews();
            if (hint)
            {
                if (moved) hint.text = "已移动一张牌，点击确认生效；点重置可重来。";
                else if (selected != null) hint.text = "已选择「" + selected.Data.cardName + "」，点击目标牌把它放到其前面，或点“放到末尾”。";
                else hint.text = "先选择一张要移动的牌。";
            }
            RenderPile(ordinaryCardsRoot, ordinary, 0);
            RenderPile(cyberneticCardsRoot, cybernetic, 1);
            if (ordinaryEndButton) ordinaryEndButton.interactable = !moved && selectedPile == 0;
            if (cyberneticEndButton) cyberneticEndButton.interactable = !moved && selectedPile == 1;
            if (confirmButton) confirmButton.interactable = moved;
        }

        private void RenderPile(RectTransform root, List<CardInstance> pile, int pileIndex)
        {
            if (!root) return;
            for (int i = 0; i < pile.Count; i++)
            {
                var card = pile[i];
                var prefab = catalog ? catalog.Find(card.Data) : null;
                if (!prefab) continue;
                var view = Instantiate(prefab, root);
                view.Bind(card);
                view.SetInteractable(!moved);
                if (selected == card) view.transform.localScale = Vector3.one * 1.08f;
                var captured = card;
                var capturedPile = pileIndex;
                view.Selected += _ => OnCardClicked(capturedPile, captured);
                views.Add(view);
            }
        }

        private void OnCardClicked(int pileIndex, CardInstance card)
        {
            if (battle == null || moved) return;
            if (selected == card) { selected = null; selectedPile = -1; Refresh(); return; }
            var pile = Pile(pileIndex);
            if (selected != null && selectedPile == pileIndex)
            {
                // 把已选牌放到点击的这张牌前面。
                pile.Remove(selected);
                int target = pile.IndexOf(card);
                if (target < 0) pile.Add(selected);
                else pile.Insert(target, selected);
                selected = null; selectedPile = -1; moved = true;
                Refresh();
                return;
            }
            // 否则切换当前选中的牌（允许改选另一张）。
            selected = card; selectedPile = pileIndex;
            Refresh();
        }

        private void PlaceAtEnd(int pileIndex)
        {
            if (battle == null || moved || selected == null || selectedPile != pileIndex) return;
            var pile = Pile(pileIndex);
            pile.Remove(selected);
            pile.Add(selected);
            selected = null; selectedPile = -1; moved = true;
            Refresh();
        }

        private void ResetOrder()
        {
            ordinary.Clear(); ordinary.AddRange(ordinaryOriginal);
            cybernetic.Clear(); cybernetic.AddRange(cyberneticOriginal);
            selected = null; selectedPile = -1; moved = false;
            Refresh();
        }

        private void Confirm()
        {
            if (battle == null || pending == null || !moved) { Refresh(); return; }
            var request = new CardPlayRequest(pending, reorder: ordinary.Concat(cybernetic).ToArray());
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
            if (ordinaryEndButton) ordinaryEndButton.onClick.AddListener(() => PlaceAtEnd(0));
            if (cyberneticEndButton) cyberneticEndButton.onClick.AddListener(() => PlaceAtEnd(1));
        }

        private void UnwireButtons()
        {
            if (!wired) return;
            wired = false;
            if (confirmButton) confirmButton.onClick.RemoveListener(Confirm);
            if (cancelButton) cancelButton.onClick.RemoveListener(Cancel);
            if (resetButton) resetButton.onClick.RemoveListener(ResetOrder);
            if (ordinaryEndButton) ordinaryEndButton.onClick.RemoveAllListeners();
            if (cyberneticEndButton) cyberneticEndButton.onClick.RemoveAllListeners();
        }

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