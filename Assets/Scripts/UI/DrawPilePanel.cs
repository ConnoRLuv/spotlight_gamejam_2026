namespace SpotlightGameJam.UI
{
    /// <summary>展示普通与义体抽牌堆；默认只读，效果可显式开启拖拽排序，第一张为下一张抽到的牌。</summary>
    public sealed class DrawPilePanel : PilePreviewPanel
    {
        protected override bool IsDrawPile => true;
    }
}
