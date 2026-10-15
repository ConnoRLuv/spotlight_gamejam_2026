using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证商店原型的真实预制体、对话回调以及只读资源契约。</summary>
    public sealed class ShopPrototypeTests
    {
        /// <summary>空、部分和完整带入牌组均按真实部位展示，同种多张只显示一份共享耐久。</summary>
        [TestCase(0)] [TestCase(2)] [TestCase(4)]
        public void RepairSlotsShowActualInitialLoadout(int count)
        {
            using (var assets = new TestAssets())
            {
                var run = new RunState(assets.Create<BattleRules>());
                var ids = new[] { "PrometheusBrainCard", "TorsoCard", "PrometheusHandsCard", "LegsCard" };
                for (int i = 0; i < count; i++)
                    run.Loadout.AddInitial(AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + ids[i] + ".asset"));
                if (count > 0) run.Loadout.Owned[0].TryConsumeDurability();
                WithPanel("ShopPanel", panel =>
                {
                    panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { run });
                    for (int i = 0; i < 4; i++)
                    {
                        var source = run.Loadout.Owned.FirstOrDefault(card => (int)card.Data.slot == i);
                        Assert.That(RepairText(panel, i, "Name"), Is.EqualTo(source == null ? "" : source.Card.cardName));
                        Assert.That(RepairText(panel, i, "Durability"), Is.EqualTo(source == null ? "" : "耐久 " + source.Durability + " / " + source.Data.maxDurability));
                    }
                });
            }
        }

        /// <summary>零耐久仍保留带入义体，重开读取修复后的值，解除绑定后清空内容。</summary>
        [Test]
        public void RepairSlotsKeepBrokenCardsAndClearWhenUnbound()
        {
            using (var assets = new TestAssets())
            {
                var run = new RunState(assets.Create<BattleRules>());
                var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/PrometheusBrainCard.asset");
                run.Loadout.AddInitial(data);
                WithPanel("ShopPanel", panel =>
                {
                    var type = panel.GetType();
                    var copy = run.Loadout.Owned[1];
                    while (copy.TryConsumeDurability()) { }
                    type.GetMethod("Bind").Invoke(panel, new object[] { run });
                    type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                    Assert.That(RepairText(panel, 0, "Name"), Is.EqualTo(copy.Card.cardName));
                    Assert.That(RepairText(panel, 0, "Durability"), Is.EqualTo("耐久 0 / " + data.maxDurability));
                    type.GetMethod("Hide").Invoke(panel, new object[] { 0f });
                    copy.Repair(2);
                    type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                    Assert.That(RepairText(panel, 0, "Durability"), Is.EqualTo("耐久 2 / " + data.maxDurability));
                    type.GetMethod("Bind").Invoke(panel, new object[] { null });
                    copy.Repair(1);
                    Assert.That(RepairText(panel, 0, "Name"), Is.Empty);
                    Assert.That(RepairText(panel, 0, "Durability"), Is.Empty);
                });
            }
        }

        /// <summary>新增型号加入修复库存，默认保留原带入型号，可切换选择且不替换原牌。</summary>
        [Test]
        public void AcquiredModelsCanBeSelectedWithoutReplacingInitialModel()
        {
            using (var assets = new TestAssets())
            {
                var run = new RunState(assets.Create<BattleRules>());
                var initial = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/BrainCard.asset");
                run.Loadout.AddInitial(initial);
                var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/PrometheusBrainCard.asset");
                run.Loadout.AcquireCard(data, data.cards[0]);
                WithPanel("ShopPanel", panel =>
                {
                    panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { run });
                    Assert.That(RepairText(panel, 0, "Name"), Is.EqualTo(initial.cards[0].cardName));
                    panel.GetType().GetMethod("CycleRepairSlot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panel, new object[] { 0 });
                    Assert.That(RepairText(panel, 0, "Name"), Is.EqualTo(data.cards[0].cardName));
                    Assert.That(RepairText(panel, 0, "Durability"), Is.EqualTo("耐久 " + data.maxDurability + " / " + data.maxDurability));
                });
            }
        }

        /// <summary>读取真实预制体维修位置的文字，避免测试依赖额外的展示接口。</summary>
        private static string RepairText(Component panel, int index, string child)
        {
            var slot = new SerializedObject(panel).FindProperty("repairSlots").GetArrayElementAtIndex(index).objectReferenceValue as RectTransform;
            var text = slot.Find(child).GetComponent("Text");
            return (string)text.GetType().GetProperty("text").GetValue(text);
        }

        /// <summary>修改商品配置后卡面必须同步，避免显示旧型号却购买另一型号。</summary>
        [Test]
        public void ProductPreviewMatchesConfiguredTransactionTarget()
        {
            using (var assets = new TestAssets())
            {
                WithPanel("ShopPanel", panel =>
                {
                    var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/BrainCard.asset");
                    var fields = new SerializedObject(panel); fields.FindProperty("productDefinitions").GetArrayElementAtIndex(0).objectReferenceValue = data; fields.ApplyModifiedPropertiesWithoutUndo();
                    panel.GetType().GetMethod("Bind").Invoke(panel, new object[] { new RunState(assets.Create<BattleRules>()) });
                    var slot = (RectTransform)fields.FindProperty("productSlots").GetArrayElementAtIndex(0).objectReferenceValue;
                    var view = slot.Find("CardPreview").GetComponent("CardView");
                    Assert.That(view.GetType().GetProperty("Data").GetValue(view), Is.SameAs(data.cards[0]));
                });
            }
        }

        /// <summary>顺序对话必须显示首句，逐句推进且只在最后确认时完成一次。</summary>
        [Test]
        public void DialogueCompletesOnlyAfterFinalLineAndCanBeReused()
        {
            WithPanel("DialoguePanel", panel =>
            {
                var sequence = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GameData/UI/MerchantDialogue.asset");
                Assert.That(sequence, Is.Not.Null);
                int completed = 0, cancelled = 0;
                var type = panel.GetType();
                Action finish = () => completed++;
                Action cancel = () => cancelled++;
                type.GetMethod("Play").Invoke(panel, new object[] { sequence, finish, cancel });
                Assert.That((int)type.GetProperty("CurrentLineIndex").GetValue(panel), Is.Zero);
                type.GetMethod("Advance").Invoke(panel, null);
                Assert.That(completed, Is.Zero);
                Assert.That((int)type.GetProperty("CurrentLineIndex").GetValue(panel), Is.EqualTo(1));
                type.GetMethod("Advance").Invoke(panel, null);
                type.GetMethod("Advance").Invoke(panel, null);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(cancelled, Is.Zero);
                // 第二轮使用独立事件文本，回调不应残留上一轮的订阅。
                var alternate = UnityEngine.Object.Instantiate(sequence);
                try
                {
                    var serialized = new SerializedObject(alternate);
                    serialized.FindProperty("lines").arraySize = 1;
                    serialized.FindProperty("lines").GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue = "另一段事件对话";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    type.GetMethod("Play").Invoke(panel, new object[] { alternate, finish, cancel });
                    type.GetMethod("Cancel").Invoke(panel, null);
                    type.GetMethod("Cancel").Invoke(panel, null);
                    Assert.That(completed, Is.EqualTo(1));
                    Assert.That(cancelled, Is.EqualTo(1));
                }
                finally { UnityEngine.Object.DestroyImmediate(alternate); }
            });
        }

        /// <summary>预制体初始隐藏，未绑定探索时所有付费服务均禁用。</summary>
        [Test]
        public void ShopHasFourRepairsSixProductsAndDisabledServices()
        {
            WithPanel("ShopPanel", panel =>
            {
                Assert.That(panel.gameObject.activeSelf, Is.False);
                var serialized = new SerializedObject(panel);
                Assert.That(serialized.FindProperty("repairSlots").arraySize, Is.EqualTo(4));
                Assert.That(serialized.FindProperty("productSlots").arraySize, Is.EqualTo(6));
                var buttonType = Type.GetType("UnityEngine.UI.Button, UnityEngine.UI");
                foreach (Component button in panel.GetComponentsInChildren(buttonType, true))
                    if (button.name != "ExitButton" && button.name != "CycleButton")
                        Assert.That((bool)buttonType.GetProperty("interactable").GetValue(button), Is.False, button.name);
            });
        }

        /// <summary>绑定、显示和关闭商店不得消耗任何玩家属性，也不能留下射线遮挡。</summary>
        [Test]
        public void ShopBrowsingPreservesResourcesAndReleasesInput()
        {
            using (var assets = new TestAssets())
            {
                var run = new RunState(assets.Create<BattleRules>());
                run.AddGold(42); run.Player.ReceiveDamage(7); run.Player.TrySpendSanity(3);
                var health = run.Player.Health; var sanity = run.Player.Sanity;
                WithPanel("ShopPanel", panel =>
                {
                    var type = panel.GetType();
                    type.GetMethod("Bind").Invoke(panel, new object[] { run });
                    for (int i = 0; i < 3; i++)
                    {
                        type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                        type.GetMethod("Hide").Invoke(panel, new object[] { 0f });
                    }
                    Assert.That(panel.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                    Assert.That(run.Gold, Is.EqualTo(42));
                    Assert.That(run.Player.Health, Is.EqualTo(health));
                    Assert.That(run.Player.Sanity, Is.EqualTo(sanity));
                });
            }
        }

        /// <summary>从真实资产创建临时面板，断言结束后清理，不改动场景。</summary>
        private static void WithPanel(string name, Action<Component> assertion)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name + " 尚未创建。");
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = root.GetComponent(name);
                Assert.That(panel, Is.Not.Null);
                assertion(panel);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
