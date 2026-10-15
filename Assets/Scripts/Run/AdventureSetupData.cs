using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>探索入口的共享配置：基础牌、规则、可选义体、默认选择及全局 buff。</summary>
    [CreateAssetMenu(menuName = "Spotlight/探索/出发配置")]
    public sealed class AdventureSetupData : ScriptableObject
    {
        public BattleRules rules;
        public CardData phantomPain;
        public CardData[] ordinaryDeck = Array.Empty<CardData>();
        public CyberneticData[] availableCybernetics = Array.Empty<CyberneticData>();
        public CyberneticData[] defaultCybernetics = Array.Empty<CyberneticData>();
        public RunBuffData[] buffs = Array.Empty<RunBuffData>();
        public NewWorldSetBonusData[] setBonuses = Array.Empty<NewWorldSetBonusData>();
        public int randomSeed = 1;
        /// <summary>校验实际带入的选择，允许零至四个部位，不把未装备当作错误。</summary>
        public IReadOnlyList<string> ValidateSelection(IEnumerable<CyberneticData> selection)
        {
            var errors = new List<string>();
            if (!rules) errors.Add("缺少战斗规则。"); else errors.AddRange(rules.Validate());
            if (!phantomPain) errors.Add("缺少幻痛配置。");
            else
            {
                errors.AddRange(phantomPain.Validate());
                if (!phantomPain.locksInHand || phantomPain.category != CardCategory.Special) errors.Add("幻痛必须是锁定特殊牌。");
            }
            if (ordinaryDeck == null) errors.Add("普通牌堆不能为空引用。");
            else foreach (var card in ordinaryDeck)
            {
                if (!card) { errors.Add("普通牌含空引用。"); continue; }
                errors.AddRange(card.Validate());
                if (card.category != CardCategory.Basic && card.category != CardCategory.Function) errors.Add("普通牌类型错误。");
            }
            if (selection == null) { errors.Add("义体选择不能为空引用。"); return errors; }
            var slots = new HashSet<CyberneticSlot>();
            foreach (var data in selection)
            {
                if (!data) { errors.Add("已选择的义体含空引用。"); continue; }
                if (!slots.Add(data.slot)) errors.Add("同一部位只能选择一种义体。");
                errors.AddRange(data.Validate());
            }
            if (buffs == null) errors.Add("Buff 列表不能为空引用。");
            else foreach (var buff in buffs)
                if (!buff) errors.Add("Buff 含空引用。"); else errors.AddRange(buff.Validate());
            if (setBonuses == null) errors.Add("套装列表不能为空引用。");
            else foreach (var bonus in setBonuses)
                if (!bonus) errors.Add("套装含空引用。"); else errors.AddRange(bonus.Validate());
            return errors;
        }
        /// <summary>创建独立探索牌组及可触发 buff 配置，不因携带完整四部位而自动激活。</summary>
        public RunState CreateRun(IEnumerable<CyberneticData> selection)
        {
            var chosen = selection?.ToArray();
            var errors = ValidateSelection(chosen);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            var run = new RunState(rules);
            foreach (var data in chosen) run.Loadout.AddInitial(data);
            run.Buffs.Configure(buffs);
            run.SetBonuses.Configure(setBonuses);
            return run;
        }
    }
}
