using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证普罗米修斯的费用展示及改造选牌界面，不让 UI 预先修改战斗状态。</summary>
    public sealed class PrometheusUITests
    {
        /// <summary>共用选牌面板必须按效果显示基础牌或义体牌标题。</summary>
        [TestCase("TransformBasicToCyberneticEffectData", "选择一张基础牌")]
        [TestCase("CyberneticPeekEffectData", "选择一张义体牌")]
        public void ChoiceTitleMatchesEffectPurpose(string name,string expected)
        {
            var effect=CreateEffect(name);
            try
            {
                var property=effect.GetType().GetProperty("ChoiceTitle");
                Assert.That(property,Is.Not.Null,"选牌效果需要独立的弹窗标题。");
                Assert.That(property.GetValue(effect),Is.EqualTo(expected));
            }
            finally {UnityEngine.Object.DestroyImmediate(effect);}
        }
        /// <summary>真实场景候选列表需拥有裁剪视口和纵向滚动，不能只使用溢出的水平布局。</summary>
        [Test] public void SceneChoiceListUsesClippedScrollingGrid()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity",UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var hud=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Single(c=>c&&c.GetType().Name=="BattleHUD");
                var fields=new UnityEditor.SerializedObject(hud);var property=fields.FindProperty("choiceScroll");
                Assert.That(property,Is.Not.Null,"选牌面板缺少滚动引用。");
                var scroll=property.objectReferenceValue as ScrollRect;Assert.That(scroll,Is.Not.Null);
                Assert.That(scroll.vertical,Is.True);Assert.That(scroll.horizontal,Is.False);
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(),Is.Not.Null);
                Assert.That(scroll.content.parent,Is.SameAs(scroll.viewport));
                Assert.That(scroll.content.GetComponent<GridLayoutGroup>(),Is.Not.Null);
                Assert.That(scroll.content.GetComponent<ContentSizeFitter>().verticalFit,Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
                Assert.That(fields.FindProperty("choiceTitle").objectReferenceValue,Is.Not.Null);
                Assert.That(fields.FindProperty("choiceHint").objectReferenceValue,Is.Not.Null);
            }
            finally {if(opened)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);}
        }
        private TestAssets assets;
        private GameObject owner;
        private BattleContext context;

        /// <summary>为每个界面用例创建独立配置和临时控件。</summary>
        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            owner = new GameObject("Prometheus UI test", typeof(RectTransform));
            owner.SetActive(false);
        }

        /// <summary>解除战斗资源订阅并销毁测试控件，避免保留场景对象。</summary>
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(owner);
            context?.Dispose();
            assets.Dispose();
        }

        /// <summary>验证本回合基本牌免 AP 时卡面展示零费用，回合结束后恢复配置费用。</summary>
        [Test]
        public void BasicCardCostReflectsTheCurrentTurnWaiver()
        {
            var data = assets.Card(assets.Create<HealEffectData>());
            context = assets.Context(new[] { data });
            TestAssets.Prepare(context);
            var card = context.Ordinary.Hand.Cards.Single();
            var view = CreateView(data, out var cost);
            BindForBattle(view, card);
            Assert.That(cost.text, Is.EqualTo("AP 1"));
            var effect = CreateEffect("FreeBasicCardsEffectData");
            try
            {
                effect.Execute(context, card, context.Player);
                view.GetType().GetMethod("Refresh").Invoke(view, null);
                Assert.That(cost.text, Is.EqualTo("AP 0"));
                var battle = new BattleController(context, 0);
                Assert.That(battle.EndTurn(), Is.True);
                view.GetType().GetMethod("Refresh").Invoke(view, null);
                Assert.That(cost.text, Is.EqualTo("AP 1"));
                Assert.That(data.cost, Is.EqualTo(1), "展示不能改写共享卡牌配置。");
            }
            finally { UnityEngine.Object.DestroyImmediate(effect); }
        }

        /// <summary>验证腿部效果仅改变义体费用展示，过载仍展示理智费用。</summary>
        [Test]
        public void HealthPaymentChangesCyberneticCostButKeepsOverloadSanityCost()
        {
            var cyber = assets.Card(assets.Create<HealEffectData>(), CardTargetType.Self, CardCategory.Cybernetic, 0, 8);
            var overload = assets.Card(assets.Create<ActionPointEffectData>(), CardTargetType.Self, CardCategory.Function, 0, 5);
            context = assets.Context(Array.Empty<CardData>());
            TestAssets.Prepare(context);
            var cyberView = CreateView(cyber, out var cyberCost);
            var overloadView = CreateView(overload, out var overloadCost);
            BindForBattle(cyberView, new CardInstance(cyber));
            BindForBattle(overloadView, new CardInstance(overload));
            Assert.That(cyberCost.text, Is.EqualTo("理智 8"));
            var effect = CreateEffect("HealthPaymentEffectData");
            try
            {
                effect.Execute(context, new CardInstance(cyber), context.Player);
                cyberView.GetType().GetMethod("Refresh").Invoke(cyberView, null);
                overloadView.GetType().GetMethod("Refresh").Invoke(overloadView, null);
                Assert.That(cyberCost.text, Is.EqualTo("生命 8"));
                Assert.That(overloadCost.text, Is.EqualTo("理智 5"));
                Assert.That(cyber.sanityCost, Is.EqualTo(8));
            }
            finally { UnityEngine.Object.DestroyImmediate(effect); }
        }

        /// <summary>验证只读绑定清除先前战斗引用，避免预览继承其他战斗的费用状态。</summary>
        [Test]
        public void ReadOnlyBindingClearsThePreviousBattleCost()
        {
            var data = assets.Card(assets.Create<HealEffectData>(), CardTargetType.Self, CardCategory.Cybernetic, 0, 8);
            context = assets.Context(Array.Empty<CardData>());
            var effect = CreateEffect("HealthPaymentEffectData");
            try
            {
                effect.Execute(context, new CardInstance(data), context.Player);
                var view = CreateView(data, out var cost);
                BindForBattle(view, new CardInstance(data));
                Assert.That(cost.text, Is.EqualTo("生命 8"));
                view.GetType().GetMethod("Bind").Invoke(view, new object[] { new CardInstance(data) });
                Assert.That(cost.text, Is.EqualTo("理智 8"));
            }
            finally { UnityEngine.Object.DestroyImmediate(effect); }
        }

        /// <summary>创建轻量卡面并接入真实文本控件，通过公开绑定观察费用。</summary>
        private Component CreateView(CardData data, out Text cost)
        {
            var type = Type.GetType("SpotlightGameJam.UI.CardView, SpotlightGameJam.UI");
            Assert.That(type, Is.Not.Null);
            var root = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(owner.transform, false);
            var view = root.AddComponent(type);
            cost = TextChild(root.transform, "Cost");
            type.GetMethod("Configure").Invoke(view, new object[]
            {
                data, null, Color.gray, root.GetComponent<Image>(), root.GetComponent<Button>(),
                TextChild(root.transform, "Title"), cost, TextChild(root.transform, "Description"), TextChild(root.transform, "Source")
            });
            return view;
        }

        /// <summary>从运行时程序集创建效果，让未实现能力产生清楚的失败信息。</summary>
        private static CardEffectData CreateEffect(string name)
        {
            var type = Type.GetType("SpotlightGameJam." + name + ", SpotlightGameJam.Runtime");
            Assert.That(type, Is.Not.Null, "缺少普罗米修斯效果 " + name);
            return (CardEffectData)ScriptableObject.CreateInstance(type);
        }

        /// <summary>通过新增的战斗绑定入口提供权威状态，保留旧单参数绑定兼容。</summary>
        private void BindForBattle(Component view, CardInstance card)
        {
            var method = view.GetType().GetMethod("BindForBattle");
            Assert.That(method, Is.Not.Null, "卡面尚未接入动态战斗费用。");
            method.Invoke(view, new object[] { card, context });
        }

        /// <summary>创建临时文本控件，测试只读取最终可见字符串。</summary>
        private static Text TextChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Text));
            child.transform.SetParent(parent, false);
            return child.GetComponent<Text>();
        }
    }
}
