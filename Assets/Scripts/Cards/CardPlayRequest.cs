using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>一次完整出牌请求，同时支持主分支选牌与新世界牌堆重排。</summary>
    public sealed class CardPlayRequest
    {
        public CardInstance Card { get; }
        public CombatantState Target { get; }
        public Guid? ChoiceId { get; }
        public IReadOnlyList<CardInstance> Reorder { get; }
        internal CardInstance Choice { get; }
        public CardPlayRequest(CardInstance card, CombatantState target = null, Guid? choiceId = null,
            IReadOnlyList<CardInstance> reorder = null)
            : this(card, target, choiceId, null, reorder) { }
        internal CardPlayRequest(CardInstance card, CombatantState target, Guid? choiceId, CardInstance choice,
            IReadOnlyList<CardInstance> reorder = null)
        {
            Card = card; Target = target; ChoiceId = choiceId; Choice = choice;
            Reorder = Array.AsReadOnly(reorder?.ToArray() ?? Array.Empty<CardInstance>());
        }
    }
}
