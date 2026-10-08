using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpotlightGameJam.UI
{
    /// <summary>弃牌堆悬停入口；鼠标移到面板上可继续浏览，离开两个区域后关闭。</summary>
    public sealed class DiscardPileHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private DiscardPanel panel;
        [SerializeField, Min(0)] private float fadeInSeconds = 0.15f;
        [SerializeField, Min(0)] private float fadeOutSeconds = 0.15f;
        [SerializeField, Min(0)] private float exitGraceSeconds = 0.1f;
        private bool overPile;
        private bool overPanel;
        private Coroutine pendingClose;

        /// <summary>编辑器接入场景时设置面板及过渡秒数。</summary>
        public void Configure(DiscardPanel target, float fadeIn = 0.15f, float fadeOut = 0.15f)
        {
            if (panel) panel.PointerPresenceChanged -= OnPanelPointer;
            panel = target; fadeInSeconds = fadeIn; fadeOutSeconds = fadeOut;
            if (isActiveAndEnabled && panel) panel.PointerPresenceChanged += OnPanelPointer;
        }
        private void OnEnable() { if (panel) panel.PointerPresenceChanged += OnPanelPointer; }
        private void OnDisable()
        {
            if (panel) panel.PointerPresenceChanged -= OnPanelPointer;
            CancelClose(); overPile = overPanel = false;
            if (panel) panel.Hide();
        }
        /// <summary>记录鼠标进入弃牌堆，取消待关闭任务并显示弃牌面板。</summary>
        public void OnPointerEnter(PointerEventData eventData)
        { overPile = true; CancelClose(); if (panel) panel.Show(fadeInSeconds); }
        /// <summary>记录鼠标离开弃牌堆，并安排跨区域移动后的关闭检查。</summary>
        public void OnPointerExit(PointerEventData eventData) { overPile = false; ScheduleClose(); }
        /// <summary>同步鼠标在弃牌面板中的状态，进入时取消关闭，离开时重新检查。</summary>
        private void OnPanelPointer(bool entered)
        {
            overPanel = entered;
            if (entered) { CancelClose(); if (panel) panel.Show(fadeInSeconds); }
            else ScheduleClose();
        }
        /// <summary>仅在鼠标离开弃牌堆和面板后启动延迟关闭任务。</summary>
        private void ScheduleClose()
        {
            CancelClose();
            if (isActiveAndEnabled && !overPile && !overPanel) pendingClose = StartCoroutine(CloseAfterTransfer());
        }
        /// <summary>等待不受游戏时间缩放影响的宽限时间，再检查是否需要淡出面板。</summary>
        private IEnumerator CloseAfterTransfer()
        {
            // 留出穿过触发按钮与面板之间小间隙的时间；游戏暂停时仍可关闭。
            yield return new WaitForSecondsRealtime(exitGraceSeconds);
            pendingClose = null;
            if (!overPile && !overPanel && panel && panel.IsVisible) panel.Hide(fadeOutSeconds);
        }
        /// <summary>停止待执行的关闭协程，避免鼠标返回后面板仍被隐藏。</summary>
        private void CancelClose() { if (pendingClose != null) StopCoroutine(pendingClose); pendingClose = null; }
    }
}
