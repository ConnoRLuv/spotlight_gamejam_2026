using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证商店支付、持久牌组新增和共享耐久修复，不依赖场景控件。</summary>
    public sealed class ShopTransactionTests
    {
        private TestAssets assets;
        private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();

        /// <summary>为每个用例创建隔离配置。</summary>
        [SetUp] public void SetUp() { assets = new TestAssets(); }
        /// <summary>清理临时价格与卡牌资产。</summary>
        [TearDown] public void TearDown()
        {
            foreach (var item in temporary) UnityEngine.Object.DestroyImmediate(item);
            temporary.Clear(); assets.Dispose();
        }

        /// <summary>回血收取固定100，最多恢复10；满血、余额不足或已死亡时不收费。</summary>
        [TestCase(20, 100, 90, 0, true)] [TestCase(20, 99, 80, 99, false)]
        [TestCase(5, 100, 100, 0, true)] [TestCase(0, 100, 100, 100, false)]
        [TestCase(100, 100, 0, 100, false)]
        public void HealingRespectsPriceHealthCapAndFailureBoundaries(int damage, int balance, int health, int money, bool success)
        {
            var run = new RunState(assets.Create<BattleRules>()); run.Player.ReceiveDamage(damage); run.AddGold(balance);
            run.Player.AddShield(7); var shield = run.Player.Shield; var sanity = run.Player.Sanity;
            var service = Service(run); var method = service.GetType().GetMethod("TryHeal");
            Assert.That(method, Is.Not.Null, "缺少付费生命恢复入口。");
            Assert.That((bool)method.Invoke(service, new object[] { null }), Is.EqualTo(success));
            Assert.That(run.Gold, Is.EqualTo(money)); Assert.That(run.Player.Health, Is.EqualTo(health));
            Assert.That(run.Player.Shield, Is.EqualTo(shield)); Assert.That(run.Player.Sanity, Is.EqualTo(sanity));
        }

        /// <summary>余额边界正确，成功购买只新增一张，不替换同部位原牌，并进入下一场牌组。</summary>
        [TestCase(999, false)] [TestCase(1000, true)] [TestCase(1100, true)]
        public void PurchaseAddsOnePersistentCardWithoutReplacingOriginal(int balance, bool success)
        {
            var run = new RunState(assets.Create<BattleRules>()); run.AddGold(balance);
            var original = Part("original", 3, 150); run.Loadout.AddInitial(original);
            var product = Part("prometheus", 3, 300); var shop = Service(run);
            Assert.That(Call(shop, "TryPurchase", product), Is.EqualTo(success));
            Assert.That(run.Gold, Is.EqualTo(success ? balance - 1000 : balance));
            Assert.That(run.Loadout.Owned.Count(card => card.Data == original), Is.EqualTo(3));
            var added = run.Loadout.Owned.Where(card => card.Data == product).ToArray();
            Assert.That(added.Length, Is.EqualTo(success ? 1 : 0));
            if (success)
            {
                Assert.That(added[0].IsInitialCard, Is.False);
                Assert.That(run.Loadout.GetUsableCards().Count(card => card.Source.Data == product), Is.EqualTo(1));
                Assert.That(Call(shop, "TryPurchase", product), Is.False, "已拥有型号不能再次收费购买。");
                Assert.That(run.Gold, Is.EqualTo(balance - 1000));
            }
        }

        /// <summary>各系列单次修复仅增加一点共享耐久，零耐久也允许修复，余额准确扣除。</summary>
        [TestCase(150)] [TestCase(200)] [TestCase(300)]
        public void RepairRestoresExactlyOneSharedPoint(int price)
        {
            var run = new RunState(assets.Create<BattleRules>()); run.AddGold(price);
            var data = Part("repair", 3, price); run.Loadout.AddInitial(data);
            var source = run.Loadout.Owned[1]; while (source.TryConsumeDurability()) { }
            Assert.That(Call(Service(run), "TryRepair", source), Is.True);
            Assert.That(run.Gold, Is.Zero);
            Assert.That(run.Loadout.Owned.Select(card => card.Durability), Is.All.EqualTo(1));
        }

        /// <summary>满耐久、余额不足和不属于玩家的义体都不得收费。</summary>
        [Test]
        public void RejectedRepairsDoNotSpendMoneyOrChangeDurability()
        {
            var run = new RunState(assets.Create<BattleRules>()); run.AddGold(299);
            var data = Part("repair", 3, 300); run.Loadout.AddInitial(data); var shop = Service(run);
            Assert.That(Call(shop, "TryRepair", run.Loadout.Owned[0]), Is.False);
            run.Loadout.Owned[0].TryConsumeDurability();
            Assert.That(Call(shop, "TryRepair", run.Loadout.Owned[0]), Is.False);
            Assert.That(Call(shop, "TryRepair", new CyberneticInstance(data)), Is.False);
            Assert.That(run.Gold, Is.EqualTo(299));
            Assert.That(run.Loadout.Owned.Select(card => card.Durability), Is.All.EqualTo(3));
        }

        /// <summary>没有购买定价的义体拒绝交易；价格配置缺失也不能免费购买。</summary>
        [Test]
        public void MissingOrDisabledPurchasePriceDoesNotBecomeFree()
        {
            var run = new RunState(assets.Create<BattleRules>()); run.AddGold(5000); var shop = Service(run);
            var data = Part("unpriced", 1, 150);
            var fields = new SerializedObject(data); var price = fields.FindProperty("shopPricing").objectReferenceValue;
            var config = new SerializedObject(price); config.FindProperty("canPurchase").boolValue = false; config.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Call(shop, "TryPurchase", data), Is.False);
            fields.FindProperty("shopPricing").objectReferenceValue = null; fields.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Call(shop, "TryPurchase", data), Is.False);
            Assert.That(run.Gold, Is.EqualTo(5000)); Assert.That(run.Loadout.Owned, Is.Empty);
        }

        /// <summary>会话下一场战斗沿用购买后的金币和持久新牌，当前战斗牌区不插入商品。</summary>
        [Test]
        public void PurchasedCardAndPaidBalancePersistIntoNextEncounter()
        {
            var original = Part("original", 3, 150); var product = Part("purchased", 3, 300);
            var setup = assets.Create<AdventureSetupData>(); setup.rules = assets.Create<BattleRules>(); setup.phantomPain = assets.Phantom();
            var session = AdventureSession.GetOrCreate();
            try
            {
                Assert.That(session.TryBegin(setup, new[] { original }), Is.True, session.LastError);
                Assert.That(session.TryStartEncounter(1, 0), Is.True, session.LastError);
                var run = session.Run; run.AddGold(1300);
                Assert.That(Call(Service(run), "TryPurchase", product), Is.True);
                var source = run.Loadout.Owned.Single(card => card.Data == product); source.TryConsumeDurability();
                var first = session.CurrentBattle.Context;
                Assert.That(first.Cybernetic.DrawPile.Cards.Concat(first.Cybernetic.Hand.Cards).Any(card => card.Source.Data == product), Is.False);
                first.Enemies[0].ReceiveDamage(1); first.CheckOutcome(); session.ReleaseEncounter(session.CurrentBattle);
                Assert.That(session.TryStartEncounter(1, 0), Is.True, session.LastError);
                var next = session.CurrentBattle.Context;
                var added = next.Cybernetic.DrawPile.Cards.Concat(next.Cybernetic.Hand.Cards).Where(card => card.Source.Data == product).ToArray();
                Assert.That(added.Length, Is.EqualTo(1)); Assert.That(added[0].Source, Is.SameAs(source));
                Assert.That(added[0].Source.Durability, Is.EqualTo(3)); Assert.That(run.Gold, Is.EqualTo(300));
            }
            finally { session.ReleaseEncounter(session.CurrentBattle); UnityEngine.Object.DestroyImmediate(session.gameObject); }
        }

        /// <summary>已保存的系列资产与策划价格一致；未提供购买价的系列不得标记为可购买。</summary>
        [TestCase("Standard", false, 0, 150)] [TestCase("NewWorld", false, 0, 150)]
        [TestCase("Prometheus", true, 1000, 300)] [TestCase("Nemesis", true, 600, 200)]
        public void SavedPriceProfilesMatchDesign(string name, bool purchase, int price, int repair)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GameData/ShopPrices/" + name + ".asset");
            Assert.That(data, Is.Not.Null);
            var fields = new SerializedObject(data);
            Assert.That(fields.FindProperty("canPurchase").boolValue, Is.EqualTo(purchase));
            Assert.That(fields.FindProperty("purchasePrice").intValue, Is.EqualTo(price));
            Assert.That(fields.FindProperty("repairPrice").intValue, Is.EqualTo(repair));
        }

        /// <summary>创建带价格与重复卡配置的义体，购买仍应只取得其中一张。</summary>
        private CyberneticData Part(string id, int copies, int repairPrice)
        {
            var data = assets.Create<CyberneticData>(); data.cyberneticId = id; data.maxDurability = 4;
            var card = assets.Card(assets.Create<HealEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 1);
            data.cards = Enumerable.Repeat(card, copies).ToArray();
            var type = Type.GetType("SpotlightGameJam.CyberneticShopPriceData, SpotlightGameJam.Runtime");
            Assert.That(type, Is.Not.Null, "缺少商店价格配置。");
            var price = ScriptableObject.CreateInstance(type); temporary.Add(price);
            var serialized = new SerializedObject(price);
            serialized.FindProperty("canPurchase").boolValue = true;
            serialized.FindProperty("purchasePrice").intValue = 1000;
            serialized.FindProperty("repairPrice").intValue = repairPrice;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var fields = new SerializedObject(data); fields.FindProperty("shopPricing").objectReferenceValue = price; fields.ApplyModifiedPropertiesWithoutUndo();
            return data;
        }

        /// <summary>通过反射创建服务，让缺失实现表现为断言失败。</summary>
        private static object Service(RunState run)
        {
            var type = Type.GetType("SpotlightGameJam.ShopService, SpotlightGameJam.Runtime");
            Assert.That(type, Is.Not.Null, "缺少商店交易服务。");
            return Activator.CreateInstance(type, run);
        }

        /// <summary>调用带失败原因输出的交易接口。</summary>
        private static bool Call(object shop, string method, object target)
        {
            return (bool)shop.GetType().GetMethod(method).Invoke(shop, new[] { target, null });
        }
    }
}
