namespace SpotlightGameJam
{
    /// <summary>单场战斗的义体临时状态；不写入共享配置，也不随冒险跨战斗保留。</summary>
    public sealed class BattleTurnEffects
    {
        public bool DamageToSanityArmed { get; internal set; }
        public CardData ConversionAttack { get; internal set; }
        public bool ConversionEnabled => ConversionAttack != null;
        public int ExtraTurns { get; internal set; }

        /// <summary>清除上一回合的承伤保护和攻击转换状态，开始新的玩家回合。</summary>
        internal void BeginTurn()
        {
            // 承伤保护保留至敌人行动结束，而 AP 转换仅属于玩家行动阶段。
            DamageToSanityArmed = false;
            ConversionAttack = null;
        }
        /// <summary>结束玩家行动阶段，关闭仅在本阶段有效的攻击转换。</summary>
        internal void EndPlayerAction() { ConversionAttack = null; }
        /// <summary>清空临时义体效果，包括尚未消费的额外回合。</summary>
        internal void Clear()
        { DamageToSanityArmed = false; ConversionAttack = null; ExtraTurns = 0; }
    }
}
