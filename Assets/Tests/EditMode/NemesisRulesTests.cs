using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证复仇女神资源、弃牌回收、基本牌免 AP 和受伤抽牌的边界规则。</summary>
    public sealed class NemesisRulesTests
    {
        private TestAssets assets;
        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();

        /// <summary>读取正式部位配置，缺少资产时明确报告未接入的部位。</summary>
        private static CyberneticData Part(string part)
        {
            var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/Nemesis" + part + "Card.asset");
            Assert.That(data, Is.Not.Null, part);
            return data;
        }

        /// <summary>只在本用例准备成功四部位记录，组合判定继续复用 RunBuffState。</summary>
        private static void Activate(BattleContext context)
        {
            var buff = AssetDatabase.LoadAssetAtPath<RunBuffData>("Assets/GameData/Buffs/Nemesis.asset");
            Assert.That(buff, Is.Not.Null);
            context.Run.Buffs.Configure(new[] { buff });
            context.Run.Buffs.BeginTurn();
            foreach (var part in new[] { "Brain", "Torso", "Hands", "Legs" })
            {
                var data = Part(part); context.Run.Loadout.AddInitial(data);
                context.Run.Buffs.RecordPlayed(context.Run.Loadout.GetUsableCards().First(card => card.Source.Data == data));
            }
            Assert.That(context.Run.Buffs.Active, Does.Contain(buff));
        }

        /// <summary>脑机仅转换当前普通手牌，保留幻痛和义体；弃置后回合结束也能恢复原牌。</summary>
        [Test]
        public void BrainTransformsOnlyCurrentOrdinaryHandAndRestoresOriginalIdentity()
        {
            var defense = assets.Card(assets.Create<ShieldEffectData>()); defense.cardType = CardType.Skill;
            var overload = assets.Card(assets.Create<ActionPointEffectData>(), category: CardCategory.Function, ap: 0, sanity: 5);
            overload.cardType = CardType.Power;
            using (var context = assets.Context(new[] { defense, overload, defense, defense }))
            {
                TestAssets.Prepare(context);
                var originals = context.Ordinary.Hand.Cards.ToDictionary(card => card, card => card.Data);
                var drawBefore = context.Ordinary.DrawPile.Cards.Single().Data;
                var phantom = new CardInstance(context.PhantomPain); context.Ordinary.Hand.TryAdd(phantom);
                var data = Part("Brain"); context.Run.Loadout.AddInitial(data);
                var brain = context.Run.Loadout.GetUsableCards().First(); context.Cybernetic.Hand.TryAdd(brain);
                Assert.That(new CardPlayService().TryPlay(context, brain).Success, Is.True);
                foreach (var pair in originals)
                { Assert.That(pair.Key.Data.cardType, Is.EqualTo(CardType.Attack)); Assert.That(context.Ordinary.Hand.Contains(pair.Key), Is.True); }
                Assert.That(phantom.Data, Is.SameAs(context.PhantomPain));
                Assert.That(context.Cybernetic.DiscardPile.Cards.Single().Data, Is.SameAs(data.cards[0]));
                Assert.That(context.Ordinary.DrawPile.Cards.Single().Data, Is.SameAs(drawBefore));
                var discarded = originals.Keys.First(); context.Ordinary.Discard(discarded);
                new TurnController().EndTurn(context, 0);
                foreach (var pair in originals) Assert.That(pair.Key.Data, Is.SameAs(pair.Value));
            }
        }

        /// <summary>转换攻击造成致命一击时，结算中暂时无牌区的本牌也应在战斗结束恢复原配置。</summary>
        [Test]
        public void KillingAttackRestoresItsOriginalCardBeforeEnteringDiscard()
        {
            var defense = assets.Card(assets.Create<ShieldEffectData>()); defense.cardType = CardType.Skill;
            using (var context = assets.Context(new[] { defense }))
            {
                TestAssets.Prepare(context);
                var chosen = context.Ordinary.Hand.Cards.Single();
                Part("Brain").cards[0].effects[0].Execute(context, null, null);
                context.Enemies[0].ReceiveDamage(99);
                Assert.That(new CardPlayService().TryPlay(context, chosen, context.Enemies[0]).Success, Is.True);
                Assert.That(context.State, Is.EqualTo(BattleState.Victory));
                Assert.That(context.Ordinary.DiscardPile.Contains(chosen), Is.True); Assert.That(chosen.Data, Is.SameAs(defense));
            }
        }

        /// <summary>躯干保留到敌方行动，玩家护盾不被消耗；下一回合和战斗结束后恢复正常承伤。</summary>
        [Test]
        public void TorsoRedirectsWholeEnemyHitAndExpiresAtNextPlayerTurn()
        {
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                TestAssets.Prepare(context);
                Part("Torso").cards[0].effects[0].Execute(context, null, null);
                context.Player.AddShield(5); int health = context.Player.Health;
                context.TurnEffects.EndPlayerAction();
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 8);
                Assert.That(context.Player.Health, Is.EqualTo(health)); Assert.That(context.Player.Shield, Is.EqualTo(5));
                Assert.That(context.Enemies[0].Health, Is.EqualTo(92));
                context.Player.TrySpendHealth(1); Assert.That(context.Player.Health, Is.EqualTo(health - 1), "主动生命费用照常支付。");
                context.TurnEffects.BeginTurn();
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 8);
                Assert.That(context.Player.Health, Is.EqualTo(health - 4));
            }
        }

        /// <summary>躯干把敌方理智攻击转为敌人生命伤害，不消耗玩家理智或触发受伤抽牌，主动理智费用不转移。</summary>
        [Test]
        public void TorsoRedirectsSanityDamageButDoesNotRedirectActiveCosts()
        {
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                TestAssets.Prepare(context); Activate(context);
                context.Ordinary.DrawPile.TryAdd(new CardInstance(assets.Card(assets.Create<HealEffectData>())));
                Part("Torso").cards[0].effects[0].Execute(context, null, null);
                int sanity = context.Player.Sanity, health = context.Player.Health;
                Assert.That(DamageResolver.ApplySanity(context, context.Enemies[0], context.Player, 4), Is.EqualTo(4));
                Assert.That(context.Enemies[0].Health, Is.EqualTo(96));
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity)); Assert.That(context.Player.Health, Is.EqualTo(health));
                Assert.That(context.Ordinary.Hand.Count, Is.Zero);
                context.Player.TrySpendSanity(2); Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 2));
                context.TurnEffects.BeginTurn();
                DamageResolver.ApplySanity(context, context.Enemies[0], context.Player, 3);
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 5)); Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1));
            }
        }

        /// <summary>通过实际出牌服务成功使用四部位才触发套装；伤害转移期间不受伤，之后实际承伤才抽牌。</summary>
        [Test]
        public void RealFourPartsActivateSetInOneTurnAndDamageDrawsAfterRedirectExpires()
        {
            var basic = assets.Card(assets.Create<HealEffectData>()); basic.cardType = CardType.Skill;
            using (var context = assets.Context(Enumerable.Repeat(basic, 15).ToArray()))
            {
                var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/Nemesis.asset");
                var buff = AssetDatabase.LoadAssetAtPath<RunBuffData>("Assets/GameData/Buffs/Nemesis.asset");
                foreach (var part in deck.cybernetics) context.Run.Loadout.AddInitial(part);
                context.Run.Buffs.Configure(new[] { buff });
                var battle = new BattleController(context, 1); Assert.That(battle.StartBattle(), Is.True, battle.LastError);
                var cards = context.Run.Loadout.GetUsableCards().GroupBy(card => card.Source.Data.slot).Select(group => group.First())
                    .OrderBy(card => card.Source.Data.slot).ToArray();
                foreach (var card in cards) Assert.That(context.Cybernetic.Hand.TryAdd(card), Is.True);
                var attack = new CardInstance(AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset"));
                context.Ordinary.DiscardPile.TryAdd(attack);
                for (int i = 0; i < cards.Length; i++)
                {
                    var result = i == 2 ? battle.TryPlayRequest(new CardPlayRequest(cards[i], choiceId: attack.Id)) : battle.TryPlay(cards[i]);
                    Assert.That(result.Success, Is.True, result.Message);
                    Assert.That(context.Run.Buffs.Active.Count, Is.EqualTo(i == 3 ? 1 : 0));
                }
                Assert.That(context.Run.Loadout.Owned.Where(source => source.Data.slot == CyberneticSlot.Legs).Select(source => source.Durability), Is.All.EqualTo(1));
                int health = context.Player.Health;
                Assert.That(battle.EndTurn(), Is.True); Assert.That(context.Player.Health, Is.EqualTo(health));
                foreach (var card in context.Ordinary.Hand.Cards.ToArray()) context.Ordinary.Discard(card);
                int before = context.Ordinary.DrawPile.Count;
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 1);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1)); Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(before - 1));
                Assert.That(context.Run.Buffs.Active.Count, Is.EqualTo(1));
            }
        }

        /// <summary>回收费用导致理智归零时预留幻痛的位置，避免扣费后才发现无法回收。</summary>
        [Test]
        public void HandsRejectsWhenPaymentWouldFillLastSlotWithPhantomPain()
        {
            using (var context = assets.Context(Array.Empty<CardData>(), capacity: 1))
            {
                TestAssets.Prepare(context);
                var data = Part("Hands"); context.Run.Loadout.AddInitial(data);
                var hands = context.Run.Loadout.GetUsableCards().First(); context.Cybernetic.Hand.TryAdd(hands);
                var attack = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
                var chosen = new CardInstance(attack); context.Ordinary.DiscardPile.TryAdd(chosen);
                context.Player.TrySpendSanity(context.Player.Sanity - 1);
                Assert.That(new CardPlayService().TryPlayRequest(context, new CardPlayRequest(hands, choiceId: chosen.Id)).Success, Is.False);
                Assert.That(context.Player.Sanity, Is.EqualTo(1)); Assert.That(hands.Source.Durability, Is.EqualTo(3));
                Assert.That(context.Ordinary.DiscardPile.Contains(chosen), Is.True);
            }
        }

        /// <summary>成功回收指定攻击牌；取消、选错牌和手牌已满时不扣资源或耐久。</summary>
        [Test]
        public void HandsRecoversChosenAttackAndRejectsOtherCardsOrFullHand()
        {
            var attack = assets.Card(assets.Create<DamageEffectData>(), CardTargetType.SingleEnemy); attack.cardType = CardType.Attack;
            var defense = assets.Card(assets.Create<ShieldEffectData>()); defense.cardType = CardType.Skill;
            using (var context = assets.Context(Array.Empty<CardData>(), capacity: 1))
            {
                TestAssets.Prepare(context);
                var data = Part("Hands"); context.Run.Loadout.AddInitial(data);
                var hands = context.Run.Loadout.GetUsableCards().First(); context.Cybernetic.Hand.TryAdd(hands);
                var chosen = new CardInstance(attack); var wrong = new CardInstance(defense);
                context.Ordinary.DiscardPile.TryAdd(chosen); context.Ordinary.DiscardPile.TryAdd(wrong);
                var play = new CardPlayService(); int sanity = context.Player.Sanity;
                Assert.That(play.TryPlay(context, hands).Success, Is.False);
                Assert.That(play.TryPlayRequest(context, new CardPlayRequest(hands, choiceId: wrong.Id)).Success, Is.False);
                var blocking = new CardInstance(defense); context.Ordinary.Hand.TryAdd(blocking);
                Assert.That(play.TryPlayRequest(context, new CardPlayRequest(hands, choiceId: chosen.Id)).Success, Is.False);
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity)); Assert.That(hands.Source.Durability, Is.EqualTo(3));
                context.Ordinary.Discard(blocking);
                Assert.That(play.TryPlayRequest(context, new CardPlayRequest(hands, choiceId: chosen.Id)).Success, Is.True);
                Assert.That(context.Ordinary.Hand.Cards, Is.EqualTo(new[] { chosen }));
                Assert.That(context.Ordinary.DiscardPile.Contains(wrong), Is.True);
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 6)); Assert.That(hands.Source.Durability, Is.EqualTo(2));
            }
        }

        /// <summary>腿部仅令基本牌免 AP，功能牌和义体不改变费用，并在玩家行动结束清除。</summary>
        [Test]
        public void LegsMakesBasicCardsFreeForOneTurn()
        {
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                TestAssets.Prepare(context);
                Part("Legs").cards[0].effects[0].Execute(context, null, null);
                var basic = new CardInstance(assets.Card(assets.Create<HealEffectData>()));
                Assert.That(context.GetEffectiveApCost(basic), Is.Zero);
                context.TurnEffects.EndPlayerAction();
                Assert.That(context.GetEffectiveApCost(basic), Is.EqualTo(1));
            }
        }

        /// <summary>抽牌堆耗尽后，套装抽牌也必须洗回弃牌，再获得基本牌，不能因绕过普通抽牌入口而失效。</summary>
        [Test]
        public void DamageDrawRecyclesEmptyDrawPileAndKeepsFunctionCardsInDrawPile()
        {
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                TestAssets.Prepare(context); Activate(context);
                var basic = new CardInstance(assets.Card(assets.Create<HealEffectData>()));
                var otherBasic = new CardInstance(assets.Card(assets.Create<ShieldEffectData>()));
                var function = new CardInstance(assets.Card(assets.Create<ActionPointEffectData>(), category: CardCategory.Function, ap: 0, sanity: 5));
                context.Ordinary.DiscardPile.TryAdd(basic);
                context.Ordinary.DiscardPile.TryAdd(function);
                context.Ordinary.DiscardPile.TryAdd(otherBasic);
                int health = context.Player.Health;
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 1);
                Assert.That(context.Player.Health, Is.EqualTo(health - 1));
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1), "弃牌有基本牌时，空抽牌堆应先洗回再结算套装抽牌。");
                Assert.That(context.Ordinary.Hand.Cards.Single().Data.category, Is.EqualTo(CardCategory.Basic));
                Assert.That(context.Ordinary.DrawPile.Contains(function), Is.True);
                Assert.That(context.Ordinary.DiscardPile.Count, Is.Zero);
                Assert.That(context.Ordinary.Hand.Cards.Concat(context.Ordinary.DrawPile.Cards), Is.EquivalentTo(new[] { basic, otherBasic, function }));
            }
        }

        /// <summary>一次实际受伤只抽堆顶方向第一张基本牌；全护盾、满手和主动生命支付均不触发。</summary>
        [Test]
        public void SetDrawsOneBasicOnDamageWithoutDrawingFunctionsOrExceedingHandLimit()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var function = assets.Card(assets.Create<ActionPointEffectData>(), category: CardCategory.Function, ap: 0, sanity: 5);
            using (var context = assets.Context(new[] { basic, function, basic }, capacity: 1))
            {
                TestAssets.Prepare(context);
                foreach (var card in context.Ordinary.Hand.Cards.ToArray()) context.Ordinary.Discard(card);
                // 准备固定堆顶：功能牌在最顶，随后是基本牌。被动不能把功能牌抽入手牌。
                foreach (var data in new[] { basic, function }) context.Ordinary.DrawPile.TryAdd(new CardInstance(data));
                Activate(context);
                context.Player.AddShield(4);
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 4);
                Assert.That(context.Ordinary.Hand.Count, Is.Zero);
                context.Player.TrySpendHealth(1); Assert.That(context.Ordinary.Hand.Count, Is.Zero);
                int before = context.Ordinary.DrawPile.Count;
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1));
                Assert.That(context.Ordinary.Hand.Cards.Single().Data, Is.SameAs(basic));
                Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(before - 1));
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
                Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(before - 1), "满手不移动抽牌堆。");
                context.Run.Buffs.EndBattle(); context.Ordinary.Discard(context.Ordinary.Hand.Cards.Single());
                DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
                Assert.That(context.Ordinary.Hand.Count, Is.Zero, "战斗结束后套装已失效。");
            }
        }
    }
}
