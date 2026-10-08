namespace SpotlightGameJam
{
    /// <summary>
    /// 区分基本牌、功能牌、义体牌和特殊牌，供玩法规则判断费用与牌区。
    /// </summary>
    /// <remarks>
    /// 与遗留 CardType 展示分类不同；序列化数值固定，新增类别时应追加而非重排。
    /// </remarks>
    public enum CardCategory { Basic = 0, Function = 1, Cybernetic = 2, Special = 3 }
}
