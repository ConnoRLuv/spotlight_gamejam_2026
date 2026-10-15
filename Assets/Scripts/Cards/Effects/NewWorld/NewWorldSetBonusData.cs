using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>套装触发时机：目前只支持“每打出一张牌”，后续新时机在此追加。</summary>
    public enum NewWorldSetTrigger { OnAnyCardPlayed }
    /// <summary>套装全局效果种类：后续新增效果在此追加并同步扩展结算入口。</summary>
    public enum NewWorldSetEffect { ReturnBasicFromDiscard }
    /// <summary>回收弃牌堆基本牌时的选择策略。</summary>
    public enum NewWorldSetReturnPolicy { MostRecent, Random }

    /// <summary>
    /// 新世界系列套装配置：穿戴一整套同系列义体后持续生效的全局被动效果。
    /// </summary>
    /// <remarks>
    /// 按“拥有/穿戴”判定：四个部位都装备了 requiredParts 指定的义体即视为穿戴齐全，
    /// 随后每次出牌触发一次全局效果。判定不依赖耐久，报废部件仍视为已穿戴。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/新世界/套装效果配置")]
    public sealed class NewWorldSetBonusData : ScriptableObject
    {
        public string setId;
        public string setName;
        [TextArea] public string description;
        [Tooltip("穿戴齐全所需部件；必须覆盖脑机、躯干、手部、腿部四个部位。")]
        public CyberneticData[] requiredParts = Array.Empty<CyberneticData>();
        public NewWorldSetTrigger trigger = NewWorldSetTrigger.OnAnyCardPlayed;
        public NewWorldSetEffect effect = NewWorldSetEffect.ReturnBasicFromDiscard;
        public NewWorldSetReturnPolicy returnPolicy = NewWorldSetReturnPolicy.MostRecent;

        /// <summary>拒绝空系列、缺部位、跨系列混搭及无效义体，保证套装不会被误触发。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(setId)) errors.Add("套装 ID 不能为空。");
            if (string.IsNullOrWhiteSpace(setName)) errors.Add("套装名称不能为空。");
            if (!Enum.IsDefined(typeof(NewWorldSetTrigger), trigger)) errors.Add("未知触发时机。");
            if (!Enum.IsDefined(typeof(NewWorldSetEffect), effect)) errors.Add("未知套装效果。");
            if (!Enum.IsDefined(typeof(NewWorldSetReturnPolicy), returnPolicy)) errors.Add("未知回收策略。");
            if (requiredParts == null || requiredParts.Length != 4)
            { errors.Add("套装必须恰好配置四个部位。"); return errors; }
            if (requiredParts.Any(part => !part))
            { errors.Add("套装部件含空引用。"); return errors; }
            if (requiredParts.Select(part => part.slot).Distinct().Count() != 4)
                errors.Add("套装必须覆盖脑机、躯干、手部、腿部四个部位。");
            if (requiredParts.Select(part => part.cyberneticId).Distinct().Count() != 4)
                errors.Add("套装部件身份重复。");
            foreach (var part in requiredParts) errors.AddRange(part.Validate());
            return errors;
        }

        /// <summary>判断四个部位是否都装备了本套部件；按义体 ID 判重，不要求部件耐久大于零。</summary>
        public bool IsComplete(CyberneticLoadout loadout) =>
            loadout != null && requiredParts != null && requiredParts.Length == 4 &&
            requiredParts.All(part => part && loadout.Get(part.slot) != null &&
                loadout.Get(part.slot).Data.cyberneticId == part.cyberneticId);
    }
}
