using System;
using NUnit.Framework;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证基础效果、出牌拒绝、费用支付、幻痛及义体来源限制。
    /// </summary>
    /// <remarks>
    /// 通过 CardPlayService 检查可观察结果；TestAssets.Prepare 用于隔离回合流程，单独测试出牌规则。
    /// </remarks>
    public sealed class CardPlayTests
    {
        private TestAssets assets;
        private readonly CardPlayService service = new CardPlayService();
        /// <summary>为用例创建独立的测试状态，避免用例之间共享可变资源。</summary>
        /// <summary>清理测试创建的临时对象和状态，避免污染后续用例。</summary>
        /// <summary>验证攻击扣除一点行动点并造成六点伤害。</summary>
        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();
        [Test] public void AttackDealsSixAndSpendsOneAp()
        {
            var data = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.SingleEnemy);
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            var card = context.Ordinary.Hand.Cards[0];
            Assert.That(service.TryPlay(context,card,context.Enemies[0]).Success, Is.True);
            Assert.That(context.Enemies[0].Health, Is.EqualTo(94));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(1));
            Assert.That(context.Ordinary.DiscardPile.Contains(card), Is.True);
        }
        /// <summary>验证防御与回复分别为玩家增加三点护盾和生命。</summary>
        [Test] public void ShieldAndHealApplyThreeToPlayer()
        {
            var shield = assets.Card(assets.Create<ShieldEffectData>());
            var heal = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[]{shield,heal}); TestAssets.Prepare(context);
            context.Player.ReceiveDamage(10);
            foreach(var card in new System.Collections.Generic.List<CardInstance>(context.Ordinary.Hand.Cards))
                Assert.That(service.TryPlay(context,card).Success, Is.True);
            Assert.That(context.Player.Shield, Is.EqualTo(3));
            Assert.That(context.Player.Health, Is.EqualTo(93));
            Assert.That(context.ActionPoints.Total, Is.Zero);
        }
        /// <summary>验证过载只支付理智费用，并增加一点临时行动点。</summary>
        [Test] public void OverloadSpendsOnlySanityAndAddsTemporaryAp()
        {
            var cardData = assets.Card(assets.Create<ActionPointEffectData>(),CardTargetType.None,CardCategory.Function,0,4);
            var context = assets.Context(new[]{cardData}); TestAssets.Prepare(context);
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0]).Success, Is.True);
            Assert.That(context.Player.Sanity, Is.EqualTo(46));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
            Assert.That(context.ActionPoints.Temporary, Is.EqualTo(1));
        }
        /// <summary>验证非法目标和不属于当前手牌的卡牌不会扣除资源。</summary>
        [Test] public void RejectedTargetAndUnownedCardDoNotSpendAnything()
        {
            var data = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.SingleEnemy);
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0],new CombatantState("foreign",10)).Failure,
                Is.EqualTo(CardPlayFailure.InvalidTarget));
            Assert.That(service.TryPlay(context,new CardInstance(data),context.Enemies[0]).Failure,
                Is.EqualTo(CardPlayFailure.CardNotInHand));
            Assert.That(context.ActionPoints.Total, Is.EqualTo(2));
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1));
        }
        /// <summary>验证行动点或理智不足时保留原手牌及资源。</summary>
        [Test] public void InsufficientApAndSanityDoNotChangeHand()
        {
            var data = assets.Card(assets.Create<HealEffectData>(),ap:3);
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            var card = context.Ordinary.Hand.Cards[0];
            Assert.That(service.TryPlay(context,card).Failure, Is.EqualTo(CardPlayFailure.InsufficientAp));
            card.TemporaryApCost = 1; data.sanityCost = 51;
            Assert.That(service.TryPlay(context,card).Failure, Is.EqualTo(CardPlayFailure.InsufficientSanity));
            Assert.That(context.Player.Sanity, Is.EqualTo(50));
            Assert.That(context.ActionPoints.Total, Is.EqualTo(2));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.True);
        }
        /// <summary>验证错误配置及锁定卡牌不能进入出牌结算。</summary>
        [Test] public void InvalidConfigurationAndLockedCardAreRejected()
        {
            var data = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            var card = context.Ordinary.Hand.Cards[0]; data.effects = Array.Empty<CardEffectData>();
            Assert.That(service.TryPlay(context,card).Failure, Is.EqualTo(CardPlayFailure.InvalidConfiguration));
            data.locksInHand = true;
            Assert.That(service.TryPlay(context,card).Failure, Is.EqualTo(CardPlayFailure.LockedCard));
            Assert.That(context.ActionPoints.Total, Is.EqualTo(2));
        }
        /// <summary>验证负数效果和未明确配置费用的功能牌被拒绝。</summary>
        [Test] public void NegativeEffectAndUnconfiguredFunctionCostAreRejected()
        {
            var damage = assets.Create<DamageEffectData>(); damage.amount = -1;
            var data = assets.Card(damage,CardTargetType.SingleEnemy);
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0],context.Enemies[0]).Failure,
                Is.EqualTo(CardPlayFailure.InvalidConfiguration));
            damage.amount = 6; data.category = CardCategory.Function; data.cost = 0;
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0],context.Enemies[0]).Failure,
                Is.EqualTo(CardPlayFailure.InvalidConfiguration));
        }
        /// <summary>验证理智归零时先生成锁定幻痛，再继续同张牌的伤害效果。</summary>
        [Test] public void SanityAtZeroLocksPhantomBeforeSameCardDamage()
        {
            var data = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.SingleEnemy,CardCategory.Function,0,50);
            var context = assets.Context(new[]{data},capacity:1); TestAssets.Prepare(context);
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0],context.Enemies[0]).Success, Is.True);
            Assert.That(context.HasPhantomPain, Is.True);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1));
            Assert.That(context.Player.Sanity, Is.Zero);
            // Any direct player damage must use the same random resolver.
            for(int i=0;i<12;i++) DamageResolver.Apply(context,context.Player,context.Enemies[0],1);
            Assert.That(context.Player.Health, Is.LessThan(100));
        }
        /// <summary>验证伤害仅选择存活参战者，并支持全体敌人目标。</summary>
        [Test] public void DamageSelectsOnlyAliveParticipantsAndAllEnemiesIsSupported()
        {
            var data = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.AllEnemies);
            var context = assets.Context(new[]{data}); TestAssets.Prepare(context);
            Assert.That(service.TryPlay(context,context.Ordinary.Hand.Cards[0]).Success, Is.True);
            Assert.That(context.Enemies[0].Health, Is.EqualTo(94));
            context.Ordinary.EnsureLockedCard(context.PhantomPain);
            context.Enemies[0].ReceiveDamage(200);
            // Finished battles cannot receive further damage.
            context.CheckOutcome();
            Assert.That(DamageResolver.Apply(context,context.Player,context.Player,1), Is.Zero);
        }
        /// <summary>验证抽牌效果将一张可用牌加入手牌。</summary>
        [Test] public void DrawEffectAddsOneAvailableCard()
        {
            var data = assets.Card(assets.Create<DrawEffectData>(),CardTargetType.None);
            var heal = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[]{data,heal,heal,heal});
            context.SetState(BattleState.PlayerAction); context.ActionPoints.BeginTurn(2);
            var drawCard = System.Linq.Enumerable.First(context.Ordinary.DrawPile.Cards,c => c.Data == data);
            context.Ordinary.DrawPile.TryRemove(drawCard); context.Ordinary.Hand.TryAdd(drawCard);
            int totalBefore = context.Ordinary.Hand.Count + context.Ordinary.DrawPile.Count + context.Ordinary.DiscardPile.Count;
            Assert.That(service.TryPlay(context,drawCard).Success, Is.True);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1));
            Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(2));
            Assert.That(context.Ordinary.DiscardPile.Contains(drawCard), Is.True);
            Assert.That(context.Ordinary.Hand.Count + context.Ordinary.DrawPile.Count + context.Ordinary.DiscardPile.Count,
                Is.EqualTo(totalBefore));
        }
        /// <summary>验证义体使用失败时不扣资源、耐久及使用次数。</summary>
        [Test] public void CyberneticFailuresDoNotSpendResourcesOrDurability()
        {
            var data = assets.Card(assets.Create<ShieldEffectData>(),CardTargetType.Self,CardCategory.Cybernetic,0,2);
            var config = assets.Create<CyberneticData>(); config.cyberneticId = "hands";
            config.slot = CyberneticSlot.Hands; config.maxDurability = 5; config.cards = new[]{data};
            var instance = new CyberneticInstance(config);
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules); var random = new RandomUtility(1);
            run.Loadout.Equip(instance);
            var context = new BattleContext(run,rules,new[]{new CombatantState("e",100)},
                new CardDeck(13,Array.Empty<CardInstance>(),random),
                new CardDeck(5,new[]{new CardInstance(data,instance),new CardInstance(data,instance)},random),
                assets.Phantom(),random);
            context.SetState(BattleState.PlayerAction); context.ActionPoints.BeginTurn(2); context.Cybernetic.Draw(2);
            Assert.That(service.TryPlay(context,context.Cybernetic.Hand.Cards[0]).Success, Is.True);
            var second = context.Cybernetic.Hand.Cards[0];
            Assert.That(service.TryPlay(context,second).Failure, Is.EqualTo(CardPlayFailure.UnavailableCybernetic));
            Assert.That(instance.Durability, Is.EqualTo(4)); Assert.That(context.Player.Sanity, Is.EqualTo(48));
            Assert.That(context.Cybernetic.Hand.Contains(second), Is.True);
            run.Loadout.Equip(new CyberneticInstance(config));
            context.Usage.BeginTurn();
            Assert.That(service.TryPlay(context,second).Failure, Is.EqualTo(CardPlayFailure.UnavailableCybernetic));
        }
    }
}
