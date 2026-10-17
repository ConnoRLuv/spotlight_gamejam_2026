using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神·躯干：本回合玩家受到的伤害完全转移给敌人代为承受。</summary>
    /// <remarks>
    /// 只开启本回合的伤害转移标记；实际转移由 DamageResolver 在玩家即将承伤时统一处理，
    /// 持续到下一个玩家回合开始，期间所有落到玩家身上的伤害都由存活敌人承受。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/复仇女神·伤害转敌方")]
    public sealed class GoddessOfVengeanceTorsoEffectData : CardEffectData
    {
        /// <summary>开启本回合的伤害转移，下一回合开始自动恢复。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.DamageToEnemyArmed = true;
    }
}