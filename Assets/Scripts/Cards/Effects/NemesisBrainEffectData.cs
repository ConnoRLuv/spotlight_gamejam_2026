using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神脑机：将当前普通手牌中的非攻击牌临时转为标准攻击，幻痛保持原样。</summary>
    [CreateAssetMenu(menuName = "Spotlight/复仇女神/脑机转换手牌")]
    public sealed class NemesisBrainEffectData : CardEffectData
    {
        public CardData attackCard;
        /// <summary>要求有效的普通攻击模板，不允许用义体或锁定牌作为转换结果。</summary>
        public override string Validate() => !attackCard || attackCard.category != CardCategory.Basic ||
            attackCard.cardType != CardType.Attack || attackCard.locksInHand || attackCard.Validate().Count > 0
                ? "需要有效的基本攻击牌模板。" : null;
        /// <summary>仅修改当前手牌实例；后抽到的牌不转换，已转牌在回合结束或战斗结束恢复。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        {
            foreach (var value in context.Ordinary.Hand.Cards.Where(value => !value.Data.locksInHand &&
                value.Data.cardType != CardType.Attack).ToArray()) value.TransformForTurn(attackCard);
        }
    }
}
