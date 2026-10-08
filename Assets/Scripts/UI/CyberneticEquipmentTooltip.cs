using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>义体部位的只读悬停详情；复用原卡牌预制体显示大图和描述，不拦截鼠标。</summary>
    public sealed class CyberneticEquipmentTooltip : BasePanel
    {
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private RectTransform cardRoot;
        [SerializeField] private Text title;
        [SerializeField] private Text description;
        [SerializeField] private Text details;
        private CardView view;
        private Vector2 lastCardAreaSize;
        public CyberneticEquipmentSlot Owner { get; private set; }
        public CardInstance DisplayedCard { get; private set; }
        /// <summary>绑定组件所需的界面引用，供编辑器生成工具初始化使用。</summary>
        public void Configure(CardPrefabCatalog cards, RectTransform faceRoot, Text heading, Text body, Text stats)
        { catalog = cards; cardRoot = faceRoot; title = heading; description = body; details = stats; }

        /// <summary>显示真实卡牌的大图；没有成功使用的卡牌时保持隐藏。</summary>
        public void ShowCard(CyberneticEquipmentSlot owner, CardInstance card, int uses)
        {
            if (card == null) return;
            if (DisplayedCard != card)
            {
                ClearView(); DisplayedCard = card;
                view = Instantiate(catalog.Find(card.Data), cardRoot);
                var rect = view.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero; rect.localScale = Vector3.one * 1.4f;
                view.Bind(card); view.GetComponent<Button>().transition = Selectable.Transition.None; view.SetInteractable(false);
            }
            Owner = owner; RefreshDetails(uses); Show(0.1f);
            FitCardFace();
            // 悬停面板不是交互窗口；不遮挡部位触发区或产生进入/离开抖动。
            Group.blocksRaycasts = Group.interactable = false;
        }
        private void LateUpdate()
        {
            if (view && cardRoot.rect.size != lastCardAreaSize) FitCardFace();
        }
        /// <summary>按详情容器尺寸等比缩放卡面，避免越界或覆盖文字。</summary>
        private void FitCardFace()
        {
            if (!view) return;
            lastCardAreaSize = cardRoot.rect.size;
            // 4:3 内容区会缩小逻辑高度；等比适配可避免大卡面压住标题或越出面板。
            var face = view.GetComponent<RectTransform>();
            float scale = Mathf.Min(1.4f, lastCardAreaSize.x / face.rect.width, lastCardAreaSize.y / face.rect.height);
            face.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }
        /// <summary>更新部位、理智消耗、耐久和本场使用次数，并同步卡面状态。</summary>
        public void RefreshDetails(int uses)
        {
            if (DisplayedCard == null) return;
            var card = DisplayedCard; var source = card.Source;
            title.text = card.Data.cardName; description.text = card.Data.description;
            details.text = "部位：" + Owner.SlotName + "\n理智消耗 " + card.Data.sanityCost + "\n耐久 " + source.Durability + "/" + source.Data.maxDurability
                + "\n本场使用 " + uses + "/3\n每回合同部位最多使用一次" + (source.Durability == 0 ? "\n已报废" : "");
            if (view) view.Refresh();
        }
        /// <summary>仅在指定装备槽仍拥有此详情面板时关闭，避免旧退出事件误关新内容。</summary>
        public void HideFor(CyberneticEquipmentSlot owner) { if (Owner == owner) Hide(0.1f); }
        /// <summary>清除当前装备槽归属，并按指定秒数隐藏详情面板。</summary>
        public override void Hide(float fadeSeconds = 0f) { Owner = null; base.Hide(fadeSeconds); }
        protected override void OnDisable() { Owner = null; base.OnDisable(); }
        protected override void OnDestroy() { ClearView(); base.OnDestroy(); }
        /// <summary>停用并销毁详情卡面，清空当前展示的卡牌引用。</summary>
        private void ClearView()
        {
            if (!view) return;
            view.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject);
            view = null; DisplayedCard = null;
        }
    }
}
