using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace SpotlightGameJam.Tests
{
    /// <summary>验证同种共享耐久、同回合四部位组合 buff 以及会话与生命费用。</summary>
    public sealed class ExplorationRulesTests
    {
        /// <summary>直接打开战斗场景也应加载组合定义，不依赖先创建探索会话。</summary>
        [Test] public void StandaloneBootstrapLoadsConfiguredBuffs()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var owner=new GameObject("Standalone buff bootstrap");
                try
                {
                    var bootstrap=owner.AddComponent<GameBootstrap>();var fields=new UnityEditor.SerializedObject(bootstrap);
                    fields.FindProperty("rules").objectReferenceValue=setup.rules;
                    fields.FindProperty("phantomPain").objectReferenceValue=setup.phantomPain;
                    var selected=fields.FindProperty("initialCybernetics");selected.arraySize=4;
                    for(int i=0;i<4;i++)selected.GetArrayElementAtIndex(i).objectReferenceValue=parts[i];
                    var buffs=fields.FindProperty("runBuffs");Assert.That(buffs,Is.Not.Null,"独立战斗需要 buff 配置入口。");
                    buffs.arraySize=1;buffs.GetArrayElementAtIndex(0).objectReferenceValue=setup.buffs[0];fields.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(bootstrap.StartNewRun(),Is.True,bootstrap.LastError);Assert.That(bootstrap.Run.Buffs.Active,Is.Empty);
                    PrepareParts(bootstrap.Battle.Context);for(int i=0;i<4;i++)PlayPart(bootstrap.Battle,i);
                    Assert.That(bootstrap.Run.Buffs.Active.Count,Is.EqualTo(1));
                }
                finally {UnityEngine.Object.DestroyImmediate(owner);}
            }
        }
        /// <summary>构造一个部位的三份义体牌，同种共享耐久上限为四。</summary>
        private static CyberneticData Part(TestAssets a,int slot)
        {
            var card=a.Card(a.Create<ShieldEffectData>(),category:CardCategory.Cybernetic,ap:0,sanity:2);
            var data=a.Create<CyberneticData>();data.cyberneticId="part-"+slot;data.slot=(CyberneticSlot)slot;
            data.maxDurability=4;data.cards=new[]{card,card,card};return data;
        }
        /// <summary>构造可开战的探索配置及四部位组合。</summary>
        private static AdventureSetupData Setup(TestAssets a,CyberneticData[] parts)
        {
            var buff=a.Create<RunBuffData>();buff.buffId="combo";buff.buffName="组合";buff.requiredCybernetics=parts;
            var setup=a.Create<AdventureSetupData>();setup.rules=a.Create<BattleRules>();setup.phantomPain=a.Phantom();
            setup.buffs=new[]{buff};setup.ordinaryDeck=Enumerable.Repeat(a.Card(a.Create<HealEffectData>()),13).ToArray();return setup;
        }
        /// <summary>同配置三份牌共享耐久，报废后全部不可入堆，维修与跨战斗重建保持同一状态。</summary>
        [Test] public void RepeatedCyberneticCardsSharePersistentDurability()
        {
            using(var a=new TestAssets())
            {
                var inventory=new CyberneticLoadout();inventory.AddInitial(Part(a,0));
                var cards=inventory.GetUsableCards().ToArray();
                Assert.That(cards.Select(c=>c.Source.Id).Distinct().Count(),Is.EqualTo(3));
                cards[0].Source.TryConsumeDurability();
                Assert.That(cards.Select(c=>c.Source.Durability),Is.EqualTo(new[]{3,3,3}));
                Assert.That(inventory.GetUsableCards().Select(c=>c.Source),Is.EqualTo(cards.Select(c=>c.Source)));
                for(int i=0;i<3;i++) cards[0].Source.TryConsumeDurability();
                Assert.That(inventory.Owned.Count,Is.EqualTo(3));Assert.That(inventory.GetUsableCards(),Is.Empty);
                cards[1].Source.Repair(1);
                Assert.That(inventory.GetUsableCards().Select(c=>c.Source.Durability),Is.EqualTo(new[]{1,1,1}));
            }
        }
        /// <summary>共享只发生在同一次探索的同种义体，不污染其他种类或另一场探索。</summary>
        [Test] public void SharedDurabilityIsIsolatedByConfigurationAndRun()
        {
            using(var a=new TestAssets())
            {
                var brain=Part(a,0);var hand=Part(a,2);
                var first=new CyberneticLoadout();first.AddInitial(brain);first.AddInitial(hand);
                var second=new CyberneticLoadout();second.AddInitial(brain);
                first.Owned[0].TryConsumeDurability();
                Assert.That(first.Owned.Where(c=>c.Data==brain).Select(c=>c.Durability),Is.All.EqualTo(3));
                Assert.That(first.Owned.Where(c=>c.Data==hand).Select(c=>c.Durability),Is.All.EqualTo(4));
                Assert.That(second.Owned.Select(c=>c.Durability),Is.All.EqualTo(4));
                first.Owned[1].Repair(50);
                Assert.That(first.Owned.Select(c=>c.Durability),Is.All.EqualTo(4));
                Assert.That(brain.maxDurability,Is.EqualTo(4));
            }
        }
        /// <summary>无义体、部分义体和完整四部位均可出发，携带完整牌组也不自动触发 buff。</summary>
        [TestCase(0)] [TestCase(1)] [TestCase(4)]
        public void DepartureAllowsEmptyPartialAndFullSelection(int count)
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var run=setup.CreateRun(parts.Take(count));Assert.That(run.Loadout.Owned.Count,Is.EqualTo(count*3));
                Assert.That(run.Buffs.Active,Is.Empty);
            }
        }
        /// <summary>构造真实战斗，并将各持有种类的一张义体牌置入手牌供组合测试。</summary>
        private static BattleController Battle(AdventureSetupData setup,RunState run,CardData ordinary=null)
        {
            var random=new RandomUtility(1);
            var context=new BattleContext(run,setup.rules,new[]{new CombatantState("enemy",9999)},
                new CardDeck(13,TestAssets.Instances(ordinary?new[]{ordinary}:Array.Empty<CardData>()),random),
                new CardDeck(5,run.Loadout.GetUsableCards(),random,true),setup.phantomPain,random);
            var battle=new BattleController(context,0);
            Assert.That(battle.StartBattle(),Is.True,battle.LastError);
            PrepareParts(context);return battle;
        }
        /// <summary>为当前回合补入缺少部位的牌，不调用 buff 或绕过出牌校验。</summary>
        private static void PrepareParts(BattleContext context)
        {
            foreach(var data in context.Run.Loadout.Owned.Select(c=>c.Data).Distinct())
            {
                if(context.Cybernetic.Hand.Cards.Any(c=>c.Source.Data==data))continue;
                var next=context.Cybernetic.DrawPile.Cards.FirstOrDefault(c=>c.Source.Data==data);
                if(next!=null)Assert.That(context.Cybernetic.TakeFromDrawPile(next),Is.True);
            }
        }
        /// <summary>经真实出牌入口成功使用一个部位，避免把预览或弃牌误作触发。</summary>
        private static void PlayPart(BattleController battle,int slot)
        {
            var card=battle.Context.Cybernetic.Hand.Cards.First(c=>(int)c.Source.Data.slot==slot);
            var result=battle.TryPlay(card);Assert.That(result.Success,Is.True,result.Message);
        }
        /// <summary>第四个成功部位才触发，下一回合仍有效，重复组合不叠加，战斗结束清除。</summary>
        [Test] public void BuffRequiresFourSuccessfulPartsInOneTurn()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<3;i++){PlayPart(battle,i);Assert.That(run.Buffs.Active,Is.Empty);}
                    PlayPart(battle,3);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                    Assert.That(battle.EndTurn(),Is.True);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                    PrepareParts(battle.Context);foreach(int slot in new[]{3,2,1,0})PlayPart(battle,slot);
                    Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                    battle.Context.Enemies[0].ReceiveDamage(9999);battle.Context.CheckOutcome();Assert.That(run.Buffs.Active,Is.Empty);
                }
            }
        }
        /// <summary>不能把上回合的三个部位累积到本回合，重复失败和单纯弃置也不计入。</summary>
        [Test] public void PreviousTurnFailedPlaysAndDiscardsDoNotCompleteBuff()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<3;i++)PlayPart(battle,i);
                    Assert.That(battle.EndTurn(),Is.True);PrepareParts(battle.Context);PlayPart(battle,3);
                    Assert.That(run.Buffs.Active,Is.Empty);
                    var brain=battle.Context.Cybernetic.Hand.Cards.First(c=>c.Source.Data.slot==CyberneticSlot.Brain);
                    battle.Context.Cybernetic.Discard(brain);battle.Context.NotifyChanged();Assert.That(run.Buffs.Active,Is.Empty);
                    run.Player.TrySpendSanity(50);
                    var hand=battle.Context.Cybernetic.Hand.Cards.First(c=>c.Source.Data.slot==CyberneticSlot.Hands);
                    Assert.That(battle.TryPlay(hand).Success,Is.False);Assert.That(run.Buffs.Active,Is.Empty);
                }
            }
        }
        /// <summary>使用后耐久归零仍算成功打出该部位，维修或牌区移动本身不会触发 buff。</summary>
        [Test] public void LastDurabilityPlayCountsButRepairAloneDoesNotTrigger()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();foreach(var part in parts)part.maxDurability=1;
                var setup=Setup(a,parts);var run=setup.CreateRun(parts);var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<4;i++)PlayPart(battle,i);
                    Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));Assert.That(run.Loadout.Owned.Select(c=>c.Durability),Is.All.Zero);
                    Assert.That(battle.EndTurn(),Is.True);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                    battle.Context.Enemies[0].ReceiveDamage(9999);battle.Context.CheckOutcome();
                    run.Loadout.Owned[0].Repair(1);Assert.That(run.Buffs.Active,Is.Empty);
                }
            }
        }
        /// <summary>改造牌符合配置即可参与本回合组合，取得该牌本身不会激活。</summary>
        [Test] public void AcquiredCardsCanParticipateAfterSuccessfulPlay()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts.Take(3));
                var acquired=run.Loadout.AcquireCard(parts[3],parts[3].cards[0]);Assert.That(acquired.Source.IsInitialCard,Is.False);
                Assert.That(run.Buffs.Active,Is.Empty);var battle=Battle(setup,run);
                using(battle.Context){for(int i=0;i<4;i++)PlayPart(battle,i);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));}
            }
        }
        /// <summary>同一部位允许配置不同系列作为候选，混搭仍需在同回合逐个成功打出。</summary>
        [Test] public void MixedCombinationCanUseAlternativePart()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var alternative=Part(a,0);alternative.cyberneticId="alternative";
                setup.buffs[0].requiredCybernetics=parts.Concat(new[]{alternative}).ToArray();
                var run=setup.CreateRun(parts.Skip(1).Concat(new[]{alternative}));Assert.That(run.Buffs.Active,Is.Empty);
                var battle=Battle(setup,run);
                using(battle.Context){for(int i=0;i<4;i++)PlayPart(battle,i);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));}
            }
        }
        /// <summary>前三部位已成功时，第四张费用不足不能触发；恢复资源后成功才触发。</summary>
        [Test] public void FailedFourthPartDoesNotTriggerBuff()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<3;i++)PlayPart(battle,i);
                    run.Player.TrySpendSanity(50);
                    var leg=battle.Context.Cybernetic.Hand.Cards.First(c=>c.Source.Data.slot==CyberneticSlot.Legs);
                    Assert.That(battle.TryPlay(leg).Success,Is.False);Assert.That(run.Buffs.Active,Is.Empty);
                    Assert.That(leg.Source.Durability,Is.EqualTo(4));Assert.That(battle.Context.Usage.GetUses(CyberneticSlot.Legs),Is.Zero);
                    run.Player.RestoreSanity(2);PlayPart(battle,3);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                }
            }
        }
        /// <summary>四个部位都打出仍须符合组合允许的种类，其他系列不能冒充。</summary>
        [Test] public void WrongCyberneticConfigurationDoesNotCompleteBuff()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var other=Part(a,3);other.cyberneticId="other-legs";
                var run=setup.CreateRun(parts.Take(3).Concat(new[]{other}));var battle=Battle(setup,run);
                using(battle.Context){for(int i=0;i<4;i++)PlayPart(battle,i);Assert.That(run.Buffs.Active,Is.Empty);}
            }
        }
        /// <summary>额外玩家行动也是新回合，不能沿用前一回合的三个部位补齐组合。</summary>
        [Test] public void ExtraTurnStartsFreshCombinationProgress()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<3;i++)PlayPart(battle,i);battle.Context.TurnEffects.ExtraTurns=1;
                    Assert.That(battle.EndTurn(),Is.True);PlayPart(battle,3);Assert.That(run.Buffs.Active,Is.Empty);
                }
            }
        }
        /// <summary>退出未结束的战斗也清理 buff；下一场沿用耐久但必须重新完成同回合组合。</summary>
        [Test] public void NextEncounterKeepsSharedDurabilityButNotActiveBattleBuff()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var first=Battle(setup,run);
                using(first.Context){for(int i=0;i<4;i++)PlayPart(first,i);Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));}
                Assert.That(run.Buffs.Active,Is.Empty);var second=Battle(setup,run);
                using(second.Context)
                {
                    Assert.That(run.Buffs.Active,Is.Empty);Assert.That(run.Loadout.Owned.Select(c=>c.Durability),Is.All.EqualTo(3));
                    PlayPart(second,3);Assert.That(run.Buffs.Active,Is.Empty);
                }
            }
        }
        /// <summary>旧战斗重复释放不能清掉新战斗的出牌记录或已触发 buff。</summary>
        [TestCase(false)] [TestCase(true)]
        public void RepeatedOldBattleDisposalCannotResetCurrentCombination(bool afterTrigger)
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var old=Battle(setup,run);old.Context.Dispose();var current=Battle(setup,run);
                using(current.Context)
                {
                    if(afterTrigger)for(int i=0;i<4;i++)PlayPart(current,i);
                    old.Context.Dispose();
                    if(!afterTrigger)for(int i=0;i<4;i++)PlayPart(current,i);
                    Assert.That(run.Buffs.Active.Count,Is.EqualTo(1));
                }
            }
        }
        /// <summary>持续配置只改变过期时机，不改变同回合四部位触发条件。</summary>
        [TestCase(RunBuffDuration.PlayerTurn,false,false)]
        [TestCase(RunBuffDuration.Battle,true,false)]
        [TestCase(RunBuffDuration.Adventure,true,true)]
        public void ConfiguredDurationExpiresAtItsBoundary(RunBuffDuration duration,bool afterTurn,bool afterBattle)
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                setup.buffs[0].duration=duration;var run=setup.CreateRun(parts);var battle=Battle(setup,run);
                using(battle.Context)
                {
                    for(int i=0;i<4;i++)PlayPart(battle,i);Assert.That(battle.EndTurn(),Is.True);
                    Assert.That(run.Buffs.Active.Any(),Is.EqualTo(afterTurn));
                    battle.Context.Enemies[0].ReceiveDamage(9999);battle.Context.CheckOutcome();
                    Assert.That(run.Buffs.Active.Any(),Is.EqualTo(afterBattle));
                }
                Assert.That(setup.CreateRun(parts).Buffs.Active,Is.Empty);
            }
        }
        /// <summary>真实普罗米修斯资源完成四部位结算，耗尽躯干也触发，并在卡面同步过载生命费用。</summary>
        [Test] public void RealPrometheusAssetsShareDurabilityAndTriggerBattleBuff()
        {
            var setup=UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            var deck=UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/Prometheus.asset");
            var overload=UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/OverloadCard.asset");
            var attack=UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
            var run=setup.CreateRun(deck.cybernetics);var random=new RandomUtility(1);
            using(var context=new BattleContext(run,setup.rules,new[]{new CombatantState("enemy",9999)},
                new CardDeck(13,TestAssets.Instances(new[]{attack,overload}),random),
                new CardDeck(5,run.Loadout.GetUsableCards(),random,true),setup.phantomPain,random))
            {
                var battle=new BattleController(context,0);Assert.That(battle.StartBattle(),Is.True,battle.LastError);PrepareParts(context);
                PlayPart(battle,3);PlayPart(battle,0);PlayPart(battle,2);Assert.That(run.Buffs.Active,Is.Empty);
                var torso=context.Cybernetic.Hand.Cards.First(c=>c.Source.Data.slot==CyberneticSlot.Torso);
                var basic=context.Ordinary.Hand.Cards.Single(c=>c.Data==attack);
                var result=battle.TryPlayRequest(new CardPlayRequest(torso,choiceId:basic.Id));
                Assert.That(result.Success,Is.True,result.Message);Assert.That(torso.Source.Durability,Is.Zero);
                Assert.That(run.Buffs.Has(RunBuffEffect.OverloadPaysHealth),Is.True);
                Assert.That(run.Loadout.Owned.Where(c=>c.Data==deck.cybernetics[0]).Select(c=>c.Durability),Is.All.EqualTo(3));
                var card=context.Ordinary.Hand.Cards.Single(c=>c.Data==overload);
                var root=UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/OverloadCard.prefab"));
                try
                {
                    var view=root.GetComponent("CardView");view.GetType().GetMethod("BindForBattle").Invoke(view,new object[]{card,context});
                    var text=new UnityEditor.SerializedObject(view).FindProperty("costText").objectReferenceValue as UnityEngine.UI.Text;
                    Assert.That(text.text,Is.EqualTo("生命 5"));
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
                int health=run.Player.Health,sanity=run.Player.Sanity;run.Player.AddShield(7);int shield=run.Player.Shield;
                Assert.That(battle.TryPlay(card).Success,Is.True);Assert.That(run.Player.Health,Is.EqualTo(health-5));
                Assert.That(run.Player.Sanity,Is.EqualTo(sanity));Assert.That(run.Player.Shield,Is.EqualTo(shield));
                Assert.That(battle.EndTurn(),Is.True);Assert.That(run.Buffs.Has(RunBuffEffect.OverloadPaysHealth),Is.True);
            }
            Assert.That(run.Buffs.Active,Is.Empty);
        }
        /// <summary>过载零理智付血，不扣盾、不回理智，支付后至少保留一生命。</summary>
        [TestCase(5,false)] [TestCase(6,true)] [TestCase(100,true)]
        public void OverloadHealthPaymentPreservesSanityShieldAndOneLife(int health,bool success)
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);var run=setup.CreateRun(parts);
                var ap=a.Create<ActionPointEffectData>();ap.amount=1;
                var overload=a.Card(ap,CardTargetType.None,CardCategory.Function,0,5);overload.cardId="OverloadCard";
                run.Player.ReceiveDamage(100-health);
                var random=new RandomUtility(1);
                using(var context=new BattleContext(run,setup.rules,new[]{new CombatantState("enemy",100)},
                    new CardDeck(13,TestAssets.Instances(new[]{overload}),random),
                    new CardDeck(5,run.Loadout.GetUsableCards(),random,true),setup.phantomPain,random))
                {
                    var battle=new BattleController(context,0);Assert.That(battle.StartBattle(),Is.True);
                    PrepareParts(context);for(int i=0;i<4;i++)PlayPart(battle,i);
                    run.Player.TrySpendSanity(50);run.Player.ClearShield();run.Player.AddShield(10);
                    var card=context.Ordinary.Hand.Cards.Single(c=>c.Data==overload);
                    Assert.That(context.CanPayCardCost(card),Is.EqualTo(success));
                    Assert.That(battle.TryPlay(card).Success,Is.EqualTo(success));
                    Assert.That(run.Player.Health,Is.EqualTo(success?health-5:health));
                    Assert.That(run.Player.Sanity,Is.Zero);Assert.That(run.Player.Shield,Is.EqualTo(10));
                    Assert.That(context.ActionPoints.Total,Is.EqualTo(success?3:2));
                    Assert.That(context.Ordinary.Hand.Contains(card),Is.EqualTo(!success));
                    Assert.That(battle.EndTurn(),Is.True);
                    Assert.That(context.UsesHealthFor(card),Is.True);
                    context.Enemies[0].ReceiveDamage(100);context.CheckOutcome();
                    Assert.That(context.UsesHealthFor(card),Is.False);
                }
            }
        }
        /// <summary>同种共享耐久的多张牌仍不能绕过部位回合和战斗次数。</summary>
        [Test] public void SharedCardsCannotBypassSlotUsageLimits()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var run=setup.CreateRun(new[]{parts[2]});var random=new RandomUtility(1);
                using(var context=new BattleContext(run,setup.rules,new[]{new CombatantState("enemy",100)},
                    new CardDeck(13,Array.Empty<CardInstance>(),random),new CardDeck(5,run.Loadout.GetUsableCards(),random,true),setup.phantomPain,random))
                {
                    context.SetState(BattleState.PlayerAction);context.ActionPoints.BeginTurn(2);context.Cybernetic.Draw(3);
                    var cards=context.Cybernetic.Hand.Cards.ToArray();var service=new CardPlayService();
                    Assert.That(service.TryPlay(context,cards[0]).Success,Is.True);
                    Assert.That(service.TryPlay(context,cards[1]).Success,Is.False);
                    Assert.That(cards.Select(c=>c.Source.Durability),Is.EqualTo(new[]{3,3,3}));
                    for(int i=1;i<3;i++) { context.Usage.BeginTurn();Assert.That(service.TryPlay(context,cards[i]).Success,Is.True); }
                    context.Usage.BeginTurn();context.Cybernetic.Draw(1);
                    Assert.That(service.TryPlay(context,context.Cybernetic.Hand.Cards[0]).Success,Is.False);
                }
            }
        }
        /// <summary>出发没有战斗；释放场景后新遭遇复用探索属性、逐张耐久和 buff。</summary>
        [Test] public void SessionPreparesWithoutBattleAndRetainsStateAcrossEncounters()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var owner=new GameObject("Session test");var session=owner.AddComponent<AdventureSession>();
                try
                {
                    int requests=0;session.ExplorationRequested+=state=>requests++;
                    Assert.That(session.TryBegin(setup,parts),Is.True);var run=session.Run;
                    Assert.That(requests,Is.EqualTo(1));Assert.That(session.CurrentBattle,Is.Null);Assert.That(session.EncounterIndex,Is.Zero);
                    run.Player.ReceiveDamage(7);run.AddGold(10);run.Loadout.Owned[0].TryConsumeDurability();
                    Assert.That(session.TryStartEncounter(100,0),Is.True);
                    session.CurrentBattle.Context.Enemies[0].ReceiveDamage(100);session.CurrentBattle.Context.CheckOutcome();
                    session.ReleaseEncounter(session.CurrentBattle);
                    Assert.That(session.TryStartEncounter(100,0),Is.True);Assert.That(session.Run,Is.SameAs(run));
                    Assert.That(session.EncounterIndex,Is.EqualTo(2));Assert.That(run.Player.Health,Is.EqualTo(93));
                    Assert.That(run.Gold,Is.EqualTo(10));Assert.That(run.Loadout.Owned[0].Durability,Is.EqualTo(3));
                }
                finally {session.ReleaseEncounter(session.CurrentBattle);UnityEngine.Object.DestroyImmediate(owner);}
            }
        }
        /// <summary>从会话战斗重开独立测试冒险时，应释放旧会话的战斗锁。</summary>
        [Test] public void RestartingBootstrapReleasesPreviousSessionEncounter()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                var session=AdventureSession.GetOrCreate();var owner=new GameObject("Bootstrap restart test");
                try
                {
                    Assert.That(session.TryBegin(setup,parts),Is.True);
                    var bootstrap=owner.AddComponent<GameBootstrap>();
                    var fields=new UnityEditor.SerializedObject(bootstrap);
                    fields.FindProperty("rules").objectReferenceValue=setup.rules;
                    fields.FindProperty("phantomPain").objectReferenceValue=setup.phantomPain;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(bootstrap.StartSessionEncounter(),Is.True);
                    Assert.That(bootstrap.StartNewRun(),Is.True);
                    Assert.That(session.CurrentBattle,Is.Null);
                    Assert.That(session.TryBegin(setup,Array.Empty<CyberneticData>()),Is.True);
                }
                finally {UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(session.gameObject);}
            }
        }

        /// <summary>胜利事件仍在出牌结算中时，订阅者不能重入并替换探索。</summary>
        [Test] public void VictoryCallbackCannotRestartAdventureDuringResolution()
        {
            using(var a=new TestAssets())
            {
                var parts=Enumerable.Range(0,4).Select(i=>Part(a,i)).ToArray();var setup=Setup(a,parts);
                setup.ordinaryDeck=new[]{a.Card(a.Create<DamageEffectData>(),CardTargetType.SingleEnemy)};
                var session=AdventureSession.GetOrCreate();
                try
                {
                    Assert.That(session.TryBegin(setup,parts),Is.True);Assert.That(session.TryStartEncounter(1,0),Is.True);
                    var battle=session.CurrentBattle;var run=session.Run;bool attempted=false,allowed=false;
                    battle.Context.StateChanged+=state=>{if(state==BattleState.Victory){attempted=true;allowed=session.TryBegin(setup,parts);}};
                    Assert.That(battle.TryPlay(battle.Context.Ordinary.Hand.Cards[0],battle.Context.Enemies[0]).Success,Is.True);
                    Assert.That(attempted,Is.True);Assert.That(allowed,Is.False);Assert.That(session.Run,Is.SameAs(run));
                }
                finally {session.ReleaseEncounter(session.CurrentBattle);UnityEngine.Object.DestroyImmediate(session.gameObject);}
            }
        }

    }
}
