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
        private readonly bool discardOverflow;
        public CardZone Hand { get; }
        public CardZone DrawPile { get; } = new CardZone(int.MaxValue);
        public CardZone DiscardPile { get; } = new CardZone(int.MaxValue);
        /// <summary>创建独立牌区；默认满手停止抽牌，义体牌区可显式启用溢出弃牌。</summary>
        public CardDeck(int capacity, IEnumerable<CardInstance> initial, RandomUtility random, bool discardOverflow = false)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.discardOverflow = discardOverflow;
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
        /// 按堆顶顺序抽取指定数量，不按卡牌种类配额；堆空时重洗本套弃牌继续抽取。
        /// 返回实际加入手牌的新抽牌数量；普通满手停止，义体可启用溢出弃牌，两堆皆空则停止。
        /// </summary>
        public int Draw(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (!discardOverflow && Hand.Count >= Hand.Capacity) return 0;
            // 仅回收本次抽牌前的弃牌，避免溢出的牌在同一指令里被反复抽取。
            var recyclable = DiscardPile.Cards.ToArray();
            int attempts = Math.Min(count, DrawPile.Count + recyclable.Length);
            int added = 0;
            for (int i = 0; i < attempts; i++)
            {
                // 普通手牌满时连堆顶都不移出，也不触发洗牌或产生额外弃牌。
                if (!discardOverflow && Hand.Count >= Hand.Capacity) break;
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
            return card.ExpiresAtTurnEnd || DiscardPile.TryAdd(card);
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
            if (!DrawPile.Contains(card)) return false;
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
        /// 确保该锁定配置在手牌中恰好出现一次；满手时随机弃一张非义体、非锁定牌再补入。
        /// </summary>
        public bool EnsureLockedCard(CardData data)
        {
            if (!data || !data.locksInHand) throw new ArgumentException("必须传入锁定牌配置。",nameof(data));
            // 用配置引用保证同一张锁定特殊牌不重复进入手牌。
            if (Hand.Cards.Any(card => card.Data == data)) return true;
            if (Hand.Count >= Hand.Capacity)
            {
                // 使用本场随机源保证可复现；只从普通手牌候选中选，不影响义体牌区。
                var candidates = Hand.Cards.Where(card => !card.Data.locksInHand &&
                    card.Data.category != CardCategory.Cybernetic).ToArray();
                if (candidates.Length == 0) return false;
                Discard(random.Choose(candidates));
            }
            return Hand.TryAdd(new CardInstance(data));
        }
        /// <summary>恢复理智时移除本套牌区中的幻痛；这是规则清除入口，不属于出牌或弃牌。</summary>
        internal void RemoveLockedCard(CardData data)
        {
            foreach (var zone in new[] { Hand, DrawPile, DiscardPile })
                foreach (var card in zone.Cards.Where(card => card.Data == data).ToArray())
                    zone.RemoveLockedCard(card);
        }
        internal IEnumerable<CardInstance> AllCards =>
            DrawPile.Cards.Concat(Hand.Cards).Concat(DiscardPile.Cards);
    }
}
