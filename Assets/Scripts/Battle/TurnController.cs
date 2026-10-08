using System;
namespace SpotlightGameJam
{
    /// <summary>
    /// 按固定顺序刷新玩家回合资源，并在回合结束时驱动敌人行动。
    /// </summary>
    /// <remarks>
    /// 由 BattleController 调用，TurnNumber 仅属于当前战斗；此类不处理 UI 或卡牌费用。
    /// </remarks>
    public sealed class TurnController
    {
        public int TurnNumber { get; private set; }
        /// <summary>
        /// 清除过期状态、刷新 AP/义体回合标记、处理幻痛并抽牌，最后开放玩家行动。
        /// </summary>
        internal void BeginTurn(BattleContext context)
        {
            context.CheckOutcome();
            if (context.IsFinished) return;
            context.SetState(BattleState.TurnStart);
            TurnNumber++;
            context.TurnEffects.BeginTurn();
            // 在玩家回合开始清除，保证上一回合防御可先抵挡敌人行动。
            context.Player.ClearShield();
            foreach (var enemy in context.Enemies) enemy.ClearShield();
            // 首回合按配置起始值，此后线性增长；long 中间值避免乘法溢出。
            int allowance = (int)Math.Min(context.Rules.MaxAp,
                context.Rules.InitialAp + (long)(TurnNumber - 1) * context.Rules.ApGrowth);
            context.ActionPoints.BeginTurn(allowance); context.Usage.BeginTurn();
            // 上一场留下的零理智也会在本场首回合触发；锁定牌计入普通手牌容量。
            context.EnsurePhantomPain();
            context.Ordinary.Draw(context.Rules.OrdinaryDraw);
            context.Cybernetic.Draw(context.Rules.CyberneticDraw);
            context.SetState(BattleState.PlayerAction);
        }
        /// <summary>
        /// 清除临时 AP，按敌人顺序攻击；战斗仍未结束才开始下一玩家回合。
        /// </summary>
        internal void EndTurn(BattleContext context, int enemyAttackDamage)
        {
            // 临时 AP 在结束玩家行动时失效，不等到敌人行动之后才清除。
            context.ActionPoints.EndTurn();
            context.Ordinary.RemoveTurnCards(); context.Cybernetic.RemoveTurnCards();
            context.TurnEffects.EndPlayerAction();
            if (context.TurnEffects.ExtraTurns > 0)
            {
                context.TurnEffects.ExtraTurns--;
                BeginTurn(context);
                return;
            }
            context.SetState(BattleState.EnemyAction);
            foreach (var enemy in context.Enemies)
            {
                // 任何一次攻击导致死亡后，其他敌人不再行动。
                if (context.IsFinished) break;
                if (enemy.IsAlive) DamageResolver.Apply(context,enemy,context.Player,enemyAttackDamage);
            }
            context.CheckOutcome();
            if (!context.IsFinished) BeginTurn(context);
        }
    }
}
