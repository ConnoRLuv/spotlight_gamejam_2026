namespace SpotlightGameJam
{
    /// <summary>
    /// 枚举出牌失败原因，供 UI 显示提示或选择不同反馈。
    /// </summary>
    /// <remarks>
    /// None 表示成功；其余值区分阶段、归属、目标、费用、配置和义体限制等拒绝情况。
    /// </remarks>
    public enum CardPlayFailure
    {
        None, InvalidPhase, Busy, CardNotInHand, LockedCard, InvalidConfiguration,
        InvalidTarget, InsufficientAp, InsufficientSanity, UnavailableCybernetic,
        ChoiceRequired, InvalidChoice, HandFull, EffectUnavailable
    }
    /// <summary>
    /// 携带一次出牌指令的结果，包含是否成功、失败类型和可读提示。
    /// </summary>
    /// <remarks>
    /// CardPlayService 返回此值，BattleController 将结果通知订阅者；正常拒绝无需通过异常处理。
    /// </remarks>
    public readonly struct CardPlayResult
    {
        public bool Success => Failure == CardPlayFailure.None;
        public CardPlayFailure Failure { get; }
        public string Message { get; }
        /// <summary>保存出牌结果和失败说明，供调用方反馈给玩家。</summary>
        public CardPlayResult(CardPlayFailure failure, string message = "")
        { Failure = failure; Message = message; }
        public static CardPlayResult Played => new CardPlayResult(CardPlayFailure.None);
    }
}
