namespace SpotlightGameJam
{
    /// <summary>
    /// 描述战斗流程阶段，用于限制指令执行时机和驱动界面状态。
    /// </summary>
    /// <remarks>
    /// NotStarted 为未开始；TurnStart 刷新资源；PlayerAction 允许出牌；EnemyAction 结算敌人；Victory/Defeat 表示结束。
    /// </remarks>
    public enum BattleState { NotStarted, TurnStart, PlayerAction, EnemyAction, Victory, Defeat }
}
