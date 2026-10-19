using UnityEngine;
using UnityEngine.EventSystems;

namespace SpotlightGameJam.UI
{
    /// <summary>仅转发手牌悬停指针，长按、单击与拖拽仍由原有组件负责。</summary>
    [DisallowMultipleComponent]
    public sealed class HandCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private HandCardLayout owner;
        private CardView view;
        /// <summary>绑定所在手牌行，不修改卡牌选择事件。</summary>
        public void Bind(HandCardLayout layout, CardView card) { owner = layout; view = card; }
        /// <summary>移入时交给手牌行按固定槽位判断目标。</summary>
        public void OnPointerEnter(PointerEventData data) { if (owner) owner.TrackPointer(data); }
        /// <summary>移出仍保留真实指针位置，避免卡牌自身抬起造成反复跳动。</summary>
        public void OnPointerExit(PointerEventData data) { if (owner) owner.TrackPointer(data); }
        private void OnDisable() { if (owner) owner.ReleaseHover(view); }
    }
}
