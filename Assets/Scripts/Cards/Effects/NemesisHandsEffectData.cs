using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>复仇女神手部：浏览两套弃牌堆，确认后回收普通弃牌堆的一张攻击牌。</summary>
    [CreateAssetMenu(menuName = "Spotlight/复仇女神/弃牌回收攻击")]
    public sealed class NemesisHandsEffectData : CardEffectData
    {
        public override bool RequiresChoice => true;
        public override bool ChoiceUsesDiscardPile => true;
        public override string ChoiceTitle => "选择一张攻击牌";
        /// <summary>返回可回收的真实攻击牌实例，浏览其他弃牌不意味着能够回收它们。</summary>
        public override IReadOnlyList<CardInstance> GetChoices(BattleContext context, CardInstance card) =>
            context.Ordinary.DiscardPile.Cards.Where(value => !value.Data.locksInHand &&
                value.Data.category == CardCategory.Basic && value.Data.cardType == CardType.Attack).Reverse().ToArray();
        /// <summary>提前计入支付到零后新增的幻痛，避免支付后手牌已满而无法兑现回收。</summary>
        public override bool CanExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            if (!request.ChoiceId.HasValue || !GetChoices(context, request.Card).Any(value => value.Id == request.ChoiceId.Value)) return false;
            bool createsPhantom = !context.UsesHealthFor(request.Card) && request.Card.Data.sanityCost >= context.Player.Sanity &&
                !context.HasPhantomPain;
            return context.Ordinary.Hand.IgnoreCapacity || context.Ordinary.Hand.Count + (createsPhantom ? 1 : 0) < context.Ordinary.Hand.Capacity;
        }
        /// <summary>无选择请求不能直接回收，确保失败和取消均不产生移动副作用。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            throw new InvalidOperationException("回收需要提交弃牌中的攻击牌身份。");
        /// <summary>支付成功后只移动所选实例，不重新生成牌、不更改其他弃牌的顺序。</summary>
        public override void ExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            var chosen = request.Choice;
            if (chosen == null || !context.Ordinary.CanAddToHand() || !context.Ordinary.DiscardPile.TryRemove(chosen))
                throw new InvalidOperationException("已校验的回收目标失效。");
            if (!context.Ordinary.Hand.TryAdd(chosen)) throw new InvalidOperationException("回收攻击牌失败。");
        }
    }
}
