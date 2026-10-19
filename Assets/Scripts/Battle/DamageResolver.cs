using System;
using System.Collections.Generic;
using System.Linq;
namespace SpotlightGameJam
{
    /// <summary>
    /// 统一结算战斗伤害，处理幻痛随机目标、护盾吸收和即时胜负判断。
    /// </summary>
    /// <remarks>
    /// 卡牌与敌人攻击均通过 Apply 进入；新伤害效果也必须使用此入口，避免绕过幻痛规则。
    /// </remarks>
    public static class DamageResolver
    {
        /// <summary>结算理智伤害；复仇女神躯干将敌方理智攻击转为攻击者承受的生命伤害，主动费用不调用此入口。</summary>
        public static int ApplySanity(BattleContext context, CombatantState source, CombatantState target, int damage)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (context.IsFinished || source == null || !source.IsAlive || target == null || !target.IsAlive ||
                source != context.Player && !context.Enemies.Contains(source) ||
                target != context.Player && !context.Enemies.Contains(target)) return 0;
            // 整次理智攻击在消耗玩家理智前转成生命伤害，不通过支付接口，也不递归再次转移。
            if (target == context.Player && source != context.Player && context.TurnEffects.RedirectEnemyDamage)
                return Apply(context, source, source, damage);
            int lost = target.ReceiveSanityDamage(damage);
            if (target == context.Player) context.EnsurePhantomPain();
            context.CheckOutcome();
            if (target == context.Player) context.NotifyPlayerDamaged(0, lost);
            return lost;
        }

        /// <summary>
        /// 结算一次伤害并检查胜负；幻痛可能替换玩家伤害的目标。
        /// 返回实际生命损失；战斗已结束或角色不属于本场战斗时不造成伤害。
        /// </summary>
        public static int Apply(BattleContext context, CombatantState source, CombatantState target, int damage)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (context.IsFinished || source == null || !source.IsAlive) return 0;
            bool SourceBelongs() => source == context.Player || context.Enemies.Contains(source);
            if (!SourceBelongs()) return 0;
            // 只重定向玩家造成的伤害；每次调用重新选择，群伤也逐次随机。
            if (source == context.Player && context.HasPhantomPain)
            {
                var alive = new List<CombatantState>();
                // 幻痛候选包含玩家自身，同时排除死亡角色。
                if (context.Player.IsAlive) alive.Add(context.Player);
                alive.AddRange(context.Enemies.Where(e => e.IsAlive));
                if (alive.Count == 0) return 0;
                target = context.Random.Choose(alive);
            }
            if (target == null || !target.IsAlive ||
                (target != context.Player && !context.Enemies.Contains(target))) return 0;
            // 在任何护盾和理智保护之前转移整次敌方伤害；不递归调用，避免反射或幻痛再次改向。
            if (target == context.Player && source != context.Player && context.TurnEffects.RedirectEnemyDamage)
                target = source;
            bool protect = target == context.Player && context.TurnEffects.DamageToSanityArmed && damage > target.Shield;
            if (protect) context.TurnEffects.DamageToSanityArmed = false;
            int previousSanity = target.Sanity;
            int result = target.ReceiveDamage(damage, protect);
            if (protect) context.EnsurePhantomPain();
            // 立即终结战斗，让外层牌效/敌人循环及时停止后续伤害。
            context.CheckOutcome();
            if (target == context.Player) context.NotifyPlayerDamaged(result, previousSanity - target.Sanity);
            return result;
        }
    }
}
