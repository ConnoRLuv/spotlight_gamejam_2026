using System;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>
    /// 执行出牌规则：先校验全部前置条件，再支付资源、执行效果并弃牌。
    /// </summary>
    /// <remarks>
    /// 依赖 BattleContext；不依赖界面。校验失败时保留资源与牌区，结算期间通过 IsResolving 阻止事件回调重入。
    /// </remarks>
    public sealed class CardPlayService
    {
        /// <summary>
        /// 校验阶段、归属、配置、目标、费用和义体限制，全部通过后才执行结算。
        /// 效果前置校验必须无副作用；成功牌执行完毕进入对应弃牌堆，失败牌保留原位置。
        /// </summary>
        public CardPlayResult TryPlay(BattleContext context, CardInstance card, CombatantState target = null)
            => TryPlayRequest(context, new CardPlayRequest(card, target));

        /// <summary>处理包含选牌的请求；所有验证完成后才统一支付和执行。</summary>
        public CardPlayResult TryPlayRequest(BattleContext context, CardPlayRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var card = request?.Card;
            var target = request?.Target;
            // 构造失败结果，统一携带失败原因与面向玩家的提示；不修改战斗状态。
            CardPlayResult Reject(CardPlayFailure failure, string message) => new CardPlayResult(failure,message);
            // 阶段和归属检查优先，避免外部卡牌或事件回调触发嵌套结算。
            if (context.IsResolving) return Reject(CardPlayFailure.Busy,"正在结算。");
            if (context.State != BattleState.PlayerAction || context.IsFinished || !context.Player.IsAlive)
                return Reject(CardPlayFailure.InvalidPhase,"当前不能出牌。");
            var deck = card != null && context.Ordinary.Hand.Contains(card) ? context.Ordinary :
                card != null && context.Cybernetic.Hand.Contains(card) ? context.Cybernetic : null;
            if (deck == null) return Reject(CardPlayFailure.CardNotInHand,"该卡不在本场战斗的手牌区。");
            if (card.Data.locksInHand) return Reject(CardPlayFailure.LockedCard,"该牌不能离开手牌。");
            var errors = card.Data.Validate();
            if (errors.Count > 0 || card.EffectiveApCost < 0)
                return Reject(CardPlayFailure.InvalidConfiguration,string.Join("\n",errors));
            bool isCybernetic = card.Data.category == CardCategory.Cybernetic;
            if ((isCybernetic && deck != context.Cybernetic) ||
                (!isCybernetic && deck != context.Ordinary))
                return Reject(CardPlayFailure.InvalidConfiguration,"卡牌所在牌区与类别不匹配。");
            // 这里校验玩家提交的名义目标；幻痛的真实目标稍后由伤害入口选择。
            switch (card.Data.targetType)
            {
                case CardTargetType.Self:
                    if (target != null && target != context.Player) return Reject(CardPlayFailure.InvalidTarget,"需要选择自身。");
                    target = context.Player; break;
                case CardTargetType.SingleEnemy:
                    if (target == null || !context.Enemies.Contains(target) || !target.IsAlive)
                        return Reject(CardPlayFailure.InvalidTarget,"需要选择存活敌人。");
                    break;
                case CardTargetType.AllEnemies:
                    if (!context.Enemies.Any(e => e.IsAlive) || target != null)
                        return Reject(CardPlayFailure.InvalidTarget,"需要存在存活敌人，且无需指定单个目标。");
                    break;
                case CardTargetType.None:
                    if (target != null) return Reject(CardPlayFailure.InvalidTarget,"该牌无需目标。");
                    break;
            }
            // 以下只读校验必须全部通过后再修改资源，失败路径不扣费、不扣耐久。
            if (card.Data.requiresEquippedCybernetic && !context.Run.Loadout.Equipped.Any())
                return Reject(CardPlayFailure.UnavailableCybernetic,"需要先装载义体才能使用该牌。");
            if (!context.ActionPoints.CanSpend(card.EffectiveApCost))
                return Reject(CardPlayFailure.InsufficientAp,"行动点不足。");
            if (context.Player.Sanity < card.Data.sanityCost)
                return Reject(CardPlayFailure.InsufficientSanity,"理智不足。");
            if (isCybernetic && (!context.IsUsableSource(card) || !context.Usage.CanUse(card.Source.Data.slot)))
                return Reject(CardPlayFailure.UnavailableCybernetic,"来源义体未装备、已报废或超过使用限制。");
            var resolvedRequest = new CardPlayRequest(card, target, request.ChoiceId);
            bool requiresChoice = card.Data.effects.Any(effect => effect.RequiresChoice);
            if (requiresChoice && !request.ChoiceId.HasValue) return Reject(CardPlayFailure.ChoiceRequired,"请从候选义体牌中选择一张。");
            if (!requiresChoice && request.ChoiceId.HasValue) return Reject(CardPlayFailure.InvalidChoice,"该牌不接受选牌参数。");
            if (requiresChoice && card.Data.effects.Any(effect => effect.RequiresChoice && !effect.CanExecuteRequest(context, resolvedRequest)))
                return Reject(CardPlayFailure.InvalidChoice,"候选牌已变化或当前无法取牌。");
            if (card.Data.effects.Any(effect => !effect.CanExecuteRequest(context,resolvedRequest)))
                return Reject(CardPlayFailure.InvalidTarget,"效果前置条件不满足。");
            int ap = card.EffectiveApCost, sanity = card.Data.sanityCost;
            // 固定本次效果顺序，避免执行过程中数组引用被替换影响当前结算。
            var effects = card.Data.effects.ToArray();
            context.IsResolving = true;
            try
            {
                // 先从手牌预留成功使用的牌，保证满手时幻痛不会把结算中的牌当作弃牌。
                deck.Hand.TryRemove(card);
                context.ActionPoints.TrySpend(ap); context.Player.TrySpendSanity(sanity);
                if (isCybernetic)
                {
                    context.Usage.RecordUse(card.Source.Data.slot); card.Source.TryConsumeDurability();
                    context.RecordUsedCyberneticCard(card);
                }
                // 理智支付到零时立即加入幻痛，同一张牌后续的伤害也受随机目标影响。
                context.EnsurePhantomPain();
                foreach (var effect in effects)
                {
                    // 致命伤害后不再执行回血、抽牌或加 AP 等剩余效果。
                    if (context.IsFinished) break;
                    effect.ExecuteRequest(context,resolvedRequest);
                    context.CheckOutcome();
                }
                // 执行期间暂不放入弃牌堆，防止本张牌的抽牌效果立即重抽自身。
                if (!card.ExpiresAtTurnEnd) deck.DiscardPile.TryAdd(card);
                context.NotifyChanged();
                return CardPlayResult.Played;
            }
            finally { context.IsResolving = false; }
        }
    }
}
