using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存单个战斗角色的生命、理智和护盾等可变状态。
    /// </summary>
    /// <remarks>
    /// 通过方法限制数值范围并发出 Changed 事件；玩家实例由 RunState 跨战斗复用，敌人实例按遭遇创建。
    /// </remarks>
    public sealed class CombatantState
    {
        public string Id { get; }
        public int MaxHealth { get; }
        public int MaxSanity { get; }
        public int Health { get; private set; }
        public int Sanity { get; private set; }
        public int Shield { get; private set; }
        public bool IsAlive => Health > 0;
        // 属性级事件在修改时即时发出；整体战斗 UI 也可订阅 Context.Changed。
        public event Action<CombatantState> Changed;
        /// <summary>创建角色状态，设置生命与理智上限及初始资源。</summary>
        public CombatantState(string id, int maxHealth, int maxSanity = 0)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("角色 ID 不能为空。", nameof(id));
            if (maxHealth <= 0 || maxSanity < 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            Id = id; MaxHealth = maxHealth; MaxSanity = maxSanity;
            Health = maxHealth; Sanity = maxSanity;
        }
        /// <summary>
        /// 只读检查理智费用：存活且理智大于零时允许透支；零费用不要求拥有理智。
        /// </summary>
        public bool CanSpendSanity(int amount) => amount >= 0 && IsAlive && (amount == 0 || Sanity > 0);
        /// <summary>
        /// 支付理智，允许不足时扣至零，透支不扣生命或护盾；零理智拒绝正费用。
        /// 幻痛加入及满手时的随机弃牌由战斗牌区处理。
        /// </summary>
        public bool TrySpendSanity(int amount)
        {
            if (!CanSpendSanity(amount)) return false;
            int sanitySpent = Math.Min(Sanity, amount);
            Sanity -= sanitySpent;
            Changed?.Invoke(this); return true;
        }
        /// <summary>恢复理智但不超过上限；供后续商店和地图事件调用，零恢复量不触发变化。</summary>
        public void RestoreSanity(int amount)
        {
            RequireNonNegative(amount);
            if (!IsAlive || amount == 0 || Sanity == MaxSanity) return;
            Sanity += Math.Min(amount, MaxSanity - Sanity);
            Changed?.Invoke(this);
        }
        /// <summary>
        /// 恢复生命但不超过最大值；已死亡角色不会被此基础治疗复活。
        /// </summary>
        public void Heal(int amount)
        {
            RequireNonNegative(amount);
            // 基础恢复效果不复活角色，死亡状态由战斗结果保持。
            if (!IsAlive) return;
            Health += Math.Min(amount, MaxHealth - Health); Changed?.Invoke(this);
        }
        /// <summary>
        /// 为存活角色叠加护盾；持续时间由回合系统控制。
        /// </summary>
        public void AddShield(int amount)
        {
            RequireNonNegative(amount);
            if (!IsAlive) return;
            Shield = checked(Shield + amount); Changed?.Invoke(this);
        }
        /// <summary>
        /// 先消耗护盾，再扣生命，返回实际失去的生命值。
        /// </summary>
        public int ReceiveDamage(int amount) => ReceiveDamage(amount, false);
        /// <summary>护盾优先；启用保护时剩余伤害先消耗理智，不足部分才扣生命。</summary>
        internal int ReceiveDamage(int amount, bool convertToSanity)
        {
            RequireNonNegative(amount);
            if (!IsAlive) return 0;
            // 护盾只抵扣本次伤害；返回值统计实际生命损失，不包含被吸收部分。
            int absorbed = Math.Min(Shield, amount); Shield -= absorbed;
            int remaining = amount - absorbed;
            if (convertToSanity)
            {
                int sanityLost = Math.Min(Sanity, remaining);
                Sanity -= sanityLost; remaining -= sanityLost;
            }
            int lost = Math.Min(Health, remaining);
            Health -= lost; Changed?.Invoke(this); return lost;
        }
        /// <summary>
        /// 清除护盾并通知属性变化，通常由回合开始流程调用。
        /// </summary>
        public void ClearShield() { Shield = 0; Changed?.Invoke(this); }
        /// <summary>拒绝负数资源参数，避免非法输入改变角色状态。</summary>
        private static void RequireNonNegative(int amount)
        { if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount)); }
    }
}
