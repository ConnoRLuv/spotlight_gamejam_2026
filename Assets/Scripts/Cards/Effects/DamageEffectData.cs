using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 配置对目标造成伤害的效果，基础攻击默认伤害为 6。
    /// </summary>
    /// <remarks>
    /// 按卡牌目标模式展开受击角色，逐次通过 DamageResolver 结算；每次伤害都独立遵循幻痛与胜负规则。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/伤害")]
    public sealed class DamageEffectData : CardEffectData
    {
        [Min(0)] public int amount = 6;
        public override bool RequiresTarget => true;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate() => amount < 0 ? "伤害不能为负数。" : null;
        /// <summary>检查当前目标及状态是否允许执行效果，不修改战斗资源。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) =>
            card.Data.targetType != CardTargetType.None;
        /// <summary>按卡牌目标规则解析伤害目标，并通过伤害结算器施加伤害。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        {
            foreach (var recipient in Targets(context,card,target))
            {
                if (context.IsFinished) break;
                // 所有伤害都走统一入口；不能直接调用 ReceiveDamage 绕过幻痛。
                DamageResolver.Apply(context,context.Player,recipient,amount);
            }
        }
    }
}
