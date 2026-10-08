using System.Collections.Generic;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 保存战斗初始属性、AP 增长、抽牌数量、容量及义体使用上限的共享配置。
    /// </summary>
    /// <remarks>
    /// 在 Inspector 创建并调整；运行时状态读取只读属性，不把当前回合值写回资产。Validate 汇总配置错误。
    /// </remarks>
    [CreateAssetMenu(menuName = "Spotlight/战斗规则")]
    public sealed class BattleRules : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0)] private int maxSanity = 50;
        [SerializeField, Min(0)] private int initialAp = 2;
        [SerializeField, Min(0)] private int apGrowth = 1;
        [SerializeField, Min(1)] private int maxAp = 12;
        [SerializeField, Min(0)] private int ordinaryDraw = 3;
        [SerializeField, Min(0)] private int cyberneticDraw = 1;
        [SerializeField, Min(1)] private int ordinaryCapacity = 13;
        [SerializeField, Min(1)] private int cyberneticCapacity = 5;
        [SerializeField, Min(1)] private int usesPerSlot = 3;
        public int MaxHealth => maxHealth;
        public int MaxSanity => maxSanity;
        public int InitialAp => initialAp;
        public int ApGrowth => apGrowth;
        public int MaxAp => maxAp;
        public int OrdinaryDraw => ordinaryDraw;
        public int CyberneticDraw => cyberneticDraw;
        public int OrdinaryCapacity => ordinaryCapacity;
        public int CyberneticCapacity => cyberneticCapacity;
        public int UsesPerSlot => usesPerSlot;
        /// <summary>检查配置是否合法；返回错误说明，无错误时返回空结果。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (maxHealth <= 0 || maxSanity < 0) errors.Add("角色初始属性不合法。");
            if (initialAp < 0 || apGrowth < 0 || maxAp < initialAp) errors.Add("AP 规则不合法。");
            if (ordinaryDraw < 0 || cyberneticDraw < 0 || ordinaryCapacity <= 0 || cyberneticCapacity <= 0)
                errors.Add("抽牌数量或手牌容量不合法。");
            if (usesPerSlot <= 0) errors.Add("义体使用上限必须大于 0。");
            return errors;
        }
    }
}
