using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotlightGameJam.Tests
{
    /// <summary>检查交付的七种卡牌资产，而非仅验证运行时临时配置。</summary>
    public sealed class CardPrefabAssetTests
    {
        /// <summary>验证正式场景包含指定的两套初始牌组，首回合分别抽三张普通牌和一张义体牌。</summary>
        [Test]
        public void BattleSceneStartsWithConfiguredOrdinaryAndCyberneticDecks()
        {
            // 复用已加载场景或以附加方式读取；仅在临时入口上执行开战，保留编辑现场。
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            var owner = new GameObject("Deck configuration test");
            try
            {
                var source = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true))
                    .Single(component => component.gameObject != owner);
                var bootstrap = owner.AddComponent<GameBootstrap>();
                EditorUtility.CopySerialized(source, bootstrap);
                Assert.That(bootstrap.StartNewRun(), Is.True, bootstrap.LastError);
                var context = bootstrap.Battle.Context;
                var ordinary = context.Ordinary.AllCards.ToArray();
                Assert.That(ordinary.Length, Is.EqualTo(13));
                foreach (var entry in new[] { new { Id = "AttackCard", Count = 4 }, new { Id = "DefenseCard", Count = 3 },
                    new { Id = "HealCard", Count = 3 }, new { Id = "OverloadCard", Count = 3 } })
                    Assert.That(ordinary.Count(card => card.Data.cardId == entry.Id), Is.EqualTo(entry.Count), entry.Id);
                var cybernetic = context.Cybernetic.AllCards.ToArray();
                Assert.That(cybernetic.Length, Is.EqualTo(12));
                foreach (var key in new[] { "BrainCard", "TorsoCard", "HandsCard", "LegsCard" })
                    Assert.That(cybernetic.Count(card => card.Data.cardId == key), Is.EqualTo(3), key);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(3));
                Assert.That(context.Cybernetic.Hand.Count, Is.EqualTo(1));
                var nextOrdinary = context.Ordinary.Peek(3).ToArray();
                var nextCybernetic = context.Cybernetic.Peek(1).ToArray();
                Assert.That(bootstrap.Battle.EndTurn(), Is.True);
                Assert.That(context.Ordinary.Hand.Cards.Skip(3), Is.EqualTo(nextOrdinary));
                Assert.That(context.Cybernetic.Hand.Cards.Skip(1), Is.EqualTo(nextCybernetic));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>验证卡牌预制体、脚本引用及对应配置资产已正确接入。</summary>
        [TestCase("AttackCard", "攻击", 1, 0)]
        [TestCase("DefenseCard", "防御", 1, 0)]
        [TestCase("HealCard", "回复", 1, 0)]
        [TestCase("BrainCard", "军用战术协调矩阵", 0, 8)]
        [TestCase("TorsoCard", "军用生命维持器系统", 0, 4)]
        [TestCase("HandsCard", "军用脉冲发射器", 0, 2)]
        [TestCase("LegsCard", "军用战术推进器", 0, 8)]
        public void PrefabAndConfigurationAreReady(string key, string label, int ap, int sanity)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + key + ".prefab");
            Assert.That(prefab, Is.Not.Null, "卡牌预制体缺失：" + key);
            var view = prefab.GetComponent("CardView");
            Assert.That(view, Is.Not.Null, "预制体未挂载 CardView。");
            var data = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/" + key + ".asset");
            Assert.That(data, Is.Not.Null);
            Assert.That(data.cardName, Is.EqualTo(label));
            Assert.That(data.cost, Is.EqualTo(ap));
            Assert.That(data.sanityCost, Is.EqualTo(sanity));
            Assert.That(data.Validate(), Is.Empty);
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.Zero);
            var serialized = new SerializedObject(view);
            Assert.That(serialized.FindProperty("cardData").objectReferenceValue, Is.SameAs(data));
        }
    }
}
