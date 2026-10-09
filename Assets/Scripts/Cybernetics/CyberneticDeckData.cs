using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>可复用的初始义体牌组配置；新增系列时创建新资产，无需修改战斗代码。</summary>
    /// <remarks>本资产组合初始装备；每种义体的部位、耐久、卡牌数量和效果仍由其配置负责。</remarks>
    [CreateAssetMenu(menuName = "Spotlight/义体牌组", fileName = "CyberneticDeck")]
    public sealed class CyberneticDeckData : ScriptableObject
    {
        [Tooltip("牌组稳定标识，供后续解锁或选择系统使用。")]
        public string deckId;
        [Tooltip("供成员识别的系列或牌组名称。")]
        public string deckName;
        [Tooltip("本套初始义体，每个部位最多一个；可以只配置部分部位。各义体的 cards 数组决定入堆张数。")]
        public CyberneticData[] cybernetics = Array.Empty<CyberneticData>();

        /// <summary>启用牌组前检查身份、部位冲突和关联卡效果；空草稿不会作为可用牌组开战。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(deckId)) errors.Add("义体牌组 ID 不能为空。");
            if (string.IsNullOrWhiteSpace(deckName)) errors.Add("义体牌组名称不能为空。");
            if (cybernetics == null || cybernetics.Length == 0)
            {
                errors.Add("义体牌组至少需要一个义体配置。");
                return errors;
            }
            var slots = new HashSet<CyberneticSlot>();
            foreach (var data in cybernetics)
            {
                if (!data) { errors.Add("义体牌组含空引用。"); continue; }
                if (!slots.Add(data.slot)) errors.Add("义体牌组重复占用同一部位。");
                errors.AddRange(data.Validate());
            }
            return errors;
        }
    }
}
