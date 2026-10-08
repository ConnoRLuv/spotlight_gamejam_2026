using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 配置为目标增加护盾的效果，基础防御默认增加 3 点。
    /// </summary>
    /// <remarks>
    /// 护盾吸收后续伤害，下个玩家回合开始时由 TurnController 清除。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/护盾")]
    public sealed class ShieldEffectData : CardEffectData
    {
        [Min(0)] public int amount = 3;
        public override bool RequiresTarget => true;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate() => amount < 0 ? "护盾不能为负数。" : null;
        /// <summary>检查当前目标及状态是否允许执行效果，不修改战斗资源。</summary>
        public override bool CanExecute(BattleContext context, CardInstance card, CombatantState target) =>
            card.Data.targetType != CardTargetType.None;
        /// <summary>为目标增加护盾，交由伤害和回合流程处理吸收与过期。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        { foreach (var recipient in Targets(context,card,target)) recipient.AddShield(amount); }
    }
}
