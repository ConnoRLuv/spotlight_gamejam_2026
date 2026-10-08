using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>
    /// 在 Unity 场景中组装配置、冒险状态和战斗系统的入口组件。
    /// </summary>
    /// <remarks>
    /// Inspector 配置规则、卡牌、义体和敌人；StartNewRun 创建新冒险，StartEncounter 复用冒险状态开始下一场。
    /// </remarks>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private BattleRules rules;
        [SerializeField] private CardData[] ordinaryDeck = Array.Empty<CardData>();
        [SerializeField] private CardData phantomPain;
        [SerializeField] private CyberneticData[] initialCybernetics = Array.Empty<CyberneticData>();
        [SerializeField, Min(1)] private int enemyHealth = 30;
        [SerializeField, Min(0)] private int enemyAttackDamage = 6;
        [SerializeField] private int randomSeed = 1;
        [SerializeField] private bool startOnPlay = true;
        // 用于生成下一场遭遇的种子；开启新冒险时从 0 重建。
        private int encounterIndex;
        public RunState Run { get; private set; }
        public BattleController Battle { get; private set; }
        public string LastError { get; private set; }
        // UI 获取新战斗时应解除旧 Context 订阅，再绑定新的战斗事件。
        public event Action<BattleController> BattleCreated;

        private void Start() { if (startOnPlay) StartNewRun(); }
        /// <summary>
        /// 校验 Inspector 配置，创建新冒险及首场战斗；初始化成功后才替换公开引用。
        /// </summary>
        public bool StartNewRun()
        {
            if (Battle != null && Battle.Context.IsResolving) return Fail("当前仍在结算。");
            if (initialCybernetics == null) return Fail("初始义体列表为空引用。");
            var errors = ValidateEncounterConfiguration(initialCybernetics);
            var slots = new HashSet<CyberneticSlot>();
            foreach (var data in initialCybernetics)
            {
                if (!data) errors.Add("初始义体含空引用。");
                else
                {
                    if (!slots.Add(data.slot)) errors.Add("初始义体重复占用同一部位。");
                }
            }
            if (errors.Count > 0) return Fail(string.Join("\n",errors));
            // 用候选对象初始化，避免配置失败时覆盖现有冒险引用。
            var nextRun = new RunState(rules);
            foreach (var data in initialCybernetics) nextRun.Loadout.Equip(new CyberneticInstance(data));
            var candidate = CreateEncounter(nextRun,0);
            if (!candidate.StartBattle()) return Fail(candidate.LastError);
            // 首场启动成功后才发布新冒险；失败不会替换旧 Run/Battle。
            Run = nextRun; Battle = candidate; encounterIndex = 1; LastError = null;
            BattleCreated?.Invoke(Battle); return true;
        }
        /// <summary>
        /// 在上一场结束后创建下一场战斗；复用玩家及装备，不恢复生命、理智或耐久。
        /// </summary>
        public bool StartEncounter()
        {
            if (Run == null) return Fail("请先开始冒险。");
            if (Battle != null && (Battle.Context.IsResolving || !Battle.Context.IsFinished))
                return Fail("当前战斗尚未结束。");
            if (!Run.Player.IsAlive) return Fail("玩家已经死亡，请开始新冒险。");
            var errors = ValidateEncounterConfiguration(Run.Loadout.Equipped.Select(instance => instance.Data));
            if (errors.Count > 0) return Fail(string.Join("\n",errors));
            // 下一场只替换战斗对象，继续引用同一个 RunState 中的持久状态。
            var candidate = CreateEncounter(Run,encounterIndex);
            if (!candidate.StartBattle()) return Fail(candidate.LastError);
            Battle = candidate; encounterIndex++; LastError = null;
            BattleCreated?.Invoke(Battle); return true;
        }
        /// <summary>
        /// 将界面结束回合请求转发到当前战斗；尚未创建战斗时返回 false。
        /// </summary>
        public bool EndTurn() => Battle != null && Battle.EndTurn();
        /// <summary>转发包含脑机选牌信息的指令。</summary>
        public CardPlayResult TryPlayRequest(CardPlayRequest request) => Battle == null
            ? new CardPlayResult(CardPlayFailure.InvalidPhase,"尚未开始战斗。") : Battle.TryPlayRequest(request);
        /// <summary>
        /// 将界面出牌请求转发到当前战斗，统一返回可展示的成功或拒绝结果。
        /// </summary>
        public CardPlayResult TryPlay(CardInstance card, CombatantState target = null) =>
            Battle == null ? new CardPlayResult(CardPlayFailure.InvalidPhase,"尚未开始战斗。") : Battle.TryPlay(card,target);
        /// <summary>
        /// 在实例和牌堆构建前深度校验配置，避免错误引用变成构造阶段异常。
        /// </summary>
        private List<string> ValidateEncounterConfiguration(IEnumerable<CyberneticData> cybernetics)
        {
            var errors = new List<string>();
            if (!rules) errors.Add("请配置战斗规则。");
            else errors.AddRange(rules.Validate());
            if (!phantomPain) errors.Add("请配置幻痛。");
            else
            {
                errors.AddRange(phantomPain.Validate());
                if (!phantomPain.locksInHand || phantomPain.category != CardCategory.Special)
                    errors.Add("幻痛必须是锁定特殊牌。");
            }
            if (enemyHealth <= 0 || enemyAttackDamage < 0) errors.Add("敌人属性配置不合法。");
            if (ordinaryDeck == null) errors.Add("初始牌堆为空引用。");
            else foreach (var card in ordinaryDeck)
            {
                if (!card) errors.Add("普通牌堆含空引用。");
                else
                {
                    errors.AddRange(card.Validate());
                    if (card.category != CardCategory.Basic && card.category != CardCategory.Function)
                        errors.Add("普通牌堆只能包含基本牌和功能牌。");
                }
            }
            // 深度检查关联卡，尤其是锁定牌，避免 CardDeck 构造时直接抛异常。
            foreach (var data in cybernetics)
            {
                if (!data) errors.Add("义体配置含空引用。");
                else errors.AddRange(data.Validate());
            }
            return errors;
        }
        /// <summary>
        /// 为遭遇生成独立牌实例、随机源和战斗上下文；只有传入的 RunState 跨战斗复用。
        /// </summary>
        private BattleController CreateEncounter(RunState run, int index)
        {
            // 固定种子加遭遇序号，方便复现相同冒险中每一场的随机过程。
            var random = new RandomUtility(unchecked(randomSeed + index));
            var ordinary = new List<CardInstance>();
            bool hasCybernetic = run.Loadout.Equipped.Any();
            foreach (var data in ordinaryDeck)
            {
                // 初始牌组保留静态配置，构建遭遇时按当前装备筛选已解锁的牌。
                if (data.requiresEquippedCybernetic && !hasCybernetic) continue;
                ordinary.Add(new CardInstance(data));
            }
            var context = new BattleContext(run,rules,new[]{new CombatantState("enemy",enemyHealth)},
                new CardDeck(rules.OrdinaryCapacity,ordinary,random),
                new CardDeck(rules.CyberneticCapacity,run.Loadout.GetUsableCards(),random),
                phantomPain,random);
            return new BattleController(context,enemyAttackDamage);
        }
        /// <summary>
        /// 保存并输出入口错误，同时返回 false 供调用者判断。
        /// </summary>
        private bool Fail(string message)
        { LastError = message; Debug.LogError(message,this); return false; }
    }
}
