using System;
using System.Collections.Generic;

namespace SpotlightGameJam
{
    /// <summary>一次完整出牌指令；选牌使用实例 ID，重排携带牌区顺序，避免同配置牌混淆。</summary>
    public sealed class CardPlayRequest
    {
        public CardInstance Card { get; }
        public CombatantState Target { get; }
        public Guid? ChoiceId { get; }
        // 提交的抽牌堆重排顺序（下一张在前）；无重排交互时为空列表。
        public IReadOnlyList<CardInstance> Reorder { get; }
        /// <summary>封装出牌实例、目标、可选的候选牌身份与牌堆顺序，供统一校验使用。</summary>
        public CardPlayRequest(CardInstance card, CombatantState target = null, Guid? choiceId = null, IReadOnlyList<CardInstance> reorder = null)
        { Card = card; Target = target; ChoiceId = choiceId; Reorder = reorder ?? Array.Empty<CardInstance>(); }
    }
}
