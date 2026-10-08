using System.Collections.Generic;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 定义可扩展卡牌效果的配置与执行契约。
    /// </summary>
    /// <remarks>
    /// Validate 检查静态配置，CanExecute 无副作用地检查前置条件，Execute 在扣费成功后同步完成效果；需要交互选择时须先扩展指令协议。
    /// </remarks>
    public abstract class CardEffectData : ScriptableObject
    {
        /// <summary>
        /// 校验效果自身的静态配置；返回 null 表示有效，否则返回错误说明。
        /// </summary>
        public virtual string Validate() => null;
        /// <summary>
        /// 声明效果是否必须具备目标模式，供 CardData 在开战前检查配置兼容性。
        /// </summary>
        public virtual bool RequiresTarget => false;
        /// <summary>需要选牌时，普通 TryPlay 在支付前返回 ChoiceRequired。</summary>
        public virtual bool RequiresChoice => false;
        /// <summary>扩展请求入口；基础效果沿用原有签名，避免破坏既有调用。</summary>
        public virtual bool CanExecuteRequest(BattleContext context, CardPlayRequest request) =>
            CanExecute(context, request.Card, request.Target);
        /// <summary>根据已校验的出牌请求执行效果，使用请求中的卡牌和目标。</summary>
        public virtual void ExecuteRequest(BattleContext context, CardPlayRequest request) =>
            Execute(context, request.Card, request.Target);
        /// <summary>
        /// 只读检查执行条件，不扣资源、不移动卡牌，也不消耗随机数。
        /// </summary>
        public virtual bool CanExecute(BattleContext context, CardInstance card, CombatantState target) => true;
        /// <summary>
        /// 在出牌服务完成校验和支付后同步执行；伤害必须经 DamageResolver 结算。
        /// </summary>
        public abstract void Execute(BattleContext context, CardInstance card, CombatantState target);
        /// <summary>
        /// 按卡牌目标模式展开作用对象；这里只决定名义目标，不进行幻痛重定向。
        /// </summary>
        protected IEnumerable<CombatantState> Targets(BattleContext context, CardInstance card, CombatantState target)
        {
            if (card.Data.targetType == CardTargetType.AllEnemies)
            { foreach (var enemy in context.Enemies) if (enemy.IsAlive) yield return enemy; }
            else if (card.Data.targetType == CardTargetType.Self) yield return context.Player;
            else if (target != null) yield return target;
        }
    }
}
