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
        /// <summary>验证可切换义体牌组，所选牌组覆盖旧入口并保留各卡牌的真实义体来源。</summary>
        [Test]
        public void SelectedCyberneticDeckOverridesLegacyInitialLoadout()
        {
            var deck = assets.Create<CyberneticDeckData>();
            var owner = new GameObject("Cybernetic deck selection test");
            try
            {
                var selected = assets.Create<CyberneticData>(); selected.cyberneticId = "future-brain";
                selected.slot = CyberneticSlot.Brain;
                var card = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
                selected.cards = Enumerable.Repeat(card, 3).ToArray();
                var legacy = assets.Create<CyberneticData>(); legacy.cyberneticId = "legacy-hands";
                legacy.slot = CyberneticSlot.Hands; legacy.cards = new[] { card };
                var deckSettings = new SerializedObject(deck);
                deckSettings.FindProperty("deckId").stringValue = "future-set";
                deckSettings.FindProperty("deckName").stringValue = "测试义体牌组";
                var entries = deckSettings.FindProperty("cybernetics"); entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).objectReferenceValue = selected;
                deckSettings.ApplyModifiedPropertiesWithoutUndo();

                var bootstrap = owner.AddComponent<GameBootstrap>();
                var settings = new SerializedObject(bootstrap);
                settings.FindProperty("rules").objectReferenceValue = assets.Create<BattleRules>();
                settings.FindProperty("phantomPain").objectReferenceValue = assets.Phantom();
                var oldEntries = settings.FindProperty("initialCybernetics"); oldEntries.arraySize = 1;
                oldEntries.GetArrayElementAtIndex(0).objectReferenceValue = legacy;
                var selectedField = settings.FindProperty("initialCyberneticDeck");
                Assert.That(selectedField, Is.Not.Null, "GameBootstrap 需要提供牌组选择字段。");
                selectedField.objectReferenceValue = deck; settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bootstrap.StartNewRun(), Is.True, bootstrap.LastError);
                Assert.That(bootstrap.Run.Loadout.Get(CyberneticSlot.Hands), Is.Null);
                var brain = bootstrap.Run.Loadout.Get(CyberneticSlot.Brain);
                Assert.That(brain.Data, Is.SameAs(selected));
                Assert.That(bootstrap.Battle.Context.Cybernetic.AllCards.Count(), Is.EqualTo(3));
                Assert.That(bootstrap.Battle.Context.Cybernetic.AllCards.All(instance => instance.Source == brain), Is.True);

                // 新牌组只作用于新冒险；本次冒险继续携带已有装备及耐久。
                brain.TryConsumeDurability();
                var previousRun = bootstrap.Run;
                DamageResolver.Apply(bootstrap.Battle.Context, previousRun.Player, bootstrap.Battle.Context.Enemies[0], 100);
                selectedField.objectReferenceValue = null; settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bootstrap.StartEncounter(), Is.True, bootstrap.LastError);
                Assert.That(bootstrap.Run.Loadout.Get(CyberneticSlot.Brain), Is.SameAs(brain));
                Assert.That(brain.Durability, Is.EqualTo(selected.maxDurability - 1));

                // 空草稿可以保存，但启用后必须在替换现有冒险前明确拒绝。
                entries.arraySize = 0; deckSettings.ApplyModifiedPropertiesWithoutUndo();
                settings.Update(); settings.FindProperty("initialCyberneticDeck").objectReferenceValue = deck;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var previousBattle = bootstrap.Battle;
                LogAssert.Expect(LogType.Error, "义体牌组至少需要一个义体配置。");
                Assert.That(bootstrap.StartNewRun(), Is.False);
                Assert.That(bootstrap.Run, Is.SameAs(previousRun));
                Assert.That(bootstrap.Battle, Is.SameAs(previousBattle));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        /// <summary>验证义体牌组拒绝重复部位、空引用、缺失效果及缺失身份，避免启用未完成的配置。</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CyberneticDeckRejectsIncompleteOrConflictingConfiguration(int kind)
        {
            var deck = assets.Create<CyberneticDeckData>(); deck.deckId = "future-set"; deck.deckName = "待扩展系列";
            var cybernetic = assets.Create<CyberneticData>(); cybernetic.cyberneticId = "future-brain";
            var card = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            cybernetic.cards = new[] { card }; deck.cybernetics = new[] { cybernetic };
            Assert.That(deck.Validate(), Is.Empty);
            if (kind == 0) deck.cybernetics = new[] { cybernetic, cybernetic };
            if (kind == 1) deck.cybernetics = new CyberneticData[] { null };
            if (kind == 2) card.effects = Array.Empty<CardEffectData>();
            if (kind == 3) deck.deckId = "";
            Assert.That(deck.Validate(), Is.Not.Empty);
        }

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
