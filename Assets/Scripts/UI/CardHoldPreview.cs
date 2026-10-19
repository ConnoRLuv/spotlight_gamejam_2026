using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>所有卡牌和义体图标共用的长按交互；短按无圆环，长按只预览并拦截本次点击。</summary>
    [DisallowMultipleComponent]
    public class CardHoldPreview : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Min(.1f)] private float holdSeconds = .45f;
        private const float ProgressDelaySeconds = .15f;
        private CardData card;
        private CardInstance instance;
        private CardView template;
        private BattleContext context;
        private bool pressed, suppressClick;
        private float pressedAt;
        private int pointerId;
        private PointerEventData pointer;
        private Vector2 pressedPosition;
        private HoldProgressRing progressRing;
        private CardPreviewOverlay preview;
        public bool IsPreviewVisible => preview && preview.IsVisible && preview.Owner == this;
        public CardData DisplayedCard => card;

        /// <summary>图标入口绑定静态卡牌；同一卡牌重复刷新不会中断正在进行的长按。</summary>
        public void Bind(CardData value)
        {
            if (card == value) return;
            CancelHold(); card = value; instance = null; template = null; context = null;
        }

        /// <summary>CardView 自动提供真实实例和展示模板，放大时仍显示当前费用、描述与共享耐久。</summary>
        public void Bind(CardData value, CardInstance cardInstance, CardView source, BattleContext battle)
        {
            if (card == value && instance == cardInstance && template == source && context == battle) return;
            CancelHold(); card = value; instance = cardInstance; template = source; context = battle;
        }

        /// <summary>记录主指针和真实时间；按钮被禁用或卡牌锁定时仍可浏览，但停用组件不响应。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left || !card ||
                pressed && eventData.pointerId != pointerId) return;
            CancelHold(); suppressClick = false;
            pointer = eventData; pointerId = eventData.pointerId; pressedPosition = eventData.position;
            pressed = true; pressedAt = Time.unscaledTime;
        }

        protected virtual void Update()
        {
            if (!pressed || suppressClick) return;
            // 不接管拖拽事件，让父 ScrollRect 正常滚动；超过拖动阈值即取消本次长按。
            float threshold = EventSystem.current ? EventSystem.current.pixelDragThreshold : 10;
            if (pointer != null && (pointer.dragging || (pointer.position - pressedPosition).sqrMagnitude > threshold * threshold))
            { suppressClick = true; pointer.eligibleForClick = false; CancelHold(); return; }
            float elapsed = Time.unscaledTime - pressedAt;
            float delay = Mathf.Min(ProgressDelaySeconds, holdSeconds * .5f);
            if (elapsed < delay) return;
            float progress = Mathf.Clamp01((elapsed - delay) / Mathf.Max(.01f, holdSeconds - delay));
            if (progress < 1f && (!progressRing || !progressRing.gameObject.activeSelf)) ShowProgressRing();
            if (progressRing) progressRing.SetProgress(progress);
            if (progress < 1f) return;
            if (progressRing) progressRing.gameObject.SetActive(false);
            suppressClick = true;
            preview = CardPreviewOverlay.GetOrCreate(this);
            if (preview) preview.ShowCard(this, card, instance, template, context);
        }

        /// <summary>沿用商店粗圆环与颜色，只有超过短按延迟后才创建，不拦截卡牌或滚动输入。</summary>
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
                progressRing.color = new Color(.43f, .91f, .89f); progressRing.raycastTarget = false;
            }
            progressRing.SetProgress(0); progressRing.gameObject.SetActive(true); progressRing.transform.SetAsLastSibling();
        }

        /// <summary>松开后立即收起；长按和拖动不能被 EventSystem 继续解释成出牌或选择。</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            if (suppressClick) eventData.eligibleForClick = false;
            CancelHold();
        }

        /// <summary>仅匹配当前指针的移出事件，避免其他触点取消当前预览。</summary>
        public void OnPointerExit(PointerEventData eventData)
        { if (eventData.pointerId == pointerId) CancelHold(); }

        /// <summary>点击入口的第二层保护，防止直接派发点击时绕过长按拦截；下一次按下会重新允许短按。</summary>
        public bool ConsumeClick()
        { bool consumed = suppressClick; suppressClick = false; return consumed; }

        /// <summary>结束计时并收起此组件拥有的放大面板，保留点击抑制直到消费或下一次按下。</summary>
        public void CancelHold()
        {
            pressed = false; pointer = null;
            if (progressRing) { progressRing.SetProgress(0); progressRing.gameObject.SetActive(false); }
            if (preview) preview.HideFor(this);
        }

        protected virtual void OnDisable() { CancelHold(); }
        protected virtual void OnDestroy() { CancelHold(); }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelHold(); }
    }
}
