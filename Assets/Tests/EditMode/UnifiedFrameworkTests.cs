using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证所有套装使用相同的成功出牌记录，以及交互面板与攻击减费的框架契约。</summary>
    public sealed class UnifiedFrameworkTests
    {
        /// <summary>地图进度由探索会话持有，绑定地图不重置玩家资源；新探索才清空旧地图。</summary>
        [Test]
        public void SessionRetainsMapAndResourcesUntilANewAdventure()
        {
            using (var assets = new TestAssets())
            {
                var setup = assets.Create<AdventureSetupData>();
                setup.rules = assets.Create<BattleRules>(); setup.phantomPain = assets.Phantom();
                var owner = new GameObject("Map session test");
                try
                {
                    var session = owner.AddComponent<AdventureSession>();
                    Assert.That(session.TryBegin(setup, Array.Empty<CyberneticData>()), Is.True);
                    session.Run.AddGold(321); session.Run.Player.ReceiveDamage(7); session.Run.Player.TrySpendSanity(3);
                    var mapType = Type.GetType("RoguelikeMap.Runtime.MapRunState, RoguelikeMap.Core", true);
                    var graphType = Type.GetType("RoguelikeMap.Model.MapGraph, RoguelikeMap.Core", true);
                    var map = Activator.CreateInstance(mapType, Activator.CreateInstance(graphType));
                    var bind = typeof(AdventureSession).GetMethod("TryBindMap");
                    Assert.That(bind, Is.Not.Null, "地图应绑定到跨场景探索会话。");
                    Assert.That(bind.Invoke(session, new[] { map }), Is.True);
                    Assert.That(typeof(AdventureSession).GetProperty("Map").GetValue(session), Is.SameAs(map));
                    Assert.That(session.Run.Gold, Is.EqualTo(321));
                    Assert.That(session.Run.Player.Health, Is.EqualTo(setup.rules.MaxHealth - 7));
                    Assert.That(session.Run.Player.Sanity, Is.EqualTo(setup.rules.MaxSanity - 3));
                    Assert.That(session.TryBegin(setup, Array.Empty<CyberneticData>()), Is.True);
                    Assert.That(typeof(AdventureSession).GetProperty("Map").GetValue(session), Is.Null);
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); }
            }
        }

        /// <summary>携带全套不会激活；四部位分散在两个回合也不能触发。</summary>
        [Test]
        public void CarryingOrPlayingAcrossTurnsDoesNotActivateSet()
        {
            WithSet((context, parts, cards, service) =>
            {
                Assert.That(context.Run.SetBonuses.Active, Is.Empty);
                Assert.That(service.TryPlay(context, cards[0]).Success, Is.True);
                context.Run.Buffs.EndTurn();
                context.Run.Buffs.BeginTurn(); context.Usage.BeginTurn();
                for (int i = 1; i < 4; i++) Assert.That(service.TryPlay(context, cards[i]).Success, Is.True);
                Assert.That(context.Run.SetBonuses.Active, Is.Empty);
            });
        }

        /// <summary>成功四部位激活统一 buff，最后一点耐久消耗不撤销效果；战斗结束后清除。</summary>
        [Test]
        public void FourSuccessfulPartsActivateUntilBattleEnds()
        {
            WithSet((context, parts, cards, service) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(service.TryPlay(context, cards[i]).Success, Is.True);
                    Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(i == 3 ? 1 : 0));
                }
                Assert.That(cards.All(card => card.Source.Durability == 0), Is.True);
                context.Run.Buffs.EndTurn(); context.Run.Buffs.BeginTurn();
                Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(1));
                context.Run.Buffs.EndBattle();
                Assert.That(context.Run.SetBonuses.Active, Is.Empty);
            });
        }

        /// <summary>第四部位出牌失败不能激活；修复后成功使用才完成组合。</summary>
        [Test]
        public void FailedFourthPartDoesNotActivateOrConsumeResources()
        {
            WithSet((context, parts, cards, service) =>
            {
                for (int i = 0; i < 3; i++) Assert.That(service.TryPlay(context, cards[i]).Success, Is.True);
                cards[3].Source.TryConsumeDurability();
                int sanity = context.Player.Sanity;
                Assert.That(service.TryPlay(context, cards[3]).Success, Is.False);
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
                Assert.That(context.Run.SetBonuses.Active, Is.Empty);
                cards[3].Source.Repair(1);
                Assert.That(service.TryPlay(context, cards[3]).Success, Is.True);
                Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(1));
            });
        }

        /// <summary>重排面板必须提供基类秒数显隐，并在关闭时立即释放射线。</summary>
        [Test]
        public void ReorderPanelUsesSharedVisibilityContract()
        {
            var type = Type.GetType("SpotlightGameJam.UI.NewWorldBrainPanel, SpotlightGameJam.UI", true);
            var root = new GameObject("Reorder panel test", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var panel = root.AddComponent(type);
                var hide = type.GetMethod("Hide");
                Assert.That(hide, Is.Not.Null, "重排面板应继承 BasePanel。");
                hide.Invoke(panel, new object[] { 0f });
                Assert.That(root.activeSelf, Is.False);
                Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>只免除攻击基本牌的 AP，防御与功能牌保持原费用，回合结束清除。</summary>
        [Test]
        public void FreeAttackEffectUsesExistingCostAndTurnRules()
        {
            using (var assets = new TestAssets())
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                var type = typeof(CardEffectData).Assembly.GetType("SpotlightGameJam.FreeAttackCardsEffectData");
                Assert.That(type, Is.Not.Null, "攻击减费应通过 CardEffectData 接入现有战斗。");
                var effect = (CardEffectData)ScriptableObject.CreateInstance(type);
                try
                {
                    var attackData = assets.Card(assets.Create<DamageEffectData>(), CardTargetType.SingleEnemy);
                    attackData.cardType = CardType.Attack;
                    var attack = new CardInstance(attackData);
                    var defendData = assets.Card(assets.Create<ShieldEffectData>());
                    defendData.cardType = CardType.Skill;
                    var defend = new CardInstance(defendData);
                    effect.Execute(context, null, context.Player);
                    Assert.That(context.GetEffectiveApCost(attack), Is.Zero);
                    Assert.That(context.GetEffectiveApCost(defend), Is.EqualTo(1));
                    context.TurnEffects.EndPlayerAction();
                    Assert.That(context.GetEffectiveApCost(attack), Is.EqualTo(1));
                }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
        }

        /// <summary>创建独立的四部位、共享耐久与统一成功出牌入口，避免直接调用牌效伪造触发。</summary>
        private static void WithSet(Action<BattleContext, CyberneticData[], CardInstance[], CardPlayService> test)
        {
            using (var assets = new TestAssets())
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                var parts = Enumerable.Range(0, 4).Select(index =>
                {
                    var part = assets.Create<CyberneticData>();
                    part.cyberneticId = "unified-" + index; part.slot = (CyberneticSlot)index;
                    part.maxDurability = 1;
                    part.cards = new[] { assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 1) };
                    context.Run.Loadout.AddInitial(part); return part;
                }).ToArray();
                var bonus = assets.Create<NewWorldSetBonusData>();
                bonus.setId = "unified-set"; bonus.setName = "统一测试套装"; bonus.requiredParts = parts;
                context.Run.SetBonuses.Configure(new[] { bonus });
                TestAssets.Prepare(context); context.Run.Buffs.BeginTurn();
                var cards = context.Run.Loadout.GetUsableCards().ToArray();
                foreach (var card in cards) context.Cybernetic.Hand.TryAdd(card);
                test(context, parts, cards, new CardPlayService());
            }
        }
    }
}
