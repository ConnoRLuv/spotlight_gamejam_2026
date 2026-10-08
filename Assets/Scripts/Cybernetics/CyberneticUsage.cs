using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 记录一场战斗中每个义体部位的总使用次数和本回合使用标记。
    /// </summary>
    /// <remarks>
    /// 按部位计数，避免更换装备绕过限制；每场战斗新建，BeginTurn 仅清除回合标记，不清除总次数。
    /// </remarks>
    public sealed class CyberneticUsage
    {
        private readonly int limit;
        // 按槽位而非装备实例计数，因此更换同部位装备不能获得额外使用次数。
        private readonly int[] uses = new int[4];
        // 每回合重置此数组，累计 uses 在整个战斗期间保留。
        private readonly bool[] usedThisTurn = new bool[4];
        /// <summary>初始化各部位的回合及战斗使用记录。</summary>
        public CyberneticUsage(int limit = 3)
        { if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit)); this.limit = limit; }
        /// <summary>
        /// 同时检查本回合是否使用过，以及本战斗是否达到次数上限。
        /// </summary>
        public bool CanUse(CyberneticSlot slot)
        { int i = Index(slot); return !usedThisTurn[i] && uses[i] < limit; }
        /// <summary>
        /// 在成功出牌时登记一次使用；调用前必须已通过 CanUse 校验。
        /// </summary>
        public void RecordUse(CyberneticSlot slot)
        {
            if (!CanUse(slot)) throw new InvalidOperationException("该部位当前不可使用。");
            int i = Index(slot); uses[i]++; usedThisTurn[i] = true;
        }
        /// <summary>
        /// 仅清除本回合标记，保留本战斗累计使用次数。
        /// </summary>
        public void BeginTurn() => Array.Clear(usedThisTurn,0,usedThisTurn.Length);
        /// <summary>
        /// 查询部位在本战斗中的已用次数，供界面显示。
        /// </summary>
        public int GetUses(CyberneticSlot slot) => uses[Index(slot)];
        /// <summary>校验义体部位并转换为使用记录数组的索引。</summary>
        private static int Index(CyberneticSlot slot)
        {
            int index = (int)slot;
            if (index < 0 || index >= 4) throw new ArgumentOutOfRangeException(nameof(slot));
            return index;
        }
    }
}
