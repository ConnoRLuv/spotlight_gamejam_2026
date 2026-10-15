using System;

namespace SpotlightGameJam
{
    /// <summary>一次完整出牌指令；选牌使用实例 ID，避免同配置的三张牌互相混淆。</summary>
    public sealed class CardPlayRequest
    {
        public CardInstance Card { get; }
        public CombatantState Target { get; }
        public Guid? ChoiceId { get; }
        // 支付前解析的真实候选；仅结算服务构造，防止请求方伪造手牌引用。
        internal CardInstance Choice { get; }
        /// <summary>封装出牌实例、目标和可选的候选牌身份，供统一校验使用。</summary>
        public CardPlayRequest(CardInstance card, CombatantState target = null, Guid? choiceId = null)
        { Card = card; Target = target; ChoiceId = choiceId; }
        /// <summary>由出牌服务携带已验证的候选，供支付前预留和后续执行使用。</summary>
        internal CardPlayRequest(CardInstance card, CombatantState target, Guid? choiceId, CardInstance choice)
        { Card = card; Target = target; ChoiceId = choiceId; Choice = choice; }
    }
}
