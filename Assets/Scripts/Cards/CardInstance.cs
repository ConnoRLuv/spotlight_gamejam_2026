using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 表示战斗中的一张独立卡牌，保存实例身份、来源义体及临时费用。
    /// </summary>
    /// <remarks>
    /// 多张牌可以共享同一 CardData；临时 AP 费用写入实例，不能改共享配置。Zone 用于保证同一实例只属于一个牌区。
    /// </remarks>
    public sealed class CardInstance
    {
        public Guid Id { get; } = Guid.NewGuid();
        public CardData Data { get; }
        public CyberneticInstance Source { get; }
        // 仅牌区在成功增删时维护；外部不能用赋值伪造卡牌归属。
        internal CardZone Zone { get; set; }
        // null 表示使用静态费用；临时覆盖只影响这一张实例。
        public int? TemporaryApCost { get; set; }
        public int EffectiveApCost => TemporaryApCost ?? Data.cost;
        // 转换生成的牌不会进入弃牌堆，也不会在玩家行动结束后继续存在。
        public bool ExpiresAtTurnEnd { get; }
        /// <summary>创建具有独立身份的卡牌实例，记录配置及可选的来源义体。</summary>
        public CardInstance(CardData data, CyberneticInstance source = null, bool expiresAtTurnEnd = false)
        { Data = data ?? throw new ArgumentNullException(nameof(data)); Source = source; ExpiresAtTurnEnd = expiresAtTurnEnd; }
    }
}
