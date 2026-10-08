using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>军用战术推进器：预订一个完整玩家回合，由结束回合入口消费，避免结算中重入。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/额外玩家回合")]
    public sealed class ExtraTurnEffectData : CardEffectData
    {
        /// <summary>登记额外玩家回合，供回合控制器在结束行动时跳过敌人阶段。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.ExtraTurns = checked(context.TurnEffects.ExtraTurns + 1);
    }
}
