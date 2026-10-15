using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>卡牌共用展示组件。编辑时显示配置，战斗时显示真实实例，不承担出牌规则。</summary>
    public sealed class CardView : MonoBehaviour
    {
        [SerializeField] private CardData cardData;
        [SerializeField] private CyberneticData cyberneticData;
        [SerializeField] private Color tint;
        [SerializeField] private Image background;
        [SerializeField] private Button button;
        [SerializeField] private Text nameText;
        [SerializeField] private Text costText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text sourceText;
        private BattleContext context;
        public CardData Data => Instance?.Data ?? cardData;
        public CardInstance Instance { get; private set; }
        public event Action<CardInstance> Selected;

        /// <summary>编辑器构建时绑定配置和控件；可用 Inspector 替换引用。</summary>
        public void Configure(CardData data, CyberneticData cybernetic, Color color, Image image,
            Button clickButton, Text title, Text cost, Text description, Text source)
        {
            cardData = data; cyberneticData = cybernetic; tint = color;
            background = image; button = clickButton; nameText = title; costText = cost;
            descriptionText = description; sourceText = source; Refresh();
        }

        /// <summary>绑定只读实例并解除战斗费用覆盖，供弃牌预览和装备展示使用。</summary>
        public void Bind(CardInstance instance) { Instance = instance; context = null; Refresh(); }

        /// <summary>绑定真实实例和战斗状态，按当前效果显示费用，不写入共享配置。</summary>
        public void BindForBattle(CardInstance instance, BattleContext battleContext)
        { Instance = instance; context = battleContext; Refresh(); }

        /// <summary>设置卡面的按钮可交互状态，用于手牌与只读预览复用。</summary>
        public void SetInteractable(bool value) { if (button) button.interactable = value && Data && !Data.locksInHand; }

        private void OnEnable() { if (button) button.onClick.AddListener(HandleClick); Refresh(); }
        private void OnDisable() { if (button) button.onClick.RemoveListener(HandleClick); }
        private void OnValidate() { Refresh(); }
        /// <summary>转发卡面点击事件，由调用方决定出牌或选牌行为。</summary>
        private void HandleClick() { if (Instance != null) Selected?.Invoke(Instance); }

        /// <summary>费用从战斗权威状态读取，生成牌寿命和耐久从实例读取；幻痛使用中性灰色。</summary>
        public void Refresh()
        {
            var data = Data;
            if (!data) return;
            if (background) background.color = data.category == CardCategory.Special ? new Color(0.65f, 0.65f, 0.65f) : tint;
            if (nameText) nameText.text = data.cardName;
            if (costText)
            {
                // 同一个费用判定服务于卡面和出牌结算，探索 buff 失效后费用立即恢复。
                if (data.sanityCost > 0)
                    costText.text = (context != null && Instance != null && context.UsesHealthFor(Instance) ? "生命 " : "理智 ") + data.sanityCost;
                else
                    costText.text = "AP " + (context != null && Instance != null ? context.GetEffectiveApCost(Instance) : Instance?.EffectiveApCost ?? data.cost);
            }
            if (descriptionText) descriptionText.text = data.description;
            if (sourceText)
            {
                if (Instance?.Source != null)
                    sourceText.text = new[] { "脑机", "躯干", "手部", "腿部" }[(int)Instance.Source.Data.slot] + " · 耐久 " + Instance.Source.Durability + "/" + Instance.Source.Data.maxDurability;
                else if (cyberneticData) sourceText.text = "义体 · 耐久 " + cyberneticData.maxDurability;
                else sourceText.text = Instance != null && Instance.ExpiresAtTurnEnd ? "本回合限定 · 使用后移除" :
                    data.locksInHand ? "锁定 · 无法移出手牌" : data.category == CardCategory.Function ? "功能牌" : "基本牌";
            }
        }
    }
}
