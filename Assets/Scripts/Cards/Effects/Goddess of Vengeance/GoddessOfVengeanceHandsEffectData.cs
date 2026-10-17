using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神·手部：观看普通与义体弃牌堆，并回收一张攻击牌。</summary>
    /// <remarks>
    /// 候选来自两套弃牌堆中的攻击牌（当前即普通弃牌堆里的基本攻击牌）；
    /// 选中后移入普通手牌。取消不消耗资源。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/复仇女神·弃牌堆回收攻击牌")]
    public sealed class GoddessOfVengeanceHandsEffectData : CardEffectData
    {
        public CardData attack;

        public override bool RequiresChoice => true;
        public override string ChoiceTitle => "回收一张攻击牌";
        public override string ChoicePrompt => "从普通与义体弃牌堆中选择一张攻击牌加入手牌；取消不消耗资源。";

        /// <summary>回收目标必须是 1 AP、6 伤害的基本攻击牌。</summary>
        public override string Validate()
        {
            if (!attack || attack.category != CardCategory.Basic || attack.locksInHand)
                return "回收需要有效的基本攻击牌。";
            if (attack.targetType != CardTargetType.SingleEnemy || attack.effects == null ||
                attack.effects.Length != 1 || !(attack.effects[0] is DamageEffectData damage) ||
                damage.amount != 6 || attack.cost != 1 || attack.sanityCost != 0)
                return "回收攻击必须是 1 AP、6 伤害的基本攻击牌。";
            if (attack.Validate().Count > 0) return "回收攻击配置无效。";
            return null;
        }

        /// <summary>返回两套弃牌堆中的攻击牌候选快照；不移动卡牌、不消耗随机数。</summary>
        public override IReadOnlyList<CardInstance> GetChoices(BattleContext context, CardInstance card) =>
            context.Ordinary.DiscardPile.Cards
                .Concat(context.Cybernetic.DiscardPile.Cards)
                .Where(value => value.Data == attack).ToArray();

        /// <summary>拒绝缺少选牌信息的直接执行路径。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) => false;

        /// <summary>确认选择仍在两套弃牌堆中，且普通手牌容得下回收牌。</summary>
        public override bool CanExecuteRequest(BattleContext context, CardPlayRequest request) =>
            request.ChoiceId.HasValue && GetChoices(context, request.Card).Any(value => value.Id == request.ChoiceId.Value) &&
            (context.Ordinary.Hand.IgnoreCapacity || context.Ordinary.Hand.Count < context.Ordinary.Hand.Capacity);

        /// <summary>拒绝缺少候选牌身份的执行方式。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            throw new InvalidOperationException("回收攻击需要包含选牌实例 ID 的出牌请求。");

        /// <summary>把选中的攻击牌从弃牌堆移入普通手牌；支付后手牌意外已满则放回弃牌堆。</summary>
        public override void ExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            var choice = request.Choice;
            if (choice == null) return;
            CardDeck source = context.Ordinary.DiscardPile.Contains(choice) ? context.Ordinary :
                context.Cybernetic.DiscardPile.Contains(choice) ? context.Cybernetic : null;
            if (source == null || !source.DiscardPile.TryRemove(choice)) return;
            // 支付可能触发幻痛填满普通手牌；此时回收牌退回弃牌堆，不破坏已完成的支付。
            if (!context.Ordinary.Hand.TryAdd(choice)) source.DiscardPile.TryAdd(choice);
        }
    }
}