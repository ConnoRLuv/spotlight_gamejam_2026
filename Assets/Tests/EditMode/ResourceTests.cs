using NUnit.Framework;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证普通/临时 AP 支付顺序、费用拒绝、护盾吸收及属性上限。
    /// </summary>
    /// <remarks>
    /// 直接操作资源状态，作为出牌和回合测试的基础规则验证。
    /// </remarks>
    public sealed class ResourceTests
    {
        /// <summary>验证恢复理智遵守上限，负数不改变状态，死亡角色不会被资源恢复复活。</summary>
        [Test]
        public void SanityRecoveryClampsToMaximumAndRejectsInvalidAmounts()
        {
            var player = new CombatantState("player", 100, 50);
            player.TrySpendSanity(50);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => player.RestoreSanity(-1));
            Assert.That(player.Sanity, Is.Zero);
            player.RestoreSanity(1);
            Assert.That(player.Sanity, Is.EqualTo(1));
            player.RestoreSanity(int.MaxValue);
            Assert.That(player.Sanity, Is.EqualTo(50));
            player.TrySpendSanity(50); player.ReceiveDamage(100);
            player.RestoreSanity(10);
            Assert.That(player.Sanity, Is.Zero);
        }
        /// <summary>验证临时行动点优先支付，并在回合结束时过期。</summary>
        [Test] public void TemporaryApIsSpentFirstAndExpires()
        {
            var ap = new ActionPointPool();
            ap.BeginTurn(2); ap.AddTemporary(1);
            Assert.That(ap.TrySpend(2), Is.True);
            Assert.That(ap.Normal, Is.EqualTo(1));
            Assert.That(ap.Temporary, Is.Zero);
            ap.AddTemporary(2); ap.EndTurn();
            Assert.That(ap.Temporary, Is.Zero);
            ap.BeginTurn(3);
            Assert.That(ap.Normal, Is.EqualTo(3));
        }
        /// <summary>验证非法费用、行动点不足及零理智再次支付时不改变资源。</summary>
        [Test] public void InvalidPaymentsDoNotChangeResources()
        {
            var ap = new ActionPointPool(); ap.BeginTurn(2);
            Assert.That(ap.TrySpend(3), Is.False);
            Assert.That(ap.TrySpend(-1), Is.False);
            Assert.That(ap.Normal, Is.EqualTo(2));
            var player = new CombatantState("p",100,50);
            Assert.That(player.TrySpendSanity(-1), Is.False);
            Assert.That(player.Sanity, Is.EqualTo(50));
            Assert.That(player.TrySpendSanity(50), Is.True);
            Assert.That(player.Sanity, Is.Zero);
            Assert.That(player.TrySpendSanity(1), Is.False);
            Assert.That(player.Health, Is.EqualTo(100));
            Assert.That(player.TrySpendSanity(0), Is.True, "零理智不应影响无理智费用的操作。");
        }

        /// <summary>验证四点理智支付八点费用仅扣理智至零，生命和护盾不变，并原子通知最终状态。</summary>
        [Test]
        public void SanityOverdraftPreservesHealthAndShield()
        {
            var player = new CombatantState("p", 100, 4); player.AddShield(10);
            int notifications = 0;
            player.Changed += value =>
            {
                notifications++;
                Assert.That(value.Sanity, Is.Zero);
                Assert.That(value.Health, Is.EqualTo(100));
                Assert.That(value.Shield, Is.EqualTo(10));
            };
            Assert.That(player.TrySpendSanity(8), Is.True);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(player.TrySpendSanity(8), Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        }
        /// <summary>验证护盾吸收伤害、治疗遵守生命上限且不复活死亡角色。</summary>
        [Test] public void ShieldAbsorbsDamageAndHealingIsCapped()
        {
            var player = new CombatantState("p",100,50);
            player.AddShield(3);
            Assert.That(player.ReceiveDamage(6), Is.EqualTo(3));
            Assert.That(player.Health, Is.EqualTo(97));
            Assert.That(player.Shield, Is.Zero);
            player.Heal(30);
            Assert.That(player.Health, Is.EqualTo(100));
            player.ReceiveDamage(200);
            Assert.That(player.IsAlive, Is.False);
            player.Heal(3);
            Assert.That(player.Health, Is.Zero, "Healing must not resurrect dead characters.");
        }
    }
}
