using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存玩家实际装备的一件义体及其当前耐久。
    /// </summary>
    /// <remarks>
    /// 由 RunState 的 Loadout 跨战斗保留；每回合和每战斗使用次数由独立 CyberneticUsage 记录。
    /// </remarks>
    public sealed class CyberneticInstance
    {
        public CyberneticData Data { get; }
        public int Durability { get; private set; }
        /// <summary>创建独立义体状态，初始耐久取自配置上限。</summary>
        public CyberneticInstance(CyberneticData data)
        {
            Data = data ? data : throw new ArgumentNullException(nameof(data));
            if (data.maxDurability <= 0) throw new ArgumentOutOfRangeException(nameof(data));
            Durability = data.maxDurability;
        }
        /// <summary>
        /// 成功使用时扣除一点耐久；耐久为零视为报废并拒绝继续消耗。
        /// </summary>
        public bool TryConsumeDurability()
        { if (Durability <= 0) return false; Durability--; return true; }
        /// <summary>
        /// 增加当前耐久，最多恢复到配置上限；不修改部位使用次数。
        /// </summary>
        public void Repair(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            // 修复只影响这件装备实例，不改共享配置，也不重置战斗次数。
            Durability += Math.Min(amount, Data.maxDurability - Durability);
        }
    }
}
