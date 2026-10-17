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
        /// <summary>获得实际 AP 费用，本回合免费效果只作用于基本牌，不修改共享配置。</summary>
        public int GetEffectiveApCost(CardInstance card) =>
            card != null && card.Data.category == CardCategory.Basic &&
            (TurnEffects.FreeBasicCards || TurnEffects.FreeAttackCards && card.Data.cardType == CardType.Attack)
                ? 0 : card?.EffectiveApCost ?? 0;
        /// <summary>义体生命支付来自腿部效果，过载生命支付来自探索 buff；不修改共享配置。</summary>
        public bool UsesHealthFor(CardInstance card) =>
            card != null && (card.Data.category == CardCategory.Cybernetic && TurnEffects.CyberneticsPayHealth ||
                card.Data.cardId == "OverloadCard" && Run.Buffs.Has(RunBuffEffect.OverloadPaysHealth));
        /// <summary>只读检查费用余额，供 UI 和核心校验共用；不移动卡牌或扣资源。</summary>
        public bool CanPayCardCost(CardInstance card) => card != null && ActionPoints.CanSpend(GetEffectiveApCost(card)) &&
            (UsesHealthFor(card) ? Player.CanSpendHealth(card.Data.sanityCost) : Player.CanSpendSanity(card.Data.sanityCost));
        private const string OverloadCardId = "OverloadCard";
        // 已成功使用的卡牌由本场战斗持有；不会因切换 UI 或新回合丢失，也不跨战斗复用。
        private readonly CardInstance[] usedCyberneticCards = new CardInstance[4];
        private int observedSanity;
        private bool disposed;
        /// <summary>获取对应部位本场最后成功使用的牌；展示不代表当前装备，也不影响 buff。</summary>
        public CardInstance GetUsedCyberneticCard(CyberneticSlot slot)
        {
            if (!Enum.IsDefined(typeof(CyberneticSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            var card = usedCyberneticCards[(int)slot];
            return card != null && Run.Loadout.Owns(card.Source) ? card : null;
        }
        /// <summary>仅在成功支付义体牌费用之后记录，失败或等待选牌不能改变装备栏。</summary>
        internal void RecordUsedCyberneticCard(CardInstance card)
        { usedCyberneticCards[(int)card.Source.Data.slot] = card; }
        /// <summary>统一行动点获得入口：本回合“过载”牌在义体效果开启时写入永久行动点，否则写入临时点。</summary>
        public void GainActionPoints(CardInstance card, int amount)
        {
            if (card != null && TurnEffects.OverloadApPermanent && card.Data.cardId == OverloadCardId)
                ActionPoints.AddPermanent(amount, Rules.MaxAp);
            else ActionPoints.AddTemporary(amount);
        }
        /// <summary>临时解除或恢复两套手牌容量上限；本回合生效，回合开始由控制器重置。</summary>
        public void SetNoHandLimit(bool value) { Ordinary.SetIgnoreHandCapacity(value); Cybernetic.SetIgnoreHandCapacity(value); }
        public BattleState State { get; private set; } = BattleState.NotStarted;
        public bool IsFinished => State == BattleState.Victory || State == BattleState.Defeat;
        // 支付和状态通知可能触发外部回调；结算锁阻止回调嵌套出牌或结束回合。
        internal bool IsResolving { get; set; }
        public event Action<BattleContext> Changed;
        public event Action<BattleState> StateChanged;
        // 完整成功出牌后发布，供“每打出一张牌”类全局效果订阅；结算期间不触发重入。
        public event Action<CardInstance> CardPlayed;
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
            Run.Buffs.Changed += OnBuffChanged;
            Run.SetBonuses.Attach(this);
        }
        /// <summary>
        /// 切换阶段并通知订阅者；同一阶段不会重复触发事件。
        /// </summary>
        internal void SetState(BattleState state)
        {
            if (State == state) return;
            State = state;
            if (IsFinished) { Run.Buffs.EndBattle(); TurnEffects.Clear(); Ordinary.RemoveTurnCards(); Cybernetic.RemoveTurnCards(); }
            StateChanged?.Invoke(state);
        }
        /// <summary>
        /// 在一次完整操作结束时通知 UI 刷新；订阅者须自行解除订阅。
        /// </summary>
        internal void NotifyChanged() => Changed?.Invoke(this);
        /// <summary>完整出牌结算后广播本次打出的牌；此时该牌已进入对应弃牌堆。</summary>
        internal void NotifyCardPlayed(CardInstance card) => CardPlayed?.Invoke(card);
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
        public void Dispose()
        {
            // 会话和场景可能先后释放同一个旧上下文；重复清理不能影响新战斗的组合记录。
            if (disposed) return;
            disposed = true;
            Run.SetBonuses.Detach(this);
            Player.Changed -= OnPlayerChanged; Run.Buffs.Changed -= OnBuffChanged;
            if (State != BattleState.NotStarted && !IsFinished) Run.Buffs.EndBattle();
        }
        /// <summary>组合激活或过期后刷新费用，结算期间在操作结束时统一通知。</summary>
        private void OnBuffChanged() { if (!IsResolving) NotifyChanged(); }
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
            foreach (var instance in Run.Loadout.Owned)
            {
                errors.AddRange(instance.Data.Validate());
            }
            foreach (var card in Cybernetic.AllCards)
            {
                errors.AddRange(card.Data.Validate());
                if (card.Data.category != CardCategory.Cybernetic || !IsUsableSource(card))
                    errors.Add("义体牌必须属于已持有且有耐久的来源义体。");
            }
            foreach (var reward in Run.InitialHandRewards) errors.AddRange(reward.Validate());
            return errors;
        }
        /// <summary>
        /// 检查义体卡是否来自冒险持有的真实实例，且来源有耐久并关联该卡配置。
        /// </summary>
        internal bool IsUsableSource(CardInstance card) =>
            card.Source != null && Run.Loadout.Owns(card.Source) && card.Source.Durability > 0 &&
            card.Source.Data.cards != null && card.Source.Data.cards.Contains(card.Data);
    }
}
