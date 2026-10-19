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
        [SerializeField] private Text choiceTitle;
        [SerializeField] private Text choiceHint;
        [SerializeField] private ScrollRect choiceScroll;
        [SerializeField] private Button cancelChoiceButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text[] durabilityTexts;
        [SerializeField] private Text drawPileCount;
        [SerializeField] private Text discardPileCount;
        [SerializeField] private DiscardPanel discardPanel;

        [SerializeField] private NewWorldBrainPanel brainPanel;
        [SerializeField] private DrawPilePanel drawPilePanel;
        [SerializeField] private CyberneticEquipmentSlot[] equipmentSlots = new CyberneticEquipmentSlot[0];
        private readonly List<CardView> handViews = new List<CardView>();
        private readonly List<CardView> choiceViews = new List<CardView>();
        private HandCardDragController handDrag;
        private HandCardLayout handLayout;
        private BattleController battle;
        private bool cybernetic;
        private CardInstance pendingCard;
        private readonly Vector3[] handTabCorners = new Vector3[4];
        public BattleController Battle => battle;
        public bool IsChoosing => (scene && scene.settingsPanel && scene.settingsPanel.IsOpen) ||
            (choicePanel && choicePanel.activeSelf) || (drawPilePanel && drawPilePanel.IsReordering) ||
            (discardPanel && discardPanel.IsSelecting) || (handDrag && handDrag.IsDragging);
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
            if (handLayout) handLayout.SetCards(handViews);
        }

        private void Update()
        {
            // 保留旧输入系统，仅处理取消；状态展示均由事件驱动。
            if (pendingCard != null && Input.GetKeyDown(KeyCode.Escape)) CancelSelection();
        }
        private void LateUpdate()
        {
            // 页签或窗口尺寸变化时更新可用区域，独立布局组件负责检测宽高并补间。
            if (handScroll && handScroll.viewport) FitHandViewport();
            if (choicePanel && choicePanel.activeSelf) UpdateChoiceLayout();
        }
        /// <summary>选牌窗口限制在画布内，按视口宽度调整列数，卡牌始终由裁剪列表承载。</summary>
        private void UpdateChoiceLayout()
        {
            if (!choiceScroll || !choiceScroll.viewport || !choicesRoot) return;
            var window = choiceScroll.transform.parent as RectTransform;
            var overlay = window ? window.parent as RectTransform : null;
            if (overlay)
            {
                float width = Mathf.Min(1000, Mathf.Max(300, overlay.rect.width - 48));
                float height = Mathf.Min(760, Mathf.Max(300, overlay.rect.height - 48));
                if (!Mathf.Approximately(window.rect.width, width)) window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                if (!Mathf.Approximately(window.rect.height, height)) window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
            var grid = choicesRoot.GetComponent<GridLayoutGroup>();
            if (grid)
            {
                int columns = Mathf.Clamp(Mathf.FloorToInt((choiceScroll.viewport.rect.width - grid.padding.horizontal + grid.spacing.x) /
                    (grid.cellSize.x + grid.spacing.x)), 1, 3);
                if (grid.constraintCount != columns) grid.constraintCount = columns;
            }
        }
        /// <summary>沿用真实页签边界，交给单页布局等比缩放并预留悬停空间。</summary>
        private void UpdateCardScale()
        {
            if (!handScroll || !handScroll.viewport) return;
            FitHandViewport();
            if (handLayout) handLayout.RefreshLayout();
        }

        /// <summary>按普通牌和义体牌页签的实际位置预留间隔，避免固定顶部留白浪费空间或遮住页签。</summary>
        private void FitHandViewport()
        {
            var viewport = handScroll.viewport;
            var parent = viewport.parent as RectTransform;
            if (!parent) return;
            float top = parent.rect.yMax - 8;
            top = GetHandTabBottom(ordinaryTab, parent, top);
            top = GetHandTabBottom(cyberneticTab, parent, top);
            var offset = viewport.offsetMax;
            float inset = Mathf.Max(8, parent.rect.yMax - top);
            if (!Mathf.Approximately(offset.y, -inset)) viewport.offsetMax = new Vector2(offset.x, -inset);
        }

        /// <summary>把页签下沿换算到视口父节点空间，兼容按钮自身缩放和不同分辨率。</summary>
        private float GetHandTabBottom(Button tab, RectTransform parent, float currentTop)
        {
            if (!tab || !tab.gameObject.activeInHierarchy) return currentTop;
            ((RectTransform)tab.transform).GetWorldCorners(handTabCorners);
            return Mathf.Min(currentTop, parent.InverseTransformPoint(handTabCorners[0]).y - 4);
        }

        /// <summary>解除旧战斗订阅，绑定新战斗状态并刷新界面。</summary>
        private void Bind(BattleController value)
        {
            Unbind(); battle = value; pendingCard = null; choicePanel.SetActive(false);
            battle.Context.Changed += OnBattleChanged;
            battle.Context.Run.GoldChanged += OnGoldChanged;
            battle.Context.Player.Changed += OnPlayerHealthChanged;
            foreach (var enemy in battle.Context.Enemies) enemy.Changed += OnEnemyResourcesChanged;
            if (discardPanel) discardPanel.Bind(battle.Context);

            if (drawPilePanel) drawPilePanel.Bind(battle.Context);
            Refresh();
        }
        /// <summary>解除战斗事件订阅并清空关联展示，避免引用上一场战斗。</summary>
        private void Unbind()
        {
            if (handDrag) handDrag.CancelDrag();
            if (battle != null)
            {
                battle.Context.Changed -= OnBattleChanged;
                battle.Context.Run.GoldChanged -= OnGoldChanged;
                battle.Context.Player.Changed -= OnPlayerHealthChanged;
                foreach (var enemy in battle.Context.Enemies) enemy.Changed -= OnEnemyResourcesChanged;
            }
            if (discardPanel) discardPanel.Bind(null);

            if (brainPanel) brainPanel.Close(0);
            if (drawPilePanel) drawPilePanel.Bind(null);
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

        /// <summary>地图或商店改变塞勒币时即时更新顶部余额，无需推进战斗或重新绑定。</summary>
        private void OnGoldChanged(int amount) { if (scene && scene.goldText) scene.goldText.text = amount.ToString(); }

        /// <summary>生命、理智、护盾变化时即时同步状态条和顶栏，不重建手牌或执行回合逻辑。</summary>
        private void OnPlayerHealthChanged(CombatantState player)
        {
            if (!scene) return;
            if (scene.healthText) scene.healthText.text = player.Health.ToString();
            if (scene.playerHealthText) scene.playerHealthText.text = player.Health + "/" + player.MaxHealth;
            if (scene.sanityText) scene.sanityText.text = "理智 " + player.Sanity + "/" + player.MaxSanity;
            SetResourceFill(scene.playerHealthFill, player.Health, player.MaxHealth);
            SetResourceFill(scene.playerSanityFill, player.Sanity, player.MaxSanity);
            SetShieldValue(scene.playerShieldText, player.Shield);
        }

        /// <summary>仅展示当前首位敌人的生命与护盾，复用原单敌人立绘区域；绑定切换时解除旧订阅。</summary>
        private void OnEnemyResourcesChanged(CombatantState enemy)
        {
            if (!scene || battle == null || enemy != battle.Context.Enemies.FirstOrDefault()) return;
            if (scene.enemyHealthText) scene.enemyHealthText.text = enemy.Health + "/" + enemy.MaxHealth;
            SetResourceFill(scene.enemyHealthFill, enemy.Health, enemy.MaxHealth);
            SetShieldValue(scene.enemyShieldText, enemy.Shield);
        }

        /// <summary>通过锚点按比例填充纯色条，零值完全清空，不依赖额外图片或人为设定护盾上限。</summary>
        private static void SetResourceFill(Image fill, int current, int maximum)
        {
            if (!fill) return;
            var max = fill.rectTransform.anchorMax;
            max.x = maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0;
            fill.rectTransform.anchorMax = max;
        }

        /// <summary>护盾按真实数量显示蓝色标记，无护盾时隐藏整个标记。</summary>
        private static void SetShieldValue(Text label, int amount)
        {
            if (!label) return;
            label.text = "护盾 " + amount;
            label.transform.parent.gameObject.SetActive(amount > 0);
        }

        /// <summary>外部只读界面关闭后同步当前卡面和义体耐久，不执行战斗指令。</summary>
        public void RefreshDisplay() { if (battle != null) Refresh(); }

        /// <summary>切换到普通手牌页并刷新卡牌展示。</summary>
        public void ShowOrdinary() { cybernetic = false; Refresh(); }
        /// <summary>切换到义体手牌页并刷新卡牌展示。</summary>
        public void ShowCybernetic() { cybernetic = true; Refresh(); }

        /// <summary>由效果交互层显式打开抽牌堆；调用方负责费用，本方法只负责显示。</summary>
        public void ShowDrawPile(float fadeSeconds = 0.15f)
        {
            if (!isActiveAndEnabled || battle == null || battle.Context.IsFinished || IsChoosing) return;
            if (discardPanel) discardPanel.Hide(0);
            if (drawPilePanel) drawPilePanel.Show(fadeSeconds);
        }
        /// <summary>由效果交互层显式打开弃牌堆，不绑定牌堆按钮或悬停事件。</summary>
        public void ShowDiscardPile(float fadeSeconds = 0.15f)
        {
            if (!isActiveAndEnabled || battle == null || battle.Context.IsFinished || IsChoosing) return;
            if (drawPilePanel) drawPilePanel.Hide(0);
            if (discardPanel) discardPanel.Show(fadeSeconds);
        }
        /// <summary>关闭两个牌堆面板；默认淡出，生命周期清理可传零秒立即关闭。</summary>
        public void ClosePilePanels(float fadeSeconds = 0.15f)
        {
            if (drawPilePanel) drawPilePanel.Hide(fadeSeconds);
            if (discardPanel) discardPanel.Hide(fadeSeconds);
        }

        /// <summary>自身牌直接提交；攻击等待敌人点击，交互牌统一等待候选确认。</summary>
        public void SelectCard(CardInstance card)
        {
            if (battle == null || battle.Context.IsFinished || IsChoosing || card == null || card.Data.locksInHand) return;
            CancelSelection();
            if (card.Data.effects.Any(effect => effect.RequiresReorder))
            {
                if (card.Data.category == CardCategory.Cybernetic &&
                    (card.Source == null || !battle.Context.Run.Loadout.Contains(card.Source) || card.Source.Durability <= 0 ||
                    !battle.Context.Usage.CanUse(card.Source.Data.slot))) { SetStatus("来源义体当前不可用。"); return; }
                if (!battle.Context.CanPayCardCost(card)) { SetStatus("资源不足，无法使用该牌。"); return; }
                if (!drawPilePanel) { SetStatus("缺少抽牌堆面板。"); return; }
                ClosePilePanels(0);
                // 以当前完整顺序成功使用脑机，费用、耐久、次数与套装出牌记录只在这里结算一次。
                var context = battle.Context;
                var initialOrder = context.Ordinary.GetDrawPileOrder().Concat(context.Cybernetic.GetDrawPileOrder()).ToArray();
                var result = battle.TryPlayRequest(new CardPlayRequest(card, reorder: initialOrder));
                Report(result);
                if (!result.Success || context.IsFinished) return;
                drawPilePanel.OpenReorder(context, OnDrawPileReorderFinished);
                Refresh();
                return;
            }
            if (card.Data.effects.Any(effect => effect.RequiresChoice))
            {
                var context = battle.Context;
                if (!context.CanPayCardCost(card)) { SetStatus("当前资源不足以支付卡牌费用。"); return; }
                if (card.Data.category == CardCategory.Cybernetic &&
                    (card.Source == null || !context.Run.Loadout.Owns(card.Source) || card.Source.Durability <= 0 ||
                    !context.Usage.CanUse(card.Source.Data.slot))) { SetStatus("来源义体当前不可用。"); return; }
                // 控制器决定候选来源，既支持脑机查看牌堆，也支持改造当前基本手牌。
                var choices = battle.GetCardChoices(card);
                if (choices.Count == 0) { SetStatus("当前没有可选择的卡牌。"); return; }
                if (card.Data.effects.Any(effect => effect.ChoiceUsesDiscardPile))
                {
                    if (!discardPanel) { SetStatus("缺少弃牌面板。"); return; }
                    ClosePilePanels(0);
                    pendingCard = card;
                    discardPanel.OpenSelection(context, battle.GetCardChoiceTitle(card),
                        value => battle != null && battle.GetCardChoices(card).Contains(value), OnDiscardChoiceFinished);
                    Refresh(); return;
                }
                pendingCard = card; choicePanel.SetActive(true);
                if (choiceTitle) choiceTitle.text = battle.GetCardChoiceTitle(card);
                if (choiceHint) choiceHint.text = battle.GetCardChoicePrompt(card);
                foreach (var choice in choices)
                {
                    var view = Instantiate(catalog.Find(choice.Data), choicesRoot);
                    view.BindForBattle(choice, context); view.SetInteractable(true); view.Selected += ConfirmChoice;
                    choiceViews.Add(view);
                }
                UpdateChoiceLayout();
                LayoutRebuilder.ForceRebuildLayoutImmediate(choicesRoot);
                if (choiceScroll) { choiceScroll.StopMovement(); choiceScroll.verticalNormalizedPosition = 1; }
                SetStatus(battle.GetCardChoicePrompt(card)); Refresh();
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
        /// <summary>排序关闭或被战斗切换终止后恢复界面，不再次提交出牌或支付费用。</summary>
        private void OnDrawPileReorderFinished(bool committed) { if (battle != null) Refresh(); }

        /// <summary>弃牌面板只提供选择；确认后统一提交出牌请求，取消时不调用支付入口。</summary>
        private void OnDiscardChoiceFinished(CardInstance choice)
        {
            var card = pendingCard;
            pendingCard = null;
            if (discardPanel) discardPanel.Hide(0);
            if (battle == null) return;
            if (choice == null || card == null) { Refresh(); return; }
            Report(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: choice.Id)));
        }

        /// <summary>取消仅修改展示，不修改牌堆或战斗资源。</summary>
        public void CancelSelection()
        {
            if (handDrag) handDrag.CancelDrag();
            pendingCard = null;
            if (choicePanel) choicePanel.SetActive(false);
            if (brainPanel) brainPanel.Close();
            if (drawPilePanel && drawPilePanel.IsReordering) drawPilePanel.Hide(0);
            if (discardPanel && discardPanel.IsSelecting) discardPanel.Hide(0);
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
            // 父画布正在停用时，子面板的取消回调仍可能到达；重新启用时 Bind 会统一刷新。
            if (battle == null || !isActiveAndEnabled || !cardsRoot || !cardsRoot.gameObject.activeInHierarchy) return;
            // 战斗变化或切页先撤销拖拽，再重建手牌；浮动卡不能遗留在黑幕中。
            if (handDrag) handDrag.CancelDrag();
            var context = battle.Context;
            bool canAct = context.State == BattleState.PlayerAction && !context.IsFinished && !IsChoosing;
            if (context.IsFinished) ClosePilePanels(0);
            else if (IsChoosing)
            {
                if (drawPilePanel && !drawPilePanel.IsReordering) drawPilePanel.Hide(0);
                if (discardPanel && !discardPanel.IsSelecting) discardPanel.Hide(0);
            }
            foreach (var slot in equipmentSlots)
            {
                if (!slot) continue;
                slot.Refresh(context);
                if (IsChoosing) slot.HideTooltip();
            }
            OnPlayerHealthChanged(context.Player);
            scene.goldText.text = context.Run.Gold.ToString();
            var enemy = context.Enemies.FirstOrDefault();
            if (enemy != null) { OnEnemyResourcesChanged(enemy); scene.enemyNameText.text = "训练守卫"; }
            scene.actionPointsText.text = "行动点 " + context.ActionPoints.Total;
            scene.endTurnButton.interactable = canAct;
            ordinaryTab.interactable = !IsChoosing; cyberneticTab.interactable = !IsChoosing;
            ordinaryTab.GetComponentInChildren<Text>().text = "普通牌 " + context.Ordinary.Hand.Count + "/13";
            cyberneticTab.GetComponentInChildren<Text>().text = "义体牌 " + context.Cybernetic.Hand.Count + "/5";
            // 旧 Label 保持清空，数字单独填入美术圆圈，合计两类真实牌堆而不随手牌页签切换。
            scene.drawPileButton.GetComponentInChildren<Text>().text = string.Empty;
            scene.discardPileButton.GetComponentInChildren<Text>().text = string.Empty;
            if (!drawPileCount) drawPileCount = EnsurePileCount(scene.drawPileButton, true);
            if (!discardPileCount) discardPileCount = EnsurePileCount(scene.discardPileButton, false);
            drawPileCount.text = (context.Ordinary.DrawPile.Count + context.Cybernetic.DrawPile.Count).ToString();
            discardPileCount.text = (context.Ordinary.DiscardPile.Count + context.Cybernetic.DiscardPile.Count).ToString();
            conversionButton.gameObject.SetActive(context.TurnEffects.ConversionEnabled && !context.IsFinished);
            conversionButton.interactable = canAct && context.ActionPoints.Total >= 1 && context.Ordinary.CanAddToHand();
            for (int i = 0; i < durabilityTexts.Length; i++)
            {
                var slot = (CyberneticSlot)i; var used = context.GetUsedCyberneticCard(slot); var source = used?.Source;
                // 兼容旧场景引用；新场景由 EquipmentSlot 展示完整卡面和名称。
                durabilityTexts[i].text = used == null ? "" :
                    new[] { "脑机", "躯干", "手部", "腿部" }[i] + "\n耐久 " + source.Durability + "/" + source.Data.maxDurability
                    + "\n使用 " + context.Usage.GetUses(slot) + "/3";
            }
            if (!handLayout) handLayout = cardsRoot.GetComponent<HandCardLayout>();
            if (!handLayout) handLayout = cardsRoot.gameObject.AddComponent<HandCardLayout>();
            handLayout.Configure(handScroll);
            if (!handDrag) handDrag = gameObject.AddComponent<HandCardDragController>();
            handDrag.Configure(cardsRoot, handScroll, scene.handArea, GetHandDragTarget, PlayDraggedCard, BeginHandDrag, GetHandDragHighlights);
            var wanted = (cybernetic ? context.Cybernetic : context.Ordinary).Hand.Cards;
            foreach (var removed in handViews.Where(view => !view || !wanted.Contains(view.Instance)).ToArray())
            {
                handViews.Remove(removed);
                if (!removed) continue;
                removed.Selected -= SelectCard; removed.gameObject.SetActive(false); Destroy(removed.gameObject);
            }
            var ordered = new List<CardView>(wanted.Count);
            foreach (var card in wanted)
            {
                var view = handViews.Find(value => value.Instance == card);
                if (!view)
                {
                    view = Instantiate(catalog.Find(card.Data), cardsRoot); view.Selected += SelectCard;
                }
                view.BindForBattle(card, context); view.SetInteractable(canAct); ordered.Add(view);
                handDrag.Attach(view);
            }
            handViews.Clear(); handViews.AddRange(ordered);
            handLayout.SetCards(handViews);
            UpdateCardScale();
            if (context.IsFinished) SetStatus(context.State == BattleState.Victory ? "战斗胜利" : "战斗失败");
        }

        /// <summary>按真实卡牌目标配置区分敌我，自动兼容本回合被转换为攻击牌的普通手牌。</summary>
        private RectTransform GetHandDragTarget(CardView view)
        {
            if (!isActiveAndEnabled || battle == null || IsChoosing || battle.Context.State != BattleState.PlayerAction ||
                battle.Context.IsFinished || view.Instance == null || view.Data.locksInHand) return null;
            var context = battle.Context; var card = view.Instance;
            if (!context.Ordinary.Hand.Contains(card) && !context.Cybernetic.Hand.Contains(card)) return null;
            bool enemy = card.Data.targetType == CardTargetType.SingleEnemy || card.Data.targetType == CardTargetType.AllEnemies;
            if (enemy && !context.Enemies.Any(value => value.IsAlive)) return null;
            var playTarget = card.Data.targetType == CardTargetType.SingleEnemy ? context.Enemies.FirstOrDefault(value => value.IsAlive) : null;
            if (!battle.CanStartPlay(card, playTarget)) return null;
            return enemy ? scene.enemyPortrait.rectTransform : scene.playerPortrait.rectTransform;
        }

        /// <summary>开始拖动时清除旧攻击目标并关闭只读牌堆，不刷新或销毁正在拖动的手牌。</summary>
        private void BeginHandDrag() { pendingCard = null; ClosePilePanels(0); }

        /// <summary>目标对应的血条、理智条及护盾随原立绘高亮；非目标角色保持黑幕遮挡。</summary>
        private IEnumerable<RectTransform> GetHandDragHighlights(CardInstance card)
        {
            bool enemy = card.Data.targetType == CardTargetType.SingleEnemy || card.Data.targetType == CardTargetType.AllEnemies;
            var health = enemy ? scene.enemyHealthText : scene.playerHealthText;
            var shield = enemy ? scene.enemyShieldText : scene.playerShieldText;
            if (health) yield return health.transform.parent as RectTransform;
            if (shield) yield return shield.transform.parent as RectTransform;
            if (!enemy && scene.sanityText) yield return scene.sanityText.transform.parent as RectTransform;
        }

        /// <summary>有效放置复用权威出牌入口；交互义体继续由既有选牌或排序面板完成。</summary>
        private void PlayDraggedCard(CardInstance card)
        {
            if (battle == null || IsChoosing || battle.Context.State != BattleState.PlayerAction || battle.Context.IsFinished) return;
            if (card.Data.targetType == CardTargetType.SingleEnemy)
                Report(battle.TryPlay(card, battle.Context.Enemies.FirstOrDefault(enemy => enemy.IsAlive)));
            else SelectCard(card);
        }

        /// <summary>兼容已有场景，在图标白色圆圈内补充独立数量文本，不叠加名称或拦截按钮输入。</summary>
        private Text EnsurePileCount(Button button, bool draw)
        {
            var child = button.transform.Find("Count");
            if (child) return child.GetComponent<Text>();
            var root = new GameObject("Count", typeof(RectTransform), typeof(Text));
            root.layer = button.gameObject.layer; root.transform.SetParent(button.transform, false);
            var text = root.GetComponent<Text>(); var label = button.transform.Find("Label")?.GetComponent<Text>();
            text.font = label ? label.font : scene.actionPointsText.font;
            text.fontSize = 22; text.fontStyle = FontStyle.Bold; text.color = new Color(.04f, .05f, .1f);
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = 22;
            var rect = text.rectTransform;
            // 两张原图的数量圆圈分别位于右下和左下，按原尺寸的圆心定位，保持美术原比例。
            rect.anchorMin = rect.anchorMax = draw ? new Vector2(77f / 102f, 38f / 112f) : new Vector2(21f / 98f, 39f / 102f);
            rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(32, 32);
            return text;
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
