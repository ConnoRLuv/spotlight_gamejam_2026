using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotlightGameJam
{
    /// <summary>
    /// 新世界套装的兼容展示与被动结算入口；组合激活、回合记录和清理均由 RunBuffState 负责。
    /// </summary>
    /// <remarks>
    /// 保留成员已有的 SetBonuses 配置接口；这里只监听成功出牌，不保存第二份装备或激活状态。
    /// </remarks>
    public sealed class NewWorldSetBonusState
    {
        private readonly RunBuffState buffs;
        private BattleContext context;
        public IReadOnlyList<NewWorldSetBonusData> Active => buffs.ActiveSets;
        public event Action Changed { add { buffs.Changed += value; } remove { buffs.Changed -= value; } }

        /// <summary>引用探索中的唯一组合状态，避免独立维护出牌记录和持续时间。</summary>
        public NewWorldSetBonusState(RunBuffState buffs)
        { this.buffs = buffs ?? throw new ArgumentNullException(nameof(buffs)); }

        /// <summary>保留旧配置接口，注册到统一组合状态；携带全套不会自动激活。</summary>
        public void Configure(IEnumerable<NewWorldSetBonusData> bonuses)
            => buffs.ConfigureSets(bonuses);

        /// <summary>绑定当前战斗的成功出牌事件，不因绑定或携带牌组激活套装。</summary>
        public void Attach(BattleContext battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            Detach();
            context = battle;
            context.CardPlayed += OnCardPlayed;
        }

        /// <summary>解除对指定或当前战斗的订阅；战斗释放时调用，避免旧战斗继续触发回收。</summary>
        public void Detach(BattleContext battle = null)
        {
            if (battle != null && context != battle) return;
            if (context != null) context.CardPlayed -= OnCardPlayed;
            context = null;
        }

        /// <summary>战斗仍未结束时，把每张成功打出的牌分发给当前生效的套装。</summary>
        private void OnCardPlayed(CardInstance card)
        {
            if (context == null || context.IsFinished) return;
            foreach (var bonus in Active) Apply(bonus, context);
        }

        /// <summary>把已激活套装的配置分发给被动结算，不重新判断组合或装备。</summary>
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
