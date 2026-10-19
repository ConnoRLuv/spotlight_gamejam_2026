using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpotlightGameJam.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>通过真实场景和按钮验证临时探索入口，把 StartScene 的选择原样带入 BattleScene。</summary>
    public sealed class ExplorationSceneFlowTests
    {
        private bool originalBackground;
        private float originalTimeScale;
        /// <summary>加载实际开始场景，清理旧会话并让后台运行的异步切场景也持续推进。</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalBackground = Application.runInBackground; originalTimeScale = Time.timeScale;
            Application.runInBackground = true; Time.timeScale = 1;
            SceneManager.sceneLoaded += DisableShopTestFunding;
            if (AdventureSession.Instance) UnityEngine.Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                "Assets/Scenes/StartScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("该临时场景流程测试在 Unity Editor 中运行。");
#endif
            yield return null;
        }
        /// <summary>释放测试会话并恢复运行设置，不保存测试期间的玩家属性或场景对象。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableShopTestFunding;
            if (AdventureSession.Instance) UnityEngine.Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
            Time.timeScale = originalTimeScale; Application.runInBackground = originalBackground;
        }

        /// <summary>资源持久化测试隔离旧商店入口的 9999 测试补助，仅停用运行时组件，不修改场景。</summary>
        private static void DisableShopTestFunding(Scene scene, LoadSceneMode mode)
        {
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPrototypeTestEntry>(true)))
                entry.enabled = false;
        }
        /// <summary>原制式四部位应带入十二张义体牌。</summary>
        [UnityTest] public IEnumerator OriginalDeckReachesBattle() => VerifySelection(new[]{0,1,2,3},12);
        /// <summary>普罗米修斯四部位按三、一、三、一带入八张牌。</summary>
        [UnityTest] public IEnumerator PrometheusDeckReachesBattle() => VerifySelection(new[]{4,5,6,7},8);
        /// <summary>混搭组合不能被战斗场景预设的整套普罗米修斯覆盖。</summary>
        [UnityTest] public IEnumerator MixedDeckReachesBattle() => VerifySelection(new[]{4,1,6,3},12);
        /// <summary>只选择一个部位也允许出发，其他部位不会被场景默认值补齐。</summary>
        [UnityTest] public IEnumerator PartialDeckReachesBattle() => VerifySelection(new[]{4},3);
        /// <summary>空义体牌组可以进入战斗，不会回退到场景预设牌组。</summary>
        [UnityTest] public IEnumerator EmptyDeckReachesBattle() => VerifySelection(Array.Empty<int>(),0);
        /// <summary>新世界四部位按三张一组带入十二张牌，战斗不能被场景预设覆盖。</summary>
        [UnityTest] public IEnumerator NewWorldDeckReachesBattle() => VerifySelection(new[]{8,9,10,11},12);
        /// <summary>复仇女神按三、三、三、二带入十一张牌，不被场景中的普罗米修斯默认覆盖。</summary>
        [UnityTest] public IEnumerator NemesisDeckReachesBattle() => VerifySelection(new[]{12,13,14,15},11);

        /// <summary>真实弃牌面板可浏览两类牌，只让攻击牌响应回收，取消不扣费，确认后一次结算。</summary>
        [UnityTest]
        public IEnumerator NemesisHandsCanBrowseCancelAndRecoverThroughHud()
        {
            yield return VerifySelection(new[] { 12, 13, 14, 15 }, 11);
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>(); var context = hud.Battle.Context;
            var hands = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards)
                .First(card => card.Source.Data.slot == CyberneticSlot.Hands);
            if (!context.Cybernetic.Hand.Contains(hands))
            { context.Cybernetic.DrawPile.TryRemove(hands); context.Cybernetic.Hand.TryAdd(hands); }
            var attack = context.Ordinary.Hand.Cards.Concat(context.Ordinary.DrawPile.Cards)
                .First(card => card.Data.category == CardCategory.Basic && card.Data.cardType == CardType.Attack);
            if (context.Ordinary.Hand.Contains(attack)) context.Ordinary.Discard(attack);
            else { context.Ordinary.DrawPile.TryRemove(attack); context.Ordinary.DiscardPile.TryAdd(attack); }
            var spare = context.Cybernetic.DrawPile.Cards.First(); context.Cybernetic.DrawPile.TryRemove(spare); context.Cybernetic.DiscardPile.TryAdd(spare);
            var panel = Field<DiscardPanel>(hud, "discardPanel"); int sanity = context.Player.Sanity, durability = hands.Source.Durability;
            hud.SelectCard(hands); yield return null;
            Assert.That(panel.IsVisible, Is.True); Assert.That(panel.IsSelecting, Is.True); Assert.That(hud.IsChoosing, Is.True);
            panel.ShowCybernetic(); Assert.That(panel.DisplayedCards, Does.Contain(spare));
            Assert.That(panel.GetComponentsInChildren<CardView>().All(view => !view.GetComponent<Button>().interactable), Is.True);
            panel.Close(); yield return null;
            Assert.That(hud.IsChoosing, Is.False); Assert.That(context.Player.Sanity, Is.EqualTo(sanity)); Assert.That(hands.Source.Durability, Is.EqualTo(durability));
            hud.SelectCard(hands); yield return null; Assert.That(panel.IsSelecting, Is.True);
            var chosen = panel.GetComponentsInChildren<CardView>().Single(view => view.Instance == attack);
            Click(chosen.GetComponent<Button>()); yield return null;
            Assert.That(panel.IsVisible, Is.False); Assert.That(hud.IsChoosing, Is.False);
            Assert.That(context.Ordinary.Hand.Contains(attack), Is.True); Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 6));
            Assert.That(hands.Source.Durability, Is.EqualTo(durability - 1));
            hud.ShowDiscardPile(0); Assert.That(panel.IsSelecting, Is.False);
            Assert.That(panel.GetComponentsInChildren<CardView>().All(view => !view.GetComponent<Button>().interactable), Is.True);
        }

        /// <summary>真实 HUD 使用脑机时立即结算一次，打开抽牌面板，关闭后完成并恢复操作。</summary>
        [UnityTest]
        public IEnumerator NewWorldBrainOpensDrawPilePaysOnceAndFinishesOnCloseThroughHud()
        {
            yield return VerifySelection(new[] { 8, 9, 10, 11 }, 12);
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>(); var context = hud.Battle.Context;
            var brain = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards).Concat(context.Cybernetic.DiscardPile.Cards)
                .First(card => card.Source.Data.slot == CyberneticSlot.Brain);
            if (!context.Cybernetic.Hand.Contains(brain))
            { context.Cybernetic.DrawPile.TryRemove(brain); context.Cybernetic.DiscardPile.TryRemove(brain); Assert.That(context.Cybernetic.Hand.TryAdd(brain), Is.True); }
            int sanity = context.Player.Sanity, durability = brain.Source.Durability;
            var expectedOrdinary = context.Ordinary.GetDrawPileOrder().ToArray();
            var expectedCybernetic = context.Cybernetic.GetDrawPileOrder().ToArray();
            hud.SelectCard(brain); var panel = hud.GetComponentInChildren<DrawPilePanel>(true);
            Assert.That(panel.IsVisible, Is.True, "脑机应直接打开现有抽牌堆面板。");
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 4)); Assert.That(brain.Source.Durability, Is.EqualTo(durability - 1));
            Assert.That(context.Usage.CanUse(CyberneticSlot.Brain), Is.False);
            Click(Field<Button>(panel, "closeButton")); yield return null;
            Assert.That(context.Cybernetic.Hand.Contains(brain), Is.False); Assert.That(hud.IsChoosing, Is.False);
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 4)); Assert.That(brain.Source.Durability, Is.EqualTo(durability - 1));
            Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(expectedOrdinary));
            Assert.That(context.Cybernetic.GetDrawPileOrder(), Is.EqualTo(expectedCybernetic));
        }
        /// <summary>选择候选并点击更换、出发，验证会话身份、牌数和玩家属性经过真实场景切换保持一致。</summary>
        private static IEnumerator VerifySelection(int[] indices,int count)
        {
            var controller = UnityEngine.Object.FindObjectOfType<StartSceneController>(); Assert.That(controller,Is.Not.Null);
            var setup = Field<AdventureSetupData>(controller,"setup");
#if UNITY_EDITOR
            // 完整牌组规则回归使用完整配置，不受开始界面临时隐藏某系列的测试配置影响。
            setup = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            typeof(StartSceneController).GetField("setup", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, setup);
#endif
            Assert.That(setup.availableCybernetics.Length,Is.EqualTo(16));
            controller.ClearSelection();
            var selected = indices.Select(i=>setup.availableCybernetics[i]).ToArray();
            foreach (var data in selected)
            {
                controller.SelectPart(data.slot); controller.PreviewCybernetic(data);
                Click(Field<Button>(controller,"replaceButton"));
            }
            Assert.That(controller.Selection,Is.EquivalentTo(selected));
            RunState prepared = null;
            controller.ExplorationRequested += run =>
            {
                prepared = run;
                // 在切场景前改变三种属性，确认战斗复用会话而非重新初始化玩家。
                run.AddGold(17); run.Player.ReceiveDamage(7); run.Player.TrySpendSanity(9);
            };
            var explore = Field<Button>(controller,"exploreButton"); Click(explore); Click(explore);
            Assert.That(prepared,Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 8;
            while (SceneManager.GetActiveScene().name != "BattleScene" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("BattleScene"),"出发后未进入战斗场景。");
            yield return null;
            var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>(); Assert.That(bootstrap,Is.Not.Null);
            Assert.That(bootstrap.Battle,Is.Not.Null,bootstrap.LastError);
            Assert.That(bootstrap.Run,Is.SameAs(prepared)); Assert.That(AdventureSession.Instance.Run,Is.SameAs(prepared));
            var deck = bootstrap.Battle.Context.Cybernetic;
            var cards = deck.Hand.Cards.Concat(deck.DrawPile.Cards).Concat(deck.DiscardPile.Cards).ToArray();
            Assert.That(cards.Length,Is.EqualTo(count));
            foreach (var data in selected)
                Assert.That(cards.Count(c=>c.Source.Data==data),Is.EqualTo(data.cards.Length),data.cyberneticId);
            Assert.That(cards.All(c=>selected.Contains(c.Source.Data)),Is.True);
            Assert.That(bootstrap.Run.Player.Health,Is.EqualTo(setup.rules.MaxHealth-7));
            Assert.That(bootstrap.Run.Player.Sanity,Is.EqualTo(setup.rules.MaxSanity-9));
            Assert.That(bootstrap.Run.Gold,Is.EqualTo(17)); Assert.That(bootstrap.Run.Buffs.Active,Is.Empty);
            Assert.That(AdventureSession.Instance.EncounterIndex,Is.EqualTo(1),"重复点击不能创建两个遭遇。");
        }
        /// <summary>读取场景已配置的控件引用，不为测试改变业务类的可见性。</summary>
        private static T Field<T>(object owner,string name) where T : class
        {
            for (var type = owner.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(owner) as T;
            }
            Assert.Fail("缺少字段：" + name); return null;
        }
        /// <summary>发送真实按钮左键点击，保留 Button 对禁用和重复点击的校验。</summary>
        private static void Click(Button button)
        {
            Assert.That(button,Is.Not.Null);
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
    }
}
