using NUnit.Framework;
using UnityEngine;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证义体按部位的回合与战斗限制，以及实例耐久和修复上限。
    /// </summary>
    /// <remarks>
    /// 共享配置用于证明不同实例互不影响；战斗计数与持久耐久分别断言。
    /// </remarks>
    public sealed class CyberneticTests
    {
        /// <summary>验证回合部位限制每回合重置，而每场三次限制持续累计。</summary>
        [Test] public void SlotUsageResetsEachTurnButBattleLimitPersists()
        {
            var usage = new CyberneticUsage();
            for (int i = 0; i < 3; i++)
            {
                Assert.That(usage.CanUse(CyberneticSlot.Hands), Is.True);
                usage.RecordUse(CyberneticSlot.Hands);
                Assert.That(usage.CanUse(CyberneticSlot.Hands), Is.False);
                Assert.That(usage.CanUse(CyberneticSlot.Legs), Is.True);
                usage.BeginTurn();
            }
            Assert.That(usage.CanUse(CyberneticSlot.Hands), Is.False);
            Assert.That(new CyberneticUsage().CanUse(CyberneticSlot.Hands), Is.True);
        }
        /// <summary>验证义体实例独立保存耐久，维修不能超过配置上限。</summary>
        [Test] public void DurabilityIsPerInstanceAndRepairIsCapped()
        {
            var data = ScriptableObject.CreateInstance<CyberneticData>();
            try
            {
                data.maxDurability = 3;
                var first = new CyberneticInstance(data); var second = new CyberneticInstance(data);
                for (int i = 0; i < 3; i++) Assert.That(first.TryConsumeDurability(), Is.True);
                Assert.That(first.TryConsumeDurability(), Is.False);
                Assert.That(first.Durability, Is.Zero);
                Assert.That(second.Durability, Is.EqualTo(3));
                Assert.That(data.maxDurability, Is.EqualTo(3));
                first.Repair(20); Assert.That(first.Durability, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(data); }
        }
    }
}
