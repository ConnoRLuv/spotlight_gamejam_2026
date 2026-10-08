using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 配置对目标恢复生命的效果，基础回复默认恢复 3 点。
    /// </summary>
    /// <remarks>
    /// 目标由卡牌模式决定；最大生命限制和禁止复活规则由 CombatantState.Heal 处理。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/治疗")]
    public sealed class HealEffectData : CardEffectData
    {
        [Min(0)] public int amount = 3;
        public override bool RequiresTarget => true;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate() => amount < 0 ? "治疗不能为负数。" : null;
        /// <summary>检查当前目标及状态是否允许执行效果，不修改战斗资源。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) =>
            card.Data.targetType != CardTargetType.None;
        /// <summary>恢复目标生命，由角色状态限制恢复上限及死亡后的恢复。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        { foreach (var recipient in Targets(context,card,target)) recipient.Heal(amount); }
    }
}
