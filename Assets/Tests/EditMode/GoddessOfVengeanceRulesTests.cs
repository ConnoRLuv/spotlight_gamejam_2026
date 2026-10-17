using System.Linq;
using NUnit.Framework;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证复仇女神（Type-02）四部件效果、伤害转移与穿戴全套的套装补牌规则。</summary>
    public sealed class GoddessOfVengeanceRulesTests
    {
        private TestAssets assets;

        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();

        private CardData AttackCard() =>
            assets.Card(assets.Create<DamageEffectData>(), CardTargetType.SingleEnemy, CardCategory.Basic, 1, 0);

        private CyberneticData MakePart(CyberneticSlot slot)
        {
            var card = assets.Card(assets.Create<ShieldEffectData>(), category: CardCategory.Cybernetic, ap: 0, sanity: 2);
            var data = assets.Create<CyberneticData>();
            data.cyberneticId = "goddess-part-" + (int)slot;
            data.slot = slot;
            data.maxDurability = 3;
            data.cards = new[] { card };
            return data;
        }

        private GoddessOfVengeanceSetBonusData MakeBonus(CyberneticData[] parts)
        {
            var bonus = assets.Create<GoddessOfVengeanceSetBonusData>();
            bonus.setId = "goddess-of-vengeance";
            bonus.setName = "复仇女神测试套装";
            bonus.requiredParts = parts;
            bonus.trigger = GoddessOfVengeanceSetTrigger.OnPlayerDamaged;
            bonus.effect = GoddessOfVengeanceSetEffect.DrawBasicFromDrawPile;
            return bonus;
        }

        /// <summary>脑机：把普通手牌中的非攻击牌全部替换为攻击牌，手牌数量不变。</summary>
        [Test]
        public void BrainConvertsNonAttackCardsToAttack()
        {
            var attack = AttackCard();
            var heal = assets.Card(assets.Create<HealEffectData>(), CardTargetType.Self, CardCategory.Basic, 1, 0);
            var context = assets.Context(new[] { heal, heal, heal });
            TestAssets.Prepare(context);
            var effect = assets.Create<GoddessOfVengeanceBrainEffectData>(); effect.attack = attack;
            effect.Execute(context, null, null);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(3));
            Assert.That(context.Ordinary.Hand.Cards.All(card => card.Data == attack), Is.True);
        }

        /// <summary>躯干：本回合玩家受到的伤害完全转移给敌人，玩家不受伤，标记保留到下回合。</summary>
        [Test]
        public void TorsoRedirectsDamageToEnemyForTheTurn()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[] { basic });
            TestAssets.Prepare(context);
            var enemy = context.Enemies[0];
            int playerHp = context.Player.Health; int enemyHp = enemy.Health;
            context.TurnEffects.DamageToEnemyArmed = true;
            DamageResolver.Apply(context, enemy, context.Player, 6);
            Assert.That(context.Player.Health, Is.EqualTo(playerHp));
            Assert.That(enemy.Health, Is.EqualTo(enemyHp - 6));
            Assert.That(context.TurnEffects.DamageToEnemyArmed, Is.True);
        }

        /// <summary>手部：从普通弃牌堆选择并回收一张攻击牌到普通手牌。</summary>
        [Test]
        public void HandsRecoversOneAttackCardFromDiscardPile()
        {
            var attack = AttackCard();
            var context = assets.Context(new[] { attack, attack });
            TestAssets.Prepare(context);
            var discarded = context.Ordinary.Hand.Cards.First();
            context.Ordinary.Discard(discarded);
            var played = context.Ordinary.Hand.Cards.First();
            var effect = assets.Create<GoddessOfVengeanceHandsEffectData>(); effect.attack = attack;
            var choices = effect.GetChoices(context, played);
            Assert.That(choices.Count, Is.EqualTo(1));
            Assert.That(choices[0], Is.SameAs(discarded));
            effect.ExecuteRequest(context, new CardPlayRequest(played, null, choices[0].Id, choices[0], null));
            Assert.That(context.Ordinary.Hand.Contains(discarded), Is.True);
            Assert.That(context.Ordinary.DiscardPile.Count, Is.Zero);
        }

        /// <summary>腿部：开启后本回合基本牌的费用判定为零 AP。</summary>
        [Test]
        public void LegsMakesBasicCardsFreeThisTurn()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(new[] { basic });
            var effect = assets.Create<GoddessOfVengeanceLegsEffectData>();
            effect.Execute(context, null, null);
            Assert.That(context.TurnEffects.FreeBasicCards, Is.True);
            Assert.That(context.GetEffectiveApCost(new CardInstance(basic)), Is.Zero);
        }

        /// <summary>套装：穿戴全套后每次玩家受伤，从普通抽牌堆获得一张基本牌。</summary>
        [Test]
        public void SetBonusDrawsBasicCardWhenPlayerTakesDamage()
        {
            var basic = assets.Card(assets.Create<HealEffectData>());
            var context = assets.Context(Enumerable.Repeat(basic, 5).ToArray());
            TestAssets.Prepare(context);
            var parts = new[] { MakePart(CyberneticSlot.Brain), MakePart(CyberneticSlot.Torso),
                MakePart(CyberneticSlot.Hands), MakePart(CyberneticSlot.Legs) };
            foreach (var part in parts) context.Run.Loadout.Equip(new CyberneticInstance(part));
            context.Run.GoddessSetBonuses.Configure(new[] { MakeBonus(parts) });
            int handBefore = context.Ordinary.Hand.Count;
            int pileBefore = context.Ordinary.DrawPile.Count;
            DamageResolver.Apply(context, context.Enemies[0], context.Player, 1);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(handBefore + 1));
            Assert.That(context.Ordinary.DrawPile.Count, Is.EqualTo(pileBefore - 1));
        }

        /// <summary>套装完成度：四部位齐全才算穿戴完整，缺少任一部位不触发。</summary>
        [Test]
        public void SetBonusRequiresAllFourPartsOfTheSameSet()
        {
            var parts = new[] { MakePart(CyberneticSlot.Brain), MakePart(CyberneticSlot.Torso),
                MakePart(CyberneticSlot.Hands), MakePart(CyberneticSlot.Legs) };
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