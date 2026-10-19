using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>左侧单个义体部位，成功使用后只读展示部件图标和耐久，完整信息由长按卡面提供。</summary>
    public sealed class CyberneticEquipmentSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private CyberneticSlot slot;
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform thumbnailRoot;
        [SerializeField] private Text nameText;
        [SerializeField] private Text durabilityText;
        [SerializeField] private CyberneticDurabilityBar durabilityBar;
        [SerializeField] private CyberneticDeckData[] cyberneticDecks = System.Array.Empty<CyberneticDeckData>();
        [SerializeField] private Text slotText;
        [SerializeField] private CyberneticEquipmentTooltip tooltip;
        private Image icon;
        private BattleContext context;
        private CardHoldPreview holdPreview;
        public CyberneticSlot Slot => slot;
        private static readonly string[] SlotNames = { "脑机", "躯干", "手部", "腿部" };
        public string SlotName => SlotNames[(int)slot];
        public CardInstance DisplayedCard { get; private set; }

        /// <summary>由场景构建器注入固定部位和展示控件。</summary>
        public void Configure(CyberneticSlot part, CardPrefabCatalog cards, RectTransform faceRoot,
            Text label, Text durability, CyberneticEquipmentTooltip detail, Text partLabel = null)
        { slot = part; catalog = cards; thumbnailRoot = faceRoot; nameText = label; durabilityText = durability; tooltip = detail; slotText = partLabel; Refresh(null); }

        /// <summary>HUD 在战斗状态变化时调用；图标读取义体配置，不再实例化卡面缩略图。</summary>
        public void Refresh(BattleContext battle)
        {
            context = battle;
            // 兼容旧版单独部位标签；当前两行布局将部位并入卡组名称。
            if (slotText) slotText.text = SlotName;
            var card = context?.GetUsedCyberneticCard(slot);
            if (card != DisplayedCard)
            {
                HideTooltip(); DisplayedCard = card;
            }
            RefreshIcon(card?.Source?.Data.icon);
            if (nameText)
            {
                nameText.text = card == null ? "" : DeckName(card.Source.Data) + " • " + SlotName;
                // 长名称仍保持一行，避免溢出槽位或挤占耐久标签。
                nameText.resizeTextForBestFit = false;
                nameText.fontSize = 18;
                float preferredWidth = nameText.preferredWidth;
                float availableWidth = nameText.rectTransform.rect.width;
                if (preferredWidth > availableWidth && availableWidth > 0)
                    nameText.fontSize = Mathf.Max(1, Mathf.FloorToInt(18 * availableWidth / preferredWidth));
            }
            if (durabilityText) durabilityText.text = card == null ? "" : "耐久";
            if (durabilityBar) durabilityBar.Refresh(card == null ? 0 : card.Source.Durability,
                card == null ? 0 : card.Source.Data.maxDurability);
            if (Application.isPlaying)
            {
                if (!holdPreview) holdPreview = GetComponent<CardHoldPreview>();
                if (!holdPreview) holdPreview = gameObject.AddComponent<CardHoldPreview>();
                // 图标行仍提供完整卡面预览；无美术牌也可使用目录中的模板生成大图。
                holdPreview.Bind(card?.Data, card, card != null && catalog ? catalog.Find(card.Data) : null, context);
            }
            if (tooltip && tooltip.Owner == this && card != null) tooltip.RefreshDetails(context.Usage.GetUses(slot));
        }
        /// <summary>保留旧场景的悬停接口；大图统一由长按触发，避免出现两套重叠预览。</summary>
        public void OnPointerEnter(PointerEventData eventData)
        { }
        /// <summary>响应鼠标离开触发区域，关闭或安排关闭悬停内容。</summary>
        public void OnPointerExit(PointerEventData eventData) { HideTooltip(); }
        /// <summary>关闭由当前装备槽打开的详情，避免影响其他槽的悬停展示。</summary>
        public void HideTooltip() { if (tooltip) tooltip.HideFor(this); if (holdPreview) holdPreview.CancelHold(); }
        private void OnDisable() { HideTooltip(); }
        private void OnDestroy() { HideTooltip(); }
        /// <summary>按实际义体配置查所属卡组，混搭部位不会沿用其他系列名称。</summary>
        private string DeckName(CyberneticData data)
        {
            foreach (var deck in cyberneticDecks)
                if (deck && deck.cybernetics != null && System.Array.IndexOf(deck.cybernetics, data) >= 0)
                {
                    if (string.IsNullOrWhiteSpace(deck.deckName)) return "义体";
                    return System.Text.RegularExpressions.Regex.Replace(deck.deckName.Trim(),
                        @"^Type-\d+\s*[:：]\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            return "义体";
        }
        /// <summary>同一个 Image 复用各型号部件图标，按原图比例显示；空部位不残留上一张图片。</summary>
        private void RefreshIcon(Sprite sprite)
        {
            if (!thumbnailRoot) return;
            if (!icon) icon = thumbnailRoot.GetComponent<Image>();
            if (!icon) icon = thumbnailRoot.gameObject.AddComponent<Image>();
            icon.sprite = sprite; icon.enabled = sprite;
            icon.color = Color.white; icon.preserveAspect = true; icon.raycastTarget = false;
        }
    }
}
