using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotlightGameJam
{
    /// <summary>
    /// 复仇女神套装的运行时状态：按穿戴齐全与否维护生效集合，并在玩家每次受到伤害后应用全局效果。
    /// </summary>
    /// <remarks>
    /// 每个探索创建一个实例（保存在 RunState），每场战斗通过 BattleContext 构造/释放完成
    /// 对 PlayerDamaged 事件的订阅；装备可能随战斗变化，因此每次承伤前按当前装备重算生效集合。
    /// </remarks>
    public sealed class GoddessOfVengeanceSetBonusState
    {
        private readonly CyberneticLoadout loadout;
        private GoddessOfVengeanceSetBonusData[] definitions = Array.Empty<GoddessOfVengeanceSetBonusData>();
        private readonly List<GoddessOfVengeanceSetBonusData> active = new List<GoddessOfVengeanceSetBonusData>();
        private BattleContext context;
        public IReadOnlyList<GoddessOfVengeanceSetBonusData> Active => active.ToArray();
        public event Action Changed;

        /// <summary>保存装备归属查询入口；耐久、抽弃牌和维修不会触发套装以外的操作。</summary>
        public GoddessOfVengeanceSetBonusState(CyberneticLoadout loadout)
        { this.loadout = loadout ?? throw new ArgumentNullException(nameof(loadout)); }

        /// <summary>配置可生效套装并清空旧集合；携带完整四部位不会自动触发，仍需穿戴与承伤。</summary>
        public void Configure(IEnumerable<GoddessOfVengeanceSetBonusData> bonuses)
        {
            var candidates = bonuses?.Where(value => value).Distinct().ToArray() ?? Array.Empty<GoddessOfVengeanceSetBonusData>();
            foreach (var bonus in candidates)
                if (bonus.Validate().Count > 0) throw new ArgumentException("复仇女神套装配置无效。");
            definitions = candidates;
            Recompute();
        }

        /// <summary>绑定当前战斗的“玩家受到伤害”事件，并立即按装备重算。</summary>
        public void Attach(BattleContext battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            Detach();
            context = battle;
            context.PlayerDamaged += OnPlayerDamaged;
            Recompute();
        }

        /// <summary>解除对指定或当前战斗的订阅；战斗释放时调用，避免旧战斗继续触发补牌。</summary>
        public void Detach(BattleContext battle = null)
        {
            if (battle != null && context != battle) return;
            if (context != null) context.PlayerDamaged -= OnPlayerDamaged;
            context = null;
        }

        /// <summary>按穿戴齐全与否刷新生效集合；集合不变时不发通知，避免每次承伤重复刷新。</summary>
        private void Recompute()
        {
            var next = definitions.Where(bonus => bonus.IsComplete(loadout)).ToArray();
            if (active.SequenceEqual(next)) return;
            active.Clear(); active.AddRange(next);
            Changed?.Invoke();
        }

        /// <summary>战斗仍未结束时，把本次承伤分发给当前生效的套装。</summary>
        private void OnPlayerDamaged()
        {
            if (context == null || context.IsFinished) return;
            Recompute();
            if (active.Count == 0) return;
            foreach (var bonus in active) Apply(bonus, context);
        }

        private void Apply(GoddessOfVengeanceSetBonusData bonus, BattleContext battle)
        {
            if (bonus.trigger != GoddessOfVengeanceSetTrigger.OnPlayerDamaged) return;
            if (bonus.effect == GoddessOfVengeanceSetEffect.DrawBasicFromDrawPile) DrawBasicFromDrawPile(battle);
        }

        /// <summary>手牌未满时，从普通抽牌堆取最近可抽到的一张基本牌加入手牌；无基本牌或手牌满则不做任何事。</summary>
        private void DrawBasicFromDrawPile(BattleContext battle)
        {
            if (!battle.Ordinary.CanAddToHand()) return;
            CardInstance chosen = null;
            // 列表尾部为牌堆顶；从顶部向下找第一张基本牌，符合“从抽牌堆获得一张基本牌”的直觉。
            for (int i = battle.Ordinary.DrawPile.Cards.Count - 1; i >= 0; i--)
            {
                var card = battle.Ordinary.DrawPile.Cards[i];
                if (card.Data.category == CardCategory.Basic) { chosen = card; break; }
            }
            if (chosen == null) return;
            battle.Ordinary.TakeFromDrawPile(chosen);
        }
    }
}