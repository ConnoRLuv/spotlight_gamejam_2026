using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 配置增加临时 AP 的卡牌效果，过载可复用此效果。
    /// </summary>
    /// <remarks>
    /// 默认增加 1 点；理智费用由 CardData 指定，临时 AP 由 ActionPointPool 在回合结束时清除。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/临时行动点")]
    public sealed class ActionPointEffectData : CardEffectData
    {
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        [Min(0)] public int amount = 1;
        public override string Validate() => amount < 0 ? "临时 AP 不能为负数。" : null;
        /// <summary>增加本回合有效的临时行动点，剩余点数在回合结束时清除。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target)
        { context.ActionPoints.AddTemporary(amount); }
    }
}
