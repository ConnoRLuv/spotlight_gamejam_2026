using System;
using System.Linq;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    public sealed class ZeroMergeCompatibilityTests
    {
        [Test]
        public void UnlimitedHandStillAllowsExistingPulseConversion()
        {
            using (var assets = new TestAssets())
            {
                var basic = assets.Card(assets.Create<HealEffectData>());
                using (var context = assets.Context(new[] { basic }, capacity: 1))
                {
                    TestAssets.Prepare(context);
                    context.TurnEffects.ConversionAttack = basic;
                    var controller = new BattleController(context);
                    Assert.That(controller.TryConvertApToAttack().Failure, Is.EqualTo(CardPlayFailure.HandFull));
                    context.SetNoHandLimit(true);
                    Assert.That(controller.TryConvertApToAttack().Success, Is.True);
                    Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(2));
                }
            }
        }

        [Test]
        public void UnlimitedCyberneticHandAllowsExistingPeekAndTransformation()
        {
            using (var assets = new TestAssets())
            using (var context = assets.Context(new[] { assets.Card(assets.Create<HealEffectData>()) }))
            {
                TestAssets.Prepare(context);
                context.SetNoHandLimit(true);
                var definition = assets.Card(assets.Create<ShieldEffectData>(),
                    category: CardCategory.Cybernetic, ap: 0, sanity: 2);
                for (int i = 0; i < 6; i++) context.Cybernetic.Hand.TryAdd(new CardInstance(definition));
                var peek = assets.Create<CyberneticPeekEffectData>();
                var candidate = new CardInstance(definition);
                context.Cybernetic.DrawPile.TryAdd(candidate);
                var played = context.Cybernetic.Hand.Cards[0];
                Assert.That(peek.CanExecuteRequest(context, new CardPlayRequest(played,
                    choiceId: candidate.Id)), Is.True);
                var source = assets.Create<CyberneticData>();
                source.cyberneticId = "new-transformation"; source.factionId = "tian-qiong-legion";
                source.slot = CyberneticSlot.Brain; source.maxDurability = 3; source.cards = new[] { definition };
                var transform = assets.Create<TransformBasicToCyberneticEffectData>();
                transform.candidates = new[] { source };
                Assert.That(transform.CanExecuteRequest(context, new CardPlayRequest(played,
                    choiceId: context.Ordinary.Hand.Cards[0].Id)), Is.True);
            }
        }

        [Test]
        public void PhantomPainReplacesOneCardAfterUnlimitedHandExpires()
        {
            using (var assets = new TestAssets())
            using (var context = assets.Context(Enumerable.Repeat(
                assets.Card(assets.Create<HealEffectData>()), 20).ToArray()))
            {
                TestAssets.Prepare(context);
                context.SetNoHandLimit(true);
                context.Ordinary.Draw(13);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(16));
                context.SetNoHandLimit(false);
                Assert.DoesNotThrow(() => context.Player.TrySpendSanity(context.Player.Sanity));
                Assert.That(context.HasPhantomPain, Is.True);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(16));
                Assert.That(context.Ordinary.CanAddToHand(), Is.False);
            }
        }

        [Test]
        public void ExistingBuffAndNewWorldSetBothObserveSuccessfulPlays()
        {
            using (var assets = new TestAssets())
            using (var context = assets.Context(Array.Empty<CardData>()))
            {
                var parts = Enumerable.Range(0, 4).Select(index =>
                {
                    var data = assets.Create<CyberneticData>();
                    data.cyberneticId = "merge-part-" + index;
                    data.slot = (CyberneticSlot)index;
                    data.maxDurability = 3;
                    data.cards = new[] { assets.Card(assets.Create<ShieldEffectData>(),
                        category: CardCategory.Cybernetic, ap: 0, sanity: 2) };
                    context.Run.Loadout.AddInitial(data);
                    return data;
                }).ToArray();
                var buff = assets.Create<RunBuffData>();
                buff.buffId = "existing-buff"; buff.buffName = "Existing buff";
                buff.requiredCybernetics = parts;
                context.Run.Buffs.Configure(new[] { buff });
                var bonus = assets.Create<NewWorldSetBonusData>();
                bonus.setId = "new-world"; bonus.setName = "New world";
                bonus.requiredParts = parts;
                context.Run.SetBonuses.Configure(new[] { bonus });
                TestAssets.Prepare(context);
                context.Run.Buffs.BeginTurn();
                var basic = new CardInstance(assets.Card(assets.Create<HealEffectData>()));
                context.Ordinary.DiscardPile.TryAdd(basic);
                foreach (var card in context.Run.Loadout.GetUsableCards())
                    context.Cybernetic.Hand.TryAdd(card);
                var service = new CardPlayService();
                var played = context.Cybernetic.Hand.Cards.ToArray();
                for (int i = 0; i < played.Length; i++)
                {
                    Assert.That(service.TryPlay(context, played[i]).Success, Is.True);
                    Assert.That(context.Run.Buffs.Active.Count, Is.EqualTo(i == 3 ? 1 : 0));
                }
                Assert.That(context.Ordinary.Hand.Contains(basic), Is.True,
                    "ZERO set must recycle a basic card while the existing buff also records plays.");
                Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(1));
                var overload = assets.Card(assets.Create<ActionPointEffectData>(),
                    CardTargetType.None, CardCategory.Function, 0, 5);
                overload.cardId = "OverloadCard";
                Assert.That(context.UsesHealthFor(new CardInstance(overload)), Is.True);
            }
        }

        [Test]
        public void ReorderIsValidatedBeforePaymentAndPreservedDuringResolution()
        {
            using (var assets = new TestAssets())
            {
                var basic = assets.Card(assets.Create<HealEffectData>());
                using (var context = assets.Context(Enumerable.Repeat(basic, 6).ToArray()))
                {
                    var brain = assets.Card(assets.Create<NewWorldBrainEffectData>(),
                        CardTargetType.None, CardCategory.Cybernetic, 0, 2);
                    var data = assets.Create<CyberneticData>();
                    data.cyberneticId = "reorder-brain"; data.slot = CyberneticSlot.Brain;
                    data.maxDurability = 3; data.cards = new[] { brain };
                    context.Run.Loadout.AddInitial(data);
                    var card = context.Run.Loadout.GetUsableCards().Single();
                    context.Cybernetic.Hand.TryAdd(card);
                    TestAssets.Prepare(context);
                    var order = context.Ordinary.GetDrawPileOrder().Reverse().ToArray();
                    int sanity = context.Player.Sanity;
                    var service = new CardPlayService();
                    Assert.That(service.TryPlay(context, card).Failure, Is.EqualTo(CardPlayFailure.ReorderRequired));
                    Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
                    Assert.That(service.TryPlayRequest(context, new CardPlayRequest(card,
                        reorder: order.Take(1).ToArray())).Success, Is.False);
                    Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
                    Assert.That(service.TryPlayRequest(context, new CardPlayRequest(card,
                        reorder: order)).Success, Is.True);
                    Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(order));
                    Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 2));
                    Assert.That(card.Source.Durability, Is.EqualTo(2));
                }
            }
        }
    }
}
