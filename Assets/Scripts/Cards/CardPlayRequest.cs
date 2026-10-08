using System;

namespace SpotlightGameJam
{
    /// <summary>一次完整出牌指令；选牌使用实例 ID，避免同配置的三张牌互相混淆。</summary>
    public sealed class CardPlayRequest
    {
        public CardInstance Card { get; }
        public CombatantState Target { get; }
        public Guid? ChoiceId { get; }
        /// <summary>封装出牌实例、目标和可选的候选牌身份，供统一校验使用。</summary>
        public CardPlayRequest(CardInstance card, CombatantState target = null, Guid? choiceId = null)
        { Card = card; Target = target; ChoiceId = choiceId; }
    }
}
