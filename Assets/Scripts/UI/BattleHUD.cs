using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>将场景控件绑定到战斗；只展示权威状态和转发指令，不在 UI 中结算费用。</summary>
    public sealed class BattleHUD : MonoBehaviour
    {
        [SerializeField] private GameBootstrap bootstrap;
        [SerializeField] private BattleSceneReferences scene;
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform cardsRoot;
        [SerializeField] private ScrollRect handScroll;
        [SerializeField] private Button ordinaryTab;
        [SerializeField] private Button cyberneticTab;
        [SerializeField] private Button conversionButton;
        [SerializeField] private Button enemyButton;
        [SerializeField] private GameObject choicePanel;
        [SerializeField] private RectTransform choicesRoot;
        [SerializeField] private Button cancelChoiceButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text[] durabilityTexts;
        [SerializeField] private DiscardPanel discardPanel;
        [SerializeField] private CyberneticEquipmentSlot[] equipmentSlots = new CyberneticEquipmentSlot[0];
        private readonly List<CardView> handViews = new List<CardView>();
        private readonly List<CardView> choiceViews = new List<CardView>();
        private BattleController battle;
        private bool cybernetic;
        private CardInstance pendingCard;
        private float lastViewportHeight = -1;
        public BattleController Battle => battle;
        public bool IsChoosing => choicePanel && choicePanel.activeSelf;
        public CardInstance PendingCard => pendingCard;

        private void OnEnable()
        {
            if (!bootstrap || !scene || !catalog) return;
            bootstrap.BattleCreated += Bind;
            scene.endTurnButton.onClick.AddListener(EndTurn);
            ordinaryTab.onClick.AddListener(ShowOrdinary);
            cyberneticTab.onClick.AddListener(ShowCybernetic);
            conversionButton.onClick.AddListener(Convert);
            enemyButton.onClick.AddListener(ChooseEnemy);
            cancelChoiceButton.onClick.AddListener(CancelSelection);
            // OnEnable 可能早于 bootstrap.Start，也可能在战斗进行中重新启用。
            if (bootstrap.Battle != null) Bind(bootstrap.Battle);
            else { choicePanel.SetActive(false); scene.endTurnButton.interactable = false; }
        }

        private void OnDisable()
        {
            if (bootstrap) bootstrap.BattleCreated -= Bind;
            Unbind();
            if (scene && scene.endTurnButton) scene.endTurnButton.onClick.RemoveListener(EndTurn);
            if (ordinaryTab) ordinaryTab.onClick.RemoveListener(ShowOrdinary);
            if (cyberneticTab) cyberneticTab.onClick.RemoveListener(ShowCybernetic);
            if (conversionButton) conversionButton.onClick.RemoveListener(Convert);
            if (enemyButton) enemyButton.onClick.RemoveListener(ChooseEnemy);
            if (cancelChoiceButton) cancelChoiceButton.onClick.RemoveListener(CancelSelection);
            CancelSelection(); ClearViews(handViews);
        }

        private void Update()
        {
            // 保留旧输入系统，仅处理取消；状态展示均由事件驱动。
            if (pendingCard != null && Input.GetKeyDown(KeyCode.Escape)) CancelSelection();
        }
        private void LateUpdate()
        {
            // 窗口尺寸改变时只调整现有展示，不重新生成整手牌。
            if (handScroll && handScroll.viewport && Mathf.Abs(lastViewportHeight - handScroll.viewport.rect.height) > 0.1f)
                UpdateCardScale();
        }
        /// <summary>根据手牌视口高度等比缩放现有卡面，并请求重新布局。</summary>
        private void UpdateCardScale()
        {
            if (!handScroll || !handScroll.viewport) return;
            lastViewportHeight = handScroll.viewport.rect.height;
            var scale = Mathf.Clamp((lastViewportHeight - 4) / 210f, 0.1f, 6f / 7f);
            foreach (var view in handViews) if (view) view.transform.localScale = Vector3.one * scale;
            LayoutRebuilder.MarkLayoutForRebuild(cardsRoot);
        }

        /// <summary>解除旧战斗订阅，绑定新战斗状态并刷新界面。</summary>
        private void Bind(BattleController value)
        {
            Unbind(); battle = value; pendingCard = null; choicePanel.SetActive(false);
            battle.Context.Changed += OnBattleChanged;
            if (discardPanel) discardPanel.Bind(battle.Context);
            Refresh();
        }
        /// <summary>解除战斗事件订阅并清空关联展示，避免引用上一场战斗。</summary>
        private void Unbind()
        {
            if (battle != null) battle.Context.Changed -= OnBattleChanged;
            if (discardPanel) discardPanel.Bind(null);
            foreach (var slot in equipmentSlots) if (slot) slot.Refresh(null);
            battle = null;
        }
        /// <summary>响应战斗状态变化，更新当前界面展示。</summary>
        private void OnBattleChanged(BattleContext context)
        {
            if (context.IsFinished || pendingCard != null && !context.Ordinary.Hand.Contains(pendingCard) && !context.Cybernetic.Hand.Contains(pendingCard))
                CancelSelection();
            Refresh();
        }

        /// <summary>切换到普通手牌页并刷新卡牌展示。</summary>
        public void ShowOrdinary() { cybernetic = false; Refresh(); }
        /// <summary>切换到义体手牌页并刷新卡牌展示。</summary>
        public void ShowCybernetic() { cybernetic = true; Refresh(); }

        /// <summary>自身牌直接提交；攻击等待敌人点击，脑机等待候选确认。</summary>
        public void SelectCard(CardInstance card)
        {
            if (battle == null || battle.Context.IsFinished || IsChoosing || card == null || card.Data.locksInHand) return;
            CancelSelection();
            if (card.Data.effects.Any(effect => effect.RequiresChoice))
            {
                var choices = battle.GetCyberneticChoices(card);
                if (choices.Count == 0) { SetStatus("义体抽牌堆为空。"); return; }
                if (!battle.Context.Player.CanSpendSanity(card.Data.sanityCost) || card.Source == null || card.Source.Durability <= 0 ||
                    !battle.Context.Usage.CanUse(card.Source.Data.slot)) { SetStatus("理智为零，或来源义体当前不可用。"); return; }
                pendingCard = card; choicePanel.SetActive(true);
                foreach (var choice in choices)
                {
                    var view = Instantiate(catalog.Find(choice.Data), choicesRoot);
                    view.Bind(choice); view.SetInteractable(true); view.Selected += ConfirmChoice;
                    choiceViews.Add(view);
                }
                SetStatus("选择一张义体牌；取消不消耗资源。"); Refresh();
            }
            else if (card.Data.targetType == CardTargetType.SingleEnemy)
            { pendingCard = card; SetStatus("点击敌人确认攻击；Esc 取消。"); }
            else Report(battle.TryPlay(card));
        }

        /// <summary>以当前待使用的卡牌向敌人提交出牌指令。</summary>
        public void ChooseEnemy()
        {
            if (battle == null || pendingCard == null || IsChoosing) return;
            var card = pendingCard; pendingCard = null;
            Report(battle.TryPlay(card, battle.Context.Enemies.FirstOrDefault(enemy => enemy.IsAlive)));
        }

        /// <summary>提交选定的候选牌身份，完成需要选牌的卡牌请求。</summary>
        public void ConfirmChoice(CardInstance choice)
        {
            if (battle == null || pendingCard == null || !IsChoosing) return;
            var card = pendingCard;
            CancelSelection();
            Report(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: choice.Id)));
        }

        /// <summary>取消仅修改展示，不修改牌堆或战斗资源。</summary>
        public void CancelSelection()
        {
            pendingCard = null;
            if (choicePanel) choicePanel.SetActive(false);
            ClearViews(choiceViews);
            if (battle != null) Refresh();
        }
        /// <summary>向战斗控制器提交行动点转换攻击的指令并显示结果。</summary>
        public void Convert() { if (battle != null && !IsChoosing) Report(battle.TryConvertApToAttack()); }
        /// <summary>提交结束玩家回合的指令，并刷新操作结果。</summary>
        public void EndTurn()
        {
            if (battle == null || IsChoosing) return;
            CancelSelection();
            if (battle.EndTurn()) SetStatus("第 " + battle.TurnNumber + " 回合");
        }
        /// <summary>将出牌结果转换为成功或失败提示，并刷新战斗界面。</summary>
        private void Report(CardPlayResult result) { SetStatus(result.Success ? "操作成功" : result.Message); Refresh(); }
        /// <summary>更新状态提示文本，向玩家显示当前操作反馈。</summary>
        private void SetStatus(string message) { if (statusText) statusText.text = message; }

        /// <summary>单次操作结束时刷新；不依据通知期间的结算锁永久禁用按钮。</summary>
        private void Refresh()
        {
            if (battle == null) return;
            var context = battle.Context;
            bool canAct = context.State == BattleState.PlayerAction && !context.IsFinished && !IsChoosing;
            if (IsChoosing && discardPanel) discardPanel.Hide();
            foreach (var slot in equipmentSlots)
            {
                if (!slot) continue;
                slot.Refresh(context);
                if (IsChoosing) slot.HideTooltip();
            }
            scene.healthText.text = "生命 " + context.Player.Health;
            scene.goldText.text = "金币 " + context.Run.Gold;
            scene.playerHealthText.text = context.Player.Health + "/" + context.Player.MaxHealth + "  护盾 " + context.Player.Shield;
            scene.sanityText.text = "理智值 " + context.Player.Sanity + "/" + context.Player.MaxSanity;
            var enemy = context.Enemies.FirstOrDefault();
            if (enemy != null) { scene.enemyHealthText.text = enemy.Health + "/" + enemy.MaxHealth; scene.enemyNameText.text = "训练守卫"; }
            scene.actionPointsText.text = "行动点 " + context.ActionPoints.Total;
            scene.endTurnButton.interactable = canAct;
            ordinaryTab.interactable = !IsChoosing; cyberneticTab.interactable = !IsChoosing;
            ordinaryTab.GetComponentInChildren<Text>().text = "普通牌 " + context.Ordinary.Hand.Count + "/13";
            cyberneticTab.GetComponentInChildren<Text>().text = "义体牌 " + context.Cybernetic.Hand.Count + "/5";
            scene.drawPileButton.GetComponentInChildren<Text>().text = "抽牌堆\n" + context.Ordinary.DrawPile.Count + " / " + context.Cybernetic.DrawPile.Count;
            scene.discardPileButton.GetComponentInChildren<Text>().text = "弃牌堆\n" + context.Ordinary.DiscardPile.Count + " / " + context.Cybernetic.DiscardPile.Count;
            conversionButton.gameObject.SetActive(context.TurnEffects.ConversionEnabled && !context.IsFinished);
            conversionButton.interactable = canAct && context.ActionPoints.Total >= 1 && context.Ordinary.Hand.Count < context.Ordinary.Hand.Capacity;
            for (int i = 0; i < durabilityTexts.Length; i++)
            {
                var slot = (CyberneticSlot)i; var equipped = context.Run.Loadout.Get(slot);
                // 兼容旧场景引用；新场景由 EquipmentSlot 展示完整卡面和名称。
                durabilityTexts[i].text = context.GetUsedCyberneticCard(slot) == null ? "" :
                    new[] { "脑机", "躯干", "手部", "腿部" }[i] + "\n耐久 " + (equipped?.Durability ?? 0) + "/" + (equipped?.Data.maxDurability ?? 0)
                    + "\n使用 " + context.Usage.GetUses(slot) + "/3";
            }
            ClearViews(handViews);
            foreach (var card in (cybernetic ? context.Cybernetic : context.Ordinary).Hand.Cards)
            {
                var view = Instantiate(catalog.Find(card.Data), cardsRoot);
                // 手牌区保留参考图位置；缩放卡牌以为标签栏留出空间。
                view.transform.localScale = Vector3.one * (6f / 7f);
                view.Bind(card); view.SetInteractable(canAct); view.Selected += SelectCard;
                handViews.Add(view);
            }
            if (handScroll) handScroll.horizontalNormalizedPosition = 0;
            UpdateCardScale();
            if (context.IsFinished) SetStatus(context.State == BattleState.Victory ? "战斗胜利" : "战斗失败");
        }

        /// <summary>停用并销毁现有卡牌展示对象，清空展示集合。</summary>
        private void ClearViews(List<CardView> views)
        {
            foreach (var view in views)
            {
                if (!view) continue;
                view.Selected -= SelectCard; view.Selected -= ConfirmChoice;
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
            views.Clear();
        }
    }
}
