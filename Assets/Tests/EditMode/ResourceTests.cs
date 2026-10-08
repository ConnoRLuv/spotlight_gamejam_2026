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
        /// <summary>验证费用非法或余额不足时，不改变行动点及理智。</summary>
        [Test] public void InvalidPaymentsDoNotChangeResources()
        {
            var ap = new ActionPointPool(); ap.BeginTurn(2);
            Assert.That(ap.TrySpend(3), Is.False);
            Assert.That(ap.TrySpend(-1), Is.False);
            Assert.That(ap.Normal, Is.EqualTo(2));
            var player = new CombatantState("p",100,50);
            Assert.That(player.TrySpendSanity(51), Is.False);
            Assert.That(player.TrySpendSanity(-1), Is.False);
            Assert.That(player.Sanity, Is.EqualTo(50));
            Assert.That(player.TrySpendSanity(50), Is.True);
            Assert.That(player.Sanity, Is.Zero);
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
