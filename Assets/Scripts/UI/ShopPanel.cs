using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace SpotlightGameJam.UI
{
    /// <summary>展示探索资源和义体库存，将购买、修复及回血交给商店服务结算。</summary>
    public sealed class ShopPanel : BasePanel
    {
        [SerializeField] private Text healthText;
        [SerializeField] private Text currencyText;
        [SerializeField] private Button exitButton;
        [SerializeField] private Button healButton;
        [SerializeField] private RectTransform[] repairSlots = Array.Empty<RectTransform>();
        [SerializeField] private RectTransform[] productSlots = Array.Empty<RectTransform>();
        [SerializeField] private CyberneticData[] productDefinitions = Array.Empty<CyberneticData>();
        [SerializeField] private Text transactionText;
        private RunState run;
        private ShopService service;
        private RectTransform[] productCards;
        private Button[] repairButtons, cycleButtons, buyButtons;
        private UnityAction[] repairActions, cycleActions, buyActions;
        private CyberneticInstance[] repairSources;
        public event Action CloseRequested;

        protected override void Awake()
        {
            base.Awake();
            CacheControls();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (exitButton) exitButton.onClick.AddListener(RequestClose);
            if (healButton) healButton.onClick.AddListener(HealPlayer);
            WireButtons(true);
            SubscribeRun();
            Refresh();
        }

        protected override void OnDisable()
        {
            if (exitButton) exitButton.onClick.RemoveListener(RequestClose);
            if (healButton) healButton.onClick.RemoveListener(HealPlayer);
            WireButtons(false);
            UnsubscribeRun();
            base.OnDisable();
        }

        private void Update()
        {
            if (!IsVisible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) RequestClose();
        }

        private void LateUpdate() { FitProductCards(); }

        /// <summary>绑定现有探索状态；切换绑定时解除旧角色订阅，不创建或替换牌组。</summary>
        public void Bind(RunState value)
        {
            UnsubscribeRun();
            run = value;
            service = run == null ? null : new ShopService(run);
            repairSources = new CyberneticInstance[repairSlots.Length];
            if (transactionText) transactionText.text = "购买获得 1 张牌，下场战斗入堆；修复每次恢复 1 点耐久。";
            if (isActiveAndEnabled) SubscribeRun();
            Refresh();
        }

        /// <summary>显示面板并读取最新资源，过渡时间以秒计。</summary>
        public override void Show(float fadeSeconds = 0.15f)
        {
            Refresh(); base.Show(fadeSeconds);
            FitProductCards();
            if (exitButton) exitButton.Select();
        }

        /// <summary>退出商店；先停止交互，再通知外部流程关闭遮罩。</summary>
        public void RequestClose()
        {
            if (!IsVisible) return;
            Hide(Application.isPlaying ? 0.15f : 0f); CloseRequested?.Invoke();
        }

        /// <summary>响应角色属性变化，只刷新只读数值。</summary>
        private void OnPlayerChanged(CombatantState player) { Refresh(); }

        /// <summary>刷新当前玩家资源与带入义体；商品区仍为只读展示原型。</summary>
        private void Refresh()
        {
            if (healthText) healthText.text = run == null ? "生命 — / —" : "生命 " + run.Player.Health + " / " + run.Player.MaxHealth;
            if (currencyText) currencyText.text = run == null ? "塞勒币 —" : "塞勒币 " + run.Gold;
            if (healButton)
            {
                healButton.interactable = service != null && service.CanHeal(out _);
                var label = healButton.GetComponentInChildren<Text>(true);
                if (label) label.text = "回血 +" + ShopService.HealingAmount + "\n" + ShopService.HealingPrice + " 塞勒币";
            }
            RefreshRepairSlots();
            RefreshProducts();
        }

        /// <summary>转发付费生命恢复请求，费用和上限由服务统一处理。</summary>
        private void HealPlayer()
        {
            if (service == null) return;
            service.TryHeal(out var message);
            if (transactionText) transactionText.text = message;
            Refresh();
        }

        /// <summary>按四部位展示持久库存，默认保留原带入型号，同部位多型号可切换修复对象。</summary>
        private void RefreshRepairSlots()
        {
            if (repairSources == null || repairSources.Length != repairSlots.Length) repairSources = new CyberneticInstance[repairSlots.Length];
            for (int i = 0; i < repairSlots.Length; i++)
            {
                var slot = repairSlots[i];
                if (!slot) continue;
                var models = ModelsForPart(i);
                var source = models.FirstOrDefault(card => repairSources[i] != null && card.Data.cyberneticId == repairSources[i].Data.cyberneticId) ?? models.FirstOrDefault();
                repairSources[i] = source;
                SetSlotText(slot, "Name", source == null ? "" : source.Card.cardName);
                SetSlotText(slot, "Durability", source == null ? "" : "耐久 " + source.Durability + " / " + source.Data.maxDurability);
                var repair = slot.Find("RepairButton");
                if (repair)
                {
                    repair.gameObject.SetActive(source != null);
                    var button = repair.GetComponent<Button>();
                    button.interactable = service != null && service.CanRepair(source, out _);
                    var label = repair.GetComponentInChildren<Text>(true);
                    if (label) label.text = source != null && source.Data.shopPricing ? "修复 · " + source.Data.shopPricing.repairPrice + " 塞勒币" : "修复未定价";
                }
                var cycle = slot.Find("CycleButton");
                if (cycle)
                {
                    cycle.gameObject.SetActive(models.Length > 1);
                    var label = cycle.GetComponentInChildren<Text>(true);
                    if (label) label.text = "切换 " + (Array.IndexOf(models, source) + 1) + "/" + models.Length;
                }
            }
        }

        /// <summary>每个型号只返回一张代表牌，同型号所有副本的耐久均来自共享池。</summary>
        private CyberneticInstance[] ModelsForPart(int part)
        {
            return run == null ? Array.Empty<CyberneticInstance>() : run.Loadout.Owned.Where(card => (int)card.Data.slot == part)
                .GroupBy(card => card.Data.cyberneticId).Select(group => group.First()).ToArray();
        }

        /// <summary>切换本部位的修复目标，仅改变显示选择，不构成装备或影响 buff。</summary>
        private void CycleRepairSlot(int index)
        {
            var models = ModelsForPart(index);
            if (models.Length < 2) return;
            repairSources[index] = models[(Array.IndexOf(models, repairSources[index]) + 1) % models.Length];
            RefreshRepairSlots();
        }

        /// <summary>修复按钮只转发服务请求，失败不会扣费或修改牌。</summary>
        private void RepairSlot(int index)
        {
            if (service == null || repairSources == null) return;
            service.TryRepair(repairSources[index], out var message);
            if (transactionText) transactionText.text = message;
            Refresh();
        }

        /// <summary>购买按钮只转发对应型号，购买不会插入当前手牌或抽牌堆。</summary>
        private void PurchaseSlot(int index)
        {
            if (service == null || index >= productDefinitions.Length) return;
            service.TryPurchase(productDefinitions[index], out var message);
            if (transactionText) transactionText.text = message;
            Refresh();
        }

        /// <summary>价格、已拥有状态与余额共用服务校验，未定价商品保持禁用。</summary>
        private void RefreshProducts()
        {
            for (int i = 0; i < productSlots.Length; i++)
            {
                var slot = productSlots[i]; if (!slot) continue;
                var data = i < productDefinitions.Length ? productDefinitions[i] : null;
                bool valid = data && data.Validate().Count == 0;
                var cardRoot = slot.Find("CardPreview");
                if (cardRoot)
                {
                    cardRoot.gameObject.SetActive(valid);
                    var view = cardRoot.GetComponent<CardView>();
                    // 卡面和费用必须跟随同一个商品配置，不能依赖构建时留下的旧预览。
                    if (valid && view && view.Data != data.cards[0]) view.Bind(new CardInstance(data.cards[0], new CyberneticInstance(data)));
                    if (view) view.SetInteractable(false);
                }
                SetSlotText(slot, "Part", valid ? new[] { "脑机", "躯干", "手部", "腿部" }[(int)data.slot] : "");
                bool priced = valid && data.shopPricing && data.shopPricing.canPurchase;
                SetSlotText(slot, "Price", priced ? data.shopPricing.purchasePrice + " 塞勒币" : "购买价格未定");
                var owner = slot.Find("BuyButton"); if (!owner) continue;
                var button = owner.GetComponent<Button>();
                button.interactable = service != null && service.CanPurchase(data, out _);
                var label = button.GetComponentInChildren<Text>(true);
                if (label) label.text = data && run != null && run.Loadout.OwnsConfiguration(data) ? "已拥有" : "购买 ×1";
            }
        }

        /// <summary>缓存按钮与闭包，保持多次开关只注册一组监听。</summary>
        private void CacheControls()
        {
            productCards = new RectTransform[productSlots.Length]; buyButtons = new Button[productSlots.Length]; buyActions = new UnityAction[productSlots.Length];
            repairButtons = new Button[repairSlots.Length]; cycleButtons = new Button[repairSlots.Length];
            repairActions = new UnityAction[repairSlots.Length]; cycleActions = new UnityAction[repairSlots.Length];
            for (int i = 0; i < repairSlots.Length; i++)
            {
                int index = i; var slot = repairSlots[i]; if (!slot) continue;
                repairButtons[i] = slot.Find("RepairButton")?.GetComponent<Button>();
                cycleButtons[i] = slot.Find("CycleButton")?.GetComponent<Button>();
                repairActions[i] = () => RepairSlot(index); cycleActions[i] = () => CycleRepairSlot(index);
            }
            for (int i = 0; i < productSlots.Length; i++)
            {
                int index = i; var slot = productSlots[i]; if (!slot) continue;
                productCards[i] = slot.Find("CardPreview") as RectTransform;
                buyButtons[i] = slot.Find("BuyButton")?.GetComponent<Button>(); buyActions[i] = () => PurchaseSlot(index);
            }
        }

        /// <summary>对称注册及移除购买、修复和型号切换的按钮回调。</summary>
        private void WireButtons(bool add)
        {
            if (buyButtons == null) CacheControls();
            for (int i = 0; i < buyButtons.Length; i++) WireButton(buyButtons[i], buyActions[i], add);
            for (int i = 0; i < repairButtons.Length; i++) { WireButton(repairButtons[i], repairActions[i], add); WireButton(cycleButtons[i], cycleActions[i], add); }
        }

        /// <summary>对单个可选按钮应用监听变化，允许场景尚未升级时安全停用。</summary>
        private static void WireButton(Button button, UnityAction action, bool add)
        {
            if (!button || action == null) return;
            if (add) button.onClick.AddListener(action); else button.onClick.RemoveListener(action);
        }

        /// <summary>更新已有位置的文字，未带入或解除绑定时清空内容，保留部位标识。</summary>
        private static void SetSlotText(RectTransform slot, string child, string value)
        {
            var target = slot.Find(child);
            var label = target ? target.GetComponent<Text>() : null;
            if (label) label.text = value;
        }

        /// <summary>显示期间监听属性和持久牌组，耐久消耗或修复后及时刷新。</summary>
        private void SubscribeRun()
        {
            if (run == null) return;
            run.Player.Changed += OnPlayerChanged;
            run.Loadout.Changed += OnLoadoutChanged;
            run.GoldChanged += OnGoldChanged;
        }

        /// <summary>关闭或切换绑定时解除旧探索订阅，避免窗口继续持有旧牌组。</summary>
        private void UnsubscribeRun()
        {
            if (run == null) return;
            run.Player.Changed -= OnPlayerChanged;
            run.Loadout.Changed -= OnLoadoutChanged;
            run.GoldChanged -= OnGoldChanged;
        }

        /// <summary>持久库存或共享耐久变化时同步维修对象与商品拥有状态。</summary>
        private void OnLoadoutChanged() { RefreshRepairSlots(); RefreshProducts(); }

        /// <summary>余额变化时刷新价格校验，避免扣费后按钮仍可用或补币后仍置灰。</summary>
        private void OnGoldChanged(int amount) { Refresh(); }

        /// <summary>现有卡面使用固定尺寸，按实际商品槽缩放，防止四比三屏下压住部位和价格。</summary>
        private void FitProductCards()
        {
            if (productCards == null) return;
            for (int i = 0; i < productCards.Length; i++)
            {
                var card = productCards[i]; var slot = productSlots[i];
                if (!card || !slot || card.rect.width <= 0 || card.rect.height <= 0) continue;
                float scale = Mathf.Min(slot.rect.width * (270f / 345f) / card.rect.width,
                    slot.rect.height * (258f / 380f) / card.rect.height);
                card.localScale = Vector3.one * scale;
                card.anchoredPosition = new Vector2(0, -slot.rect.height * (12f / 380f));
            }
        }
    }
}
