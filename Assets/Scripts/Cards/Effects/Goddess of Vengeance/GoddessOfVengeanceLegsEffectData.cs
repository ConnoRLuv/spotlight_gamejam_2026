using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神·腿部：本回合使用基本牌无需消耗行动点。</summary>
    /// <remarks>开启本回合的基本牌费用减免，回合推进时自动恢复。</remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/复仇女神·基本牌免行动点")]
    public sealed class GoddessOfVengeanceLegsEffectData : CardEffectData
    {
        /// <summary>启用仅持续本次玩家行动的基本牌费用减免。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.FreeBasicCards = true;
    }
}