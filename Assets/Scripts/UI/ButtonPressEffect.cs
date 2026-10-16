using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 挂在 UGUI Button 上：按住时下移并缩小，松开时恢复。
    /// 不绑定或触发 onClick，按钮原有点击事件照常使用。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("UI/Button Press Effect")]
    public sealed class ButtonPressEffect : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, ICancelHandler, IDeselectHandler
    {
        [Header("动画对象")]
        [Tooltip("留空时动画作用于按钮自身。若按钮受 Layout Group 控制，建议指定包含图片和文字的子节点；根按钮保持固定点击区域。")]
        [SerializeField] private RectTransform visualTarget;

        [Header("按下：下移并缩小")]
        [InspectorName("下移距离（UI 单位）")]
        [Tooltip("正数表示沿父级 UI 坐标的 Y 轴向下移动。受 Canvas 缩放影响，不一定等于屏幕像素。")]
        [SerializeField, Min(0f)] private float pressDistance = 6f;

        [InspectorName("按下缩放比例")]
        [Tooltip("相对于按钮原始大小：0.94 表示缩小到 94%，1 表示不缩放。保留原始 Z 缩放。")]
        [SerializeField, Range(0.1f, 1f)] private float pressedScale = 0.94f;

        [InspectorName("按下时长（秒）")]
        [SerializeField, Min(0f)] private float pressDuration = 0.08f;

        [InspectorName("按下缓动")]
        [SerializeField] private Ease pressEase = Ease.OutQuad;

        [Header("松开：恢复原位和大小")]
        [InspectorName("恢复时长（秒）")]
        [SerializeField, Min(0f)] private float releaseDuration = 0.14f;

        [InspectorName("恢复缓动")]
        [SerializeField] private Ease releaseEase = Ease.OutQuad;

        [Header("播放设置")]
        [InspectorName("忽略游戏时间缩放")]
        [Tooltip("开启后，即使 Time.timeScale 为 0，按钮动画仍然播放。")]
        [SerializeField] private bool ignoreTimeScale = true;

        private Button button;
        private RectTransform animatedTarget;
        private Sequence motion;
        private Vector2 restPosition;
        private Vector3 restScale;
        private bool hasRestPose;
        private int? activePointer;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left ||
                activePointer.HasValue || !button || !button.IsActive() || !button.IsInteractable()) return;

            // 恢复动画未结束时继续使用同一份初始状态，快速连点不会累积位移或缩放。
            if (!hasRestPose)
            {
                animatedTarget = visualTarget ? visualTarget : transform as RectTransform;
                if (!animatedTarget) return;
                restPosition = animatedTarget.anchoredPosition;
                restScale = animatedTarget.localScale;
                hasRestPose = true;
            }

            activePointer = eventData.pointerId;
            Animate(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left ||
                !activePointer.HasValue || activePointer.Value != eventData.pointerId) return;

            activePointer = null;
            Animate(false);
        }

        public void OnCancel(BaseEventData eventData) { RestoreImmediately(); }
        public void OnDeselect(BaseEventData eventData) { RestoreImmediately(); }

        private void Update()
        {
            // Button 或父级 CanvasGroup 在按住期间变为不可交互时，立即复位。
            if (hasRestPose && (!button || !button.IsActive() || !button.IsInteractable()))
                RestoreImmediately();
        }

        private void OnDisable() { RestoreImmediately(); }
        private void OnDestroy() { RestoreImmediately(); }
        private void OnApplicationFocus(bool focused) { if (!focused) RestoreImmediately(); }
        private void OnApplicationPause(bool paused) { if (paused) RestoreImmediately(); }

        private void Animate(bool pressed)
        {
            StopMotion();
            if (!animatedTarget || !hasRestPose) return;

            Vector2 destination = restPosition + (pressed ? Vector2.down * pressDistance : Vector2.zero);
            Vector3 scale = pressed
                ? new Vector3(restScale.x * pressedScale, restScale.y * pressedScale, restScale.z)
                : restScale;
            float duration = pressed ? pressDuration : releaseDuration;
            Ease ease = pressed ? pressEase : releaseEase;

            if (duration <= 0f)
            {
                animatedTarget.anchoredPosition = destination;
                animatedTarget.localScale = scale;
                if (!pressed) hasRestPose = false;
                return;
            }

            motion = DOTween.Sequence();
            motion.Join(animatedTarget.DOAnchorPos(destination, duration).SetEase(ease));
            motion.Join(animatedTarget.DOScale(scale, duration).SetEase(ease));
            motion.SetUpdate(ignoreTimeScale).OnComplete(() =>
            {
                motion = null;
                if (!pressed)
                {
                    // 明确恢复记录的原始值，避免反复点击后的浮点误差。
                    if (animatedTarget)
                    {
                        animatedTarget.anchoredPosition = restPosition;
                        animatedTarget.localScale = restScale;
                    }
                    hasRestPose = false;
                }
            });
        }

        private void StopMotion()
        {
            // 只清理本组件创建的动画，不终止同一对象上的其他 DOTween 动画。
            motion?.Kill();
            motion = null;
        }

        private void RestoreImmediately()
        {
            StopMotion();
            activePointer = null;
            if (hasRestPose && animatedTarget)
            {
                animatedTarget.anchoredPosition = restPosition;
                animatedTarget.localScale = restScale;
            }
            hasRestPose = false;
        }
    }
}
