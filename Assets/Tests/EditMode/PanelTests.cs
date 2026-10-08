using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证面板显隐契约和弃牌预览的真实资产，反射使缺失实现表现为断言失败。</summary>
    public sealed class PanelTests
    {
        /// <summary>查找 UI 程序集中的面板类型，供测试通过反射访问。</summary>
        private static Type PanelType()
        {
            var type = Type.GetType("SpotlightGameJam.UI.BasePanel, SpotlightGameJam.UI");
            Assert.That(type, Is.Not.Null, "BasePanel 尚未实现。");
            return type;
        }

        /// <summary>验证零秒显隐立即更新面板状态和射线交互。</summary>
        [Test]
        public void ZeroDurationImmediatelyUpdatesVisibilityAndRaycasts()
        {
            var type = PanelType();
            var root = new GameObject("Panel test", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var panel = root.AddComponent(type);
                var group = root.GetComponent<CanvasGroup>();
                type.GetMethod("Hide").Invoke(panel, new object[] { 0f });
                Assert.That(root.activeSelf, Is.False);
                Assert.That(group.alpha, Is.Zero);
                Assert.That(group.blocksRaycasts, Is.False);
                type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                Assert.That(root.activeSelf, Is.True);
                Assert.That(group.alpha, Is.EqualTo(1));
                Assert.That(group.interactable && group.blocksRaycasts, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>验证负数或非有限的淡入淡出时长被拒绝。</summary>
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidFadeDurationIsRejected(float duration)
        {
            var type = PanelType();
            var root = new GameObject("Panel test", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var panel = root.AddComponent(type);
                foreach (var method in new[] { "Show", "Hide" })
                {
                    var exception = Assert.Throws<TargetInvocationException>(() => type.GetMethod(method).Invoke(panel, new object[] { duration }));
                    Assert.That(exception.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>验证弃牌面板仅显示当前普通和义体弃牌堆中的牌。</summary>
        [Test]
        public void PreviewContainsCurrentOrdinaryAndCyberneticDiscardsOnly()
        {
            WithPreview((panel, type, context) =>
            {
                var ordinary = context.Ordinary.Hand.Cards[0];
                context.Ordinary.Discard(ordinary);
                var cyberData = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/BrainCard.asset");
                var cyber = new CardInstance(cyberData);
                context.Cybernetic.DiscardPile.TryAdd(cyber);
                type.GetMethod("Bind").Invoke(panel, new object[] { context });
                type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                var displayed = (IEnumerable<CardInstance>)type.GetProperty("DisplayedCards").GetValue(panel);
                Assert.That(displayed, Is.EqualTo(new[] { ordinary, cyber }));
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(1), "展示不得移动牌。");
                context.Ordinary.DiscardPile.TryRemove(ordinary);
                type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                displayed = (IEnumerable<CardInstance>)type.GetProperty("DisplayedCards").GetValue(panel);
                Assert.That(displayed.Single(), Is.SameAs(cyber), "重新查看不能保留已离开弃牌堆的牌。");
            });
        }

        /// <summary>验证弃牌堆为空时显示空状态提示。</summary>
        [Test]
        public void EmptyDiscardPileShowsAnEmptyState()
        {
            WithPreview((panel, type, context) =>
            {
                type.GetMethod("Bind").Invoke(panel, new object[] { context });
                type.GetMethod("Show").Invoke(panel, new object[] { 0f });
                Assert.That((IEnumerable<CardInstance>)type.GetProperty("DisplayedCards").GetValue(panel), Is.Empty);
                var empty = new SerializedObject(panel).FindProperty("emptyState").objectReferenceValue as GameObject;
                Assert.That(empty, Is.Not.Null);
                Assert.That(empty.activeSelf, Is.True);
            });
        }

        /// <summary>建立弃牌预览测试环境并执行断言，结束后清理临时对象。</summary>
        private static void WithPreview(Action<Component, Type, BattleContext> assertion)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DiscardPanel.prefab");
            Assert.That(prefab, Is.Not.Null, "弃牌面板预制体尚未创建。");
            var root = UnityEngine.Object.Instantiate(prefab);
            using (var assets = new TestAssets())
            {
                try
                {
                    var panel = root.GetComponent("DiscardPanel");
                    Assert.That(panel, Is.Not.Null);
                    var data = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
                    var context = assets.Context(new[] { data, data });
                    TestAssets.Prepare(context);
                    assertion(panel, panel.GetType(), context);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
    }
}
