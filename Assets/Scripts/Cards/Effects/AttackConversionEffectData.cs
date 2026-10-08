using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>军用脉冲发射器：开启本回合的 AP 转换，不在激活时强制花掉 AP。</summary>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/AP 转攻击牌")]
    public sealed class AttackConversionEffectData : CardEffectData
    {
        public CardData attack;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public override string Validate()
        {
            if (!attack || attack.category != CardCategory.Basic || attack.locksInHand)
                return "转换需要有效的基本攻击牌。";
            if (attack.targetType != CardTargetType.SingleEnemy || attack.effects == null || attack.effects.Length != 1 ||
                !(attack.effects[0] is DamageEffectData damage) || damage.amount != 6 || attack.cost != 1 || attack.sanityCost != 0)
                return "转换攻击必须是 1 AP、6 伤害的基本攻击牌。";
            // 先确认只有伤害效果，再深度校验，避免错误自引用引发递归。
            if (attack.Validate().Count > 0) return "转换攻击配置无效。";
            return null;
        }
        /// <summary>启用本回合的攻击转换，并登记生成攻击所用的配置。</summary>
        public override void Execute(BattleContext context, CardInstance card, CombatantState target) =>
            context.TurnEffects.ConversionAttack = attack;
    }
}
