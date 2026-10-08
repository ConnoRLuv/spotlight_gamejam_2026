using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>检查交付的七种卡牌资产，而非仅验证运行时临时配置。</summary>
    public sealed class CardPrefabAssetTests
    {
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
