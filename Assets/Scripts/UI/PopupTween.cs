using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 挂在弹窗的空父物体上，子级 UGUI 元素统一渐现放大、渐隐缩小。
    /// 父物体支持普通 Transform 和 UI RectTransform，无需添加 Image。
    /// Button.onClick 可直接绑定 Show() 或 Hide()。
    /// 内置 F8 键盘测试；测试开启时退场后保留物体激活，便于再次按键打开。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    [AddComponentMenu("UI/Popup Tween")]
    public sealed class PopupTween : MonoBehaviour
    {
        [Header("动画对象")]
        [Tooltip("留空时缩放当前空父物体及其全部子物体。也可指定弹窗主体子节点，让全屏遮罩只参与渐隐渐现。支持普通 Transform 和 UI RectTransform。")]
        [SerializeField] private Transform visualTarget;

        [Header("入场：渐现并放大")]
        [InspectorName("入场起始缩放")]
        [Tooltip("相对于原始大小的比例，0.85 表示从原始尺寸的 85% 开始放大。")]
        [SerializeField, Range(0.01f, 1f)] private float showStartScale = 0.85f;
        [InspectorName("显示后的缩放")]
        [SerializeField, Min(0.01f)] private float shownScale = 1f;
        [InspectorName("入场起始透明度")]
        [SerializeField, Range(0f, 1f)] private float showStartAlpha = 0f;
        [InspectorName("显示后的透明度")]
        [SerializeField, Range(0f, 1f)] private float shownAlpha = 1f;
        [InspectorName("入场时长（秒）")]
        [SerializeField, Min(0f)] private float showDuration = 0.22f;
        [InspectorName("入场缩放缓动")]
        [SerializeField] private Ease showScaleEase = Ease.OutBack;
        [InspectorName("入场透明度缓动")]
        [SerializeField] private Ease showFadeEase = Ease.OutQuad;

        [Header("退场：渐隐并缩小")]
        [InspectorName("退场结束缩放")]
        [Tooltip("相对于原始大小的比例。默认缩小到 85%。")]
        [SerializeField, Range(0.01f, 1f)] private float hideEndScale = 0.85f;
        [InspectorName("退场结束透明度")]
        [SerializeField, Range(0f, 1f)] private float hideEndAlpha = 0f;
        [InspectorName("退场时长（秒）")]
        [SerializeField, Min(0f)] private float hideDuration = 0.16f;
        [InspectorName("退场缩放缓动")]
        [SerializeField] private Ease hideScaleEase = Ease.InQuad;
        [InspectorName("退场透明度缓动")]
        [SerializeField] private Ease hideFadeEase = Ease.InQuad;

        [Header("播放设置")]
        [InspectorName("启用物体时自动入场")]
        [SerializeField] private bool playOnEnable = true;
        [InspectorName("退场完成后停用物体")]
        [Tooltip("键盘测试生效时暂不自动停用物体，以便接收下一次测试按键；关闭测试后按此设置执行。")]
        [SerializeField] private bool deactivateOnHide = true;
        [InspectorName("忽略游戏时间缩放")]
        [Tooltip("开启后，Time.timeScale 为 0 时也能播放弹窗动效。")]
        [SerializeField] private bool ignoreTimeScale = true;

        [Header("键盘测试")]
        [InspectorName("启用按键测试")]
        [Tooltip("开启后可按测试键切换入场／退场。测试期间退场完成后保留物体激活，但关闭交互，以便再次接收按键。")]
        [SerializeField] private bool enableKeyboardTest = true;
        [InspectorName("切换显示／关闭的按键")]
        [Tooltip("默认 F8；每次按下切换一次，按住不会重复触发。使用旧输入系统。")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F8;
        [InspectorName("仅在编辑器中测试")]
        [Tooltip("开启后，打包运行时不响应测试按键，也不改变退场后停用物体的设置。")]
        [SerializeField] private bool editorOnly = true;

        [Header("动画完成事件（可选）")]
        [SerializeField] private UnityEvent onShown = new UnityEvent();
        [SerializeField] private UnityEvent onHidden = new UnityEvent();

        private CanvasGroup group;
        private Transform animatedTarget;
        private Vector3 originalScale;
        private Sequence motion;
        private bool initialized;
        private bool suppressAutoShow;
        private bool isShowing;
        private bool isVisible;

        private bool KeyboardTestActive => enableKeyboardTest && (!editorOnly || Application.isEditor);

        private void Awake() { Initialize(); }

        private void OnEnable()
        {
            Initialize();
            if (playOnEnable && !suppressAutoShow) Show();
        }

        private void Update()
        {
            if (KeyboardTestActive && Input.GetKeyDown(toggleKey)) Toggle();
        }

        /// <summary>切换显示和关闭；动画过程中再次调用时，平滑切换到相反方向。</summary>
        public void Toggle()
        {
            if (gameObject.activeInHierarchy && (isVisible || (motion != null && isShowing)))
                Hide();
            else
                Show();
        }

        /// <summary>显示弹窗；关闭动画中再次调用时，从当前位置和透明度平滑返回。</summary>
        public void Show()
        {
            Initialize();
            if (!enabled || !animatedTarget) return;

            // Show 可激活原本停用的弹窗，避免 OnEnable 再重复创建一套动画。
            if (!gameObject.activeSelf)
            {
                suppressAutoShow = true;
                try { gameObject.SetActive(true); }
                finally { suppressAutoShow = false; }
            }
            if (!gameObject.activeInHierarchy) return;
            if ((motion != null && isShowing) || (motion == null && isVisible)) return;

            bool reversing = motion != null;
            StopMotion();
            if (!reversing)
            {
                group.alpha = showStartAlpha;
                animatedTarget.localScale = Scaled(showStartScale);
            }
            isShowing = true;
            isVisible = false;
            // 动画期间阻挡点击穿透；入场完成后才开放弹窗内部交互。
            group.blocksRaycasts = true;
            group.interactable = false;

            if (showDuration <= 0f) { FinishShow(); return; }
            motion = DOTween.Sequence();
            motion.Join(group.DOFade(shownAlpha, showDuration).SetEase(showFadeEase));
            motion.Join(animatedTarget.DOScale(Scaled(shownScale), showDuration).SetEase(showScaleEase));
            motion.SetUpdate(ignoreTimeScale).OnComplete(FinishShow);
        }

        /// <summary>播放关闭动效，完成后按设置停用物体。关闭按钮应绑定此方法。</summary>
        public void Hide()
        {
            Initialize();
            if (!enabled || !animatedTarget || !gameObject.activeInHierarchy) return;
            if (motion != null && !isShowing) return;

            StopMotion();
            isShowing = false;
            isVisible = false;
            group.interactable = false;
            group.blocksRaycasts = true;

            if (hideDuration <= 0f) { FinishHide(); return; }
            motion = DOTween.Sequence();
            motion.Join(group.DOFade(hideEndAlpha, hideDuration).SetEase(hideFadeEase));
            motion.Join(animatedTarget.DOScale(Scaled(hideEndScale), hideDuration).SetEase(hideScaleEase));
            motion.SetUpdate(ignoreTimeScale).OnComplete(FinishHide);
        }

        private void Initialize()
        {
            if (initialized) return;
            group = GetComponent<CanvasGroup>();
            animatedTarget = visualTarget ? visualTarget : transform;
            if (animatedTarget) originalScale = animatedTarget.localScale;
            initialized = true;
        }

        private Vector3 Scaled(float factor)
        {
            return new Vector3(originalScale.x * factor, originalScale.y * factor, originalScale.z);
        }

        private void FinishShow()
        {
            motion = null;
            group.alpha = shownAlpha;
            animatedTarget.localScale = Scaled(shownScale);
            group.blocksRaycasts = true;
            group.interactable = true;
            isVisible = true;
            onShown.Invoke();
        }

        private void FinishHide()
        {
            motion = null;
            group.alpha = hideEndAlpha;
            animatedTarget.localScale = Scaled(hideEndScale);
            group.interactable = false;
            group.blocksRaycasts = false;
            isVisible = false;
            if (deactivateOnHide && !KeyboardTestActive) gameObject.SetActive(false);
            onHidden.Invoke();
        }

        private void StopMotion()
        {
            // 只取消本组件的动画，避免快速切换时旧回调误关掉重新打开的弹窗。
            motion?.Kill();
            motion = null;
        }

        private void OnDisable()
        {
            StopMotion();
            isVisible = false;
            if (!initialized) return;
            if (group)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            if (animatedTarget) animatedTarget.localScale = originalScale;
        }

        private void OnDestroy() { StopMotion(); }
    }
}
