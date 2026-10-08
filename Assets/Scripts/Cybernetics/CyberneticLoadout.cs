using System;
using System.Collections.Generic;
namespace SpotlightGameJam
{
    /// <summary>
    /// 管理一次冒险中四个部位的装备，每个部位保存一个义体实例。
    /// </summary>
    /// <remarks>
    /// Equip 替换该槽位装备；GetUsableCards 为有耐久的装备生成新卡牌实例，并记录真实来源。
    /// </remarks>
    public sealed class CyberneticLoadout
    {
        private readonly Dictionary<CyberneticSlot,CyberneticInstance> slots =
            new Dictionary<CyberneticSlot,CyberneticInstance>();
        public IEnumerable<CyberneticInstance> Equipped => slots.Values;
        /// <summary>
        /// 将实例装入对应部位，替换旧装备；此操作不会清除战斗部位使用计数。
        /// </summary>
        public void Equip(CyberneticInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!Enum.IsDefined(typeof(CyberneticSlot),instance.Data.slot)) throw new ArgumentException("未知部位。");
            // 更换装备只替换槽位引用，战斗中的同部位使用次数由 Usage 独立保存。
            slots[instance.Data.slot] = instance;
        }
        /// <summary>
        /// 查询对应槽位的当前实例，未装备时返回 null。
        /// </summary>
        public CyberneticInstance Get(CyberneticSlot slot) =>
            slots.TryGetValue(slot,out var instance) ? instance : null;
        /// <summary>
        /// 按实例引用确认装备归属；同配置的另一件义体不能冒充当前装备。
        /// </summary>
        public bool Contains(CyberneticInstance instance) =>
            instance != null && Get(instance.Data.slot) == instance;
        /// <summary>
        /// 为当前有耐久的义体生成关联卡；每次枚举都会创建新的战斗卡牌实例。
        /// </summary>
        public IEnumerable<CardInstance> GetUsableCards()
        {
            foreach (var instance in slots.Values)
                if (instance.Durability > 0 && instance.Data.cards != null)
                    foreach (var card in instance.Data.cards)
                        // 记录真实来源，出牌时可拒绝已卸下或已报废的义体。
                        if (card) yield return new CardInstance(card,instance);
        }
    }
}
