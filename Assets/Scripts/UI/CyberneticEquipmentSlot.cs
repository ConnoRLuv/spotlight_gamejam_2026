using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>左侧单个义体部位；部位标题常显，成功使用后只读展示卡面、名称和耐久。</summary>
    public sealed class CyberneticEquipmentSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private CyberneticSlot slot;
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform thumbnailRoot;
        [SerializeField] private Text nameText;
        [SerializeField] private Text durabilityText;
        [SerializeField] private Text slotText;
        [SerializeField] private CyberneticEquipmentTooltip tooltip;
        private CardView thumbnail;
        private BattleContext context;
        public CyberneticSlot Slot => slot;
        private static readonly string[] SlotNames = { "脑机", "躯干", "手部", "腿部" };
        public string SlotName => SlotNames[(int)slot];
        public CardInstance DisplayedCard { get; private set; }

        /// <summary>由场景构建器注入固定部位和展示控件。</summary>
        public void Configure(CyberneticSlot part, CardPrefabCatalog cards, RectTransform faceRoot,
            Text label, Text durability, CyberneticEquipmentTooltip detail, Text partLabel = null)
        { slot = part; catalog = cards; thumbnailRoot = faceRoot; nameText = label; durabilityText = durability; tooltip = detail; slotText = partLabel; Refresh(null); }

        /// <summary>HUD 在战斗状态变化时调用；同一张卡只刷新数值，不反复生成缩略图。</summary>
        public void Refresh(BattleContext battle)
        {
            context = battle;
            // 部位信息不随装备清空，未使用义体时也能辨认每个槽位。
            if (slotText) slotText.text = SlotName;
            var card = context?.GetUsedCyberneticCard(slot);
            if (card != DisplayedCard)
            {
                HideTooltip(); ClearThumbnail(); DisplayedCard = card;
                if (card != null)
                {
                    thumbnail = Instantiate(catalog.Find(card.Data), thumbnailRoot);
                    var rect = thumbnail.GetComponent<RectTransform>();
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero; rect.localScale = Vector3.one * 0.27f;
                    thumbnail.Bind(card); thumbnail.GetComponent<Button>().transition = Selectable.Transition.None;
                    thumbnail.SetInteractable(false);
                    // 缩略图不接收输入，让整个部位行稳定接收悬停事件。
                    var group = thumbnail.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
                }
            }
            if (nameText) nameText.text = card == null ? "" : card.Data.cardName;
            if (durabilityText) durabilityText.text = card == null ? "" : "耐久 " + card.Source.Durability + "/" + card.Source.Data.maxDurability;
            if (thumbnail) thumbnail.Refresh();
            if (tooltip && tooltip.Owner == this && card != null) tooltip.RefreshDetails(context.Usage.GetUses(slot));
        }
        /// <summary>响应鼠标进入触发区域，显示对应的悬停内容。</summary>
        public void OnPointerEnter(PointerEventData eventData)
        { if (DisplayedCard != null && tooltip) tooltip.ShowCard(this, DisplayedCard, context.Usage.GetUses(slot)); }
        /// <summary>响应鼠标离开触发区域，关闭或安排关闭悬停内容。</summary>
        public void OnPointerExit(PointerEventData eventData) { HideTooltip(); }
        /// <summary>关闭由当前装备槽打开的详情，避免影响其他槽的悬停展示。</summary>
        public void HideTooltip() { if (tooltip) tooltip.HideFor(this); }
        private void OnDisable() { HideTooltip(); }
        private void OnDestroy() { HideTooltip(); ClearThumbnail(); }
        /// <summary>清理装备槽中的旧卡面缩略图，避免残留上次使用的卡牌。</summary>
        private void ClearThumbnail()
        {
            if (!thumbnail) return;
            thumbnail.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(thumbnail.gameObject); else DestroyImmediate(thumbnail.gameObject);
            thumbnail = null;
        }
    }
}
