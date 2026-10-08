using System;
using System.Collections.Generic;
using UnityEngine;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 为 EditMode 测试创建并统一销毁临时 ScriptableObject 配置。
    /// </summary>
    /// <remarks>
    /// 提供卡牌、幻痛和战斗上下文工厂，减少测试样板；不作为运行时系统使用。
    /// </remarks>
    internal sealed class TestAssets : IDisposable
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        /// <summary>
        /// 创建并登记临时配置，Dispose 时统一销毁，避免测试污染编辑器。
        /// </summary>
        internal T Create<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); owned.Add(value); return value; }
        /// <summary>
        /// 构造带有效 ID 和明确费用的测试卡牌配置，可按用例覆盖默认值。
        /// </summary>
        internal CardData Card(CardEffectData effect, CardTargetType target = CardTargetType.Self,
            CardCategory category = CardCategory.Basic, int ap = 1, int sanity = 0)
        {
            var card = Create<CardData>(); card.cardId = Guid.NewGuid().ToString();
            card.category = category; card.cost = ap; card.sanityCost = sanity; card.targetType = target;
            card.effects = new[]{effect}; return card;
        }
        /// <summary>
        /// 创建无费用的锁定特殊牌，作为每个测试上下文的幻痛配置。
        /// </summary>
        internal CardData Phantom()
        {
            var card = Create<CardData>(); card.cardId = "phantom-pain"; card.cardName = "幻痛";
            card.category = CardCategory.Special; card.locksInHand = true; return card;
        }
        /// <summary>
        /// 构造单敌人的隔离战斗上下文，使用固定种子保持随机用例可复现。
        /// </summary>
        internal BattleContext Context(CardData[] cards, int seed = 1, int capacity = 13)
        {
            var rules = Create<BattleRules>(); var run = new RunState(rules); var random = new RandomUtility(seed);
            return new BattleContext(run,rules,new[]{new CombatantState("enemy",100)},
                new CardDeck(capacity,Instances(cards),random),
                new CardDeck(5,Array.Empty<CardInstance>(),random),Phantom(),random);
        }
        /// <summary>将卡牌配置转换为独立实例，供测试牌堆构造使用。</summary>
        internal static IEnumerable<CardInstance> Instances(IEnumerable<CardData> cards)
        { foreach (var card in cards) yield return new CardInstance(card); }
        /// <summary>
        /// 直接布置玩家行动阶段、2 AP 和初始手牌，供只关注出牌规则的测试使用。
        /// </summary>
        // 回合流程有独立测试，此工厂步骤仅为出牌测试建立必要状态。
        internal static void Prepare(BattleContext context)
        { context.SetState(BattleState.PlayerAction); context.ActionPoints.BeginTurn(2); context.Ordinary.Draw(3); }
        /// <summary>
        /// 立即销毁工厂创建的临时 Unity 对象；不会修改项目资产。
        /// </summary>
        public void Dispose() { foreach (var value in owned) UnityEngine.Object.DestroyImmediate(value); }
    }
}
