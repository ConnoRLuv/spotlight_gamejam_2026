using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>普罗米修斯持久变量：本战斗后续义体费用改付生命并恢复等量理智。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/义体血量支付")]
    public sealed class HealthPaymentEffectData : CardEffectData
    {
        /// <summary>在本牌正常支付理智之后启用战斗持续的生命支付模式。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.CyberneticsPayHealth = true;
    }
}
