using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证错误配置在开战前被拒绝，并检查 GameBootstrap 的 Inspector 接入流程。
    /// </summary>
    /// <remarks>
    /// 使用临时 GameObject 与 SerializedObject 模拟场景配置；预期错误日志通过 LogAssert 声明，避免误判为测试失败。
    /// </remarks>
    public sealed class ConfigurationTests
    {
        private TestAssets assets;
        /// <summary>为用例创建独立的测试状态，避免用例之间共享可变资源。</summary>
        /// <summary>清理测试创建的临时对象和状态，避免污染后续用例。</summary>
        /// <summary>验证需要目标的效果缺少目标模式时，在开战前被拒绝。</summary>
        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void TargetedEffectWithoutTargetModeIsRejectedBeforeBattleStarts(int kind)
        {
            CardEffectData effect = kind == 0 ? (CardEffectData)assets.Create<DamageEffectData>() :
                kind == 1 ? (CardEffectData)assets.Create<ShieldEffectData>() : assets.Create<HealEffectData>();
            var data = assets.Card(effect,CardTargetType.None);
            Assert.That(data.Validate(), Is.Not.Empty);
            var context = assets.Context(new[]{data}); var battle = new BattleController(context);
            Assert.That(battle.StartBattle(), Is.False);
            Assert.That(context.State, Is.EqualTo(BattleState.NotStarted));
            Assert.That(context.Ordinary.Hand.Count, Is.Zero);
        }
        /// <summary>验证义体关联牌配置错误会被报告，且不替换当前冒险状态。</summary>
        [TestCase(false)] [TestCase(true)]
        public void InvalidLinkedCyberneticCardIsReportedWithoutReplacingCurrentRun(bool nextEncounter)
        {
            var owner = new GameObject("BootstrapInvalidTest");
            try
            {
                var bootstrap = owner.AddComponent<GameBootstrap>();
                var ordinary = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.SingleEnemy);
                var cyberCard = assets.Card(assets.Create<ShieldEffectData>(),category:CardCategory.Cybernetic,ap:0,sanity:2);
                var config = assets.Create<CyberneticData>(); config.cyberneticId = "test";
                config.cards = new[]{cyberCard};
                var phantom = assets.Phantom();
                // 通过真实序列化字段布置 Inspector 配置，覆盖场景入口的接入方式。
                var serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("rules").objectReferenceValue = assets.Create<BattleRules>();
                serialized.FindProperty("phantomPain").objectReferenceValue = phantom;
                var deck = serialized.FindProperty("ordinaryDeck"); deck.arraySize = 1;
                deck.GetArrayElementAtIndex(0).objectReferenceValue = ordinary;
                var cyber = serialized.FindProperty("initialCybernetics"); cyber.arraySize = 1;
                cyber.GetArrayElementAtIndex(0).objectReferenceValue = config;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bootstrap.StartNewRun(), Is.True);
                var previousRun = bootstrap.Run; var previousBattle = bootstrap.Battle;
                if (nextEncounter)
                    DamageResolver.Apply(previousBattle.Context,previousRun.Player,previousBattle.Context.Enemies[0],100);
                config.cards = new[]{phantom};
                // 明确声明预期错误日志；本用例验证可读拒绝，而非异常导致初始化中断。
                LogAssert.Expect(LogType.Error,"义体只能关联非锁定义体牌。");
                bool result = true;
                Assert.DoesNotThrow(() => result = nextEncounter ? bootstrap.StartEncounter() : bootstrap.StartNewRun());
                Assert.That(result, Is.False);
                Assert.That(bootstrap.LastError, Does.Contain("义体"));
                Assert.That(bootstrap.Run, Is.SameAs(previousRun));
                Assert.That(bootstrap.Battle, Is.SameAs(previousBattle));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        /// <summary>验证启动入口创建战斗，并在后续遭遇中复用冒险状态。</summary>
        [Test] public void BootstrapStartsBattleAndKeepsRunForNextEncounter()
        {
            var owner = new GameObject("BootstrapTest");
            try
            {
                var bootstrap = owner.AddComponent<GameBootstrap>();
                var data = assets.Card(assets.Create<DamageEffectData>(),CardTargetType.SingleEnemy);
                var serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("rules").objectReferenceValue = assets.Create<BattleRules>();
                serialized.FindProperty("phantomPain").objectReferenceValue = assets.Phantom();
                var deck = serialized.FindProperty("ordinaryDeck"); deck.arraySize = 10;
                for(int i=0;i<10;i++) deck.GetArrayElementAtIndex(i).objectReferenceValue = data;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bootstrap.StartNewRun(), Is.True);
                var run = bootstrap.Run; var previous = bootstrap.Battle;
                Assert.That(bootstrap.TryPlay(previous.Context.Ordinary.Hand.Cards[0],previous.Context.Enemies[0]).Success, Is.True);
                Assert.That(previous.Context.Enemies[0].Health, Is.EqualTo(24));
                LogAssert.Expect(LogType.Error,"当前战斗尚未结束。");
                Assert.That(bootstrap.StartEncounter(), Is.False);
                DamageResolver.Apply(previous.Context,run.Player,previous.Context.Enemies[0],100);
                Assert.That(bootstrap.StartEncounter(), Is.True);
                Assert.That(bootstrap.Run, Is.SameAs(run));
                Assert.That(bootstrap.Battle, Is.Not.SameAs(previous));
                Assert.That(bootstrap.Battle.Context.Ordinary.Hand.Count, Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
