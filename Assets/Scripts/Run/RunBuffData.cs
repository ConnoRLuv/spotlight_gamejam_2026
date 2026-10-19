using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>全局 buff 的效果种类；新增效果时扩展结算入口，不修改组合判定。</summary>
    public enum RunBuffEffect { OverloadPaysHealth, DrawBasicOnDamage }
    /// <summary>组合成功后的持续范围；默认持续到触发的战斗结束。</summary>
    public enum RunBuffDuration { PlayerTurn, Battle, Adventure }
    /// <summary>定义同回合成功使用的四部位组合、效果及持续范围；同一部位可配置多个候选。</summary>
    [CreateAssetMenu(menuName = "Spotlight/探索/全局 Buff")]
    public sealed class RunBuffData : ScriptableObject
    {
        public string buffId;
        public string buffName;
        [TextArea] public string description;
        [Tooltip("每个部位至少一个候选；同部位候选之间为或，四部位之间为且。")]
        public CyberneticData[] requiredCybernetics = Array.Empty<CyberneticData>();
        public RunBuffEffect effect;
        public RunBuffDuration duration = RunBuffDuration.Battle;
        /// <summary>拒绝缺部位、空引用及重复身份，避免未完成的配置误触发。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(buffId)) errors.Add("Buff ID 不能为空。");
            if (string.IsNullOrWhiteSpace(buffName)) errors.Add("Buff 名称不能为空。");
            if (!Enum.IsDefined(typeof(RunBuffEffect), effect)) errors.Add("未知 Buff 效果。");
            if (!Enum.IsDefined(typeof(RunBuffDuration), duration)) errors.Add("未知 Buff 持续范围。");
            if (requiredCybernetics == null || requiredCybernetics.Any(value => !value)) errors.Add("Buff 组合含空引用。");
            else
            {
                if (requiredCybernetics.Select(value => value.slot).Distinct().Count() != 4) errors.Add("Buff 组合必须覆盖四个部位。");
                if (requiredCybernetics.Select(value => value.cyberneticId).Distinct().Count() != requiredCybernetics.Length)
                    errors.Add("Buff 组合含重复义体 ID。");
                foreach (var data in requiredCybernetics) errors.AddRange(data.Validate());
            }
            return errors;
        }
        /// <summary>只判断给定牌能否覆盖四部位；StartScene 用于预告，实际触发只传成功出牌记录。</summary>
        public bool Matches(IEnumerable<CyberneticInstance> cards) => MatchesConfigurations(cards.Select(card => card.Data));
        /// <summary>匹配成功使用的义体种类；不检查结算后的耐久，最后一点耐久也算成功使用。</summary>
        internal bool MatchesConfigurations(IEnumerable<CyberneticData> played)
            => MatchesParts(requiredCybernetics, played);

        /// <summary>所有套装共用的组合匹配：同部位候选为或，四部位为且，只读取成功出牌配置。</summary>
        internal static bool MatchesParts(IEnumerable<CyberneticData> required, IEnumerable<CyberneticData> played)
        {
            var candidates = required?.Where(data => data).ToArray() ?? Array.Empty<CyberneticData>();
            var cards = played.ToArray();
            return Enumerable.Range(0, 4).All(part => cards.Any(card => card && (int)card.slot == part &&
                candidates.Any(data => data.slot == card.slot && data.cyberneticId == card.cyberneticId)));
        }
    }
}
