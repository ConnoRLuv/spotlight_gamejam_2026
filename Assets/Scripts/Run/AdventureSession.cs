using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace SpotlightGameJam
{
    /// <summary>跨场景持有一次探索及入口事件；出发只准备状态，地图流程负责决定后续场景。</summary>
    public sealed class AdventureSession : MonoBehaviour
    {
        public static AdventureSession Instance { get; private set; }
        public RunState Run { get; private set; }
        public AdventureSetupData Setup { get; private set; }
        public int EncounterIndex { get; private set; }
        public BattleController CurrentBattle { get; private set; }
        public string LastError { get; private set; }
        private CardData[] ordinary = Array.Empty<CardData>();
        private int seed;
        public event Action<RunState> ExplorationRequested;
        private void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            CurrentBattle?.Context.Dispose(); Instance = null;
        }
        /// <summary>关闭域重载时也清除上一次运行留下的静态引用。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
        /// <summary>取得唯一探索会话；没有会话的场景可以按需创建。</summary>
        public static AdventureSession GetOrCreate()
        {
            if (Instance) return Instance;
            Instance = new GameObject("AdventureSession").AddComponent<AdventureSession>();
            return Instance;
        }
        /// <summary>验证后创建新探索，发布入口事件；不会加载场景、抽牌或开始战斗。</summary>
        public bool TryBegin(AdventureSetupData setup, IEnumerable<CyberneticData> selection)
        {
            if (!setup) { LastError = "缺少探索配置。"; return false; }
            if (CurrentBattle != null && (!CurrentBattle.Context.IsFinished || CurrentBattle.Context.IsResolving))
            { LastError = "当前战斗尚未结束。"; return false; }
            var chosen = selection?.ToArray();
            var errors = setup.ValidateSelection(chosen);
            if (errors.Count > 0) { LastError = string.Join("\n", errors); return false; }
            var candidate = setup.CreateRun(chosen);
            CurrentBattle?.Context.Dispose(); CurrentBattle = null;
            Setup = setup; ordinary = setup.ordinaryDeck.ToArray(); seed = setup.randomSeed;
            Run = candidate; EncounterIndex = 0; LastError = null;
            ExplorationRequested?.Invoke(Run); return true;
        }
        /// <summary>由战斗场景请求下一遭遇；复用探索状态，只有成功开战才增加种子序号。</summary>
        public bool TryStartEncounter(int enemyHealth, int enemyDamage)
        {
            if (Run == null || !Setup) { LastError = "请先准备探索。"; return false; }
            if (CurrentBattle != null && (!CurrentBattle.Context.IsFinished || CurrentBattle.Context.IsResolving))
            { LastError = "当前战斗尚未结束。"; return false; }
            if (!Run.Player.IsAlive) { LastError = "玩家已经死亡。"; return false; }
            var errors = Setup.ValidateSelection(Array.Empty<CyberneticData>()).ToList();
            foreach (var source in Run.Loadout.Owned) errors.AddRange(source.Data.Validate());
            if (enemyHealth <= 0 || enemyDamage < 0) errors.Add("敌人属性配置不合法。");
            if (errors.Count > 0) { LastError = string.Join("\n", errors); return false; }
            var random = new RandomUtility(unchecked(seed + EncounterIndex));
            var basics = Run.ApplyOrdinaryTransformations(ordinary)
                .Where(card => !card.requiresEquippedCybernetic || Run.Loadout.Owned.Count > 0)
                .Select(card => new CardInstance(card));
            var context = new BattleContext(Run, Setup.rules, new[] { new CombatantState("enemy", enemyHealth) },
                new CardDeck(Setup.rules.OrdinaryCapacity, basics, random),
                new CardDeck(Setup.rules.CyberneticCapacity, Run.Loadout.GetUsableCards(), random, true),
                Setup.phantomPain, random);
            var battle = new BattleController(context, enemyDamage);
            if (!battle.StartBattle()) { context.Dispose(); LastError = battle.LastError; return false; }
            CurrentBattle?.Context.Dispose(); CurrentBattle = battle; EncounterIndex++; LastError = null;
            return true;
        }
        /// <summary>场景卸载只释放本场上下文，不清除探索牌组或同种义体的共享耐久。</summary>
        public void ReleaseEncounter(BattleController battle)
        {
            if (CurrentBattle != battle) return;
            CurrentBattle?.Context.Dispose(); CurrentBattle = null;
        }
    }
}
