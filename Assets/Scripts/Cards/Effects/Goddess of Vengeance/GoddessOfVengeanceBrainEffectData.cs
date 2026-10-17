using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神·脑机：把普通手牌中所有非攻击牌转化为攻击牌。</summary>
    /// <remarks>
    /// “转化”在结算时立即发生：逐张移除非攻击手牌并替换为配置的攻击牌实例，
    /// 手牌数量不变，因此不受手牌容量限制；锁定牌（如幻痛）不会被移出。
    /// 转化只作用于普通手牌，不影响抽牌堆、弃牌堆或义体牌区。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/复仇女神·非攻击牌转攻击")]
    public sealed class GoddessOfVengeanceBrainEffectData : CardEffectData
    {
        public CardData attack;

        /// <summary>转换目标必须是 1 AP、6 伤害的基本攻击牌，与 AP 转攻击共用同一张配置。</summary>
        public override string Validate()
        {
            if (!attack || attack.category != CardCategory.Basic || attack.locksInHand)
                return "转换需要有效的基本攻击牌。";
            if (attack.targetType != CardTargetType.SingleEnemy || attack.effects == null ||
                attack.effects.Length != 1 || !(attack.effects[0] is DamageEffectData damage) ||
                damage.amount != 6 || attack.cost != 1 || attack.sanityCost != 0)
                return "转换攻击必须是 1 AP、6 伤害的基本攻击牌。";
            if (attack.Validate().Count > 0) return "转换攻击配置无效。";
            return null;
        }

        /// <summary>把所有非攻击、非锁定的普通手牌替换为攻击牌实例。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        {
            var hand = context.Ordinary.Hand;
            // 固定快照，避免边遍历边增删影响结果；锁定牌由 TryRemove 自动跳过。
            var toConvert = hand.Cards.Where(value => value.Data != attack && !value.Data.locksInHand).ToArray();
            foreach (var value in toConvert)
            {
                if (!hand.TryRemove(value)) continue;
                hand.TryAdd(new CardInstance(attack));
            }
        }
    }
}