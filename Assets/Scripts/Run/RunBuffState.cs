using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>按同一玩家回合成功打出的义体记录触发组合，并按配置清理 buff。</summary>
    public sealed class RunBuffState
    {
        private readonly CyberneticLoadout inventory;
        private readonly HashSet<CyberneticData> playedThisTurn = new HashSet<CyberneticData>();
        private RunBuffData[] definitions = Array.Empty<RunBuffData>();
        private RunBuffData[] active = Array.Empty<RunBuffData>();
        private bool playerTurnActive;
        public IReadOnlyList<RunBuffData> Active => Array.AsReadOnly(active);
        public event Action Changed;
        /// <summary>保存牌归属校验入口，耐久、抽弃牌和维修均不会直接触发组合。</summary>
        public RunBuffState(CyberneticLoadout inventory)
        { this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory)); }
        /// <summary>配置可触发组合并清空旧记录；携带完整四部位不会自动激活。</summary>
        public void Configure(IEnumerable<RunBuffData> buffs)
        {
            var candidates = buffs?.Distinct().ToArray() ?? Array.Empty<RunBuffData>();
            if (candidates.Any(data => !data || data.Validate().Count > 0)) throw new ArgumentException("全局 Buff 配置无效。");
            definitions = candidates; playedThisTurn.Clear(); playerTurnActive = false;
            Publish(Array.Empty<RunBuffData>());
        }
        /// <summary>查询某种费用修饰是否存在，多条同效果组合不重复扣费。</summary>
        public bool Has(RunBuffEffect effect) => active.Any(buff => buff.effect == effect);
        /// <summary>新战斗只保留配置为探索持续的 buff，不继承上场成功出牌记录。</summary>
        internal void BeginBattle() => EndBattle();
        /// <summary>开始独立的玩家回合，额外行动回合也从零累计四部位。</summary>
        internal void BeginTurn()
        {
            playedThisTurn.Clear(); playerTurnActive = true;
            Publish(active.Where(buff => buff.duration != RunBuffDuration.PlayerTurn).ToArray());
        }
        /// <summary>结束玩家行动即清除回合 buff，不延续到敌人阶段。</summary>
        internal void EndTurn()
        {
            playedThisTurn.Clear(); playerTurnActive = false;
            Publish(active.Where(buff => buff.duration != RunBuffDuration.PlayerTurn).ToArray());
        }
        /// <summary>胜负或退出战斗时释放短期 buff，仅探索持续效果跨场景保留。</summary>
        internal void EndBattle()
        {
            playedThisTurn.Clear(); playerTurnActive = false;
            Publish(active.Where(buff => buff.duration == RunBuffDuration.Adventure).ToArray());
        }
        /// <summary>完整成功结算后记录真实义体出牌；改造牌也可参与，失败和取消不调用。</summary>
        internal void RecordPlayed(CardInstance card)
        {
            if (!playerTurnActive || card == null || card.Data.category != CardCategory.Cybernetic || !inventory.Owns(card.Source)) return;
            playedThisTurn.Add(card.Source.Data);
            // 第四张牌先正常支付和结算，新 buff 只影响之后的操作；耐久归零不撤销成功记录。
            Publish(definitions.Where(buff => active.Contains(buff) || buff.MatchesConfigurations(playedThisTurn)).ToArray());
        }
        /// <summary>仅在激活列表改变时通知，避免每次出牌重复刷新费用界面。</summary>
        private void Publish(RunBuffData[] next)
        {
            if (active.SequenceEqual(next)) return;
            active = next; Changed?.Invoke();
        }
    }
}
