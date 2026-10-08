using System;
using DG.Tweening;
using UnityEngine;

namespace SpotlightGameJam.UI
{
    /// <summary>面板显隐基础类：统一透明度、交互和 DOTween 生命周期，不处理业务数据。</summary>
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public class BasePanel : MonoBehaviour
    {
        private CanvasGroup canvasGroup;
        private Tween fade;
        /// <summary>目标可见状态；开始淡出时即为 false，此时已停止接收输入。</summary>
        public bool IsVisible { get; private set; }
        protected CanvasGroup Group => canvasGroup ? canvasGroup : canvasGroup = GetComponent<CanvasGroup>();

        protected virtual void Awake() { IsVisible = gameObject.activeSelf && Group.alpha > 0; }
        protected virtual void OnEnable() { }

        /// <summary>显示面板。fadeSeconds 单位为秒；省略或传 0 立即显示，暂停游戏时仍播放动画。</summary>
        public virtual void Show(float fadeSeconds = 0f)
        {
            ValidateDuration(fadeSeconds);
            if (fadeSeconds > 0 && IsVisible && gameObject.activeSelf) return;
            CancelFade();
            // 首次激活的面板尚未 Awake，因此在激活前获取组件并确定起始透明度。
            float start = gameObject.activeSelf ? Group.alpha : 0;
            gameObject.SetActive(true);
            IsVisible = true;
            Group.alpha = start;
            Group.interactable = Group.blocksRaycasts = true;
            if (fadeSeconds == 0) { Group.alpha = 1; return; }
            fade = Group.DOFade(1, fadeSeconds).SetEase(Ease.OutQuad).SetUpdate(true)
                .SetLink(gameObject).OnKill(() => fade = null);
        }

        /// <summary>隐藏面板。淡出期间立即停止交互，完成后停用对象；0 秒立即停用。</summary>
        public virtual void Hide(float fadeSeconds = 0f)
        {
            ValidateDuration(fadeSeconds);
            if (fadeSeconds > 0 && !IsVisible && fade != null && fade.IsActive()) return;
            CancelFade();
            IsVisible = false;
            Group.interactable = Group.blocksRaycasts = false;
            if (fadeSeconds == 0 || !gameObject.activeInHierarchy || Group.alpha == 0)
            { Group.alpha = 0; gameObject.SetActive(false); return; }
            fade = Group.DOFade(0, fadeSeconds).SetEase(Ease.OutQuad).SetUpdate(true)
                .SetLink(gameObject).OnComplete(() => { fade = null; gameObject.SetActive(false); })
                .OnKill(() => fade = null);
        }

        protected virtual void OnDisable()
        {
            // 新显隐指令会杀掉旧动画，避免旧淡出回调把已重新打开的面板关闭。
            CancelFade(); IsVisible = false;
            Group.alpha = 0; Group.interactable = Group.blocksRaycasts = false;
        }
        protected virtual void OnDestroy() { CancelFade(); }
        /// <summary>终止当前透明度动画并释放引用，避免显隐操作互相覆盖。</summary>
        private void CancelFade() { var previous = fade; fade = null; previous?.Kill(); }
        /// <summary>检查过渡秒数是否为有限非负数，拒绝非法动画时长。</summary>
        protected static void ValidateDuration(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds), "淡入淡出时间必须是有限的非负秒数。");
        }
    }
}
