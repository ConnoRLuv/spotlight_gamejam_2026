using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    /// <summary>用交付的过载资产验证费用、临时 AP、义体解锁和牌区/UI 接入。</summary>
    public sealed class FunctionCardTests
    {
        /// <summary>加载过载功能牌配置；资产尚未创建时使测试明确失败。</summary>
        private static CardData Overload()
        {
            var data = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/OverloadCard.asset");
            Assert.That(data, Is.Not.Null, "过载配置尚未创建。");
            Assert.That(data.Validate(), Is.Empty);
            Assert.That(data.sanityCost, Is.EqualTo(5), "过载应使用已确认的 5 点理智费用。");
            return data;
        }
        /// <summary>为测试装备义体，满足功能牌的装备前置条件。</summary>
        private static void Equip(BattleContext context)
        {
            var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/HandsCard.asset");
            context.Run.Loadout.Equip(new CyberneticInstance(data));
        }

        /// <summary>验证普通行动点耗尽时，过载仍能提供一点临时行动点。</summary>
        [Test]
        public void OverloadCanProvideOneTemporaryApWhenNoApRemains()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data });
                Equip(context); TestAssets.Prepare(context); context.ActionPoints.TrySpend(2);
                int sanity = context.Player.Sanity;
                var source = context.Run.Loadout.Get(CyberneticSlot.Hands); int durability = source.Durability;
                var card = context.Ordinary.Hand.Cards.Single();
                Assert.That(new CardPlayService().TryPlay(context, card).Success, Is.True);
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity - data.sanityCost));
                Assert.That(context.ActionPoints.Normal, Is.Zero);
                Assert.That(context.ActionPoints.Temporary, Is.EqualTo(1));
                Assert.That(context.Ordinary.DiscardPile.Cards.Single(), Is.SameAs(card));
                Assert.That(source.Durability, Is.EqualTo(durability));
                Assert.That(context.Usage.GetUses(CyberneticSlot.Hands), Is.Zero);
            }
        }

        /// <summary>验证理智为零时过载失败，并保留卡牌和资源。</summary>
        [Test]
        public void OverloadWithZeroSanityPreservesTheCardAndResources()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data });
                Equip(context); TestAssets.Prepare(context);
                var card = context.Ordinary.Hand.Cards.Single();
                context.Player.TrySpendSanity(context.Player.Sanity);
                int sanity = context.Player.Sanity;
                var hand = context.Ordinary.Hand.Cards.ToArray();
                var result = new CardPlayService().TryPlay(context, card);
                Assert.That(result.Failure, Is.EqualTo(CardPlayFailure.InsufficientSanity));
                Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
                Assert.That(context.ActionPoints.Total, Is.EqualTo(2));
                Assert.That(context.Ordinary.Hand.Cards, Is.EqualTo(hand));
                Assert.That(context.HasPhantomPain, Is.True);
                Assert.That(context.Ordinary.DiscardPile.Count, Is.Zero);
            }
        }

        /// <summary>验证过载理智不足时可以透支，不扣生命并正常提供临时行动点。</summary>
        [Test]
        public void OverloadOverdraftPreservesHealthAndProvidesTemporaryAp()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data });
                Equip(context); TestAssets.Prepare(context);
                context.Player.TrySpendSanity(46); context.Player.AddShield(3);
                Assert.That(new CardPlayService().TryPlay(context, context.Ordinary.Hand.Cards.Single()).Success, Is.True);
                Assert.That(context.Player.Health, Is.EqualTo(100));
                Assert.That(context.Player.Sanity, Is.Zero);
                Assert.That(context.Player.Shield, Is.EqualTo(3));
                Assert.That(context.ActionPoints.Temporary, Is.EqualTo(1));
                Assert.That(context.Ordinary.Hand.Cards.Single().Data, Is.SameAs(context.PhantomPain));
            }
        }

        /// <summary>验证过载消耗最后一点理智后生成唯一幻痛，并完成效果。</summary>
        [Test]
        public void PayingLastSanityAddsOnePhantomPainAndStillResolvesOverload()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data });
                Equip(context); TestAssets.Prepare(context);
                context.Player.TrySpendSanity(context.Player.Sanity - data.sanityCost);
                var card = context.Ordinary.Hand.Cards.Single();
                Assert.That(new CardPlayService().TryPlay(context, card).Success, Is.True);
                Assert.That(context.Player.Sanity, Is.Zero);
                Assert.That(context.ActionPoints.Total, Is.EqualTo(3));
                Assert.That(context.Ordinary.Hand.Cards.Single().Data, Is.SameAs(context.PhantomPain));
                Assert.That(context.Ordinary.DiscardPile.Cards.Single(), Is.SameAs(card));
            }
        }

        /// <summary>验证过载产生的未使用临时行动点在玩家回合结束时清除。</summary>
        [Test]
        public void UnusedOverloadApExpiresAtTheEndOfThePlayerTurn()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data }); Equip(context);
                var battle = new BattleController(context, 0);
                Assert.That(battle.StartBattle(), Is.True);
                Assert.That(battle.TryPlay(context.Ordinary.Hand.Cards.Single()).Success, Is.True);
                Assert.That(context.ActionPoints.Total, Is.EqualTo(3));
                Assert.That(battle.EndTurn(), Is.True);
                Assert.That(context.ActionPoints.Normal, Is.EqualTo(3));
                Assert.That(context.ActionPoints.Temporary, Is.Zero);
            }
        }

        /// <summary>验证过载要求已装备义体，但不消耗部位使用次数或耐久。</summary>
        [Test]
        public void OverloadRequiresEquipmentButDoesNotUseAConstrainedBodySlot()
        {
            using (var assets = new TestAssets())
            {
                var data = Overload(); var context = assets.Context(new[] { data }); TestAssets.Prepare(context);
                var card = context.Ordinary.Hand.Cards.Single();
                Assert.That(new CardPlayService().TryPlay(context, card).Failure, Is.EqualTo(CardPlayFailure.UnavailableCybernetic));
                Assert.That(context.Player.Sanity, Is.EqualTo(50));
                Assert.That(context.ActionPoints.Total, Is.EqualTo(2));
                Assert.That(context.Ordinary.Hand.Contains(card), Is.True);
                Equip(context);
                // 过载是功能牌，不受义体部位的回合/战斗使用次数限制。
                for (int i = 0; i < 3; i++) { context.Usage.BeginTurn(); context.Usage.RecordUse(CyberneticSlot.Hands); }
                Assert.That(new CardPlayService().TryPlay(context, card).Success, Is.True);
            }
        }

        /// <summary>验证启动入口仅在装备义体后将过载加入普通牌组。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void BootstrapOnlyAddsOverloadToOrdinaryDeckAfterEquippingCybernetics(bool equipped)
        {
            var owner = new GameObject("Function card bootstrap test");
            try
            {
                var overload = Overload(); var bootstrap = owner.AddComponent<GameBootstrap>();
                var serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("rules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BattleRules>("Assets/GameData/BattleRules.asset");
                serialized.FindProperty("phantomPain").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/PhantomPain.asset");
                var deck = serialized.FindProperty("ordinaryDeck"); deck.arraySize = 3;
                deck.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
                deck.GetArrayElementAtIndex(1).objectReferenceValue = overload;
                deck.GetArrayElementAtIndex(2).objectReferenceValue = overload;
                var cybernetics = serialized.FindProperty("initialCybernetics"); cybernetics.arraySize = equipped ? 1 : 0;
                if (equipped) cybernetics.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/HandsCard.asset");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bootstrap.StartNewRun(), Is.True);
                var context = bootstrap.Battle.Context;
                Assert.That(context.Ordinary.AllCards.Count(card => card.Data == overload), Is.EqualTo(equipped ? 2 : 0));
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(equipped ? 3 : 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        /// <summary>验证过载预制体显示功能牌类别，并可用于手牌及弃牌预览。</summary>
        [Test]
        public void OverloadPrefabShowsFunctionCategoryAndIsRegisteredForHandAndDiscardViews()
        {
            var data = Overload();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/OverloadCard.prefab");
            Assert.That(prefab, Is.Not.Null);
            var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GameData/CardPrefabCatalog.asset");
            var view = catalog.GetType().GetMethod("Find").Invoke(catalog, new object[] { data }) as Component;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.gameObject, Is.SameAs(prefab));
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.Zero);
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var component = root.GetComponent("CardView");
                component.GetType().GetMethod("Bind").Invoke(component, new object[] { new CardInstance(data) });
                Assert.That(root.transform.Find("Source").GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("功能牌"));
                Assert.That(root.transform.Find("Cost").GetComponent<UnityEngine.UI.Text>().text, Does.Contain("理智"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
