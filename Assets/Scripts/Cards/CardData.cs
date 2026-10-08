using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SpotlightGameJam;

/// <summary>
/// 保存一张卡牌的共享静态配置，包括展示内容、费用、类别、目标及效果。
/// </summary>
/// <remarks>
/// 在 Inspector 创建资产；运行时身份和临时费用保存在 CardInstance 中。保留全局类型及旧字段以兼容已有资产。
/// </remarks>
[CreateAssetMenu(menuName = "Cards/Card Data")]
public class CardData : ScriptableObject
{
    // 稳定的配置 ID；多张 CardInstance 可以共用这个 ID，实例身份另由 Guid 区分。
    public string cardId;

    public string cardName;
    public Sprite artwork;

    [TextArea]
    public string description;

    // 保留原字段名用于序列化兼容；cost 只表示 AP，不兼任理智费用。
    public int cost;
    // 玩法分类用于决定费用和所属牌区，不等同于遗留 cardType。
    public SpotlightGameJam.CardCategory category;
    [Min(0)] public int sanityCost;
    // 过载在装载任意义体后解锁；默认 false 保持已有卡牌配置行为。
    public bool requiresEquippedCybernetic;
    // 成功出牌后按数组顺序执行；中途结束战斗会停止后续效果。
    public SpotlightGameJam.CardEffectData[] effects = new SpotlightGameJam.CardEffectData[0];
    // 幻痛使用此标记；所有牌区移出入口都必须遵守它。
    public bool locksInHand;

    // 遗留 cardType 保持兼容；targetType 仍是当前规则使用的目标配置。
    public CardType cardType;
    public CardTargetType targetType;

    /// <summary>
    /// 检查费用、类别、目标和效果配置，返回全部错误；空列表表示配置有效。
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(cardId)) errors.Add("卡牌 ID 不能为空。");
        if (!System.Enum.IsDefined(typeof(CardCategory), category) ||
            !System.Enum.IsDefined(typeof(CardTargetType), targetType)) errors.Add("卡牌类型或目标模式不合法。");
        if (cost < 0 || sanityCost < 0) errors.Add("卡牌费用不能为负数。");
        if (category == CardCategory.Function || category == CardCategory.Cybernetic)
        {
            if (cost != 0 || sanityCost <= 0) errors.Add("功能牌和义体牌必须配置正理智费用，且不消耗 AP。");
        }
        // 锁定特殊牌不能出牌，所以不要求执行效果；可出牌则必须具备完整效果配置。
        if (locksInHand)
        {
            if (category != CardCategory.Special || cost != 0 || sanityCost != 0)
                errors.Add("锁定牌必须是无费用特殊牌。");
        }
        else
        {
            if (effects == null || effects.Length == 0) errors.Add("可出牌必须配置效果。");
            else foreach (var effect in effects)
            {
                if (!effect) errors.Add("效果引用不能为空。");
                else
                {
                    if (effect.Validate() != null) errors.Add(effect.Validate());
                    // 提前发现目标配置冲突，避免开战成功后出现永久不可用的卡牌。
                    if (effect.RequiresTarget && targetType == CardTargetType.None)
                        errors.Add("伤害、护盾和治疗效果必须配置目标模式。");
                }
            }
            // 交互选牌必须独占该卡效果，避免其他效果在支付后改变预先校验的候选牌区。
            if (effects != null && effects.Length > 1 && effects.Any(effect => effect && effect.RequiresChoice))
                errors.Add("交互选牌卡只能配置一个选牌效果。");
        }
        return errors;
    }
}

/// <summary>
/// 保留早期卡牌配置使用的 Attack/Skill/Power 分类。
/// </summary>
/// <remarks>
/// 仅用于兼容原序列化数据；费用与牌区逻辑使用 CardCategory，不要修改已有枚举数值。
/// </remarks>
public enum CardType
{
    Attack = 0,
    Skill = 1,
    Power = 2
}

/// <summary>
/// 声明出牌指令需要的目标形式：无目标、自身、单个敌人或全部敌人。
/// </summary>
/// <remarks>
/// CardPlayService 校验目标，CardEffectData.Targets 展开效果目标；幻痛可在伤害结算时再次重定向。
/// </remarks>
public enum CardTargetType
{
    None = 0,
    Self = 1,
    SingleEnemy = 2,
    AllEnemies = 3
}


