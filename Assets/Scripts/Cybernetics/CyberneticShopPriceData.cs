using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>一种系列的商店定价；义体配置显式引用，未实现系列也可以先保存价格。</summary>
    [CreateAssetMenu(menuName = "Spotlight/商店/义体系列价格")]
    public sealed class CyberneticShopPriceData : ScriptableObject
    {
        public string seriesName;
        [Tooltip("未确定购买价格时关闭，不将缺失价格解释为免费商品。")]
        public bool canPurchase;
        [Min(0)] public int purchasePrice;
        [Min(0)] public int repairPrice;
    }
}
