using System;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证装备栏只记录成功使用的义体，失败/取消不显示，战斗切换时重新清空。</summary>
    public sealed class CyberneticEquipmentDisplayTests
    {
        /// <summary>读取战斗上下文记录的已使用义体牌，验证装备栏展示依据。</summary>
        private static CardInstance Displayed(BattleContext context, CyberneticSlot slot)
        {
            var method = typeof(BattleContext).GetMethod("GetUsedCyberneticCard");
            Assert.That(method, Is.Not.Null, "尚未实现义体使用后的装备栏状态。");
            return (CardInstance)method.Invoke(context, new object[] { slot });
        }
        /// <summary>创建当前测试所需的配置和战斗对象。</summary>
        private static BattleContext Create(TestAssets assets, out CardInstance card, bool choice = false)
        {
            var data = assets.Card(choice ? (CardEffectData)assets.Create<CyberneticPeekEffectData>() : assets.Create<ShieldEffectData>(),
                CardTargetType.Self, CardCategory.Cybernetic, 0, 4);
            var cyber = assets.Create<CyberneticData>(); cyber.cyberneticId = "display-test";
            cyber.slot = CyberneticSlot.Torso; cyber.maxDurability = 5; cyber.cards = new[] { data };
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
            var source = new CyberneticInstance(cyber); run.Loadout.Equip(source); card = new CardInstance(data, source);
            var random = new RandomUtility(1);
            return new BattleContext(run, rules, new[] { new CombatantState("enemy", 30) },
                new CardDeck(13, Array.Empty<CardInstance>(), random), new CardDeck(5, new[] { card }, random), assets.Phantom(), random);
        }
        /// <summary>验证装备槽在义体成功使用前为空，成功后记录卡牌。</summary>
        [Test]
        public void SlotStaysEmptyUntilSuccessfulCyberneticPlay()
        {
            using (var assets = new TestAssets())
            {
                var context = Create(assets, out var card); var battle = new BattleController(context, 0);
                Assert.That(battle.StartBattle(), Is.True);
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.Null);
                Assert.That(battle.TryPlay(card).Success, Is.True);
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.SameAs(card));
                Assert.That(card.Source.Durability, Is.EqualTo(4));
                Assert.That(Displayed(context, CyberneticSlot.Brain), Is.Null);
                Assert.That(battle.EndTurn(), Is.True);
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.SameAs(card), "跨回合保留已使用的义体。");
            }
        }
        /// <summary>验证失败的义体出牌不会填充装备槽。</summary>
        [Test]
        public void FailedCyberneticPlayDoesNotPopulateTheSlot()
        {
            using (var assets = new TestAssets())
            {
                var context = Create(assets, out var card); var battle = new BattleController(context, 0); battle.StartBattle();
                context.Player.TrySpendSanity(49);
                Assert.That(battle.TryPlay(card).Failure, Is.EqualTo(CardPlayFailure.InsufficientSanity));
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.Null);
                Assert.That(card.Source.Durability, Is.EqualTo(5));
            }
        }
        /// <summary>验证等待脑机选牌时装备槽仍为空。</summary>
        [Test]
        public void WaitingForChoiceDoesNotPopulateTheSlot()
        {
            using (var assets = new TestAssets())
            {
                var context = Create(assets, out var card, true); var battle = new BattleController(context, 0); battle.StartBattle();
                Assert.That(battle.TryPlay(card).Failure, Is.EqualTo(CardPlayFailure.ChoiceRequired));
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.Null);
                Assert.That(card.Source.Durability, Is.EqualTo(5));
            }
        }
        /// <summary>验证新战斗清空已使用卡牌展示，同时保留义体耐久。</summary>
        [Test]
        public void NewBattleClearsDisplayButKeepsPersistentDurability()
        {
            using (var assets = new TestAssets())
            {
                var context = Create(assets, out var card); var battle = new BattleController(context, 0); battle.StartBattle(); battle.TryPlay(card);
                var random = new RandomUtility(2);
                var next = new BattleContext(context.Run, context.Rules, new[] { new CombatantState("enemy2", 30) },
                    new CardDeck(13, Array.Empty<CardInstance>(), random), new CardDeck(5, context.Run.Loadout.GetUsableCards(), random), context.PhantomPain, random);
                Assert.That(Displayed(next, CyberneticSlot.Torso), Is.Null);
                Assert.That(card.Source.Durability, Is.EqualTo(4));
                Assert.That(Displayed(context, CyberneticSlot.Torso), Is.SameAs(card));
            }
        }
    }
}
