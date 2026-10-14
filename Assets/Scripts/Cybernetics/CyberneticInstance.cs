using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存一张持久义体牌的唯一身份、关联配置和同种牌共用的耐久。
    /// </summary>
    /// <remarks>
    /// 卡牌身份保持独立，同一探索中同种义体共享耐久池；部位次数由 CyberneticUsage 统一记录。
    /// </remarks>
    public sealed class CyberneticInstance
    {
        public CyberneticData Data { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public CardData Card { get; }
        public bool IsInitialCard { get; internal set; }
        public event Action<CyberneticInstance> Changed;
        private readonly SharedDurability durability;
        public int Durability => durability.Value;
        /// <summary>创建一种义体的首张持久牌，建立初始耐久池，不修改共享配置。</summary>
        public CyberneticInstance(CyberneticData data, CardData card = null) : this(data, card, null) { }
        /// <summary>创建同种义体的另一张牌，保留独立身份并沿用来源的耐久池。</summary>
        internal CyberneticInstance(CyberneticInstance source, CardData card) : this(source?.Data, card, source?.durability) { }
        /// <summary>统一校验来源和关联牌，只有同一探索的显式复制才共享耐久池。</summary>
        private CyberneticInstance(CyberneticData data, CardData card, SharedDurability shared)
        {
            Data = data ? data : throw new ArgumentNullException(nameof(data));
            if (data.maxDurability <= 0) throw new ArgumentOutOfRangeException(nameof(data));
            Card = card ? card : data.cards != null && data.cards.Length > 0 ? data.cards[0] : null;
            if (card && (data.cards == null || Array.IndexOf(data.cards, card) < 0))
                throw new ArgumentException("该牌不属于指定义体配置。", nameof(card));
            durability = shared ?? new SharedDurability(data.maxDurability);
            durability.Changed += OnDurabilityChanged;
        }
        /// <summary>
        /// 成功使用时扣除一点耐久；耐久为零视为报废并拒绝继续消耗。
        /// </summary>
        public bool TryConsumeDurability()
        {
            if (Durability <= 0) return false;
            durability.Value--; durability.NotifyChanged(); return true;
        }
        /// <summary>
        /// 增加当前耐久，最多恢复到配置上限；不修改部位使用次数。
        /// </summary>
        public void Repair(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            // 修复同种义体的公共耐久，其他种类及其他探索不受影响，也不重置次数。
            int restored = Math.Min(amount, Data.maxDurability - Durability);
            if (restored == 0) return;
            durability.Value += restored; durability.NotifyChanged();
        }
        /// <summary>共享耐久发生变化时通知该牌的订阅者，所有同种卡面读取同一个新值。</summary>
        private void OnDurabilityChanged() => Changed?.Invoke(this);
        /// <summary>仅属于当前探索的一种义体的可变耐久池，不持有 Unity 配置或场景引用。</summary>
        private sealed class SharedDurability
        {
            public int Value;
            public event Action Changed;
            /// <summary>首张牌建立耐久池，其余同种牌显式复用。</summary>
            public SharedDurability(int initial) { Value = initial; }
            /// <summary>通知共享本池的持久牌，牌区移动不会调用此入口。</summary>
            public void NotifyChanged() => Changed?.Invoke();
        }
    }
}
