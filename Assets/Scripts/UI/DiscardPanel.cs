namespace SpotlightGameJam.UI
{
    /// <summary>展示普通与义体弃牌堆；默认只读，效果可显式开启选牌，保留现有脚本身份。</summary>
    public sealed class DiscardPanel : PilePreviewPanel
    {
        protected override bool IsDrawPile => false;
    }
}
