using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>检查复仇女神正式资源的部位、张数、共享耐久、卡面、价格及出发组合接线。</summary>
    public sealed class NemesisAssetTests
    {
        /// <summary>三脑三躯干三手两腿，完整美术与修复图标按部位对应，价格为购买600、修复200。</summary>
        [TestCase("Brain", 0, 2, 3, "脑机")]
        [TestCase("Torso", 1, 4, 3, "躯干")]
        [TestCase("Hands", 2, 6, 3, "手部")]
        [TestCase("Legs", 3, 10, 2, "腿部")]
        public void PartsHaveCorrectConfiguration(string part, int index, int cost, int durability, string icon)
        {
            var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/Nemesis" + part + "Card.asset");
            Assert.That(data, Is.Not.Null); Assert.That(data.Validate(), Is.Empty);
            Assert.That((int)data.slot, Is.EqualTo(index)); Assert.That(data.maxDurability, Is.EqualTo(durability));
            Assert.That(data.cards.Length, Is.EqualTo(durability)); Assert.That(data.cards.Distinct().Count(), Is.EqualTo(1));
            Assert.That(data.cards[0].sanityCost, Is.EqualTo(cost)); Assert.That(data.cards[0].cost, Is.Zero);
            Assert.That(data.factionId, Is.EqualTo("waste-construct-coalition"));
            Assert.That(data.cards[0].artwork, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Card/义体牌3-" + (index + 1) + ".png")));
            Assert.That(data.icon, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/" + icon + "-复仇女神.png")));
            Assert.That(data.shopPricing.purchasePrice, Is.EqualTo(600)); Assert.That(data.shopPricing.repairPrice, Is.EqualTo(200));
            Assert.That(data.shopPricing.canPurchase, Is.True);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/Nemesis" + part + "Card.prefab");
            Assert.That(prefab, Is.Not.Null); Assert.That(prefab.GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(data.cards[0].artwork));
            Assert.That(prefab.GetComponent<UnityEngine.UI.Image>().preserveAspect, Is.True);
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero);
            var catalog = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("Assets/GameData/CardPrefabCatalog.asset")).FindProperty("entries");
            Assert.That(Enumerable.Range(0, catalog.arraySize).Count(i => catalog.GetArrayElementAtIndex(i).FindPropertyRelative("data").objectReferenceValue == data.cards[0]), Is.EqualTo(1));
        }

        /// <summary>可从开始界面选择11张牌并配置统一套装，携带本身不激活，阵营不同不加入普罗米修斯改造池。</summary>
        [Test]
        public void StartSelectionRegistersElevenCardsAndUnifiedBuff()
        {
            var deck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/Nemesis.asset");
            var setup = AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            var buff = AssetDatabase.LoadAssetAtPath<RunBuffData>("Assets/GameData/Buffs/Nemesis.asset");
            Assert.That(deck, Is.Not.Null); Assert.That(buff, Is.Not.Null); Assert.That(deck.Validate(), Is.Empty); Assert.That(buff.Validate(), Is.Empty);
            Assert.That(deck.cybernetics.Sum(part => part.cards.Length), Is.EqualTo(11));
            Assert.That(deck.cybernetics.All(part => setup.availableCybernetics.Contains(part)), Is.True);
            Assert.That(setup.buffs, Does.Contain(buff));
            var run = setup.CreateRun(deck.cybernetics); Assert.That(run.Loadout.Owned.Count, Is.EqualTo(11)); Assert.That(run.Buffs.Active, Is.Empty);
            var transformation = AssetDatabase.LoadAssetAtPath<TransformBasicToCyberneticEffectData>("Assets/GameData/Effects/PrometheusTorso.asset");
            Assert.That(transformation.candidates.Intersect(deck.cybernetics), Is.Empty);
            run.AddGold(1000); var shop = new ShopService(run);
            Assert.That(shop.TryPurchase(deck.cybernetics[0], out _), Is.True); Assert.That(run.Gold, Is.EqualTo(400));
            var source = run.Loadout.Owned.First(); source.TryConsumeDurability();
            Assert.That(shop.TryRepair(source, out _), Is.True); Assert.That(run.Gold, Is.EqualTo(200)); Assert.That(source.Durability, Is.EqualTo(3));
        }
    }
}
