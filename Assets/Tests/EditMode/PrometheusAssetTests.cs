using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotlightGameJam.Tests
{
    /// <summary>检查普罗米修斯卡牌、预制体映射及探索 buff，确认独立测试场景已移除。</summary>
    public sealed class PrometheusAssetTests
    {
        private const string DeckPath = "Assets/GameData/CyberneticDecks/Prometheus.asset";
        private const string ScenePath = "Assets/Scenes/PrometheusBattleScene.unity";

        /// <summary>四个部位的正式卡牌应关联独立来源、正确费用和可交互的占位预制体。</summary>
        [TestCase("Brain", "篡改者", CyberneticSlot.Brain, 8, 4, 3)]
        [TestCase("Torso", "改造驱动", CyberneticSlot.Torso, 40, 1, 1)]
        [TestCase("Hands", "生命涌现", CyberneticSlot.Hands, 2, 3, 3)]
        [TestCase("Legs", "持久变量", CyberneticSlot.Legs, 30, 1, 1)]
        public void CardsHaveConfiguredSourcesAndCatalogEntries(string part, string name,
            CyberneticSlot slot, int sanity, int durability, int copies)
        {
            string key = "Prometheus" + part + "Card";
            var card = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/" + key + ".asset");
            var source = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + key + ".asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + key + ".prefab");
            Assert.That(card, Is.Not.Null, key);
            Assert.That(source, Is.Not.Null, key);
            Assert.That(prefab, Is.Not.Null, key);
            Assert.That(card.cardName, Is.EqualTo(name));
            Assert.That(card.category, Is.EqualTo(CardCategory.Cybernetic));
            Assert.That(card.cost, Is.Zero);
            Assert.That(card.sanityCost, Is.EqualTo(sanity));
            Assert.That(card.Validate(), Is.Empty);
            Assert.That(new SerializedObject(source).FindProperty("factionId").stringValue, Is.EqualTo("tian-qiong-legion"));
            Assert.That(source.slot, Is.EqualTo(slot));
            Assert.That(source.maxDurability, Is.EqualTo(durability));
            Assert.That(source.cards.Length, Is.EqualTo(copies));
            Assert.That(source.cards.All(value => value == card), Is.True);
            Assert.That(source.Validate(), Is.Empty);

            var view = prefab.GetComponent("CardView");
            Assert.That(view, Is.Not.Null);
            var serializedView = new SerializedObject(view);
            Assert.That(serializedView.FindProperty("cardData").objectReferenceValue, Is.SameAs(card));
            Assert.That(serializedView.FindProperty("cyberneticData").objectReferenceValue, Is.SameAs(source));
            Assert.That(serializedView.FindProperty("button").objectReferenceValue, Is.Not.Null);
            Assert.That(serializedView.FindProperty("descriptionText").objectReferenceValue, Is.Not.Null);
            Color.RGBToHSV(serializedView.FindProperty("tint").colorValue, out _, out float saturation, out _);
            Assert.That(saturation, Is.LessThan(0.4f), "卡面使用低饱和度占位色。");
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero);

            // 测试程序集只依赖运行时程序集，通过序列化字段检查 UI 目录，避免反向增加依赖。
            var catalog = AssetDatabase.LoadMainAssetAtPath("Assets/GameData/CardPrefabCatalog.asset");
            var entries = new SerializedObject(catalog).FindProperty("entries");
            int matchingEntries = 0;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("data").objectReferenceValue != card) continue;
                matchingEntries++;
                Assert.That(entry.FindPropertyRelative("prefab").objectReferenceValue, Is.SameAs(view));
            }
            Assert.That(matchingEntries, Is.EqualTo(1), "重复构建不能产生重复映射。");
        }

        /// <summary>牌组提供三脑、一躯干、三手、一腿共八张牌，每个部位保留独立来源。</summary>
        [Test]
        public void DeckContainsEightCardsAcrossFourParts()
        {
            var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>(DeckPath);
            Assert.That(deck, Is.Not.Null);
            Assert.That(deck.deckName, Is.EqualTo("Type-01：普罗米修斯"));
            Assert.That(deck.Validate(), Is.Empty);
            Assert.That(deck.cybernetics.Select(value => value.slot).Distinct().Count(), Is.EqualTo(4));
            Assert.That(deck.cybernetics.Sum(value => value.cards.Length), Is.EqualTo(8));
            var loadout = new CyberneticLoadout();
            foreach (var data in deck.cybernetics) loadout.Equip(new CyberneticInstance(data));
            var cards = loadout.GetUsableCards().ToArray();
            Assert.That(cards.Length, Is.EqualTo(8));
            Assert.That(cards.All(card => card.Source != null && loadout.Contains(card.Source)), Is.True);
            Assert.That(cards.Select(card => card.Id).Distinct().Count(), Is.EqualTo(8));
        }

        /// <summary>改造候选覆盖原制式、普罗米修斯和新世界；只校验直接配置，避免递归校验。</summary>
        [Test]
        public void EffectsAndTransformationCandidatesAreConfigured()
        {
            Assert.That(LoadEffect("Brain").GetType().Name, Is.EqualTo("DiscardDrawNextTurnEffectData"));
            Assert.That(LoadEffect("Hands").GetType().Name, Is.EqualTo("FreeBasicCardsEffectData"));
            Assert.That(LoadEffect("Legs").GetType().Name, Is.EqualTo("HealthPaymentEffectData"));
            var effect = LoadEffect("Torso");
            Assert.That(effect.GetType().Name, Is.EqualTo("TransformBasicToCyberneticEffectData"));
            var serializedCandidates = new SerializedObject(effect).FindProperty("candidates");
            Assert.That(serializedCandidates, Is.Not.Null);
            var candidates = Enumerable.Range(0, serializedCandidates.arraySize)
                .Select(index => serializedCandidates.GetArrayElementAtIndex(index).objectReferenceValue as CyberneticData).ToArray();
            Assert.That(candidates.Length, Is.EqualTo(12));
            Assert.That(candidates.Distinct().Count(), Is.EqualTo(12));
            Assert.That(candidates.All(value => value && new SerializedObject(value).FindProperty("factionId").stringValue == "tian-qiong-legion"), Is.True);
            foreach (var part in new[] { "Brain", "Torso", "Hands", "Legs" })
            {
                Assert.That(candidates, Does.Contain(AssetDatabase.LoadAssetAtPath<CyberneticData>(
                    "Assets/GameData/Cybernetics/" + part + "Card.asset")));
                Assert.That(candidates, Does.Contain(AssetDatabase.LoadAssetAtPath<CyberneticData>(
                    "Assets/GameData/Cybernetics/Prometheus" + part + "Card.asset")));
                Assert.That(candidates, Does.Contain(AssetDatabase.LoadAssetAtPath<CyberneticData>(
                    "Assets/GameData/Cybernetics/NewWorld" + part + "Card.asset")));
            }
        }

        /// <summary>全局 buff 独立配置，不改写共享过载资产的基础费用与行动点效果。</summary>
        [Test]
        public void GlobalBuffKeepsSharedOverloadAssetUnchanged()
        {
            var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>(DeckPath);
            Assert.That(deck, Is.Not.Null);
            var buff = AssetDatabase.LoadAssetAtPath<RunBuffData>("Assets/GameData/Buffs/Prometheus.asset");
            Assert.That(buff, Is.Not.Null);
            Assert.That(buff.Validate(), Is.Empty);
            Assert.That(buff.requiredCybernetics, Is.EquivalentTo(deck.cybernetics));
            var overload = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/OverloadCard.asset");
            Assert.That(overload.sanityCost, Is.EqualTo(5));
            Assert.That(overload.effects.Single(), Is.TypeOf<ActionPointEffectData>());
        }

        /// <summary>独立验证场景已移除，BattleScene 当前使用新世界作为直接运行的测试牌组。</summary>
        [Test]
        public void DedicatedSceneIsRemovedAndBattleSceneUsesNewWorld()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Null);
            AssertSceneDeck("Assets/Scenes/BattleScene.unity", "Assets/GameData/CyberneticDecks/NewWorld.asset");
        }

        /// <summary>读取部位正式卡牌的唯一效果，提供明确的资源缺失断言。</summary>
        private static CardEffectData LoadEffect(string part)
        {
            var card = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/Prometheus" + part + "Card.asset");
            Assert.That(card, Is.Not.Null, part);
            Assert.That(card.effects.Length, Is.EqualTo(1));
            return card.effects[0];
        }

        /// <summary>以附加方式只读场景引用，不保存或替换成员当前正在编辑的场景。</summary>
        private static void AssertSceneDeck(string scenePath, string deckPath)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var source = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true)).Single();
                var serialized = new SerializedObject(source);
                var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>(deckPath);
                Assert.That(serialized.FindProperty("initialCyberneticDeck").objectReferenceValue, Is.SameAs(deck));
                Assert.That(serialized.FindProperty("rules").objectReferenceValue, Is.Not.Null);
                var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Single(component => component && component.GetType().Name == "BattleHUD");
                Assert.That(new SerializedObject(hud).FindProperty("catalog").objectReferenceValue,
                    Is.SameAs(AssetDatabase.LoadMainAssetAtPath("Assets/GameData/CardPrefabCatalog.asset")));
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
