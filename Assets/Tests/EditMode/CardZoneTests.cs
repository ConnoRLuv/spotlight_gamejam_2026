using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证牌区容量、实例身份、抽牌重洗、锁定牌和随机可复现性。
    /// </summary>
    /// <remarks>
    /// 临时创建 CardData 后在 TearDown 销毁；重点检查不能通过其他牌区入口绕过幻痛限制。
    /// </remarks>
    public sealed class CardZoneTests
    {
        private CardData data;
        /// <summary>为用例创建独立的测试状态，避免用例之间共享可变资源。</summary>
        /// <summary>清理测试创建的临时对象和状态，避免污染后续用例。</summary>
        /// <summary>验证牌区容量限制及同一卡牌实例不能重复加入。</summary>
        [SetUp] public void SetUp() { data = ScriptableObject.CreateInstance<CardData>(); }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(data); }
        [Test] public void CapacityAndDuplicateIdentityAreEnforced()
        {
            var zone = new CardZone(1); var card = new CardInstance(data);
            Assert.That(zone.TryAdd(card), Is.True);
            Assert.That(zone.TryAdd(card), Is.False);
            Assert.That(zone.TryAdd(new CardInstance(data)), Is.False);
            Assert.That(zone.TryRemove(card), Is.True);
        }
        /// <summary>验证抽牌堆为空时重洗弃牌，手牌满时不会无限循环。</summary>
        [Test] public void DeckReshufflesAndOverflowDoesNotLoop()
        {
            var deck = new CardDeck(1, Enumerable.Range(0,3).Select(_ => new CardInstance(data)),new RandomUtility(7), discardOverflow: true);
            Assert.That(deck.Draw(99), Is.EqualTo(1));
            Assert.That(deck.Hand.Count, Is.EqualTo(1));
            Assert.That(deck.DiscardPile.Count, Is.EqualTo(2));
            Assert.That(deck.Discard(deck.Hand.Cards[0]), Is.True);
            Assert.That(deck.Draw(1), Is.EqualTo(1));
        }
        /// <summary>验证普通手牌达到十三张后不再取牌，堆顶和弃牌均保留，腾出一格只抽一张。</summary>
        [Test]
        public void OrdinaryDeckStopsDrawingAtCapacityWithoutMovingCards()
        {
            using (var assets = new TestAssets())
            {
                var card = assets.Card(assets.Create<HealEffectData>());
                var context = assets.Context(Enumerable.Repeat(card, 20).ToArray());
                Assert.That(context.Ordinary.Draw(13), Is.EqualTo(13));
                var top = context.Ordinary.Peek(7).ToArray();
                Assert.That(context.Ordinary.Draw(3), Is.Zero);
                Assert.That(context.Ordinary.Peek(7), Is.EqualTo(top));
                Assert.That(context.Ordinary.DiscardPile.Count, Is.Zero);
                Assert.That(context.Ordinary.Discard(context.Ordinary.Hand.Cards[0]), Is.True);
                Assert.That(context.Ordinary.Draw(3), Is.EqualTo(1));
                Assert.That(context.Ordinary.Hand.Cards.Last(), Is.SameAs(top[0]));
                Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(6));
                Assert.That(context.Ordinary.DiscardPile.Count, Is.EqualTo(1));
            }
        }
        /// <summary>验证普通抽牌直接取堆顶三张，即使三张配置相同也不按种类替换。</summary>
        [Test]
        public void DrawTakesTopThreeWithoutForcingDifferentCardTypes()
        {
            var deck = new CardDeck(13, Enumerable.Range(0, 6).Select(_ => new CardInstance(data)), new RandomUtility(7));
            var expected = deck.Peek(3).ToArray();
            Assert.That(deck.Draw(3), Is.EqualTo(3));
            Assert.That(deck.Hand.Cards, Is.EqualTo(expected));
            Assert.That(deck.DrawPile.Count, Is.EqualTo(3));
            Assert.That(deck.DiscardPile.Count, Is.Zero);
        }

        /// <summary>验证任一牌堆抽至耗尽时回收自身弃牌继续抽取，另一套牌区保持不变。</summary>
        [TestCase(13, 5)]
        [TestCase(5, 13)]
        public void DrawContinuesAcrossReshuffleWithoutChangingTheOtherDeck(int capacity, int otherCapacity)
        {
            var deck = new CardDeck(capacity, Enumerable.Range(0, 4).Select(_ => new CardInstance(data)), new RandomUtility(7));
            var other = new CardDeck(otherCapacity, Enumerable.Range(0, 4).Select(_ => new CardInstance(data)), new RandomUtility(8));
            deck.Draw(3);
            foreach (var card in deck.Hand.Cards.ToArray()) Assert.That(deck.Discard(card), Is.True);
            other.Draw(1); other.Discard(other.Hand.Cards[0]);
            var otherDraw = other.DrawPile.Cards.ToArray();
            var otherDiscard = other.DiscardPile.Cards.ToArray();
            var recycled = deck.DiscardPile.Cards.ToArray();
            var top = deck.Peek(1).Single();

            Assert.That(deck.Draw(3), Is.EqualTo(3));
            Assert.That(deck.Hand.Cards[0], Is.SameAs(top));
            Assert.That(deck.Hand.Cards.Skip(1).All(recycled.Contains), Is.True);
            Assert.That(deck.DrawPile.Count, Is.EqualTo(1));
            Assert.That(deck.DiscardPile.Count, Is.Zero);
            Assert.That(other.DrawPile.Cards, Is.EqualTo(otherDraw));
            Assert.That(other.DiscardPile.Cards, Is.EqualTo(otherDiscard));
            Assert.That(other.Hand.Count, Is.Zero);
        }

        /// <summary>验证满手时随机弃一张普通牌并立即补入唯一幻痛，幻痛仍不可手动移出。</summary>
        [Test] public void LockedCardDiscardsOneOrdinaryCardAndRemainsUnique()
        {
            var deck = new CardDeck(1,new[]{new CardInstance(data)},new RandomUtility(1)); deck.Draw(1);
            var locked = ScriptableObject.CreateInstance<CardData>(); locked.locksInHand = true;
            try
            {
                var original = deck.Hand.Cards[0];
                Assert.That(deck.EnsureLockedCard(locked), Is.True);
                Assert.That(deck.Hand.Cards[0].Data, Is.SameAs(locked));
                Assert.That(deck.DiscardPile.Cards, Is.EqualTo(new[] { original }));
                Assert.That(deck.EnsureLockedCard(locked), Is.True);
                Assert.That(deck.DiscardPile.Count, Is.EqualTo(1));
                Assert.That(deck.Hand.Count, Is.EqualTo(1));
                Assert.That(deck.Discard(deck.Hand.Cards[0]), Is.False);
                Assert.That(deck.TryPlay(deck.Hand.Cards[0]), Is.False);
                Assert.That(deck.Hand.TryRemove(deck.Hand.Cards[0]), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(locked); }
        }

        /// <summary>验证归零后立即随机弃掉一张普通手牌，补幻痛不抽取堆顶，也不重复弃牌。</summary>
        [Test]
        public void ZeroSanityReplacesOneOrdinaryCardWithoutDrawingOrRepeatingDiscard()
        {
            using (var assets = new TestAssets())
            {
                var normal = assets.Card(assets.Create<HealEffectData>());
                var context = assets.Context(Enumerable.Repeat(normal, 14).ToArray());
                context.Ordinary.Draw(13);
                var originalHand = context.Ordinary.Hand.Cards.ToArray();
                var top = context.Ordinary.Peek(1).Single();
                context.Player.TrySpendSanity(50);
                Assert.DoesNotThrow(() => context.EnsurePhantomPain());
                Assert.That(context.HasPhantomPain, Is.True);
                Assert.That(context.Ordinary.DiscardPile.Count, Is.EqualTo(1));
                Assert.That(originalHand.Contains(context.Ordinary.DiscardPile.Cards.Single()), Is.True);
                context.EnsurePhantomPain();
                Assert.That(context.Ordinary.Draw(1), Is.Zero);
                Assert.That(context.HasPhantomPain, Is.True);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(13));
                Assert.That(context.Ordinary.Peek(1).Single(), Is.SameAs(top));
                Assert.That(context.Ordinary.DiscardPile.Count, Is.EqualTo(1));
            }
        }
        /// <summary>验证随机弃牌在固定种子下可复现，同时不同种子可以选择不同位置。</summary>
        [Test]
        public void LockedCardReplacementUsesSeededRandomInsteadOfFixedPosition()
        {
            using (var assets = new TestAssets())
            {
                var locked = assets.Phantom();
                var positions = new System.Collections.Generic.HashSet<int>();
                for (int seed = 0; seed < 12; seed++)
                {
                    var first = new CardDeck(13, Enumerable.Range(0, 13).Select(_ => new CardInstance(data)), new RandomUtility(seed));
                    var second = new CardDeck(13, Enumerable.Range(0, 13).Select(_ => new CardInstance(data)), new RandomUtility(seed));
                    first.Draw(13); second.Draw(13);
                    var a = first.Hand.Cards.ToArray(); var b = second.Hand.Cards.ToArray();
                    Assert.That(first.EnsureLockedCard(locked), Is.True);
                    Assert.That(second.EnsureLockedCard(locked), Is.True);
                    int index = Array.IndexOf(a, first.DiscardPile.Cards.Single());
                    Assert.That(Array.IndexOf(b, second.DiscardPile.Cards.Single()), Is.EqualTo(index));
                    positions.Add(index);
                }
                Assert.That(positions.Count, Is.GreaterThan(1), "不得始终弃首张或末张牌。");
            }
        }
        /// <summary>验证随机弃牌候选排除义体和其他锁定牌，防止特殊配置误弃这两类牌。</summary>
        [Test]
        public void LockedCardReplacementNeverDiscardsCyberneticOrLockedCards()
        {
            using (var assets = new TestAssets())
            {
                var cyber = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
                var deck = new CardDeck(3, new[] { new CardInstance(data), new CardInstance(cyber) }, new RandomUtility(4));
                deck.Draw(2);
                var otherLocked = new CardInstance(assets.Phantom()); deck.Hand.TryAdd(otherLocked);
                var phantom = assets.Phantom();
                Assert.That(deck.EnsureLockedCard(phantom), Is.True);
                Assert.That(deck.Hand.Contains(otherLocked), Is.True);
                Assert.That(deck.Hand.Cards.Any(card => card.Data == cyber), Is.True);
                Assert.That(deck.DiscardPile.Cards.Single().Data, Is.SameAs(data));
                Assert.That(deck.Hand.Count, Is.EqualTo(3));
            }
        }
        /// <summary>验证固定种子的洗牌可复现，并实际改变牌序。</summary>
        [Test] public void SeededShuffleIsReproducibleAndChangesOrder()
        {
            var a = Enumerable.Range(0,20).ToArray(); var b = a.ToArray();
            CollectionUtility.Shuffle(a,new RandomUtility(42)); CollectionUtility.Shuffle(b,new RandomUtility(42));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(Enumerable.Range(0,20).ToArray()));
            var card = new CardInstance(data); card.TemporaryApCost = 0;
            Assert.That(data.cost, Is.Zero);
        }
    }
}
