using UnityEngine;
using UnityEngine.EventSystems;

namespace SpotlightGameJam.UI
{
    /// <summary>通用 UI 拖拽入口，只转发指针事件，不读取卡牌、战斗或费用数据。</summary>
    [DisallowMultipleComponent]
    public sealed class ReorderableDragItem : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ReorderableDragList owner;
        /// <summary>绑定所属列表；同一组件可被其他列表复用。</summary>
        public void Bind(ReorderableDragList list) { owner = list; }
        /// <summary>保留拖动阈值，短按与长按预览不会被解释成拖拽。</summary>
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = true; }
        /// <summary>达到拖动阈值后，由列表创建占位与浮动展示；排序和手牌出牌共用此入口。</summary>
        public void OnBeginDrag(PointerEventData data) { if (owner) owner.BeginDrag(this, data); }
        /// <summary>把指针位置转交列表，调整占位顺序。</summary>
        public void OnDrag(PointerEventData data) { if (owner) owner.Drag(this, data); }
        /// <summary>松开后由列表归位并报告结果，业务层决定排序或有效目标上的出牌。</summary>
        public void OnEndDrag(PointerEventData data) { if (owner) owner.EndDrag(this, data); }
        private void OnDisable() { if (owner && owner.DraggedItem == this) owner.CancelDrag(); }
    }
}
