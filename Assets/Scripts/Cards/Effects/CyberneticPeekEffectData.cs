using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>军用战术协调矩阵：只读查看义体堆顶候选，确认后取出指定实例。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/义体堆选牌")]
    public sealed class CyberneticPeekEffectData : CardEffectData
    {
        [Min(1)] public int count = 3;
        public override bool RequiresChoice => true;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate() => count <= 0 ? "查看数量必须大于零。" : null;

        /// <summary>尾部为堆顶；返回快照，不洗牌、不扣费，也不把牌预先移出。</summary>
        public IReadOnlyList<CardInstance> GetChoices(BattleContext context) => context.Cybernetic.Peek(count);

        /// <summary>拒绝不含选牌信息的直接执行路径；脑机必须通过选牌请求使用。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) => false;
        /// <summary>确认选择仍在堆顶候选中，且移出使用牌后手牌容量允许取牌。</summary>
        public override bool CanExecuteRequest(BattleContext context, CardPlayRequest request) =>
            request.ChoiceId.HasValue && GetChoices(context).Any(card => card.Id == request.ChoiceId.Value) &&
            context.Cybernetic.Hand.Count - (context.Cybernetic.Hand.Contains(request.Card) ? 1 : 0) < context.Cybernetic.Hand.Capacity;

        /// <summary>拒绝缺少候选牌身份的执行方式，提示调用方提交完整请求。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            throw new InvalidOperationException("脑机需要包含选牌实例 ID 的出牌请求。");

        /// <summary>将指定候选实例移入手牌，保持其他候选的顺序不变。</summary>
        public override void ExecuteRequest(BattleContext context, CardPlayRequest request)
        {
            var choice = GetChoices(context).Single(card => card.Id == request.ChoiceId.Value);
            // 请求已在支付前完整校验；只移走所选牌，其他候选的顺序保持不变。
            if (!context.Cybernetic.TakeFromDrawPile(choice)) throw new InvalidOperationException("候选牌移动失败。");
        }
    }
}
