using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>普罗米修斯生命涌现：本回合所有基本牌免除出牌 AP。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/本回合基本牌免费")]
    public sealed class FreeBasicCardsEffectData : CardEffectData
    {
        /// <summary>启用仅持续本次玩家行动的基本牌费用减免。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.FreeBasicCards = true;
    }
}
