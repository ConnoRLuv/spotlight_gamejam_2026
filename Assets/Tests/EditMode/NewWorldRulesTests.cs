using System;
using System.Linq;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证新世界四部件效果、牌堆重排与同回合成功四部位的套装回收规则。</summary>
    public sealed class NewWorldRulesTests
    {
        private TestAssets assets;

        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();

        /// <summary>创建一个可选部位的义体配置，用于组合完整套装。</summary>
        private CyberneticData MakePart(CyberneticSlot slot)
        {
            var card = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            var data = assets.Create<CyberneticData>();
            data.cyberneticId = "new-world-part-" + (int)slot;
            data.slot = slot;
            data.maxDurability = 3;
            data.cards = new[] { card };
            return data;
        }

        /// <summary>创建完整的新世界被动配置，触发记录由 RunBuffState 统一管理。</summary>
        private NewWorldSetBonusData MakeBonus(CyberneticData[] parts)
        {
            var bonus = assets.Create<NewWorldSetBonusData>();
            bonus.setId = "new-world";
            bonus.setName = "新世界测试套装";
            bonus.requiredParts = parts;
            bonus.trigger = NewWorldSetTrigger.OnAnyCardPlayed;
            bonus.effect = NewWorldSetEffect.ReturnBasicFromDiscard;
            bonus.returnPolicy = NewWorldSetReturnPolicy.MostRecent;
            return bonus;
        }

        /// <summary>真实卡组依次完成四部位效果，同回合激活套装；额外回合恢复上限，满手不再回收。</summary>
        [Test]
        public void RealDeckEffectsAndSetWorkTogetherAcrossExtraTurn()
        {
            var deck = UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/NewWorld.asset");
            var bonus = UnityEditor.AssetDatabase.LoadAssetAtPath<NewWorldSetBonusData>("Assets/GameData/SetBonuses/NewWorld.asset");
            var overload = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/OverloadCard.asset");
            var basic = assets.Card(assets.Create<HealEffectData>());
            using (var context = assets.Context(Enumerable.Repeat(basic, 24).Concat(new[] { overload }).ToArray()))
            {
                foreach (var part in deck.cybernetics) context.Run.Loadout.AddInitial(part);
                context.Run.SetBonuses.Configure(new[] { bonus });
                var battle = new BattleController(context, 8); Assert.That(battle.StartBattle(), Is.True, battle.LastError);
                var parts = context.Run.Loadout.GetUsableCards().GroupBy(card => card.Source.Data.slot).Select(group => group.First()).OrderBy(card => card.Source.Data.slot).ToArray();
                foreach (var card in parts) Assert.That(context.Cybernetic.Hand.TryAdd(card), Is.True);
                var expected = context.Ordinary.GetDrawPileOrder().Reverse().ToArray();
                Assert.That(battle.TryPlayRequest(new CardPlayRequest(parts[0], reorder: expected)).Success, Is.True);
                Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(expected));
                Assert.That(context.Player.Sanity, Is.EqualTo(context.Rules.MaxSanity - 4));
                Assert.That(battle.TryPlay(parts[1]).Success, Is.True);
                context.Ordinary.Draw(14); Assert.That(context.Ordinary.Hand.Count, Is.GreaterThan(13));
                Assert.That(battle.TryPlay(parts[2]).Success, Is.True);
                var overloadInstance = context.Ordinary.Hand.Cards.Concat(context.Ordinary.DrawPile.Cards).First(card => card.Data == overload);
                if (!context.Ordinary.Hand.Contains(overloadInstance))
                { context.Ordinary.DrawPile.TryRemove(overloadInstance); context.Ordinary.Hand.TryAdd(overloadInstance); }
                Assert.That(battle.TryPlay(overloadInstance).Success, Is.True);
                Assert.That(context.ActionPoints.Permanent, Is.EqualTo(1)); Assert.That(context.ActionPoints.Temporary, Is.Zero);
                var discarded = context.Ordinary.Hand.Cards.First(card => card.Data.category == CardCategory.Basic);
                context.Ordinary.Discard(discarded);
                Assert.That(battle.TryPlay(parts[3]).Success, Is.True);
                Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(1));
                Assert.That(context.Ordinary.Hand.Contains(discarded), Is.True);
                Assert.That(context.Run.Loadout.Owned.Select(source => source.Durability), Is.All.EqualTo(2));
                int health = context.Player.Health;
                Assert.That(battle.EndTurn(), Is.True); Assert.That(battle.TurnNumber, Is.EqualTo(2));
                Assert.That(context.Player.Health, Is.EqualTo(health), "额外回合跳过敌方行动。");
                Assert.That(context.ActionPoints.Permanent, Is.EqualTo(1)); Assert.That(context.Ordinary.CanAddToHand(), Is.False);
                Assert.That(context.Run.SetBonuses.Active.Count, Is.EqualTo(1));
                int before = context.Ordinary.Hand.Count;
                var played = context.Ordinary.Hand.Cards.First(card => card.Data.category == CardCategory.Basic);
                Assert.That(battle.TryPlay(played).Success, Is.True);
                Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(before - 1));
                Assert.That(context.Ordinary.DiscardPile.Contains(played), Is.True);
                context.Enemies[0].ReceiveDamage(9999); context.CheckOutcome();
                Assert.That(context.Run.SetBonuses.Active, Is.Empty);
            }
        }

        /// <summary>躯干：解除本回合手牌上限后可以超容量抽牌，恢复上限后立即停止。</summary>
        [Test]
        public void TorsoLiftsHandLimitAndRestoresAtTurnBoundary()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(Enumerable.Repeat(basic, 20).ToArray(), 1, 13);
            Assert.That(context.Ordinary.Draw(13), Is.EqualTo(13));
            Assert.That(context.Ordinary.CanAddToHand(), Is.False);
            context.SetNoHandLimit(true);
            Assert.That(context.Ordinary.CanAddToHand(), Is.True);
            Assert.That(context.Ordinary.Draw(3), Is.EqualTo(3));
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(16));
            context.SetNoHandLimit(false);
            Assert.That(context.Ordinary.Draw(1), Is.Zero);
        }

        /// <summary>手部：开启后过载获得的行动点写入永久池并封顶 12，跨回合保留。</summary>
        [Test]
        public void HandsMakesOverloadApPermanentAndCapsAtTwelve()
        {
            var effect = assets.Create<ActionPointEffectData>(); effect.amount = 1;
            var overload = assets.Card(effect, CardTargetType.None, CardCategory.Function, 0, 5);
            overload.cardId = "OverloadCard";
            var context = assets.Context(new[] { overload });
            TestAssets.Prepare(context);

            // 默认路径仍是临时行动点。
            context.GainActionPoints(new CardInstance(overload), 1);
            Assert.That(context.ActionPoints.Temporary, Is.EqualTo(1));
            Assert.That(context.ActionPoints.Permanent, Is.Zero);

            // 开启手部效果后改为永久行动点，并封顶于战斗规则 MaxAp（12）。
            context.TurnEffects.OverloadApPermanent = true;
            context.ActionPoints.BeginTurn(0);
            for (int i = 0; i < 20; i++) context.GainActionPoints(new CardInstance(overload), 1);
            Assert.That(context.ActionPoints.Permanent, Is.EqualTo(12));
            Assert.That(context.ActionPoints.Temporary, Is.Zero);

            // 永久行动点跨回合保留，普通与临时点仍按回合刷新。
            context.ActionPoints.EndTurn();
            context.ActionPoints.BeginTurn(2);
            Assert.That(context.ActionPoints.Permanent, Is.EqualTo(12));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
        }

        /// <summary>腿部：登记一个额外玩家回合，由回合控制器在结束行动时消费。</summary>
        [Test]
        public void LegsQueuesOneExtraTurn()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[] { basic });
            var effect = assets.Create<NewWorldLegsEffectData>();
            effect.Execute(context, null, null);
            Assert.That(context.TurnEffects.ExtraTurns, Is.EqualTo(1));
        }

        /// <summary>脑机：重排顺序同时作用于普通与义体抽牌堆，且只接受完整排列。</summary>
        [Test]
        public void BrainReordersBothDrawPilesAndRejectsIncompleteOrders()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[] { basic, basic, basic });
            var cyber = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            var cyberInstance = new CardInstance(cyber);
            Assert.That(context.Cybernetic.DrawPile.TryAdd(cyberInstance), Is.True);

            var ordinaryCards = context.Ordinary.DrawPile.Cards.ToArray();
            var desired = new[] { ordinaryCards[2], ordinaryCards[0], ordinaryCards[1], cyberInstance };
            var effect = assets.Create<NewWorldBrainEffectData>();

            Assert.That(effect.ValidateReorder(context, null, desired), Is.True);
            Assert.That(effect.ValidateReorder(context, null, desired.Take(2).ToArray()), Is.False);

            effect.ExecuteRequest(context, new CardPlayRequest(null, reorder: desired));
            Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(desired.Take(3)));
            Assert.That(context.Cybernetic.GetDrawPileOrder(), Is.EqualTo(new[] { cyberInstance }));
        }

        /// <summary>套装：同回合成功四部位后，每次出牌把最近弃置的一张基本牌回收入手。</summary>
        [Test]
        public void SetBonusRecyclesMostRecentBasicFromDiscardOnEveryCardPlay()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(Enumerable.Repeat(basic, 6).ToArray());
            var parts = new[]
            {
                MakePart(CyberneticSlot.Brain),
                MakePart(CyberneticSlot.Torso),
                MakePart(CyberneticSlot.Hands),
                MakePart(CyberneticSlot.Legs)
            };
            foreach (var part in parts) context.Run.Loadout.Equip(new CyberneticInstance(part));
            context.Run.SetBonuses.Configure(new[] { MakeBonus(parts) });
            TestAssets.Prepare(context);

            context.Run.Buffs.BeginTurn();
            var service = new CardPlayService();
            foreach (var card in context.Run.Loadout.GetUsableCards()) context.Cybernetic.Hand.TryAdd(card);
            foreach (var card in context.Cybernetic.Hand.Cards.ToArray())
                Assert.That(service.TryPlay(context, card).Success, Is.True);

            var discarded = context.Ordinary.Hand.Cards[0];
            context.Ordinary.Discard(discarded);
            var played = context.Ordinary.Hand.Cards[0];
            Assert.That(new CardPlayService().TryPlay(context, played).Success, Is.True);

            // 打出的基本牌进入弃牌堆后成为最近弃置基本牌，随即被套装回收回手。
            Assert.That(context.Ordinary.Hand.Contains(played), Is.True);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(2));
            Assert.That(context.Ordinary.DiscardPile.Count, Is.EqualTo(1));
            Assert.That(context.Ordinary.DiscardPile.Contains(discarded), Is.True);
        }

        /// <summary>牌组预告只判断候选覆盖，不用于实际激活套装。</summary>
        [Test]
        public void SetBonusRequiresAllFourPartsOfTheSameSet()
        {
            var parts = new[]
            {
                MakePart(CyberneticSlot.Brain),
                MakePart(CyberneticSlot.Torso),
                MakePart(CyberneticSlot.Hands),
                MakePart(CyberneticSlot.Legs)
            };
            var bonus = MakeBonus(parts);
            var complete = new CyberneticLoadout();
            foreach (var part in parts) complete.Equip(new CyberneticInstance(part));
            Assert.That(bonus.IsComplete(complete), Is.True);

            var incomplete = new CyberneticLoadout();
            for (int i = 0; i < 3; i++) incomplete.Equip(new CyberneticInstance(parts[i]));
            Assert.That(bonus.IsComplete(incomplete), Is.False);
        }
    }
}
