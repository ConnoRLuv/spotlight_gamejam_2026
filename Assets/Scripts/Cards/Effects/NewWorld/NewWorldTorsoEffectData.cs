using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>新世界·躯干：本回合视为无手牌上限限制，普通与义体手牌均可超出容量。</summary>
    /// <remarks>
    /// 容量开关保存在 CardDeck/CardZone，出牌、抽牌与奖励补牌共用同一判定；回合开始由
    /// TurnController 统一复位，额外回合开启的新回合不再继承上一回合的无上限状态。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/新世界·无手牌上限")]
    public sealed class NewWorldTorsoEffectData : CardEffectData
    {
        /// <summary>解除本回合普通与义体手牌的容量上限，回合开始自动恢复。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.SetNoHandLimit(true);
    }
}
