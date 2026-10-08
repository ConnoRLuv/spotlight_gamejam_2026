using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 管理单个玩家回合中的普通行动点与临时行动点。
    /// </summary>
    /// <remarks>
    /// 由 TurnController 刷新额度，CardPlayService 支付费用；普通 AP 与临时 AP 分开保存。
    /// </remarks>
    public sealed class ActionPointPool
    {
        public int Normal { get; private set; }
        public int Temporary { get; private set; }
        public int Total => Normal + Temporary;
        /// <summary>
        /// 将普通 AP 重置为当回合额度，同时清除上一回合临时 AP。
        /// </summary>
        public void BeginTurn(int allowance)
        {
            if (allowance < 0) throw new ArgumentOutOfRangeException(nameof(allowance));
            // 这里刷新的是当回合额度，不把上一回合剩余普通 AP 累加进来。
            Normal = allowance; Temporary = 0;
        }
        /// <summary>
        /// 增加只在本回合有效的 AP；非法负数和整数溢出会抛异常。
        /// </summary>
        public void AddTemporary(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Temporary = checked(Temporary + amount);
        }
        /// <summary>
        /// 只检查费用是否合法且余额充足，不修改任何资源。
        /// </summary>
        public bool CanSpend(int amount) => amount >= 0 && amount <= Total;
        /// <summary>
        /// 尝试支付 AP，优先使用临时点数；余额不足或费用为负时返回 false。
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (!CanSpend(amount)) return false;
            // 临时 AP 会过期，优先花掉它，剩余费用再从普通 AP 支付。
            int temporarySpent = Math.Min(Temporary, amount);
            Temporary -= temporarySpent; Normal -= amount - temporarySpent;
            return true;
        }
        /// <summary>
        /// 清除临时 AP；普通剩余 AP 将在下回合开始时重置。
        /// </summary>
        public void EndTurn() => Temporary = 0;
    }
}
