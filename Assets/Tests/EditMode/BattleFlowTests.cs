using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace SpotlightGameJam.Tests
{
    /// <summary>
    /// 验证完整回合推进、AP 增长、护盾时机、胜负停止及跨战斗状态保留。
    /// </summary>
    /// <remarks>
    /// 使用纯 C# 战斗对象和临时配置资产，避免依赖正式场景；测试后统一销毁资产。
    /// </remarks>
    public sealed class BattleFlowTests
    {
        private TestAssets assets;
        /// <summary>为用例创建独立的测试状态，避免用例之间共享可变资源。</summary>
        /// <summary>清理测试创建的临时对象和状态，避免污染后续用例。</summary>
        /// <summary>构造测试所需的遭遇战上下文。</summary>
        [SetUp] public void SetUp() => assets = new TestAssets();
        [TearDown] public void TearDown() => assets.Dispose();
        private BattleContext Encounter(RunState run, BattleRules rules, CardData ordinary, int enemyHealth = 100,
            CardData phantom = null)
        {
            var random = new RandomUtility(13);
            return new BattleContext(run,rules,new[]{new CombatantState("e",enemyHealth)},
                new CardDeck(13,TestAssets.Instances(Enumerable.Repeat(ordinary,20)),random),
                new CardDeck(5,run.Loadout.GetUsableCards(),random),phantom ?? assets.Phantom(),random);
        }
        /// <summary>验证每回合补充三张普通牌和一张义体牌，行动点额度逐回合增长至上限。</summary>
        [Test] public void TurnStartDrawsThreeOrdinaryAndOneCyberneticAndGrowsAp()
        {
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
            var heal = assets.Card(assets.Create<HealEffectData>());
            var cyberCard = assets.Card(assets.Create<ShieldEffectData>(),category:CardCategory.Cybernetic,ap:0,sanity:2);
            var data = assets.Create<CyberneticData>(); data.cyberneticId = "brain";
            data.slot = CyberneticSlot.Brain; data.cards = new[]{cyberCard,cyberCard,cyberCard};
            run.Loadout.Equip(new CyberneticInstance(data));
            var context = Encounter(run,rules,heal); var battle = new BattleController(context,0);
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(3));
            Assert.That(context.Cybernetic.Hand.Count, Is.EqualTo(1));
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(2));
            Assert.That(battle.StartBattle(), Is.False);
            context.ActionPoints.AddTemporary(1);
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(3));
            Assert.That(context.ActionPoints.Temporary, Is.Zero);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(6));
            for(int i=0;i<15;i++) Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.ActionPoints.Normal, Is.EqualTo(12));
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(13));
            Assert.That(context.Cybernetic.Hand.Count, Is.LessThanOrEqualTo(5));
        }
        /// <summary>验证护盾抵挡敌人阶段伤害，并在下个玩家回合开始时清除。</summary>
        [Test] public void ShieldProtectsEnemyActionThenExpiresAtNextPlayerTurn()
        {
            var data = assets.Card(assets.Create<ShieldEffectData>());
            var context = assets.Context(new[]{data}); var battle = new BattleController(context,6);
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(battle.TryPlay(context.Ordinary.Hand.Cards[0]).Success, Is.True);
            Assert.That(context.Player.Shield, Is.EqualTo(3));
            Assert.That(battle.EndTurn(), Is.True);
            Assert.That(context.Player.Health, Is.EqualTo(97));
            Assert.That(context.Player.Shield, Is.Zero);
            context.Player.AddShield(10); battle.EndTurn();
            Assert.That(context.Player.Health, Is.EqualTo(97));
            Assert.That(context.Player.Shield, Is.Zero);
        }
        /// <summary>验证致死伤害结束战斗后，不再执行剩余效果或接受后续指令。</summary>
        [Test] public void LethalDamageStopsRemainingEffectsAndFutureCommands()
        {
            var damage = assets.Create<DamageEffectData>(); damage.amount = 100;
            var data = assets.Card(damage,CardTargetType.SingleEnemy);
            data.effects = new CardEffectData[]{damage,assets.Create<ActionPointEffectData>()};
            var context = assets.Context(new[]{data}); var battle = new BattleController(context);
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(battle.TryPlay(context.Ordinary.Hand.Cards[0],context.Enemies[0]).Success, Is.True);
            Assert.That(context.State, Is.EqualTo(BattleState.Victory));
            Assert.That(context.ActionPoints.Temporary, Is.Zero);
            Assert.That(battle.EndTurn(), Is.False);
            Assert.That(battle.TryPlay(new CardInstance(data),context.Enemies[0]).Failure,
                Is.EqualTo(CardPlayFailure.InvalidPhase));
        }
        /// <summary>验证玩家被击杀后，其余敌人停止攻击。</summary>
        [Test] public void EnemyLethalDamageStopsFurtherEnemyAttacks()
        {
            var data = assets.Card(assets.Create<HealEffectData>());
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules); var random = new RandomUtility(3);
            var context = new BattleContext(run,rules,new[]{new CombatantState("a",10),new CombatantState("b",10)},
                new CardDeck(13,new[]{new CardInstance(data)},random),
                new CardDeck(5,Array.Empty<CardInstance>(),random),assets.Phantom(),random);
            var battle = new BattleController(context,100);
            Assert.That(battle.StartBattle(), Is.True);
            battle.EndTurn();
            Assert.That(context.State, Is.EqualTo(BattleState.Defeat));
            Assert.That(battle.TurnNumber, Is.EqualTo(1));
            Assert.That(battle.EndTurn(), Is.False);
        }
        /// <summary>验证新战斗保留冒险属性及义体耐久，同时重置部位使用次数。</summary>
        [Test] public void NewBattlePreservesAttributesAndDurabilityButResetsSlotUsage()
        {
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
            var ordinary = assets.Card(assets.Create<HealEffectData>());
            var cyber = assets.Card(assets.Create<ShieldEffectData>(),category:CardCategory.Cybernetic,ap:0,sanity:2);
            var config = assets.Create<CyberneticData>(); config.cyberneticId = "torso";
            config.slot = CyberneticSlot.Torso; config.maxDurability = 5; config.cards = new[]{cyber};
            var instance = new CyberneticInstance(config); run.Loadout.Equip(instance);
            var first = Encounter(run,rules,ordinary); var battle = new BattleController(first,0); battle.StartBattle();
            Assert.That(battle.TryPlay(first.Cybernetic.Hand.Cards[0]).Success, Is.True);
            run.Player.ReceiveDamage(10);
            // 先结束旧战斗，再检查下一场复用的生命、理智和耐久，以及重建的部位次数。
            // Finish the encounter before constructing its successor.
            DamageResolver.Apply(first,first.Player,first.Enemies[0],100);
            var second = Encounter(run,rules,ordinary); var next = new BattleController(second,0);
            Assert.That(next.StartBattle(), Is.True);
            Assert.That(second.Player.Health, Is.EqualTo(93), "上一场的护盾先吸收了 3 点伤害。");
            Assert.That(second.Player.Sanity, Is.EqualTo(48));
            Assert.That(instance.Durability, Is.EqualTo(4));
            Assert.That(second.Usage.CanUse(CyberneticSlot.Torso), Is.True);
            Assert.That(next.TryPlay(second.Cybernetic.Hand.Cards[0]).Success, Is.True);
            Assert.That(instance.Durability, Is.EqualTo(3));
        }
        /// <summary>验证无敌人时获胜，而玩家死亡时优先判定失败。</summary>
        [Test] public void EmptyEnemyListWinsAndPlayerDeathTakesPriority()
        {
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules); var random = new RandomUtility(0);
            var context = new BattleContext(run,rules,Array.Empty<CombatantState>(),
                new CardDeck(13,Array.Empty<CardInstance>(),random),new CardDeck(5,Array.Empty<CardInstance>(),random),
                assets.Phantom(),random);
            var battle = new BattleController(context);
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(context.State, Is.EqualTo(BattleState.Victory));
            context.Player.ReceiveDamage(100); context.CheckOutcome();
            Assert.That(context.State, Is.EqualTo(BattleState.Defeat));
        }
        /// <summary>验证零理智补入唯一幻痛牌，并检查奖励牌遵守容量限制。</summary>
        [Test] public void ZeroSanityAtStartAddsOneLockedCardAndRewardUsesCapacity()
        {
            var rules = assets.Create<BattleRules>(); var run = new RunState(rules);
            var data = assets.Card(assets.Create<HealEffectData>()); run.AddInitialHandReward(data);
            run.Player.TrySpendSanity(50);
            var context = Encounter(run,rules,data); var battle = new BattleController(context,0);
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(context.HasPhantomPain, Is.True);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(5));
            battle.EndTurn();
            Assert.That(context.Ordinary.Hand.Cards.Count(c => c.Data == context.PhantomPain), Is.EqualTo(1));
        }
        /// <summary>验证非法初始配置在改变战斗状态前被拒绝。</summary>
        [Test] public void InvalidSetupFailsBeforeAnyBattleMutation()
        {
            var data = assets.Card(assets.Create<HealEffectData>()); data.effects = Array.Empty<CardEffectData>();
            var context = assets.Context(new[]{data}); var battle = new BattleController(context);
            Assert.That(battle.StartBattle(), Is.False);
            Assert.That(battle.LastError, Is.Not.Empty);
            Assert.That(context.State, Is.EqualTo(BattleState.NotStarted));
            Assert.That(context.Ordinary.Hand.Count, Is.Zero);
            Assert.That(context.Player.Health, Is.EqualTo(100));
        }
        /// <summary>验证状态事件中的指令不能重入正在执行的结算。</summary>
        [Test] public void CommandsFromStateEventsCannotReenterResolution()
        {
            var context = assets.Context(new[]{assets.Card(assets.Create<HealEffectData>())});
            var battle = new BattleController(context,0); int attempts = 0;
            battle.StateChanged += state =>
            {
                if(state != BattleState.PlayerAction) return;
                attempts++; Assert.That(battle.EndTurn(), Is.False);
            };
            Assert.That(battle.StartBattle(), Is.True);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(battle.TurnNumber, Is.EqualTo(1));
        }
    }
}
