using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>商店修复框与商品卡面的共用长按预览，使用真实时间计时，松开或移出后关闭。</summary>
    [DisallowMultipleComponent]
    public sealed class ShopRepairHoldPreview : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Min(.1f)] private float holdSeconds = .45f;
        private const float ProgressDelaySeconds = .15f;
        private CardData card;
        private bool pressed, suppressClick;
        private float pressedAt;
        private int pointerId;
        private BasePanel preview;
        private HoldProgressRing progressRing;
        public bool IsPreviewVisible => preview && preview.IsVisible;
        public CardData DisplayedCard => card;

        /// <summary>跟随实际维修对象更新预览；切换型号或解除绑定时收起旧卡面。</summary>
        public void Bind(CardData value)
        {
            if (card == value) return;
            CancelHold(); card = value;
        }

        /// <summary>记录主指针按下时间；满耐久或余额不足也允许查看卡牌。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !card) return;
            CancelHold(); suppressClick = false;
            pointerId = eventData.pointerId; pressed = true; pressedAt = Time.unscaledTime;
        }

        private void Update()
        {
            if (!pressed || suppressClick) return;
            float elapsed = Time.unscaledTime - pressedAt;
            // 先区分短按和长按，再显示圆环；总预览等待时间仍为 holdSeconds。
            float delay = Mathf.Min(ProgressDelaySeconds, holdSeconds * .5f);
            if (elapsed < delay) return;
            float progress = Mathf.Clamp01((elapsed - delay) / Mathf.Max(.01f, holdSeconds - delay));
            if (progress < 1f && (!progressRing || !progressRing.gameObject.activeSelf)) ShowProgressRing();
            if (progressRing) progressRing.SetProgress(progress);
            if (progress < 1f) return;
            if (progressRing) progressRing.gameObject.SetActive(false);
            suppressClick = true; ShowPreview();
        }

        /// <summary>按住时在图标中央显示圆环，复用同一节点且不影响修复按钮接收指针。</summary>
        private void ShowProgressRing()
        {
            if (!progressRing)
            {
                var root = new GameObject("HoldProgressRing", typeof(RectTransform), typeof(HoldProgressRing));
                root.layer = gameObject.layer; root.transform.SetParent(transform, false);
                progressRing = root.GetComponent<HoldProgressRing>();
                var rect = progressRing.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(100, 100);
                progressRing.color = new Color(.43f, .91f, .89f);
                progressRing.raycastTarget = false;
            }
            progressRing.SetProgress(0); progressRing.gameObject.SetActive(true);
            progressRing.transform.SetAsLastSibling();
        }

        /// <summary>松开同一指针时收起预览，并阻止长按被解释为普通修复点击。</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed || eventData.pointerId != pointerId) return;
            if (suppressClick) eventData.eligibleForClick = false;
            CancelHold();
        }

        /// <summary>移出框时取消计时和预览，避免拖动后继续弹出卡面。</summary>
        public void OnPointerExit(PointerEventData eventData) { CancelHold(); }

        /// <summary>修复入口的第二层保护，避免直接派发点击事件时误扣费。</summary>
        public bool ConsumeClick()
        {
            bool consumed = suppressClick; suppressClick = false; return consumed;
        }

        /// <summary>在商店内创建只读大卡面，保持宽高比，不拦截松开和移出事件。</summary>
        private void ShowPreview()
        {
            var shop = GetComponentInParent<ShopPanel>();
            if (!shop || !card) return;
            if (!preview)
            {
                var root = new GameObject("RepairCardPreview", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(BasePanel));
                root.layer = gameObject.layer; root.transform.SetParent(shop.transform, false);
                var rect = (RectTransform)root.transform;
                rect.anchorMin = new Vector2(.32f, .08f); rect.anchorMax = new Vector2(.68f, .92f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                preview = root.GetComponent<BasePanel>();
            }
            preview.transform.SetAsLastSibling();
            var image = preview.GetComponent<Image>();
            image.sprite = card.artwork; image.color = card.artwork ? Color.white : new Color(.035f, .06f, .14f);
            image.preserveAspect = true; image.raycastTarget = false;
            // 尚无完整美术的型号也能查看真实名称与描述。
            var text = preview.GetComponentInChildren<Text>(true);
            if (!text)
            {
                var label = new GameObject("Description", typeof(RectTransform), typeof(Text));
                label.layer = gameObject.layer; label.transform.SetParent(preview.transform, false);
                text = label.GetComponent<Text>();
                var rect = text.rectTransform; rect.anchorMin = new Vector2(.08f, .08f); rect.anchorMax = new Vector2(.92f, .92f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var source = shop.GetComponentInChildren<Text>(true);
                text.font = source.font; text.color = Color.white; text.fontSize = 22;
                text.resizeTextForBestFit = false;
                text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            }
            text.text = card.cardName + "\n\n" + card.description;
            text.gameObject.SetActive(!card.artwork);
            preview.Show(0);
            var group = preview.GetComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
        }

        /// <summary>结束本次按住状态并立即隐藏预览，保留点击抑制直到修复入口消费。</summary>
        private void CancelHold()
        {
            pressed = false;
            if (progressRing) { progressRing.SetProgress(0); progressRing.gameObject.SetActive(false); }
            if (preview) preview.Hide(0);
        }
        private void OnDisable() { CancelHold(); }
        private void OnDestroy() { if (preview) Destroy(preview.gameObject); }
    }
}
