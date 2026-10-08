namespace SpotlightGameJam
{
    /// <summary>
    /// 定义脑机、躯干、手部、腿部四个义体槽位。
    /// </summary>
    /// <remarks>
    /// 数值同时用作 CyberneticUsage 数组索引；新增部位时必须同步调整计数数组和校验范围。
    /// </remarks>
    public enum CyberneticSlot { Brain = 0, Torso = 1, Hands = 2, Legs = 3 }
}
