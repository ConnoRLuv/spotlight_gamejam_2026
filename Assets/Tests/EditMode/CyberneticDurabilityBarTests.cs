using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    public sealed class CyberneticDurabilityBarTests
    {
        private GameObject root;
        private Component bar;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("DurabilityBar", typeof(RectTransform));
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("SpotlightGameJam.UI.CyberneticDurabilityBar"))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "缺少分格耐久条组件。");
            bar = root.AddComponent(type);
        }

        [TearDown]
        public void TearDown() { if (root) UnityEngine.Object.DestroyImmediate(root); }

        private void Refresh(int current, int maximum)
        {
            bar.GetType().GetMethod("Refresh").Invoke(bar, new object[] { current, maximum });
        }

        private Image[] Segments() => root.GetComponentsInChildren<Image>(true)
            .Where(image => image.name.StartsWith("Segment_", StringComparison.Ordinal)).ToArray();

        [Test]
        public void RemainingUsesAreBrightAndSpentUsesStayVisible()
        {
            Refresh(2, 3);
            var segments = Segments();
            Assert.That(segments.Length, Is.EqualTo(3));
            Assert.That(segments.All(segment => segment.gameObject.activeSelf), Is.True);
            Assert.That(segments[0].color, Is.EqualTo(segments[1].color));
            Assert.That(segments[2].color, Is.Not.EqualTo(segments[0].color));
            Assert.That(root.GetComponentsInChildren<Image>(true).All(image => !image.raycastTarget), Is.True);
            Refresh(0, 3);
            Assert.That(Segments().All(segment => segment.color == segments[2].color), Is.True);
            Assert.That(root.activeSelf, Is.True, "耗尽仍保留空格轨道。");
        }

        [Test]
        public void DifferentMaximumsReuseSegmentsAndEmptySlotsHideTheBar()
        {
            Refresh(4, 5);
            var original = Segments();
            Refresh(1, 2);
            Assert.That(Segments().Count(segment => segment.gameObject.activeSelf), Is.EqualTo(2));
            CollectionAssert.AreEqual(original, Segments(), "刷新不应销毁并重建色块。");
            Refresh(0, 0);
            Assert.That(root.activeSelf, Is.False);
            Refresh(7, 7);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(Segments().Length, Is.EqualTo(7), "最大耐久增加时应补齐格数。");
            Assert.That(Segments().All(segment => segment.color == Segments()[0].color), Is.True);
        }

        [TestCase("测试卡组")]
        [TestCase("Type-01：测试卡组")]
        [TestCase("Type-02: 测试卡组")]
        public void EquipmentRefreshTracksActualDurabilityAndBattleReset(string deckName)
        {
            var row = new GameObject("EquipmentSlot", typeof(RectTransform));
            root.transform.SetParent(row.transform, false);
            try
            {
                var slotType = bar.GetType().Assembly.GetType("SpotlightGameJam.UI.CyberneticEquipmentSlot");
                var slot = row.AddComponent(slotType);
                var title = new GameObject("Name", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                title.transform.SetParent(row.transform, false);
                var label = new GameObject("Durability", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(row.transform, false);
                slotType.GetField("durabilityBar", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).SetValue(slot, bar);
                slotType.GetMethod("Configure").Invoke(slot,
                    new object[] { CyberneticSlot.Torso, null, null, title, label, null, null });
                Assert.That(root.activeSelf, Is.False);
                using (var assets = new TestAssets())
                {
                    var data = assets.Card(assets.Create<ShieldEffectData>(), CardTargetType.Self,
                        CardCategory.Cybernetic, 0, 4);
                    var cyber = assets.Create<CyberneticData>();
                    cyber.cyberneticId = "durability-bar-test";
                    cyber.slot = CyberneticSlot.Torso; cyber.maxDurability = 3; cyber.cards = new[] { data };
                    var deck = assets.Create<CyberneticDeckData>();
                    deck.deckName = deckName; deck.cybernetics = new[] { cyber };
                    var deckField = slotType.GetField("cyberneticDecks", System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    Assert.That(deckField, Is.Not.Null, "槽位应从卡组配置读取名称。");
                    deckField.SetValue(slot, new[] { deck });
                    var source = new CyberneticInstance(cyber);
                    var card = new CardInstance(data, source);
                    var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
                    run.Loadout.Equip(source); var random = new RandomUtility(1);
                    var context = new BattleContext(run, rules, new[] { new CombatantState("enemy", 30) },
                        new CardDeck(13, Array.Empty<CardInstance>(), random),
                        new CardDeck(5, new[] { card }, random), assets.Phantom(), random);
                    var battle = new BattleController(context, 0); battle.StartBattle();
                    Assert.That(battle.TryPlay(card).Success, Is.True);
                    slotType.GetMethod("Refresh").Invoke(slot, new object[] { context });
                    Assert.That(root.activeSelf, Is.True);
                    Assert.That(title.text, Is.EqualTo("测试卡组 • 躯干"));
                    Assert.That(label.text, Is.EqualTo("耐久"));
                    var spentColor = Segments()[2].color;
                    Assert.That(Segments().Count(segment => segment.color != spentColor), Is.EqualTo(2));
                    source.TryConsumeDurability();
                    slotType.GetMethod("Refresh").Invoke(slot, new object[] { context });
                    Assert.That(Segments().Count(segment => segment.color != spentColor), Is.EqualTo(1));
                    slotType.GetMethod("Refresh").Invoke(slot, new object[] { null });
                    Assert.That(root.activeSelf, Is.False);
                    Assert.That(title.text, Is.Empty);
                    Assert.That(label.text, Is.Empty);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(row); }
        }
    }
}
