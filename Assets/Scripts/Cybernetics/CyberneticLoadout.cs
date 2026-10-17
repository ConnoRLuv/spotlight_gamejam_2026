using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>持有探索中的全部义体牌状态；同种多张牌共享耐久，不存在当前装备或战斗换装。</summary>
    public sealed class CyberneticLoadout
    {
        private readonly List<CyberneticInstance> cards = new List<CyberneticInstance>();
        private readonly System.Collections.ObjectModel.ReadOnlyCollection<CyberneticInstance> view;
        public IReadOnlyList<CyberneticInstance> Owned => view;
        // 旧只读入口返回全部持牌，不表示装备槽；保留源码兼容。
        public IEnumerable<CyberneticInstance> Equipped => view;
        public event Action Changed;
        /// <summary>建立只读视图，持牌集合由本类统一维护。</summary>
        public CyberneticLoadout() { view = cards.AsReadOnly(); }
        /// <summary>确认该持久牌属于本次探索，不能用另一张同配置牌冒充。</summary>
        public bool Owns(CyberneticInstance instance) => instance != null && cards.Contains(instance);
        /// <summary>旧查询接口按持牌归属判断，不再判断当前槽位。</summary>
        public bool Contains(CyberneticInstance instance) => Owns(instance);
        /// <summary>按配置 ID 判重，报废牌仍算拥有，避免改造重复获得已持有种类。</summary>
        public bool OwnsConfiguration(CyberneticData data) => data && cards.Any(value => value.Data.cyberneticId == data.cyberneticId);
        /// <summary>按配置张数创建初始持牌，身份各自独立，同种牌共用一份满耐久。</summary>
        public void AddInitial(CyberneticData data) => Equip(new CyberneticInstance(data));
        /// <summary>兼容旧初始牌组构建入口，替换该部位的初始选择并展开逐张持牌；运行中不调用。</summary>
        public void Equip(CyberneticInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!Enum.IsDefined(typeof(CyberneticSlot), instance.Data.slot)) throw new ArgumentException("未知部位。");
            var definitions = instance.Data.cards;
            if (definitions == null || definitions.Length == 0) throw new ArgumentException("义体必须关联卡牌。");
            foreach (var previous in cards.Where(value => value.Data.slot == instance.Data.slot).ToArray())
            { previous.Changed -= OnCardChanged; cards.Remove(previous); }
            Register(instance, true);
            for (int i = 1; i < definitions.Length; i++) Register(new CyberneticInstance(instance, definitions[i]), true);
            Changed?.Invoke();
        }
        /// <summary>旧调试查询返回该部位的第一张持牌；战斗规则和 buff 不依赖此查询。</summary>
        public CyberneticInstance Get(CyberneticSlot slot) => cards.FirstOrDefault(value => value.Data.slot == slot);
        /// <summary>改造增加一种未持有义体的一张满耐久持久牌，不替换同部位其他型号。</summary>
        public CardInstance AcquireCard(CyberneticData data, CardData card)
        {
            if (!data || data.Validate().Count > 0 || !data.cards.Contains(card) || OwnsConfiguration(data))
                throw new ArgumentException("改造来源无效或已经拥有。");
            var source = new CyberneticInstance(data, card);
            Register(source, false); Changed?.Invoke();
            return new CardInstance(card, source);
        }
        /// <summary>商店每次购买只增加一张；已拥有型号沿用当前共享耐久，不重置报废状态。</summary>
        public CardInstance PurchaseCard(CyberneticData data, CardData card)
        {
            if (!data || data.Validate().Count > 0 || !data.cards.Contains(card))
                throw new ArgumentException("购买义体配置无效。");
            var existing = cards.FirstOrDefault(value => value.Data.cyberneticId == data.cyberneticId);
            var source = existing != null ? new CyberneticInstance(existing, card) : new CyberneticInstance(data, card);
            Register(source, false); Changed?.Invoke();
            return new CardInstance(card, source);
        }

        /// <summary>登记持久牌并转发耐久通知，不改变任何共享配置。</summary>
        private void Register(CyberneticInstance instance, bool initial)
        { instance.IsInitialCard = initial; cards.Add(instance); instance.Changed += OnCardChanged; }
        /// <summary>转发持久牌耐久变化；组合 buff 只由成功出牌触发，牌区移动不触发此事件。</summary>
        private void OnCardChanged(CyberneticInstance instance) => Changed?.Invoke();
        /// <summary>每场生成新战斗卡面，沿用同种义体的共享耐久；报废的整组牌不会重新入堆。</summary>
        public IEnumerable<CardInstance> GetUsableCards()
        {
            foreach (var source in cards)
                if (source.Durability > 0 && source.Card) yield return new CardInstance(source.Card, source);
        }
    }
}
