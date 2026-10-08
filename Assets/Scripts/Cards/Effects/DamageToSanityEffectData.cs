using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>军用生命维持器：启用一次保护，实际承伤统一由 DamageResolver 处理。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/伤害转理智")]
    public sealed class DamageToSanityEffectData : CardEffectData
    {
        /// <summary>启用一次承伤保护，使后续伤害在护盾后优先消耗理智。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.DamageToSanityArmed = true;
    }
}
