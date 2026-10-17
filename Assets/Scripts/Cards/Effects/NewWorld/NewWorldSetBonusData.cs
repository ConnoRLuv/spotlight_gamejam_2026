using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>套装激活后的被动结算时机；四部位激活条件统一由 RunBuffState 管理。</summary>
    public enum NewWorldSetTrigger { OnAnyCardPlayed }
    /// <summary>套装全局效果种类：后续新增效果在此追加并同步扩展结算入口。</summary>
    public enum NewWorldSetEffect { ReturnBasicFromDiscard }
    /// <summary>回收弃牌堆基本牌时的选择策略。</summary>
    public enum NewWorldSetReturnPolicy { MostRecent, Random }

    /// <summary>
    /// 新世界套装配置：同一玩家回合成功打出指定四部位后，持续到本场战斗结束。
    /// </summary>
    /// <remarks>
    /// 保留原序列化字段与资产身份；实际组合记录统一由 RunBuffState 管理。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/新世界/套装效果配置")]
    public sealed class NewWorldSetBonusData : ScriptableObject
    {
        public string setId;
        public string setName;
        [TextArea] public string description;
        [Tooltip("同回合需成功打出的部件；必须覆盖脑机、躯干、手部、腿部四个部位。")]
        public CyberneticData[] requiredParts = Array.Empty<CyberneticData>();
        public NewWorldSetTrigger trigger = NewWorldSetTrigger.OnAnyCardPlayed;
        public NewWorldSetEffect effect = NewWorldSetEffect.ReturnBasicFromDiscard;
        public NewWorldSetReturnPolicy returnPolicy = NewWorldSetReturnPolicy.MostRecent;

        /// <summary>拒绝空身份、缺部位、重复身份及无效义体；同部位允许配置多个候选。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(setId)) errors.Add("套装 ID 不能为空。");
            if (string.IsNullOrWhiteSpace(setName)) errors.Add("套装名称不能为空。");
            if (!Enum.IsDefined(typeof(NewWorldSetTrigger), trigger)) errors.Add("未知触发时机。");
            if (!Enum.IsDefined(typeof(NewWorldSetEffect), effect)) errors.Add("未知套装效果。");
            if (!Enum.IsDefined(typeof(NewWorldSetReturnPolicy), returnPolicy)) errors.Add("未知回收策略。");
            if (requiredParts == null || requiredParts.Length < 4)
            { errors.Add("套装必须配置完整四个部位。"); return errors; }
            if (requiredParts.Any(part => !part))
            { errors.Add("套装部件含空引用。"); return errors; }
            if (requiredParts.Select(part => part.slot).Distinct().Count() != 4)
                errors.Add("套装必须覆盖脑机、躯干、手部、腿部四个部位。");
            if (requiredParts.Select(part => part.cyberneticId).Distinct().Count() != requiredParts.Length)
                errors.Add("套装部件身份重复。");
            foreach (var part in requiredParts) errors.AddRange(part.Validate());
            return errors;
        }

        /// <summary>仅预告持有牌组是否覆盖组合；实际激活由成功出牌记录决定，不使用部位首张查询。</summary>
        public bool IsComplete(CyberneticLoadout loadout) =>
            loadout != null && RunBuffData.MatchesParts(requiredParts, loadout.Owned.Select(card => card.Data));
    }
}
