using UnityEngine;
using UnityEngine.EventSystems;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 鼠标进入敌人肖像时显示说明，离开时隐藏。
    /// 编辑模式保留说明面板可见，便于成员按线框图调整排版。
    /// </summary>
    public sealed class BattleEnemyTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject tooltip;

        /// <summary>绑定独立的说明面板；面板不应是肖像的父节点。</summary>
        public void Configure(GameObject tooltipPanel) { tooltip = tooltipPanel; }

        private void OnEnable()
        {
            // 首次进入 Play Mode 不显示说明，等待真正的鼠标悬停事件。
            if (Application.isPlaying && tooltip) tooltip.SetActive(false);
        }

        /// <summary>响应鼠标进入触发区域，显示对应的悬停内容。</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip) tooltip.SetActive(true);
        }

        /// <summary>响应鼠标离开触发区域，关闭或安排关闭悬停内容。</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip) tooltip.SetActive(false);
        }

        private void OnDisable()
        {
            // 肖像被隐藏或场景退出时，不留下孤立的说明面板。
            if (Application.isPlaying && tooltip) tooltip.SetActive(false);
        }
    }
}
