using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>核验新世界正式卡组、指定美术、维修与出发配置，避免只生成代码却无法在游戏选择。</summary>
    public sealed class NewWorldAssetTests
    {
        /// <summary>四部位各三张、共享耐久三点，费用和完整卡面按策划表对应。</summary>
        [TestCase("Brain", 0, 4, "脑机")]
        [TestCase("Torso", 1, 8, "躯干")]
        [TestCase("Hands", 2, 3, "手部")]
        [TestCase("Legs", 3, 10, "腿部")]
        public void PartsHaveCorrectArtPricesAndCopies(string part, int index, int cost, string icon)
        {
            var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/NewWorld" + part + "Card.asset");
            Assert.That(data, Is.Not.Null);
            Assert.That(data.Validate(), Is.Empty); Assert.That((int)data.slot, Is.EqualTo(index));
            Assert.That(data.maxDurability, Is.EqualTo(3)); Assert.That(data.cards.Length, Is.EqualTo(3));
            Assert.That(data.cards.Distinct().Count(), Is.EqualTo(1)); Assert.That(data.cards[0].sanityCost, Is.EqualTo(cost));
            Assert.That(data.factionId, Is.EqualTo("tian-qiong-legion"));
            Assert.That(data.cards[0].artwork, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Card/义体牌2-" + (index + 1) + ".png")));
            Assert.That(data.icon, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/" + icon + "-新世界.png")));
            Assert.That(data.shopPricing, Is.Not.Null); Assert.That(data.shopPricing.repairPrice, Is.EqualTo(150));
            Assert.That(data.shopPricing.canPurchase, Is.False, "没有给出新世界购买价，本次只开放维修。");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/NewWorld" + part + "Card.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent("CardView"); Assert.That(view, Is.Not.Null);
            Assert.That(new SerializedObject(view).FindProperty("cardData").objectReferenceValue, Is.SameAs(data.cards[0]));
            var catalog = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GameData/CardPrefabCatalog.asset")).FindProperty("entries");
            Assert.That(Enumerable.Range(0, catalog.arraySize).Any(i => catalog.GetArrayElementAtIndex(i).FindPropertyRelative("data").objectReferenceValue == data.cards[0] &&
                catalog.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue == view), Is.True);
            Assert.That(prefab.GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(data.cards[0].artwork));
        }

        /// <summary>开始界面可选新世界且默认带入十二张，套装和改造候选入口均已登记。</summary>
        [Test]
        public void StartSetupOffersNewWorldAndRegistersSet()
        {
            var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/NewWorld.asset");
            Assert.That(deck, Is.Not.Null); Assert.That(deck.Validate(), Is.Empty);
            var setup = AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            Assert.That(deck.cybernetics.All(data => setup.availableCybernetics.Contains(data)), Is.True);
            Assert.That(setup.defaultCybernetics, Is.EquivalentTo(deck.cybernetics));
            var bonus = AssetDatabase.LoadAssetAtPath<NewWorldSetBonusData>("Assets/GameData/SetBonuses/NewWorld.asset");
            Assert.That(bonus, Is.Not.Null); Assert.That(bonus.Validate(), Is.Empty); Assert.That(setup.setBonuses, Does.Contain(bonus));
            var run = setup.CreateRun(deck.cybernetics);
            Assert.That(run.Loadout.Owned.Count, Is.EqualTo(12)); Assert.That(run.SetBonuses.Active, Is.Empty);
            var transform = AssetDatabase.LoadAssetAtPath<TransformBasicToCyberneticEffectData>("Assets/GameData/Effects/PrometheusTorso.asset");
            Assert.That(deck.cybernetics.All(data => transform.candidates.Contains(data)), Is.True);
        }
    }
}
