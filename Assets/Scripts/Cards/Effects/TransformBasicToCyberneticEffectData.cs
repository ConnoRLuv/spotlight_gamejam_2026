using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>普罗米修斯改造驱动：选择基本手牌，随机改造为未拥有的同阵营义体牌。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/基本牌改造义体")]
    public sealed class TransformBasicToCyberneticEffectData : CardEffectData
    {
        public string factionId = "tian-qiong-legion";
        public CyberneticData[] candidates = new CyberneticData[0];
        public override bool RequiresChoice => true;
        public override string ChoiceTitle => "选择一张基础牌";
        public override bool ReserveChoiceBeforePayment => true;
        public override string ChoicePrompt => "选择一张基本手牌永久改造为未拥有的天穹军团义体牌；取消不扣费。";
        /// <summary>校验候选配置引用；不能递归校验候选牌效，因为候选可包含本义体自身。</summary>
        public override string Validate() => string.IsNullOrWhiteSpace(factionId) ? "改造阵营不能为空。" :
            candidates == null || candidates.Length == 0 || candidates.Any(data => !data) ? "改造需要配置非空义体候选池。" : null;
        /// <summary>排除已持有（包括报废）、其他阵营及无效配置；同 ID 仅保留一个候选。</summary>
        private IReadOnlyList<CyberneticData> AvailableCybernetics(BattleContext context) =>
            (candidates ?? Array.Empty<CyberneticData>()).Where(data => data && data.factionId == factionId &&
                !context.Run.Loadout.OwnsConfiguration(data) && data.Validate().Count == 0)
                .GroupBy(data => data.cyberneticId).Select(group => group.First()).ToArray();
        /// <summary>玩家只选择非临时基本手牌；无可获得的新义体时不开放选择。</summary>
        public override IReadOnlyList<CardInstance> GetChoices(BattleContext context, CardInstance card) =>
            AvailableCybernetics(context).Count == 0 ? Array.Empty<CardInstance>() :
            context.Ordinary.Hand.Cards.Where(value => value.Data.category == CardCategory.Basic &&
                !value.Data.locksInHand && !value.ExpiresAtTurnEnd).ToArray();
        /// <summary>确认目标仍在手牌且新牌放得下；不支付、不抽随机数、不改变牌区。</summary>
        public override bool CanExecuteRequest(BattleContext context, CardPlayRequest request) =>
            request.ChoiceId.HasValue && GetChoices(context, request.Card).Any(card => card.Id == request.ChoiceId.Value) &&
            (context.Cybernetic.Hand.IgnoreCapacity ||
             context.Cybernetic.Hand.Count - (context.Cybernetic.Hand.Contains(request.Card) ? 1 : 0) < context.Cybernetic.Hand.Capacity);
        /// <summary>改造必须带有玩家选择，拒绝绕过完整请求的直接执行方式。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            throw new InvalidOperationException("改造需要选择基本手牌。");
        /// <summary>支付后随机获得一件未拥有来源的一张关联牌，并记录原基本牌的跨战斗移除。</summary>
        public override void ExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            if (request.Choice == null) throw new InvalidOperationException("缺少预留的改造目标。");
            var data = context.Random.Choose(AvailableCybernetics(context));
            // 候选以义体配置为单位，当前每个配置的关联牌相同；仅生成第一种关联牌的一张实例。
            var acquired = context.Run.Loadout.AcquireCard(data, data.cards[0]);
            context.Run.RecordOrdinaryTransformation(request.Choice);
            if (!context.Cybernetic.Hand.TryAdd(acquired)) throw new InvalidOperationException("改造新牌无法加入手牌。");
        }
    }
}
