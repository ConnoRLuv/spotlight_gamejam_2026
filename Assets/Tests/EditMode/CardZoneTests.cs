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
            var deck = new CardDeck(1, Enumerable.Range(0,3).Select(_ => new CardInstance(data)),new RandomUtility(7));
            Assert.That(deck.Draw(99), Is.EqualTo(1));
            Assert.That(deck.Hand.Count, Is.EqualTo(1));
            Assert.That(deck.DiscardPile.Count, Is.EqualTo(2));
            Assert.That(deck.Discard(deck.Hand.Cards[0]), Is.True);
            Assert.That(deck.Draw(1), Is.EqualTo(1));
        }
        /// <summary>验证锁定幻痛牌保持唯一，并能按规则进入已满手牌。</summary>
        [Test] public void LockedCardIsUniqueAndCanEnterAFullHand()
        {
            var deck = new CardDeck(1,new[]{new CardInstance(data)},new RandomUtility(1)); deck.Draw(1);
            var locked = ScriptableObject.CreateInstance<CardData>(); locked.locksInHand = true;
            try
            {
                Assert.That(deck.EnsureLockedCard(locked), Is.True);
                Assert.That(deck.EnsureLockedCard(locked), Is.True);
                Assert.That(deck.Hand.Count, Is.EqualTo(1));
                Assert.That(deck.Discard(deck.Hand.Cards[0]), Is.False);
                Assert.That(deck.TryPlay(deck.Hand.Cards[0]), Is.False);
                Assert.That(deck.Hand.TryRemove(deck.Hand.Cards[0]), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(locked); }
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
