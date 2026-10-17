using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证新商店美术与原有交易控件接到同一预制体，避免有画面但无法结算。</summary>
    public sealed class ShopArtIntegrationTests
    {
        /// <summary>部位占位文字跟随当前探索，解除绑定后不显示上一位玩家的义体信息。</summary>
        [Test]
        public void RepairArtworkClearsWhenBindingChanges()
        {
            var rules = ScriptableObject.CreateInstance<BattleRules>();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ShopPanel.prefab"));
            try
            {
                var run = new RunState(rules);
                run.Loadout.AddInitial(AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/PrometheusBrainCard.asset"));
                var panel = root.GetComponent("ShopPanel");
                panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { run });
                var slot = root.transform.Find("Layout_16x9/RepairSlot_脑机");
                Assert.That(slot.Find("Symbol").GetComponent<Text>().text, Is.Empty);
                Assert.That(slot.Find("Icon").GetComponent<Image>().sprite,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/脑机-普罗米修斯.png")));
                var durability = slot.Find("Durability").GetComponent<Text>();
                var fee = slot.Find("RepairButton/Label").GetComponent<Text>();
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
                Assert.That(durability.font, Is.SameAs(font));
                Assert.That(fee.font, Is.SameAs(font));
                Assert.That(durability.fontSize, Is.EqualTo(22));
                Assert.That(fee.fontSize, Is.EqualTo(22));
                Assert.That(fee.text, Is.EqualTo("300"));
                Assert.That(slot.Find("RepairCurrencyIcon").GetComponent<Image>().sprite,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Goldicon.png")));
                panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { null });
                Assert.That(slot.Find("Symbol").GetComponent<Text>().text, Is.Empty);
                Assert.That(slot.Find("Icon").GetComponent<Image>().enabled, Is.False);
                Assert.That(slot.Find("RepairCurrencyIcon").GetComponent<Image>().enabled, Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(rules); }
        }

        /// <summary>美术背景、回血和退出均引用主分支资源，同时保留四个维修位与六个商品位。</summary>
        [Test]
        public void ArtworkAndTransactionBindingsShareTheExistingPanel()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ShopPanel.prefab");
            var frame = root.transform.Find("Layout_16x9");
            Assert.That(frame.Find("HealthText"), Is.Null, "旧生命文字应删除而非隐藏。");
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                Assert.That(text.font, Is.SameAs(font), text.name);
                Assert.That(text.fontSize, Is.EqualTo(text.name == "HealingAmount" || text.name == "HealingPrice" ? 28 : 22), text.name);
            }
            Assert.That(frame.Find("HealingHealthIcon").GetComponent<Image>().sprite,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Healthicon.png")));
            Assert.That(frame.Find("HealingCurrencyIcon").GetComponent<Image>().sprite,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Goldicon.png")));
            var background = frame.Find("ArtBackground");
            Assert.That(background, Is.Not.Null, "商店尚未接入新美术。");
            var art = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Shop_Popup.prefab");
            Assert.That(background.GetComponent<Image>().sprite, Is.SameAs(art.transform.Find("Background").GetComponent<Image>().sprite));
            var fields = new SerializedObject(root.GetComponent("ShopPanel"));
            Assert.That(fields.FindProperty("repairSlots").arraySize, Is.EqualTo(4));
            Assert.That(fields.FindProperty("productSlots").arraySize, Is.EqualTo(6));
            foreach (var pair in new[] { new[] { "healButton", "HealButton" }, new[] { "exitButton", "CancelButton" } })
            {
                var button = fields.FindProperty(pair[0]).objectReferenceValue as Button;
                Assert.That(button, Is.Not.Null);
                Assert.That(button.GetComponent<Image>().sprite, Is.SameAs(art.transform.Find(pair[1]).GetComponent<Image>().sprite));
            }
            Assert.That(root.GetComponent("PopupTween"), Is.Null, "显隐统一由 BasePanel 控制。");
        }
    }
}
