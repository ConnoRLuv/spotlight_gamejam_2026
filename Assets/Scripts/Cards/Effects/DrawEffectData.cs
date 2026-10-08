using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 配置从普通牌堆或义体牌堆抽牌的效果。
    /// </summary>
    /// <remarks>
    /// cyberneticHand 选择牌堆；实际容量、溢出和弃牌堆重洗由 CardDeck.Draw 处理。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/抽牌")]
    public sealed class DrawEffectData : CardEffectData
    {
        [Min(0)] public int amount = 1;
        public bool cyberneticHand;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate() => amount < 0 ? "抽牌数量不能为负数。" : null;
        /// <summary>从指定牌堆补充手牌，具体容量及弃牌重洗由牌堆处理。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        { (cyberneticHand ? context.Cybernetic : context.Ordinary).Draw(amount); }
    }
}
