using System;
using System.Linq;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证普罗米修斯费用、下一回合抽弃牌、改造及与旧规则的组合。</summary>
    public sealed class PrometheusRulesTests
    {
        private TestAssets assets;
        /// <summary>每个用例独立创建临时配置。</summary>
        [SetUp] public void SetUp() => assets = new TestAssets();
        /// <summary>销毁临时配置以免影响项目资产。</summary>
        [TearDown] public void TearDown() => assets.Dispose();

        /// <summary>构造普通牌充足、指定义体已装载且首回合已开始的战斗。</summary>
        private BattleController Battle(CardEffectData effect, CyberneticSlot slot, int sanity)
        {
            var heal = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(Enumerable.Repeat(heal, 20).ToArray());
            AddCybernetic(context, effect, slot, sanity);
            var battle = new BattleController(context, 0);
            Assert.That(battle.StartBattle(), Is.True);
            return battle;
        }
        /// <summary>登记有效来源，将一张义体牌加入待抽牌堆。</summary>
        private CardInstance AddCybernetic(BattleContext context, CardEffectData effect, CyberneticSlot slot, int sanity)
        {
            var card = assets.Card(effect, category: CardCategory.Cybernetic, ap: 0, sanity: sanity);
            var data = assets.Create<CyberneticData>(); data.cyberneticId = Guid.NewGuid().ToString();
            data.slot = slot; data.maxDurability = 5; data.cards = new[] { card };
            var source = new CyberneticInstance(data); context.Run.Loadout.Equip(source);
            var instance = new CardInstance(card, source); context.Cybernetic.DrawPile.TryAdd(instance);
            return instance;
        }
        /// <summary>免 AP 对已持有及新抽到基本牌生效，结束行动后恢复原费用。</summary>
        [Test] public void FreeBasicsWorksAtZeroApAndExpiresNextTurn()
        {
            var battle = Battle(assets.Create<FreeBasicCardsEffectData>(), CyberneticSlot.Hands, 2);
            var context = battle.Context;
            Assert.That(battle.TryPlay(context.Cybernetic.Hand.Cards[0]).Success, Is.True);
            context.ActionPoints.TrySpend(2);
            var basic = context.Ordinary.Hand.Cards[0];
            Assert.That(battle.TryPlay(basic).Success, Is.True);
            Assert.That(context.ActionPoints.Total, Is.Zero);
            context.Ordinary.Draw(1);
            Assert.That(battle.TryPlay(context.Ordinary.Hand.Cards.Last()).Success, Is.True);
            battle.EndTurn(); context.ActionPoints.TrySpend(context.ActionPoints.Total);
            Assert.That(battle.TryPlay(context.Ordinary.Hand.Cards[0]).Failure, Is.EqualTo(CardPlayFailure.InsufficientAp));
            Assert.That(basic.Data.cost, Is.EqualTo(1), "不得改写共享费用配置。");
        }
        /// <summary>篡改者仅影响下一玩家回合，弃牌足够时不移动原抽牌堆顶。</summary>
        [Test] public void NextTurnDrawsExistingDiscardsBeforeDrawPile()
        {
            var battle = Battle(assets.Create<DiscardDrawNextTurnEffectData>(), CyberneticSlot.Brain, 8);
            var context = battle.Context;
            var discarded = context.Ordinary.Hand.Cards.ToArray();
            foreach (var card in discarded) context.Ordinary.Discard(card);
            var top = context.Ordinary.Peek(3).ToArray();
            var brain = context.Cybernetic.Hand.Cards.Single();
            Assert.That(battle.TryPlay(brain).Success, Is.True);
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.Ordinary.Hand.Cards.All(discarded.Contains), Is.True);
            Assert.That(context.Ordinary.Peek(3), Is.EqualTo(top));
            Assert.That(context.Cybernetic.Hand.Contains(brain), Is.True, "下一回合可抽回篡改者自身。");
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.Ordinary.Hand.Cards.Skip(3), Is.EqualTo(top));
        }
        /// <summary>持久变量自身先付理智，之后零理智可付血，护盾不扣且恢复理智会解除幻痛。</summary>
        [Test] public void HealthModePaysLifeAtZeroSanityAndRestoresSanity()
        {
            var battle = Battle(assets.Create<HealthPaymentEffectData>(), CyberneticSlot.Legs, 30);
            var context = battle.Context;
            Assert.That(battle.TryPlay(context.Cybernetic.Hand.Cards[0]).Success, Is.True);
            Assert.That(context.Player.Sanity, Is.EqualTo(20));
            Assert.That(context.Player.Health, Is.EqualTo(100));
            var other = AddCybernetic(context, assets.Create<ShieldEffectData>(), CyberneticSlot.Brain, 8);
            context.Cybernetic.TakeFromDrawPile(other);
            context.Player.TrySpendSanity(20); context.Player.AddShield(10);
            Assert.That(context.HasPhantomPain, Is.True);
            Assert.That(battle.TryPlay(other).Success, Is.True);
            Assert.That(context.Player.Health, Is.EqualTo(92));
            Assert.That(context.Player.Sanity, Is.EqualTo(8));
            Assert.That(context.Player.Shield, Is.EqualTo(13));
            Assert.That(context.HasPhantomPain, Is.False);
        }
        /// <summary>血量费用不能致死，拒绝时不移动卡牌也不扣耐久或部位次数。</summary>
        [Test] public void HealthPaymentRejectsLethalCostWithoutSideEffects()
        {
            var battle = Battle(assets.Create<HealthPaymentEffectData>(), CyberneticSlot.Legs, 30);
            var context = battle.Context; battle.TryPlay(context.Cybernetic.Hand.Cards[0]);
            var other = AddCybernetic(context, assets.Create<ShieldEffectData>(), CyberneticSlot.Brain, 8);
            context.Cybernetic.TakeFromDrawPile(other); context.Player.ReceiveDamage(92);
            Assert.That(battle.TryPlay(other).Success, Is.False);
            Assert.That(context.Player.Health, Is.EqualTo(8));
            Assert.That(context.Player.Sanity, Is.EqualTo(20));
            Assert.That(other.Source.Durability, Is.EqualTo(5));
            Assert.That(context.Cybernetic.Hand.Contains(other), Is.True);
            Assert.That(context.Usage.GetUses(CyberneticSlot.Brain), Is.Zero);
        }
        /// <summary>套装尚未实现，腿部血付模式不得改变功能牌过载的理智费用。</summary>
        [Test] public void HealthModeLeavesOverloadPayingSanity()
        {
            var battle = Battle(assets.Create<HealthPaymentEffectData>(), CyberneticSlot.Legs, 30);
            var context = battle.Context; battle.TryPlay(context.Cybernetic.Hand.Cards[0]);
            var overload = assets.Card(assets.Create<ActionPointEffectData>(), CardTargetType.None, CardCategory.Function, 0, 5);
            var card = new CardInstance(overload); context.Ordinary.Hand.TryAdd(card);
            Assert.That(battle.TryPlay(card).Success, Is.True);
            Assert.That(context.Player.Health, Is.EqualTo(100));
            Assert.That(context.Player.Sanity, Is.EqualTo(15));
            Assert.That(context.ActionPoints.Temporary, Is.EqualTo(1));
        }
        /// <summary>改造选择的一张基本牌被永久移除，获得一张独立来源义体牌，不复制整套数量。</summary>
        [Test] public void TransformRemovesSelectedBasicAndAddsSingleNewCybernetic()
        {
            var effect = assets.Create<TransformBasicToCyberneticEffectData>();
            var reward = assets.Create<CyberneticData>(); reward.cyberneticId = "reward"; reward.slot = CyberneticSlot.Brain;
            reward.maxDurability = 3;
            var rewardCard = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            reward.cards = new[] { rewardCard, rewardCard, rewardCard }; effect.candidates = new[] { reward };
            var battle = Battle(effect, CyberneticSlot.Torso, 40); var context = battle.Context;
            var basic = context.Ordinary.Hand.Cards[0];
            var torso = context.Cybernetic.Hand.Cards.Single();
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(torso, choiceId: basic.Id)).Success, Is.True);
            Assert.That(context.Ordinary.Hand.Contains(basic), Is.False);
            Assert.That(context.Ordinary.DiscardPile.Contains(basic), Is.False);
            Assert.That(context.Cybernetic.Hand.Cards.Count(card => card.Data == rewardCard), Is.EqualTo(1));
            Assert.That(context.Run.Loadout.GetUsableCards().Count(card => card.Data == rewardCard), Is.EqualTo(1));
            Assert.That(context.Player.Sanity, Is.EqualTo(10));
        }
        /// <summary>弃牌不足只补足剩余额度，原抽牌堆未抽到的牌保持顺序。</summary>
        [Test] public void DiscardDrawFallsBackOnlyForMissingCardsAndStopsAtCapacity()
        {
            var data = assets.Card(assets.Create<HealEffectData>());
            var deck = new CardDeck(4, TestAssets.Instances(Enumerable.Repeat(data, 6)), new RandomUtility(4));
            deck.Draw(3); var discarded = deck.Hand.Cards[0]; deck.Discard(discarded);
            var top = deck.Peek(3).ToArray();
            Assert.That(deck.Draw(3, true), Is.EqualTo(2));
            Assert.That(deck.Hand.Cards.Skip(2), Is.EqualTo(new[] { discarded, top[0] }));
            Assert.That(deck.Peek(2), Is.EqualTo(top.Skip(1)));
            Assert.That(deck.DiscardPile.Count, Is.Zero);

            var cyber = new CardDeck(1, TestAssets.Instances(Enumerable.Repeat(data, 3)), new RandomUtility(5), true);
            cyber.Draw(1);
            var extra = cyber.DrawPile.Cards[0]; cyber.DrawPile.TryRemove(extra); cyber.DiscardPile.TryAdd(extra);
            Assert.That(cyber.Draw(99, true), Is.Zero);
            Assert.That(cyber.DiscardPile.Count, Is.EqualTo(2));
            Assert.That(cyber.DrawPile.Count, Is.Zero);
        }
        /// <summary>满手理智透支归零时，预留的改造目标不会被幻痛随机弃掉，也不会多弃普通牌。</summary>
        [Test] public void TransformReservesTheChosenBasicBeforePhantomInsertion()
        {
            var effect = assets.Create<TransformBasicToCyberneticEffectData>();
            var reward = assets.Create<CyberneticData>(); reward.cyberneticId = "reserved-reward"; reward.slot = CyberneticSlot.Brain;
            reward.cards = new[] { assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2) };
            effect.candidates = new[] { reward };
            var battle = Battle(effect, CyberneticSlot.Torso, 40); var context = battle.Context;
            context.Ordinary.Draw(10); context.Player.TrySpendSanity(46);
            var basic = context.Ordinary.Hand.Cards[0]; var torso = context.Cybernetic.Hand.Cards.Single();
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(torso, choiceId: basic.Id)).Success, Is.True);
            Assert.That(context.Player.Health, Is.EqualTo(100));
            Assert.That(context.Player.Sanity, Is.Zero);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(13));
            Assert.That(context.HasPhantomPain, Is.True);
            Assert.That(context.Ordinary.DiscardPile.Count, Is.Zero);
            Assert.That(basic.Zone, Is.Null);
        }
        /// <summary>无新义体候选时拒绝改造；临时攻击牌也不能作为永久改造目标。</summary>
        [Test] public void TransformRejectsAlreadyOwnedAndTemporaryTargetsWithoutPayment()
        {
            var effect = assets.Create<TransformBasicToCyberneticEffectData>();
            var reward = assets.Create<CyberneticData>(); reward.cyberneticId = "owned-reward"; reward.slot = CyberneticSlot.Brain;
            reward.cards = new[] { assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2) };
            effect.candidates = new[] { reward };
            var battle = Battle(effect, CyberneticSlot.Torso, 40); var context = battle.Context;
            var torso = context.Cybernetic.Hand.Cards.Single();
            var temporary = new CardInstance(context.Ordinary.Hand.Cards[0].Data, expiresAtTurnEnd: true);
            context.Ordinary.Hand.TryAdd(temporary);
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(torso, choiceId: temporary.Id)).Failure, Is.EqualTo(CardPlayFailure.InvalidChoice));
            var owned = new CyberneticInstance(reward); context.Run.Loadout.Equip(owned);
            while (owned.Durability > 0) owned.TryConsumeDurability();
            Assert.That(battle.GetCardChoices(torso), Is.Empty, "报废也算已拥有。");
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(torso, choiceId: context.Ordinary.Hand.Cards[0].Id)).Failure,
                Is.EqualTo(CardPlayFailure.InvalidChoice));
            Assert.That(context.Player.Sanity, Is.EqualTo(50));
            Assert.That(torso.Source.Durability, Is.EqualTo(5));
            Assert.That(context.Cybernetic.Hand.Contains(torso), Is.True);
        }
        /// <summary>改造来源使用时切换，保留旧来源并共享同部位次数；后续战斗继承一张新牌和剩余耐久。</summary>
        [Test] public void AcquiredCyberneticSharesSlotLimitsAndPersistsWithoutEquipmentSwitch()
        {
            var effect = assets.Create<TransformBasicToCyberneticEffectData>();
            var reward = assets.Create<CyberneticData>(); reward.cyberneticId = "switch-reward"; reward.slot = CyberneticSlot.Brain;
            reward.maxDurability = 3;
            var rewardCard = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            reward.cards = new[] { rewardCard, rewardCard, rewardCard }; effect.candidates = new[] { reward };
            var battle = Battle(effect, CyberneticSlot.Torso, 40); var context = battle.Context;
            var oldBrain = AddCybernetic(context, assets.Create<ShieldEffectData>(), CyberneticSlot.Brain, 2);
            context.Cybernetic.TakeFromDrawPile(oldBrain);
            var basic = context.Ordinary.Hand.Cards[0];
            var initial = Enumerable.Repeat(basic.Data, 20).ToArray();
            var torso = context.Cybernetic.Hand.Cards.Single(card => card.Source.Data.slot == CyberneticSlot.Torso);
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(torso, choiceId: basic.Id)).Success, Is.True);
            var acquired = context.Cybernetic.Hand.Cards.Single(card => card.Data == rewardCard);
            Assert.That(context.Run.Loadout.Get(CyberneticSlot.Brain), Is.SameAs(oldBrain.Source));
            Assert.That(battle.TryPlay(acquired).Success, Is.True);
            Assert.That(context.Run.Loadout.Owns(acquired.Source), Is.True);
            Assert.That(context.GetUsedCyberneticCard(CyberneticSlot.Brain), Is.SameAs(acquired));
            Assert.That(context.Run.Loadout.Owns(oldBrain.Source), Is.True);
            Assert.That(battle.TryPlay(oldBrain).Failure, Is.EqualTo(CardPlayFailure.UnavailableCybernetic));
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(battle.TryPlay(oldBrain).Success, Is.True);
            Assert.That(acquired.Source.Durability, Is.EqualTo(2));
            Assert.That(context.Run.Loadout.Owns(acquired.Source), Is.True);
            context.Enemies[0].ReceiveDamage(1000); context.CheckOutcome(); context.Dispose();

            var random = new RandomUtility(7);
            var next = new BattleContext(context.Run, context.Rules, new[] { new CombatantState("next", 100) },
                new CardDeck(13, TestAssets.Instances(context.Run.ApplyOrdinaryTransformations(initial)), random),
                new CardDeck(5, context.Run.Loadout.GetUsableCards(), random, true), context.PhantomPain, random);
            Assert.That(new BattleController(next, 0).StartBattle(), Is.True);
            Assert.That(next.Ordinary.AllCards.Count(), Is.EqualTo(19));
            Assert.That(next.Cybernetic.AllCards.Count(card => card.Data == rewardCard), Is.EqualTo(1));
            Assert.That(next.Cybernetic.AllCards.Single(card => card.Data == rewardCard).Source, Is.SameAs(acquired.Source));
            next.Dispose();
        }
        /// <summary>生命支付恢复理智封顶，跨玩家回合保留但新战斗清除。</summary>
        [Test] public void HealthModeCapsSanityAndDoesNotCarryToNextEncounter()
        {
            var battle = Battle(assets.Create<HealthPaymentEffectData>(), CyberneticSlot.Legs, 30);
            var context = battle.Context; battle.TryPlay(context.Cybernetic.Hand.Cards[0]);
            var other = AddCybernetic(context, assets.Create<ShieldEffectData>(), CyberneticSlot.Brain, 8);
            context.Cybernetic.TakeFromDrawPile(other); context.Player.RestoreSanity(29);
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(battle.TryPlay(other).Success, Is.True);
            Assert.That(context.Player.Sanity, Is.EqualTo(50));
            Assert.That(context.Player.Health, Is.EqualTo(92));
            context.Enemies[0].ReceiveDamage(1000); context.CheckOutcome(); context.Dispose();
            var random = new RandomUtility(8);
            var next = new BattleContext(context.Run, context.Rules, new[] { new CombatantState("next", 100) },
                new CardDeck(13, Array.Empty<CardInstance>(), random),
                new CardDeck(5, context.Run.Loadout.GetUsableCards(), random, true), context.PhantomPain, random);
            Assert.That(new BattleController(next, 0).StartBattle(), Is.True);
            Assert.That(next.TurnEffects.CyberneticsPayHealth, Is.False);
            next.Dispose();
        }
        /// <summary>改造初始奖励基本牌时只删除奖励来源，不减少基础牌组中的同配置牌。</summary>
        [Test] public void TransformingInitialRewardDoesNotRemoveMatchingBaseDeckCard()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var effect = assets.Create<TransformBasicToCyberneticEffectData>();
            var reward = assets.Create<CyberneticData>(); reward.cyberneticId = "reward-origin"; reward.slot = CyberneticSlot.Brain;
            reward.cards = new[] { assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2) };
            effect.candidates = new[] { reward };
            var context = assets.Context(Enumerable.Repeat(basic, 4).ToArray());
            context.Run.AddInitialHandReward(basic);
            AddCybernetic(context, effect, CyberneticSlot.Torso, 40);
            var battle = new BattleController(context, 0); Assert.That(battle.StartBattle(), Is.True);
            var initialReward = context.Ordinary.Hand.Cards.Single(card => card.IsInitialHandReward);
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(context.Cybernetic.Hand.Cards[0], choiceId: initialReward.Id)).Success, Is.True);
            Assert.That(context.Run.InitialHandRewards, Is.Empty);
            Assert.That(context.Run.ApplyOrdinaryTransformations(Enumerable.Repeat(basic, 4)).Count(), Is.EqualTo(4));
            Assert.That(context.Ordinary.AllCards.Count(), Is.EqualTo(4));
            context.Dispose();
        }
    }
}
