using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace SpotlightGameJam
{
    /// <summary>
    /// 表示一个有容量限制的牌容器，统一维护实例归属和锁定牌移出限制。
    /// </summary>
    /// <remarks>
    /// CardDeck 使用多个 CardZone 组成完整牌区；对外提供只读视图，所有增删操作通过 TryAdd/TryRemove。
    /// </remarks>
    public sealed class CardZone
    {
        private readonly List<CardInstance> cards = new List<CardInstance>();
        private readonly ReadOnlyCollection<CardInstance> view;
        // 只读实时视图仍会随牌区更新变化；结算前需要固定列表时应自行复制。
        public IReadOnlyList<CardInstance> Cards => view;
        public int Count => cards.Count;
        public int Capacity { get; }
        /// <summary>创建具有容量限制的牌区，保存卡牌实例而非共享配置。</summary>
        public CardZone(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity; view = cards.AsReadOnly();
        }
        /// <summary>
        /// 按运行时归属判断是否在本区，避免仅按相同配置误认同一张牌。
        /// </summary>
        public bool Contains(CardInstance card) => card != null && card.Zone == this;
        /// <summary>
        /// 仅接纳无归属的独立实例；同一张牌不能重复加入或同时存在于两个牌区。
        /// </summary>
        public bool TryAdd(CardInstance card)
        {
            // 已有归属既包括本区重复添加，也包括尚未从另一区释放的实例。
            if (card == null || card.Zone != null || Count >= Capacity) return false;
            cards.Add(card); card.Zone = this; return true;
        }
        /// <summary>
        /// 只移除属于本区的非锁定牌，成功后释放归属供另一区接纳。
        /// </summary>
        public bool TryRemove(CardInstance card)
        {
            // 普通移牌、弃牌和出牌都经此约束；恢复理智清除幻痛走单独的内部规则入口。
            if (!Contains(card) || card.Data.locksInHand) return false;
            cards.Remove(card); card.Zone = null; return true;
        }
        /// <summary>仅供规则系统在恢复理智时清除锁定牌，不对 UI 或普通移牌流程开放。</summary>
        internal bool RemoveLockedCard(CardInstance card)
        {
            if (!Contains(card) || !card.Data.locksInHand) return false;
            cards.Remove(card); card.Zone = null; return true;
        }
        /// <summary>使用传入的随机源打乱牌区顺序。</summary>
        internal void Shuffle(RandomUtility random) => CollectionUtility.Shuffle(cards,random);
    }
}
