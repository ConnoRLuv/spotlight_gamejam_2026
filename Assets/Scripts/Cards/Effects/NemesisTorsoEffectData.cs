using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神躯干：敌方生命及理智伤害均转为攻击者的生命伤害，主动费用照常支付。</summary>
    [CreateAssetMenu(menuName = "Spotlight/复仇女神/本回合伤害转移")]
    public sealed class NemesisTorsoEffectData : CardEffectData
    {
        /// <summary>伤害由统一结算器重定向；效果保留至敌方行动结束，不修改角色或共享配置。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.RedirectEnemyDamage = true;
    }
}
