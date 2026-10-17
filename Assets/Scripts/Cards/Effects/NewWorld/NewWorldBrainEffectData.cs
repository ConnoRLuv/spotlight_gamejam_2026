using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>新世界·脑机：观看普通牌堆与义体牌堆，并按抽取顺序重排两套抽牌堆。</summary>
    /// <remarks>
    /// 重排结果经 CardPlayRequest.Reorder 提交；服务在支付前只读校验，支付后统一应用，
    /// 与选牌交互共用同一原子结算入口，避免结算期间牌区被其他回调改变。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/新世界·观看并调整抽牌堆")]
    public sealed class NewWorldBrainEffectData : CardEffectData
    {
        public override bool RequiresReorder => true;
        public override string ReorderTitle => "观看并调整抽牌堆";
        public override string ReorderPrompt => "按抽取顺序排列两套抽牌堆（下一张在前）；取消不消耗资源。";

        /// <summary>返回参与重排的全部卡牌快照：普通抽牌堆在前、义体抽牌堆在后，均按堆顶优先。</summary>
        public override IReadOnlyList<CardInstance> GetReorderCards(BattleContext context, CardInstance card) =>
            context.Ordinary.GetDrawPileOrder().Concat(context.Cybernetic.GetDrawPileOrder()).ToArray();

        /// <summary>拒绝缺少重排请求的直接执行路径；脑机必须提交完整顺序。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) => false;

        /// <summary>只读校验：顺序必须恰好覆盖两套抽牌堆、无重复且普通牌与义体牌各自完整。</summary>
        public override bool ValidateReorder(BattleContext context, CardInstance card, IReadOnlyList<CardInstance> order)
        {
            if (context == null || order == null) return false;
            var ordinary = context.Ordinary.DrawPile;
            var cybernetic = context.Cybernetic.DrawPile;
            if (order.Count != ordinary.Count + cybernetic.Count) return false;
            var seen = new HashSet<CardInstance>();
            int ordinaryCount = 0;
            foreach (var value in order)
            {
                if (value == null || !seen.Add(value)) return false;
                if (ordinary.Contains(value)) ordinaryCount++;
                else if (!cybernetic.Contains(value)) return false;
            }
            return ordinaryCount == ordinary.Count;
        }

        /// <summary>统一结算入口：校验请求中的顺序并分别重排两套抽牌堆，不改变牌区外的卡牌。</summary>
        public override bool CanExecuteRequest(BattleContext context, CardPlayRequest request) =>
            request != null && ValidateReorder(context, request.Card, request.Reorder);

        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            throw new InvalidOperationException("脑机需要包含牌堆顺序的出牌请求。");

        public override void ExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            if (request == null || !ValidateReorder(context, request.Card, request.Reorder))
                throw new InvalidOperationException("牌堆顺序无效或已变化。");
            var ordinaryOrder = request.Reorder.Where(card => context.Ordinary.DrawPile.Contains(card)).ToArray();
            var cyberneticOrder = request.Reorder.Where(card => context.Cybernetic.DrawPile.Contains(card)).ToArray();
            if (!context.Ordinary.ReorderDrawPile(ordinaryOrder) || !context.Cybernetic.ReorderDrawPile(cyberneticOrder))
                throw new InvalidOperationException("牌堆重排失败。");
        }
    }
}
