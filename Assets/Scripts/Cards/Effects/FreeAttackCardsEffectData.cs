using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复用战斗费用入口：本回合攻击基本牌无需 AP，其他费用、耐久和部位次数照常结算。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/本回合攻击牌免费")]
    public sealed class FreeAttackCardsEffectData : CardEffectData
    {
        /// <summary>登记玩家行动阶段的攻击减费，由统一回合控制器在结束行动时清除。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.FreeAttackCards = true;
    }
}
