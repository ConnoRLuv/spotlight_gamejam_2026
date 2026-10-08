using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>
    /// 管理一套卡牌的抽牌堆、手牌和弃牌堆，以及抽牌、弃牌和回收流程。
    /// </summary>
    /// <remarks>
    /// 普通牌与义体牌各创建一个实例；依赖 CardZone 维护归属与容量，依赖 RandomUtility 洗牌。
    /// </remarks>
    public sealed class CardDeck
    {
        private readonly RandomUtility random;
        public CardZone Hand { get; }
        public CardZone DrawPile { get; } = new CardZone(int.MaxValue);
        public CardZone DiscardPile { get; } = new CardZone(int.MaxValue);
        /// <summary>创建独立的抽牌、手牌和弃牌区域，并关联洗牌随机源。</summary>
        public CardDeck(int capacity, IEnumerable<CardInstance> initial, RandomUtility random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            Hand = new CardZone(capacity);
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            foreach (var card in initial)
            {
                if (card == null || card.Data.locksInHand || !DrawPile.TryAdd(card))
                    throw new ArgumentException("初始牌堆含无效、锁定或重复卡牌实例。",nameof(initial));
            }
            // 初始排列也参与随机化；调用方可固定种子复现整场抽牌顺序。
            DrawPile.Shuffle(random);
        }
        /// <summary>
        /// 尝试抽取指定数量的牌，返回实际加入手牌的数量；溢出牌进入弃牌堆。
        /// </summary>
        public int Draw(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            // 仅回收本次抽牌前的弃牌，避免溢出的牌在同一指令里被反复抽取。
            var recyclable = DiscardPile.Cards.ToArray();
            int attempts = Math.Min(count, DrawPile.Count + recyclable.Length);
            int added = 0;
            for (int i = 0; i < attempts; i++)
            {
                if (DrawPile.Count == 0)
                {
                    foreach (var discarded in recyclable)
                        if (DiscardPile.TryRemove(discarded)) DrawPile.TryAdd(discarded);
                    DrawPile.Shuffle(random);
                    // 本次快照只回收一次，新产生的溢出弃牌留到后续抽牌指令。
                    recyclable = Array.Empty<CardInstance>();
                }
                if (DrawPile.Count == 0) break;
                // 列表尾部作为牌堆顶，移出后再加入手牌；归属由 CardZone 维护。
                var card = DrawPile.Cards[DrawPile.Count - 1];
                DrawPile.TryRemove(card);
                if (Hand.TryAdd(card)) added++;
                // 抽到但放不下仍算一次抽牌尝试，防止满手时无限重抽。
                else DiscardPile.TryAdd(card);
            }
            return added;
        }
        /// <summary>
        /// 将本套手牌中的非锁定牌移至弃牌堆；不接受外部牌或幻痛。
        /// </summary>
        public bool Discard(CardInstance card)
        {
            if (!Hand.TryRemove(card)) return false;
            if (card.ExpiresAtTurnEnd) return true;
            return DiscardPile.TryAdd(card);
        }
        /// <summary>只读查看当前抽牌堆；不回收弃牌、不消耗随机数。</summary>
        public IReadOnlyList<CardInstance> Peek(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return DrawPile.Cards.Reverse().Take(count).ToArray();
        }
        /// <summary>从当前抽牌堆取出特定实例；容量不足或来源错误时不移动。</summary>
        public bool TakeFromDrawPile(CardInstance card)
        {
            if (Hand.Count >= Hand.Capacity || !DrawPile.Contains(card) || !DrawPile.TryRemove(card)) return false;
            return Hand.TryAdd(card);
        }
        /// <summary>清理本回合生成牌，包含因其他操作进入弃牌堆的临时实例。</summary>
        internal void RemoveTurnCards()
        {
            foreach (var zone in new[] { Hand, DrawPile, DiscardPile })
                foreach (var card in zone.Cards.Where(card => card.ExpiresAtTurnEnd).ToArray()) zone.TryRemove(card);
        }
        /// <summary>
        /// 只执行牌区层面的出牌移出，不结算效果或费用；完整出牌请调用 BattleController。
        /// </summary>
        public bool TryPlay(CardInstance card) => Discard(card);
        /// <summary>
        /// 确保该锁定配置在手牌中恰好出现一次；满手时替换首张非锁定牌。
        /// </summary>
        public bool EnsureLockedCard(CardData data)
        {
            if (!data || !data.locksInHand) throw new ArgumentException("必须传入锁定牌配置。",nameof(data));
            // 用配置引用保证同一张锁定特殊牌不重复进入手牌。
            if (Hand.Cards.Any(card => card.Data == data)) return true;
            if (Hand.Count == Hand.Capacity)
            {
                // 当前模板采用确定性的自动替换，不在结算中打开弃牌选择界面。
                var replacement = Hand.Cards.FirstOrDefault(card => !card.Data.locksInHand);
                if (replacement == null || !Discard(replacement)) return false;
            }
            return Hand.TryAdd(new CardInstance(data));
        }
        internal IEnumerable<CardInstance> AllCards =>
            DrawPile.Cards.Concat(Hand.Cards).Concat(DiscardPile.Cards);
    }
}
