using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存一次冒险中跨战斗保留的玩家、装备、金币和初始手牌奖励。
    /// </summary>
    /// <remarks>
    /// 开始新冒险时创建；下一场战斗复用 Player 与 Loadout。奖励在每场开战时生成独立卡牌实例。
    /// </remarks>
    public sealed class RunState
    {
        private readonly List<CardData> rewards = new List<CardData>();
        private readonly ReadOnlyCollection<CardData> rewardView;
        public CombatantState Player { get; }
        public CyberneticLoadout Loadout { get; } = new CyberneticLoadout();
        public int Gold { get; private set; }
        // 保存奖励配置，不保存上一场的牌实例；每场开战会创建全新的奖励手牌。
        public IReadOnlyList<CardData> InitialHandRewards => rewardView;
        /// <summary>根据规则创建冒险状态，跨战斗保存角色属性与义体装备。</summary>
        public RunState(BattleRules rules)
        {
            if (!rules || rules.Validate().Count > 0) throw new ArgumentException("战斗规则配置不合法。");
            Player = new CombatantState("player",rules.MaxHealth,rules.MaxSanity);
            rewardView = rewards.AsReadOnly();
        }
        /// <summary>
        /// 加入非负金币奖励；不在此类中决定奖励数额。
        /// </summary>
        public void AddGold(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Gold = checked(Gold + amount);
        }
        /// <summary>
        /// 尝试消费金币；余额不足或费用为负时不修改金币。
        /// </summary>
        public bool TrySpendGold(int amount)
        { if (amount < 0 || amount > Gold) return false; Gold -= amount; return true; }
        /// <summary>
        /// 登记后续每场战斗的初始基本牌奖励；这里只记录配置，开战时生成实例。
        /// </summary>
        public void AddInitialHandReward(CardData card)
        {
            if (!card || card.category != CardCategory.Basic || card.locksInHand)
                throw new ArgumentException("初始手牌奖励必须是普通基本牌。",nameof(card));
            rewards.Add(card);
        }
    }
}
