using System;
using System.Linq;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证四种军用义体的真实规则；反射探针使未实现效果产生明确断言失败。</summary>
    public sealed class CyberneticEffectTests
    {
        private TestAssets assets;
        /// <summary>为用例创建独立的测试状态，避免用例之间共享可变资源。</summary>
        /// <summary>清理测试创建的临时对象和状态，避免污染后续用例。</summary>
        /// <summary>验证生命维持效果使伤害优先消耗理智，再扣生命。</summary>
        [SetUp] public void SetUp() { assets = new TestAssets(); }
        [TearDown] public void TearDown() { assets.Dispose(); }

        [Test]
        public void LifeSupportConvertsDamageToSanityBeforeHealth()
        {
            var type = Type.GetType("SpotlightGameJam.DamageToSanityEffectData, SpotlightGameJam.Runtime");
            Assert.That(type, Is.Not.Null, "生命维持器效果尚未实现。");
            var effect = (CardEffectData)UnityEngine.ScriptableObject.CreateInstance(type);
            try
            {
                var data = assets.Card(effect);
                var context = assets.Context(new[] { data });
                TestAssets.Prepare(context);
                effect.Execute(context, context.Ordinary.Hand.Cards[0], context.Player);
                context.Player.AddShield(3);
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 9);
                Assert.That(context.Player.Health, Is.EqualTo(100));
                Assert.That(context.Player.Sanity, Is.EqualTo(44));
                Assert.That(context.Player.Shield, Is.Zero);
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
                Assert.That(context.Player.Health, Is.EqualTo(98), "保护仅生效一次。");
            }
            finally { UnityEngine.Object.DestroyImmediate(effect); }
        }

        /// <summary>配置真实来源和牌堆，通过公开战斗入口测试费用及效果。</summary>
        private BattleController Battle(CardEffectData effect, CyberneticSlot slot, int sanity, int copies = 4)
        {
            var data = assets.Card(effect, CardTargetType.Self, CardCategory.Cybernetic, 0, sanity);
            var cyber = assets.Create<CyberneticData>();
            cyber.cyberneticId = slot.ToString(); cyber.slot = slot; cyber.maxDurability = slot <= CyberneticSlot.Torso ? 5 : 3;
            cyber.cards = Enumerable.Repeat(data, copies).ToArray();
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
            run.Loadout.Equip(new CyberneticInstance(cyber));
            var random = new RandomUtility(7);
            var attack = assets.Card(assets.Create<DamageEffectData>(), CardTargetType.SingleEnemy);
            var context = new BattleContext(run, rules, new[] { new CombatantState("enemy", 100) },
                new CardDeck(13, TestAssets.Instances(Enumerable.Repeat(attack, 20)), random),
                new CardDeck(5, run.Loadout.GetUsableCards(), random), assets.Phantom(), random);
            var battle = new BattleController(context, 6);
            Assert.That(battle.StartBattle(), Is.True);
            return battle;
        }

        /// <summary>验证脑机取得指定实例，并保留其余候选牌的顺序。</summary>
        [Test] public void BrainSelectsExactInstanceAndPreservesOtherOrder()
        {
            var battle = Battle(assets.Create<CyberneticPeekEffectData>(), CyberneticSlot.Brain, 8);
            var context = battle.Context; var card = context.Cybernetic.Hand.Cards[0];
            var choices = battle.GetCyberneticChoices(card);
            var before = context.Cybernetic.DrawPile.Cards.ToArray();
            Assert.That(choices.Count, Is.EqualTo(3));
            Assert.That(battle.TryPlay(card).Failure, Is.EqualTo(CardPlayFailure.ChoiceRequired));
            Assert.That(context.Player.Sanity, Is.EqualTo(50));
            var chosen = choices[1];
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: chosen.Id)).Success, Is.True);
            Assert.That(context.Cybernetic.Hand.Cards.Single(), Is.SameAs(chosen));
            Assert.That(context.Cybernetic.DrawPile.Cards, Is.EqualTo(before.Where(value => value != chosen)));
            Assert.That(context.Player.Sanity, Is.EqualTo(42));
            Assert.That(card.Source.Durability, Is.EqualTo(4));
            Assert.That(context.Usage.GetUses(CyberneticSlot.Brain), Is.EqualTo(1));
        }

        /// <summary>验证已经失效的脑机选择被拒绝，且不扣除费用。</summary>
        [Test] public void BrainRejectsStaleChoiceWithoutPayment()
        {
            var battle = Battle(assets.Create<CyberneticPeekEffectData>(), CyberneticSlot.Brain, 8);
            var context = battle.Context; var card = context.Cybernetic.Hand.Cards[0];
            var chosen = battle.GetCyberneticChoices(card)[0];
            context.Cybernetic.Draw(1);
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: chosen.Id)).Failure, Is.EqualTo(CardPlayFailure.InvalidChoice));
            Assert.That(context.Player.Sanity, Is.EqualTo(50));
            Assert.That(card.Source.Durability, Is.EqualTo(5));
            Assert.That(context.Cybernetic.Hand.Contains(card), Is.True);
        }

        /// <summary>验证脑机仅查看当前抽牌堆中的牌，不因查看触发重洗。</summary>
        [TestCase(1, 0)] [TestCase(2, 1)] [TestCase(3, 2)]
        public void BrainOnlyPeeksExistingCards(int copies, int remaining)
        {
            var battle = Battle(assets.Create<CyberneticPeekEffectData>(), CyberneticSlot.Brain, 8, copies);
            var card = battle.Context.Cybernetic.Hand.Cards[0];
            Assert.That(battle.GetCyberneticChoices(card).Count, Is.EqualTo(remaining));
            Assert.That(battle.Context.Cybernetic.DrawPile.Count, Is.EqualTo(remaining));
            if (remaining == 0)
            {
                Assert.That(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: Guid.NewGuid())).Success, Is.False);
                Assert.That(battle.Context.Player.Sanity, Is.EqualTo(50));
            }
        }

        /// <summary>验证义体手牌满时，可利用脑机使用后腾出的空间接收候选牌。</summary>
        [Test] public void BrainCanReplaceItselfWhenCyberneticHandIsFull()
        {
            var battle = Battle(assets.Create<CyberneticPeekEffectData>(), CyberneticSlot.Brain, 8, 8);
            var context = battle.Context; var card = context.Cybernetic.Hand.Cards[0];
            context.Cybernetic.Draw(4);
            Assert.That(context.Cybernetic.Hand.Count, Is.EqualTo(5));
            var chosen = battle.GetCyberneticChoices(card)[0];
            Assert.That(battle.TryPlayRequest(new CardPlayRequest(card, choiceId: chosen.Id)).Success, Is.True);
            Assert.That(context.Cybernetic.Hand.Count, Is.EqualTo(5));
            Assert.That(context.Cybernetic.Hand.Contains(chosen), Is.True);
        }

        /// <summary>验证护盾先吸收伤害，理智不足时剩余伤害扣生命。</summary>
        [Test] public void LifeSupportShieldCanPreserveProtectionAndInsufficientSanityCostsHealth()
        {
            var battle = Battle(assets.Create<DamageToSanityEffectData>(), CyberneticSlot.Torso, 4);
            var context = battle.Context;
            Assert.That(battle.TryPlay(context.Cybernetic.Hand.Cards[0]).Success, Is.True);
            context.Player.AddShield(3);
            DamageResolver.Apply(context, context.Enemies[0], context.Player, 3);
            Assert.That(context.TurnEffects.DamageToSanityArmed, Is.True);
            Assert.That(context.Player.Sanity, Is.EqualTo(46));
            DamageResolver.Apply(context, context.Enemies[0], context.Player, 50);
            Assert.That(context.Player.Sanity, Is.Zero);
            Assert.That(context.Player.Health, Is.EqualTo(96));
            Assert.That(context.HasPhantomPain, Is.True);
            Assert.That(context.TurnEffects.DamageToSanityArmed, Is.False);
        }

        /// <summary>验证承伤保护在下一个玩家回合开始时失效。</summary>
        [Test] public void LifeSupportExpiresAtNextPlayerTurn()
        {
            var battle = Battle(assets.Create<DamageToSanityEffectData>(), CyberneticSlot.Torso, 4);
            var context = battle.Context;
            battle.TryPlay(context.Cybernetic.Hand.Cards[0]);
            context.Player.AddShield(6);
            battle.EndTurn();
            Assert.That(context.Player.Health, Is.EqualTo(100));
            Assert.That(context.TurnEffects.DamageToSanityArmed, Is.False);
            DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
            Assert.That(context.Player.Health, Is.EqualTo(98));
        }

        /// <summary>构造启用手部攻击转换所需的战斗状态。</summary>
        private BattleController HandsBattle()
        {
            var effect = assets.Create<AttackConversionEffectData>();
            effect.attack = assets.Card(assets.Create<DamageEffectData>(), CardTargetType.SingleEnemy);
            var battle = Battle(effect, CyberneticSlot.Hands, 2);
            Assert.That(battle.TryPlay(battle.Context.Cybernetic.Hand.Cards[0]).Success, Is.True);
            return battle;
        }

        /// <summary>验证攻击转换优先支付临时行动点，生成攻击使用后移除。</summary>
        [Test] public void ConversionPaysTemporaryApAndGeneratedAttackIsRemovedAfterUse()
        {
            var battle = HandsBattle(); var context = battle.Context;
            context.ActionPoints.AddTemporary(1);
            Assert.That(battle.TryConvertApToAttack().Success, Is.True);
            Assert.That(context.ActionPoints.Temporary, Is.Zero);
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
            var generated = context.Ordinary.Hand.Cards.Single(card => card.ExpiresAtTurnEnd);
            Assert.That(generated.EffectiveApCost, Is.Zero);
            Assert.That(battle.TryPlay(generated, context.Enemies[0]).Success, Is.True);
            Assert.That(context.Enemies[0].Health, Is.EqualTo(94));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
            Assert.That(context.Ordinary.AllCards.Contains(generated), Is.False);
            Assert.That(context.Player.Sanity, Is.EqualTo(48));
        }

        /// <summary>验证回合结束时清理未使用的生成牌并关闭转换。</summary>
        [Test] public void UnusedGeneratedCardsExpireAndConversionStopsAfterEndTurn()
        {
            var battle = HandsBattle(); var context = battle.Context;
            battle.TryConvertApToAttack();
            var generated = context.Ordinary.Hand.Cards.Single(card => card.ExpiresAtTurnEnd);
            battle.EndTurn();
            Assert.That(context.Ordinary.AllCards.Contains(generated), Is.False);
            Assert.That(battle.TryConvertApToAttack().Failure, Is.EqualTo(CardPlayFailure.EffectUnavailable));
        }

        /// <summary>验证攻击转换被拒绝时不消耗行动点。</summary>
        [Test] public void ConversionRejectionsDoNotSpendAp()
        {
            var battle = HandsBattle(); var context = battle.Context;
            context.Ordinary.Draw(20);
            Assert.That(battle.TryConvertApToAttack().Failure, Is.EqualTo(CardPlayFailure.HandFull));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
            context.Ordinary.Discard(context.Ordinary.Hand.Cards[0]);
            context.ActionPoints.TrySpend(2);
            Assert.That(battle.TryConvertApToAttack().Failure, Is.EqualTo(CardPlayFailure.InsufficientAp));
        }

        /// <summary>验证额外回合跳过敌人、刷新回合资源且保留战斗使用次数。</summary>
        [Test] public void ExtraTurnSkipsEnemyRefreshesResourcesAndRetainsBattleUsage()
        {
            var battle = Battle(assets.Create<ExtraTurnEffectData>(), CyberneticSlot.Legs, 8);
            var context = battle.Context; var card = context.Cybernetic.Hand.Cards[0];
            battle.TryPlay(card);
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.Player.Health, Is.EqualTo(100));
            Assert.That(battle.TurnNumber, Is.EqualTo(2));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(3));
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(6));
            Assert.That(context.Cybernetic.Hand.Count, Is.EqualTo(1));
            Assert.That(context.Usage.GetUses(CyberneticSlot.Legs), Is.EqualTo(1));
            Assert.That(context.Usage.CanUse(CyberneticSlot.Legs), Is.True);
            Assert.That(card.Source.Durability, Is.EqualTo(2));
            Assert.That(context.TurnEffects.ExtraTurns, Is.Zero);
            battle.EndTurn();
            Assert.That(context.Player.Health, Is.EqualTo(94));
        }
    }
}
