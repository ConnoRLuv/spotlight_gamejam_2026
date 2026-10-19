using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>完整黑幕拦截输入；角色与资源条通过原对象的显示层级高亮，不给透明背景挖窗口。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HandDragBackdrop : Image { }
}
