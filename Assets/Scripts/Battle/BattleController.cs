using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>
    /// 提供开始战斗、出牌和结束回合的统一入口。
    /// </summary>
    /// <remarks>
    /// 场景或 UI 通过此类提交指令；实际出牌规则交给 CardPlayService，回合推进交给 TurnController。
    /// </remarks>
    public sealed class BattleController
    {
        private readonly CardPlayService cards = new CardPlayService();
        private readonly TurnController turns = new TurnController();
        private readonly int enemyAttackDamage;
        public BattleContext Context { get; }
        public int TurnNumber => turns.TurnNumber;
        public string LastError { get; private set; }
        public event Action<BattleState> StateChanged
        { add => Context.StateChanged += value; remove => Context.StateChanged -= value; }
        public event Action<CardInstance,CardPlayResult> CardPlayed;
        /// <summary>创建战斗控制器并关联回合流程与出牌服务。</summary>
        public BattleController(BattleContext context, int enemyAttackDamage = 6)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (enemyAttackDamage < 0) throw new ArgumentOutOfRangeException(nameof(enemyAttackDamage));
            this.enemyAttackDamage = enemyAttackDamage;
        }
        /// <summary>
        /// 校验配置并初始化第一回合；失败不扣资源，配置错误保存在 LastError。
        /// </summary>
        public bool StartBattle()
        {
            if (Context.IsResolving || Context.State != BattleState.NotStarted) return false;
            // 在抽牌、生成奖励和刷新回合前检查配置，错误开局不会产生部分状态。
            var errors = Context.Validate();
            if (errors.Count > 0) { LastError = string.Join("\n",errors); return false; }
            // 开局也会触发事件；直到首回合初始化完毕前都不接受嵌套指令。
            LastError = null; Context.IsResolving = true;
            try
            {
                Context.CheckOutcome();
                if (!Context.IsFinished)
                {
                    Context.Run.Buffs.BeginBattle();
                    // 奖励持有配置引用，开战才创建实例；超出容量的奖励进入弃牌堆。
                    foreach (var data in Context.Run.InitialHandRewards)
                    {
                        var card = new CardInstance(data) { IsInitialHandReward = true };
                        if (!Context.Ordinary.Hand.TryAdd(card)) Context.Ordinary.DiscardPile.TryAdd(card);
                    }
                    turns.BeginTurn(Context);
                }
                Context.NotifyChanged(); return true;
            }
            // 无论正常返回还是扩展效果抛异常，都释放结算锁，避免永久锁死入口。
            finally { Context.IsResolving = false; }
        }
        /// <summary>
        /// 转交出牌指令并通知 CardPlayed；单敌目标需显式传入，其他目标按卡牌规则处理。
        /// </summary>
        public CardPlayResult TryPlay(CardInstance card, CombatantState target = null)
        {
            var result = cards.TryPlay(Context,card,target);
            CardPlayed?.Invoke(card,result); return result;
        }
        /// <summary>提交包含候选选择的完整指令，复用出牌服务的原子校验与通知。</summary>
        public CardPlayResult TryPlayRequest(CardPlayRequest request)
        {
            var result = cards.TryPlayRequest(Context, request);
            CardPlayed?.Invoke(request?.Card, result); return result;
        }
        /// <summary>为界面返回候选快照；确认时仍须提交请求并重新验证。</summary>
        public IReadOnlyList<CardInstance> GetCyberneticChoices(CardInstance card)
            => GetCardChoices(card);
        /// <summary>统一返回脑机取牌或躯干改造候选，确认时仍按实例重新校验。</summary>
        public IReadOnlyList<CardInstance> GetCardChoices(CardInstance card)
        {
            if (Context.IsFinished || Context.IsResolving || Context.State != BattleState.PlayerAction ||
                card == null || !Context.Cybernetic.Hand.Contains(card) || card.Data.Validate().Count > 0)
                return Array.Empty<CardInstance>();
            var effect = card.Data.effects.FirstOrDefault(value => value.RequiresChoice);
            return effect == null ? Array.Empty<CardInstance>() : effect.GetChoices(Context, card);
        }
        /// <summary>返回当前选牌效果的操作说明，不把具体义体规则写入界面。</summary>
        public string GetCardChoicePrompt(CardInstance card) =>
            card?.Data.effects.FirstOrDefault(effect => effect.RequiresChoice)?.ChoicePrompt ?? "请选择卡牌。";
        /// <summary>返回牌效定义的选牌标题，共用面板不固定为义体牌。</summary>
        public string GetCardChoiceTitle(CardInstance card) =>
            card?.Data.effects.FirstOrDefault(effect => effect.RequiresChoice)?.ChoiceTitle ?? "选择一张卡牌";
        /// <summary>按需支付 1 AP 生成一张仅存在本回合的免费攻击；失败不修改资源。</summary>
        public CardPlayResult TryConvertApToAttack()
        {
            if (Context.IsResolving) return new CardPlayResult(CardPlayFailure.Busy,"正在结算。");
            if (Context.IsFinished || Context.State != BattleState.PlayerAction || !Context.Player.IsAlive)
                return new CardPlayResult(CardPlayFailure.InvalidPhase,"当前不能转换。");
            if (!Context.TurnEffects.ConversionEnabled) return new CardPlayResult(CardPlayFailure.EffectUnavailable,"本回合尚未启用脉冲发射器。");
            if (Context.TurnEffects.ConversionAttack.Validate().Count > 0)
                return new CardPlayResult(CardPlayFailure.InvalidConfiguration,"转换攻击配置已失效。");
            if (!Context.ActionPoints.CanSpend(1)) return new CardPlayResult(CardPlayFailure.InsufficientAp,"需要 1 AP。");
            if (!Context.Ordinary.CanAddToHand()) return new CardPlayResult(CardPlayFailure.HandFull,"普通手牌已满。");
            Context.IsResolving = true;
            try
            {
                var instance = new CardInstance(Context.TurnEffects.ConversionAttack, expiresAtTurnEnd: true) { TemporaryApCost = 0 };
                Context.Ordinary.Hand.TryAdd(instance);
                Context.ActionPoints.TrySpend(1);
                Context.NotifyChanged(); return CardPlayResult.Played;
            }
            finally { Context.IsResolving = false; }
        }
        /// <summary>
        /// 仅在玩家行动阶段且未结算时推进回合；否则返回 false。
        /// </summary>
        public bool EndTurn()
        {
            if (Context.IsResolving || Context.State != BattleState.PlayerAction || Context.IsFinished) return false;
            Context.IsResolving = true;
            try { turns.EndTurn(Context,enemyAttackDamage); Context.NotifyChanged(); return true; }
            finally { Context.IsResolving = false; }
        }
    }
}
