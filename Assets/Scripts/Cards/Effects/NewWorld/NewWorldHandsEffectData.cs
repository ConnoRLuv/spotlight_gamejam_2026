using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>新世界·手部：本回合“过载”牌获得的行动点视为永久行动点（不超过 12）。</summary>
    /// <remarks>
    /// 只开启本回合的获得修饰，不直接发放行动点；实际发放仍由过载牌结算时经
    /// BattleContext.GainActionPoints 统一路由，永久点跨回合保留并封顶于战斗规则 MaxAp。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/新世界·过载永久行动点")]
    public sealed class NewWorldHandsEffectData : CardEffectData
    {
        /// <summary>开启本回合的过载永久化标记，回合开始清除。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.OverloadApPermanent = true;
    }
}
