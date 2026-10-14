namespace SpotlightGameJam.UI
{
    /// <summary>只读展示普通与义体抽牌堆；第一张显示牌为下一张将抽到的牌。</summary>
    public sealed class DrawPilePanel : PilePreviewPanel
    {
        protected override bool IsDrawPile => true;
    }
}
