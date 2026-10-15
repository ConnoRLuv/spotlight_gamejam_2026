using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotlightGameJam
{
    /// <summary>
    /// 新世界套装的运行时状态：按穿戴齐全与否维护生效集合，并在每次成功出牌后应用全局效果。
    /// </summary>
    /// <remarks>
    /// 每个探索创建一个实例（保存在 RunState），每场战斗通过 BattleContext 构造/释放完成
    /// 对 CardPlayed 事件的订阅；装备变化会即时重算，跨战斗保留判定结果。
    /// </remarks>
    public sealed class NewWorldSetBonusState
    {
        private readonly CyberneticLoadout loadout;
        private readonly List<NewWorldSetBonusData> definitions = new List<NewWorldSetBonusData>();
        private readonly List<NewWorldSetBonusData> active = new List<NewWorldSetBonusData>();
        private BattleContext context;
        private bool attached;
        public IReadOnlyList<NewWorldSetBonusData> Active => active.ToArray();
        public event Action Changed;

        /// <summary>保存装备归属查询入口；耐久、抽弃牌和维修不会触发套装重算以外的操作。</summary>
        public NewWorldSetBonusState(CyberneticLoadout loadout)
        { this.loadout = loadout ?? throw new ArgumentNullException(nameof(loadout)); }

        /// <summary>配置可生效套装并清空旧集合；携带完整四部位不会自动触发，仍需装备与出牌。</summary>
        public void Configure(IEnumerable<NewWorldSetBonusData> bonuses)
        {
            var candidates = bonuses?.Where(value => value).Distinct().ToArray() ?? Array.Empty<NewWorldSetBonusData>();
            foreach (var bonus in candidates)
                if (bonus.Validate().Count > 0) throw new ArgumentException("新世界套装配置无效。");
            definitions.Clear(); definitions.AddRange(candidates);
            Recompute();
        }

        /// <summary>绑定当前战斗的“成功出牌”事件，并立即按装备重算；重复绑定同一战斗只刷新。</summary>
        public void Attach(BattleContext battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (attached && context == battle) { Recompute(); return; }
            Detach();
            context = battle;
            context.CardPlayed += OnCardPlayed;
            loadout.Changed += OnLoadoutChanged;
            attached = true;
            Recompute();
        }

        /// <summary>解除对指定或当前战斗的订阅；战斗释放时调用，避免旧战斗继续触发回收。</summary>
        public void Detach(BattleContext battle = null)
        {
            if (battle != null && context != battle) return;
            if (context != null) context.CardPlayed -= OnCardPlayed;
            loadout.Changed -= OnLoadoutChanged;
            context = null; attached = false;
        }

        /// <summary>按穿戴齐全与否刷新生效集合；集合不变时不发通知，避免每次出牌重复刷新。</summary>
        private void Recompute()
        {
            var next = definitions.Where(bonus => bonus.IsComplete(loadout)).ToArray();
            if (active.SequenceEqual(next)) return;
            active.Clear(); active.AddRange(next);
            Changed?.Invoke();
        }

        private void OnLoadoutChanged() { Recompute(); }

        /// <summary>战斗仍未结束时，把每张成功打出的牌分发给当前生效的套装。</summary>
        private void OnCardPlayed(CardInstance card)
        {
            if (context == null || context.IsFinished || active.Count == 0) return;
            foreach (var bonus in active) Apply(bonus, context);
        }

        private void Apply(NewWorldSetBonusData bonus, BattleContext battle)
        {
            if (bonus.trigger != NewWorldSetTrigger.OnAnyCardPlayed) return;
            if (bonus.effect == NewWorldSetEffect.ReturnBasicFromDiscard) ReturnBasicFromDiscard(battle, bonus);
        }

        /// <summary>手牌未满时，从普通弃牌堆取一张基本牌加入手牌；满手或无基本牌则不做任何事。</summary>
        private void ReturnBasicFromDiscard(BattleContext battle, NewWorldSetBonusData bonus)
        {
            if (!battle.Ordinary.CanAddToHand()) return;
            CardInstance chosen = null;
            if (bonus.returnPolicy == NewWorldSetReturnPolicy.Random)
            {
                var candidates = battle.Ordinary.DiscardPile.Cards
                    .Where(card => card.Data.category == CardCategory.Basic).ToArray();
                if (candidates.Length > 0) chosen = battle.Random.Choose(candidates);
            }
            else
            {
                // 列表尾部为最近弃置；从尾部向前取最近一张基本牌，符合“回收弃牌堆”的直觉。
                for (int i = battle.Ordinary.DiscardPile.Cards.Count - 1; i >= 0; i--)
                {
                    var card = battle.Ordinary.DiscardPile.Cards[i];
                    if (card.Data.category == CardCategory.Basic) { chosen = card; break; }
                }
            }
            if (chosen == null) return;
            if (!battle.Ordinary.DiscardPile.TryRemove(chosen)) return;
            if (!battle.Ordinary.Hand.TryAdd(chosen)) battle.Ordinary.DiscardPile.TryAdd(chosen);
        }
    }
}
