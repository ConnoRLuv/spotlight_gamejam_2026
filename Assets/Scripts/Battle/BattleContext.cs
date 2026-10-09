using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存一场战斗的角色、牌区、随机源、阶段及规则依赖。
    /// </summary>
    /// <remarks>
    /// 复用 RunState 中的玩家和义体，单独新建 AP 与部位使用计数；由 BattleController 组织结算并通知 UI。
    /// </remarks>
    public sealed class BattleContext : IDisposable
    {
        public RunState Run { get; }
        public BattleRules Rules { get; }
        // 玩家由冒险持有，因此下一场战斗继续使用同一个生命/理智状态。
        public CombatantState Player => Run.Player;
        public IReadOnlyList<CombatantState> Enemies { get; }
        public CardDeck Ordinary { get; }
        public CardDeck Cybernetic { get; }
        public CardData PhantomPain { get; }
        public RandomUtility Random { get; }
        public ActionPointPool ActionPoints { get; } = new ActionPointPool();
        public CyberneticUsage Usage { get; }
        public BattleTurnEffects TurnEffects { get; } = new BattleTurnEffects();
        // 已成功使用的卡牌由本场战斗持有；不会因切换 UI 或新回合丢失，也不跨战斗复用。
        private readonly CardInstance[] usedCyberneticCards = new CardInstance[4];
        private int observedSanity;
        /// <summary>获取对应部位本场最后成功使用的义体牌；未使用或来源装备已替换时为空。</summary>
        public CardInstance GetUsedCyberneticCard(CyberneticSlot slot)
        {
            if (!Enum.IsDefined(typeof(CyberneticSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            var card = usedCyberneticCards[(int)slot];
            return card != null && Run.Loadout.Contains(card.Source) ? card : null;
        }
        /// <summary>仅在成功支付义体牌费用之后记录，失败或等待选牌不能改变装备栏。</summary>
        internal void RecordUsedCyberneticCard(CardInstance card)
        { usedCyberneticCards[(int)card.Source.Data.slot] = card; }
        public BattleState State { get; private set; } = BattleState.NotStarted;
        public bool IsFinished => State == BattleState.Victory || State == BattleState.Defeat;
        // 支付和状态通知可能触发外部回调；结算锁阻止回调嵌套出牌或结束回合。
        internal bool IsResolving { get; set; }
        public event Action<BattleContext> Changed;
        public event Action<BattleState> StateChanged;
        /// <summary>建立单场战斗上下文，关联冒险状态、牌区、敌人和随机数源。</summary>
        public BattleContext(RunState run, BattleRules rules, IReadOnlyList<CombatantState> enemies,
            CardDeck ordinary, CardDeck cybernetic, CardData phantomPain, RandomUtility random)
        {
            if (run == null || !rules || enemies == null || ordinary == null ||
                cybernetic == null || !phantomPain || random == null) throw new ArgumentNullException();
            if (enemies.Any(e => e == null || e == run.Player) ||
                enemies.Distinct().Count() != enemies.Count)
                throw new ArgumentException("敌人列表含无效或重复角色。",nameof(enemies));
            // 复制敌人列表结构，防止调用方增删列表改变本场战斗参与者。
            Run = run; Rules = rules; Enemies = Array.AsReadOnly(enemies.ToArray());
            Ordinary = ordinary; Cybernetic = cybernetic; PhantomPain = phantomPain; Random = random;
            // 每个 Context 新建次数记录；只让义体实例的耐久跨战斗保留。
            Usage = new CyberneticUsage(rules.UsesPerSlot);
            observedSanity = Player.Sanity;
            Player.Changed += OnPlayerChanged;
        }
        /// <summary>
        /// 切换阶段并通知订阅者；同一阶段不会重复触发事件。
        /// </summary>
        internal void SetState(BattleState state)
        {
            if (State == state) return;
            State = state;
            if (IsFinished) { TurnEffects.Clear(); Ordinary.RemoveTurnCards(); Cybernetic.RemoveTurnCards(); }
            StateChanged?.Invoke(state);
        }
        /// <summary>
        /// 在一次完整操作结束时通知 UI 刷新；订阅者须自行解除订阅。
        /// </summary>
        internal void NotifyChanged() => Changed?.Invoke(this);
        public bool HasPhantomPain => Ordinary.Hand.Cards.Any(c => c.Data == PhantomPain);
        /// <summary>
        /// 理智归零登记持久幻痛并立即入手，满手随机弃普通牌；恢复为正数则移除当前幻痛。
        /// </summary>
        public void EnsurePhantomPain()
        {
            if (Player.Sanity == 0)
            {
                Run.AddPhantomPain(PhantomPain);
                if (!Ordinary.EnsureLockedCard(Run.PhantomPain))
                    throw new InvalidOperationException("普通手牌没有可弃置的非锁定牌，无法补入幻痛。");
            }
            else Ordinary.RemoveLockedCard(PhantomPain);
        }
        /// <summary>同步理智变化引发的幻痛增删；完整结算期间由控制器统一通知 UI。</summary>
        private void OnPlayerChanged(CombatantState player)
        {
            // 同一资源事件也涵盖生命和护盾；只有理智实际变化才同步幻痛。
            if (observedSanity == player.Sanity) return;
            observedSanity = player.Sanity;
            // 开战校验通过前不改变牌区，保留配置失败时的无副作用约定。
            if (State == BattleState.NotStarted) return;
            EnsurePhantomPain();
            if (!IsResolving) NotifyChanged();
        }
        /// <summary>解除持久玩家事件订阅，避免上一场战斗继续接收新战斗或地图的资源变化。</summary>
        public void Dispose() => Player.Changed -= OnPlayerChanged;
        /// <summary>
        /// 即时判断胜负；同时失去生存条件时优先判定玩家失败。
        /// </summary>
        public void CheckOutcome()
        {
            // 先检查玩家，保证双方均死亡时不会误判为胜利。
            if (!Player.IsAlive) SetState(BattleState.Defeat);
            else if (!Enemies.Any(e => e.IsAlive)) SetState(BattleState.Victory);
        }
        /// <summary>
        /// 汇总开战所需的规则、牌堆、装备和奖励配置错误，不修改战斗状态。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(Rules.Validate());
            if (!PhantomPain.locksInHand || PhantomPain.category != CardCategory.Special)
                errors.Add("需要配置锁定的幻痛特殊牌。");
            errors.AddRange(PhantomPain.Validate());
            foreach (var card in Ordinary.AllCards)
            {
                errors.AddRange(card.Data.Validate());
                if (card.Data.category != CardCategory.Basic && card.Data.category != CardCategory.Function)
                    errors.Add("普通牌堆只能包含基本牌和功能牌。");
            }
            foreach (var instance in Run.Loadout.Equipped)
            {
                errors.AddRange(instance.Data.Validate());
            }
            foreach (var card in Cybernetic.AllCards)
            {
                errors.AddRange(card.Data.Validate());
                if (card.Data.category != CardCategory.Cybernetic || !IsUsableSource(card))
                    errors.Add("义体牌必须属于已装备且有耐久的来源义体。");
            }
            foreach (var reward in Run.InitialHandRewards) errors.AddRange(reward.Validate());
            return errors;
        }
        /// <summary>
        /// 检查义体卡是否来自当前装备的真实实例，且来源有耐久并关联该卡配置。
        /// </summary>
        internal bool IsUsableSource(CardInstance card) =>
            card.Source != null && Run.Loadout.Contains(card.Source) && card.Source.Durability > 0 &&
            card.Source.Data.cards != null && card.Source.Data.cards.Contains(card.Data);
    }
}
