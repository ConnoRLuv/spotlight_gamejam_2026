using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证只读牌堆的实际预制体、独立页签、展示顺序及绑定生命周期。</summary>
    public sealed class PilePanelTests
    {
        /// <summary>同一张预览牌耐久变化时刷新文字，保留卡面对象与滚动位置。</summary>
        [TestCase("DrawPilePanel")] [TestCase("DiscardPanel")]
        public void DurabilityChangesRefreshExistingCardView(string name)
        {
            WithPanel(name,(panel,type,context)=>
            {
                var data=AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/BrainCard.asset");
                var source=new CyberneticInstance(data);var card=new CardInstance(data.cards[0],source);
                (name=="DrawPilePanel"?context.Cybernetic.DrawPile:context.Cybernetic.DiscardPile).TryAdd(card);
                type.GetMethod("Bind").Invoke(panel,new object[]{context});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                type.GetMethod("ShowCybernetic").Invoke(panel,null);
                var viewType=Type.GetType("SpotlightGameJam.UI.CardView, SpotlightGameJam.UI");
                var view=panel.GetComponentsInChildren(viewType).Single();
                var label=new SerializedObject(view).FindProperty("sourceText").objectReferenceValue;
                source.TryConsumeDurability();context.NotifyChanged();
                Assert.That((string)label.GetType().GetProperty("text").GetValue(label),
                    Does.Contain("耐久 "+source.Durability+"/"+data.maxDurability));
                Assert.That(panel.GetComponentsInChildren(viewType).Single(),Is.SameAs(view));
            });
        }
        /// <summary>胜负阶段通知应直接关闭预览，不依赖下一次资源刷新。</summary>
        [TestCase("DrawPilePanel")] [TestCase("DiscardPanel")]
        public void BattleEndingImmediatelyClosesPreview(string name)
        {
            WithPanel(name,(panel,type,context)=>
            {
                type.GetMethod("Bind").Invoke(panel,new object[]{context});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                context.SetState(BattleState.Victory);
                Assert.That((bool)type.GetProperty("IsVisible").GetValue(panel),Is.False);
            });
        }
        /// <summary>按真实资产建立测试界面，不对生产类加入测试专用分支。</summary>
        private static void WithPanel(string name, Action<Component,Type,BattleContext> assertion)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/"+name+".prefab");
            Assert.That(prefab,Is.Not.Null,name);
            var root=UnityEngine.Object.Instantiate(prefab);
            using(var assets=new TestAssets())
            {
                var data=assets.Card(assets.Create<HealEffectData>());
                var context=assets.Context(Enumerable.Repeat(data,20).ToArray());TestAssets.Prepare(context);
                try{var panel=root.GetComponent(name);Assert.That(panel,Is.Not.Null);assertion(panel,panel.GetType(),context);}
                finally{context.Dispose();UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        /// <summary>读取当前显示实例快照，不按配置 ID 混淆同名牌。</summary>
        private static CardInstance[] Displayed(Component panel,Type type) =>
            ((IEnumerable<CardInstance>)type.GetProperty("DisplayedCards").GetValue(panel)).ToArray();
        /// <summary>抽牌预览第一张为实际堆顶；切页和关闭不改变任何牌区或资源。</summary>
        [Test] public void DrawPreviewMatchesNextDrawAndDoesNotMutateState()
        {
            WithPanel("DrawPilePanel",(panel,type,context)=>
            {
                var ordinary=context.Ordinary.DrawPile.Cards.ToArray();var hand=context.Ordinary.Hand.Cards.ToArray();
                var cyber=new CardInstance(AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/BrainCard.asset"));
                context.Cybernetic.DrawPile.TryAdd(cyber);
                int sanity=context.Player.Sanity,ap=context.ActionPoints.Total;
                type.GetMethod("Bind").Invoke(panel,new object[]{context});
                Assert.That((bool)type.GetProperty("IsVisible").GetValue(panel),Is.False);
                type.GetMethod("Show").Invoke(panel,new object[]{0f});
                Assert.That(Displayed(panel,type),Is.EqualTo(context.Ordinary.Peek(int.MaxValue)));
                type.GetMethod("ShowCybernetic").Invoke(panel,null);Assert.That(Displayed(panel,type),Is.EqualTo(new[]{cyber}));
                type.GetMethod("ShowOrdinary").Invoke(panel,null);type.GetMethod("Hide").Invoke(panel,new object[]{0f});
                Assert.That(context.Ordinary.DrawPile.Cards,Is.EqualTo(ordinary));Assert.That(context.Ordinary.Hand.Cards,Is.EqualTo(hand));
                Assert.That(context.Cybernetic.DrawPile.Cards,Is.EqualTo(new[]{cyber}));
                Assert.That(context.Player.Sanity,Is.EqualTo(sanity));Assert.That(context.ActionPoints.Total,Is.EqualTo(ap));
            });
        }
        /// <summary>最近弃置优先展示，普通与义体页签不混合。</summary>
        [Test] public void DiscardsDisplayNewestFirstInSeparatePages()
        {
            WithPanel("DiscardPanel",(panel,type,context)=>
            {
                var cards=context.Ordinary.Hand.Cards.Take(2).ToArray();foreach(var card in cards)context.Ordinary.Discard(card);
                type.GetMethod("Bind").Invoke(panel,new object[]{context});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                Assert.That(Displayed(panel,type),Is.EqualTo(new[]{cards[1],cards[0]}));
                type.GetMethod("ShowCybernetic").Invoke(panel,null);Assert.That(Displayed(panel,type),Is.Empty);
                var empty=new SerializedObject(panel).FindProperty("emptyState").objectReferenceValue as GameObject;
                Assert.That(empty.activeSelf,Is.True);
            });
        }
        /// <summary>两个面板的卡面均禁止交互，关闭开始便停止射线拦截。</summary>
        [TestCase("DrawPilePanel")] [TestCase("DiscardPanel")]
        public void PreviewCardsAreReadOnlyAndClosingStopsRaycasts(string name)
        {
            WithPanel(name,(panel,type,context)=>
            {
                context.Ordinary.Discard(context.Ordinary.Hand.Cards[0]);
                type.GetMethod("Bind").Invoke(panel,new object[]{context});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                var viewType=Type.GetType("SpotlightGameJam.UI.CardView, SpotlightGameJam.UI");
                foreach(var view in panel.GetComponentsInChildren(viewType))
                {
                    var button=view.GetComponent("Button");
                    Assert.That((bool)button.GetType().GetProperty("interactable").GetValue(button),Is.False);
                }
                type.GetMethod("Close").Invoke(panel,null);
                Assert.That((bool)type.GetProperty("IsVisible").GetValue(panel),Is.False);
                Assert.That(panel.GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
            });
        }
        /// <summary>更换战斗清空旧内容；旧战斗的变化不能重新显示或污染当前面板。</summary>
        [TestCase("DrawPilePanel")] [TestCase("DiscardPanel")]
        public void BindingNewBattleClosesAndClearsOldCards(string name)
        {
            WithPanel(name,(panel,type,old)=>
            {
                type.GetMethod("Bind").Invoke(panel,new object[]{old});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                using(var assets=new TestAssets())
                using(var next=assets.Context(Array.Empty<CardData>()))
                {
                    type.GetMethod("Bind").Invoke(panel,new object[]{next});
                    Assert.That((bool)type.GetProperty("IsVisible").GetValue(panel),Is.False);Assert.That(Displayed(panel,type),Is.Empty);
                    type.GetMethod("Show").Invoke(panel,new object[]{0f});old.Ordinary.Discard(old.Ordinary.Hand.Cards[0]);old.NotifyChanged();
                    Assert.That(Displayed(panel,type),Is.Empty);
                    type.GetMethod("Bind").Invoke(panel,new object[]{null});type.GetMethod("Show").Invoke(panel,new object[]{0f});
                    Assert.That((bool)type.GetProperty("IsVisible").GetValue(panel),Is.False);
                }
            });
        }
    }
}
