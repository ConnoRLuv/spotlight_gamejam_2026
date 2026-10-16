using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>普罗米修斯篡改者：下一个玩家回合优先从两套弃牌堆抽牌。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/下回合弃牌优先")]
    public sealed class DiscardDrawNextTurnEffectData : CardEffectData
    {
        /// <summary>登记下一回合的抽牌来源替换。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.DrawFromDiscardNextTurn = true;
    }
}
