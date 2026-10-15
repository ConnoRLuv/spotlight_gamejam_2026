namespace SpotlightGameJam.UI
{
    /// <summary>只读展示普通与义体弃牌堆；保留脚本身份以兼容现有预制体和场景。</summary>
    public sealed class DiscardPanel : PilePreviewPanel
    {
        protected override bool IsDrawPile => false;
    }
}
