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
                Assert.That(slot.Find("Symbol").GetComponent<Text>().text, Is.EqualTo("脑机"));
                panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { null });
                Assert.That(slot.Find("Symbol").GetComponent<Text>().text, Is.Empty);
                Assert.That(slot.Find("Icon").GetComponent<Image>().enabled, Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(rules); }
        }

        /// <summary>美术背景、回血和退出均引用主分支资源，同时保留四个维修位与六个商品位。</summary>
        [Test]
        public void ArtworkAndTransactionBindingsShareTheExistingPanel()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ShopPanel.prefab");
            var frame = root.transform.Find("Layout_16x9");
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
