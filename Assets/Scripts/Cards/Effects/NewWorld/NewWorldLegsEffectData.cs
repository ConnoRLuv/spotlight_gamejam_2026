using UnityEngine;

namespace SpotlightGameJam
{
    /// <summary>新世界·腿部：预订一个完整的额外玩家回合，由结束回合入口消费。</summary>
    /// <remarks>
    /// 与通用额外回合效果保持同一条回合推进路径：结束行动时先消费额外回合并直接开始新回合，
    /// 跳过敌人阶段；复用 BattleTurnEffects.ExtraTurns，避免额外引入第二套回合计数器。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/卡牌效果/新世界·额外行动回合")]
    public sealed class NewWorldLegsEffectData : ExtraTurnEffectData
    {
        // 保留脚本身份以兼容已有资产，结算直接继承通用额外回合模板。
    }
}
