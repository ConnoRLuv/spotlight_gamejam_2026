using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>同一画布共用一个只读放大层，置于滚动裁剪之外；关闭后不残留鼠标遮挡。</summary>
    public sealed class CardPreviewOverlay : BasePanel
    {
        private Image artwork;
        private RectTransform faceRoot;
        private CardView fallbackView;
        private Text fallbackText;
        private CardData displayed;
        private CardView sourceTemplate;
        private Vector2 lastSize;
        public CardHoldPreview Owner { get; private set; }

        /// <summary>按所属根画布复用展示层，不建立跨场景单例；画布销毁时随之清理。</summary>
        public static CardPreviewOverlay GetOrCreate(Component source)
        {
            var canvas = source.GetComponentInParent<Canvas>();
            if (!canvas) return null;
            var parent = canvas.rootCanvas;
            var existing = parent.GetComponentInChildren<CardPreviewOverlay>(true);
            if (existing) return existing;
            var root = new GameObject("CardPreviewOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(CardPreviewOverlay));
            root.layer = parent.gameObject.layer; root.transform.SetParent(parent.transform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var overlay = root.GetComponent<CardPreviewOverlay>(); overlay.Hide(0); return overlay;
        }

        /// <summary>展示静态卡图或真实模板；模板副本禁止再次长按，所有图形均不接收输入。</summary>
        public void ShowCard(CardHoldPreview owner, CardData data, CardInstance instance, CardView template, BattleContext context)
        {
            if (!owner || !data) return;
            ClearFace(); Owner = owner; displayed = data; sourceTemplate = template;
            if (!faceRoot)
            {
                var face = new GameObject("CardFace", typeof(RectTransform), typeof(Image));
                face.layer = gameObject.layer; face.transform.SetParent(transform, false);
                faceRoot = (RectTransform)face.transform; faceRoot.anchorMin = faceRoot.anchorMax = faceRoot.pivot = new Vector2(.5f, .5f);
                artwork = face.GetComponent<Image>(); artwork.raycastTarget = false; artwork.preserveAspect = true;
            }
            artwork.sprite = data.artwork; artwork.color = data.artwork ? Color.white : new Color(.035f, .06f, .14f);
            if (!data.artwork && template)
            {
                fallbackView = Instantiate(template, faceRoot);
                fallbackView.name = "PreviewCard"; fallbackView.SetHoldPreviewEnabled(false); fallbackView.SetInteractable(false);
                if (context != null && instance != null) fallbackView.BindForBattle(instance, context);
                else fallbackView.Bind(instance ?? new CardInstance(data));
                // 源卡面可能已有商店适配组件或进度环，放大副本不应复制这些交互和进度。
                foreach (var hold in fallbackView.GetComponentsInChildren<CardHoldPreview>(true)) hold.enabled = false;
                foreach (var ring in fallbackView.GetComponentsInChildren<HoldProgressRing>(true)) ring.gameObject.SetActive(false);
                foreach (var graphic in fallbackView.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            }
            else if (!data.artwork)
            {
                var label = new GameObject("Description", typeof(RectTransform), typeof(Text));
                label.layer = gameObject.layer; label.transform.SetParent(faceRoot, false);
                fallbackText = label.GetComponent<Text>();
                var rect = fallbackText.rectTransform; rect.anchorMin = new Vector2(.08f, .08f); rect.anchorMax = new Vector2(.92f, .92f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var sourceText = owner.GetComponentInChildren<Text>(true);
                if (!sourceText) sourceText = owner.GetComponentInParent<Canvas>().GetComponentInChildren<Text>(true);
                fallbackText.font = sourceText ? sourceText.font : null; fallbackText.fontSize = 22;
                fallbackText.color = Color.white; fallbackText.alignment = TextAnchor.MiddleCenter; fallbackText.raycastTarget = false;
                fallbackText.text = data.cardName + "\n\n" + (data.sanityCost > 0 ? "理智 " + data.sanityCost : "AP " + data.cost) + "\n\n" + data.description;
            }
            transform.SetAsLastSibling(); Show(0); Group.blocksRaycasts = Group.interactable = false; FitFace();
        }

        /// <summary>只关闭当前归属的预览，旧卡面退出事件不能误关另一张卡。</summary>
        public void HideFor(CardHoldPreview owner) { if (Owner == owner) Hide(0); }

        /// <summary>隐藏时释放来源和卡面副本，不让根画布保留已销毁手牌的引用。</summary>
        public override void Hide(float fadeSeconds = 0f)
        { Owner = null; displayed = null; sourceTemplate = null; ClearFace(); base.Hide(fadeSeconds); }

        private void LateUpdate()
        {
            if (!IsVisible) return;
            if (!Owner || !Owner.isActiveAndEnabled) { Hide(0); return; }
            if (((RectTransform)transform).rect.size != lastSize) FitFace();
        }

        /// <summary>卡面以原比例居中放大，限制在当前画布内，兼容16:9及4:3。</summary>
        private void FitFace()
        {
            if (!faceRoot || !displayed) return;
            lastSize = ((RectTransform)transform).rect.size;
            float width = Mathf.Min(420, lastSize.x * .82f), height = Mathf.Min(660, lastSize.y * .84f);
            faceRoot.anchoredPosition = Vector2.zero; faceRoot.sizeDelta = new Vector2(width, height);
            if (!fallbackView || !sourceTemplate) return;
            var rect = (RectTransform)fallbackView.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
            float scale = Mathf.Min(width / Mathf.Max(1, rect.rect.width), height / Mathf.Max(1, rect.rect.height));
            rect.localScale = Vector3.one * scale;
        }

        /// <summary>先停用后销毁，防止延迟销毁期间出现叠印，也不会销毁共享卡牌资产。</summary>
        private void ClearFace()
        {
            if (fallbackView) { fallbackView.gameObject.SetActive(false); Destroy(fallbackView.gameObject); fallbackView = null; }
            if (fallbackText) { fallbackText.gameObject.SetActive(false); Destroy(fallbackText.gameObject); fallbackText = null; }
            if (artwork) artwork.sprite = null;
        }
        protected override void OnDisable()
        { Owner = null; displayed = null; sourceTemplate = null; ClearFace(); base.OnDisable(); }
    }
}
