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
<<<<<<< Updated upstream
=======
        public virtual string ChoiceTitle => "选择一张卡牌";
        public virtual string ChoicePrompt => "请选择一张卡牌；取消不消耗资源。";
        public virtual bool ReserveChoiceBeforePayment => false;
        /// <summary>返回交互候选快照；默认效果无需选择，具体选牌效果覆盖此入口。</summary>
        public virtual IReadOnlyList<CardInstance> GetChoices(BattleContext context, CardInstance card) =>
            System.Array.Empty<CardInstance>();
        /// <summary>需要重排抽牌堆时返回 true；重排结果经 CardPlayRequest.Reorder 提交。</summary>
        public virtual bool RequiresReorder => false;
        public virtual string ReorderTitle => "调整抽牌堆顺序";
        public virtual string ReorderPrompt => "按抽取顺序排列卡牌；取消不消耗资源。";
        /// <summary>返回参与重排的全部卡牌快照（下一张在前）；默认效果无需重排。</summary>
        public virtual IReadOnlyList<CardInstance> GetReorderCards(BattleContext context, CardInstance card) =>
            System.Array.Empty<CardInstance>();
        /// <summary>只读校验提交的重排顺序是否恰好覆盖目标牌区；不移动卡牌。</summary>
        public virtual bool ValidateReorder(BattleContext context, CardInstance card, IReadOnlyList<CardInstance> order) =>
            order != null && order.Count > 0;
>>>>>>> Stashed changes
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
