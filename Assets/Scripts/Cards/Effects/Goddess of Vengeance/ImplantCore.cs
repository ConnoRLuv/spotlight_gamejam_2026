using System;
using System.Collections.Generic;

namespace ImplantSystem
{
    /// <summary>
    /// 义体定义（纯数据，可在配置表 / Inspector 里编辑）。
    ///
    /// 一个"部位"对应一张义体卡：脑机 / 躯干 / 手部 / 腿部。
    /// 耐久度是义体自己的血量：打出时 -1、可修复、归零报废。
    /// MaxUsesPerRun 是硬性限制：每局每部位最多使用 3 次（不可修复，防止无限刷）。
    /// </summary>
    [Serializable]
    public sealed class ImplantDefinition
    {
        public string Key;           // "brain" / "torso" / "hand" / "leg"
        public string DisplayName;   // "军用战术协调矩阵" 等
        public int Durability;       // 耐久度上限（可修复）
        public int SanityCost;       // 打出时消耗的理智值
        public int MaxUsesPerRun;    // 义体限制：每局每部位最多使用次数（=3）
        public string EffectType;    // 效果键，交给 EffectCatalog 解析
        public int EffectParam;      // 效果数值参数，如"看前 3 张"里的 3

        public ImplantDefinition Clone()
        {
            return (ImplantDefinition)MemberwiseClone();
        }
    }

    /// <summary>义体实例（运行时状态）。定义是共享的只读数据，这里只存会变的部分。</summary>
    public sealed class Implant
    {
        public ImplantDefinition Definition;
        public int CurrentDurability;
        public int UsesThisRun;

        public Implant(ImplantDefinition definition)
        {
            Definition = definition;
            CurrentDurability = definition != null ? definition.Durability : 0;
        }

        /// <summary>耐久度归零即报废。</summary>
        public bool IsDestroyed => CurrentDurability <= 0;

        /// <summary>每部位本局使用次数已用完。</summary>
        public bool UsesExhausted =>
            Definition != null && UsesThisRun >= Definition.MaxUsesPerRun;

        /// <summary>能否打出：没报废，且本局次数没用完。</summary>
        public bool CanBePlayed => !IsDestroyed && !UsesExhausted;

        /// <summary>打出时耐久 -1。</summary>
        public void ReduceDurability(int amount)
        {
            if (amount < 0) amount = 0;
            CurrentDurability -= amount;
            if (CurrentDurability < 0) CurrentDurability = 0;
        }

        /// <summary>修复耐久（不超过上限）。</summary>
        public void Repair(int amount)
        {
            if (amount < 0) amount = 0;
            CurrentDurability += amount;
            if (Definition != null && CurrentDurability > Definition.Durability)
                CurrentDurability = Definition.Durability;
        }

        /// <summary>记录一次使用（每部位次数 +1，此值不随修复恢复）。</summary>
        public void RecordUse()
        {
            UsesThisRun++;
        }

        public override string ToString()
        {
            return Definition.DisplayName +
                   "（耐久 " + CurrentDurability + "/" + Definition.Durability +
                   "，本局已用 " + UsesThisRun + "/" + Definition.MaxUsesPerRun + "）";
        }
    }

    /// <summary>义体牌堆（抽牌堆）。末尾 = 牌堆顶。脑机要"看顶 3 张"，所以支持 Peek。</summary>
    public sealed class ImplantDeck
    {
        readonly List<Implant> _cards = new List<Implant>();

        public int Count => _cards.Count;
        public IReadOnlyList<Implant> Cards => _cards;

        public void AddToTop(Implant card)
        {
            if (card != null) _cards.Add(card);
        }

        public void AddToBottom(Implant card)
        {
            if (card != null) _cards.Insert(0, card);
        }

        public Implant Draw()
        {
            if (_cards.Count == 0) return null;
            Implant card = _cards[_cards.Count - 1];
            _cards.RemoveAt(_cards.Count - 1);
            return card;
        }

        public List<Implant> PeekTop(int n)
        {
            var list = new List<Implant>();
            for (int i = 0; i < n && i < _cards.Count; i++)
                list.Add(_cards[_cards.Count - 1 - i]);
            return list;
        }

        public bool RemoveCard(Implant card)
        {
            return card != null && _cards.Remove(card);
        }

        public void Shuffle(Random rng)
        {
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                Implant tmp = _cards[i];
                _cards[i] = _cards[j];
                _cards[j] = tmp;
            }
        }
    }
}
