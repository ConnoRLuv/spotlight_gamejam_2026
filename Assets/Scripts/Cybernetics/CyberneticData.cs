using System.Collections.Generic;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存一种义体的共享配置：部位、最大耐久和关联卡牌。
    /// </summary>
    /// <remarks>
    /// cards 的每个条目对应一张战斗牌，可重复引用同一配置；当前耐久保存在 CyberneticInstance，牌的费用由 CardData 定义。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/义体配置")]
    public sealed class CyberneticData : ScriptableObject
    {
        public string cyberneticId;
        public CyberneticSlot slot;
        [Min(1)] public int maxDurability = 3;
        // 可重复引用同一配置以表示多张牌；理智费用统一定义在各 CardData 中。
        public CardData[] cards = new CardData[0];
        /// <summary>
        /// 校验部位、耐久及每张关联卡，构建义体牌堆前必须检查此结果。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(cyberneticId)) errors.Add("义体 ID 不能为空。");
            if (maxDurability <= 0) errors.Add("义体耐久必须大于 0。");
            if (!System.Enum.IsDefined(typeof(CyberneticSlot), slot)) errors.Add("未知义体部位。");
            if (cards == null || cards.Length == 0) errors.Add("义体必须配置关联卡。");
            // 在创建运行时义体和牌堆前发现关联卡错误，保证入口可返回可读失败原因。
            else foreach (var card in cards)
            {
                if (!card) errors.Add("义体卡引用不能为空。");
                else
                {
                    errors.AddRange(card.Validate());
                    if (card.category != CardCategory.Cybernetic || card.locksInHand)
                        errors.Add("义体只能关联非锁定义体牌。");
                }
            }
            return errors;
        }
    }
}
