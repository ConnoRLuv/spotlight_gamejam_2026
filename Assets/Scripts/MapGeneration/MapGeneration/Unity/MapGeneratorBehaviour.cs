using System;
using RoguelikeMap.Config;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Runtime;
using RoguelikeMap.Serialization;
using UnityEngine;

namespace RoguelikeMap.Unity
{
    /// <summary>
    /// 把生成器接进 Unity 生命周期的最小封装。
    ///
    /// 设计上它只做三件事：持有配置资产、调生成器、把结果暴露给别的系统。
    /// 战斗、奖励、UI 都通过事件或属性来拿数据，互相之间没有耦合。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapGeneratorBehaviour : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private MapGenerationConfigAsset _configAsset;
        [SerializeField] private bool _generateOnStart = true;

        [Header("种子")]
        [Tooltip("打开后用固定种子，方便复现问题；关掉则每次随机")]
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private int _fixedSeed = 12345;

        [Header("调试")]
        [SerializeField] private bool _logDiagnostics = true;
        [Tooltip("把地图以 ASCII 形式打到 Console，不开编辑器也能对")]
        [SerializeField] private bool _logAsciiMap;

        public MapGraph Graph { get; private set; }
        public MapRunState RunState { get; private set; }
        public NodeTypeCatalog Catalog { get; private set; }
        public MapGenerationResult LastResult { get; private set; }

        /// <summary>每次生成完成后触发，UI 与关卡流程挂在这里。</summary>
        public event Action<MapGenerationResult> Generated;

        MapGenerator _generator;

        public MapGenerationConfig Config =>
            _configAsset != null ? _configAsset.Config : MapGenerationConfig.CreateDefault();

        /// <summary>暴露出去让表现层能读到配色、图标等表现数据。</summary>
        public MapGenerationConfigAsset ConfigAsset => _configAsset;

        private void Awake()
        {
            if (_configAsset == null || !_configAsset.UsePreGeneratedMaps)
                _generator = _configAsset != null ? _configAsset.CreateGenerator() : MapGenerator.CreateLayered();
            Catalog = _configAsset != null
                ? _configAsset.BuildCatalog()
                : NodeTypeCatalog.CreateDefault();
        }

        private void Start()
        {
            if (_generateOnStart && Graph == null) Generate();
        }

        /// <summary>采用探索会话保存的同一地图状态，不重算地图、不重置进度；通知现有视图重新绑定。</summary>
        public void BindRun(MapRunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            Graph = run.Graph; RunState = run;
            if (Catalog == null) Catalog = _configAsset != null ? _configAsset.BuildCatalog() : NodeTypeCatalog.CreateDefault();
            LastResult = new MapGenerationResult(Graph, true, Array.Empty<string>());
            Generated?.Invoke(LastResult);
        }

        /// <summary>按 Inspector 的种子选项生成地图，独立地图演示仍可使用此入口。</summary>
        public MapGenerationResult Generate()
        {
            return Generate(_useFixedSeed ? unchecked((uint)_fixedSeed) : 0u);
        }

        /// <summary>离线池启用时只选择已有地图，0 随机，1..10 对应地图编号；不执行生成器。</summary>
        public MapGenerationResult Generate(uint seed)
        {
            if (_configAsset != null && _configAsset.UsePreGeneratedMaps)
                LastResult = _configAsset.LoadPreGeneratedMap(seed);
            else
            {
                if (_generator == null) _generator = _configAsset != null ? _configAsset.CreateGenerator() : MapGenerator.CreateLayered();
                LastResult = _generator.Generate(Config, Catalog, seed);
            }

            if (_logDiagnostics && LastResult.Diagnostics.Count > 0)
                Debug.Log("[地图生成]\n" + LastResult.DiagnosticsText);

            if (_logAsciiMap)
                Debug.Log(MapGraphDebug.ToAscii(Graph));

            if (!LastResult.Success)
            {
                Debug.LogError("[地图生成] 校验未通过，保留上一次合法地图。种子=" + seed + "\n" + LastResult.DiagnosticsText);
                return LastResult;
            }

            Graph = LastResult.Graph;
            RunState = new MapRunState(Graph);

            if (Generated != null) Generated(LastResult);
            return LastResult;
        }

        /// <summary>
        /// 用存档里的种子重建地图，并把进度恢复回去。
        /// 因为生成是确定性的，重建出来的地形和存档时完全一致。
        /// </summary>
        public bool TryRestore(MapRunSnapshot snapshot)
        {
            if (snapshot == null) return false;

            int currentHash = _configAsset != null ? _configAsset.GenerationHash() : Config.ComputeHash();
            if (snapshot.ConfigHash != 0 && snapshot.ConfigHash != currentHash)
            {
                Debug.LogWarning("[地图生成] 存档的地图配置与当前配置不一致，重建出来的地图会不一样。" +
                                 "如果这是一次平衡性改动，请让旧存档作废。");
            }

            // 新约束地图直接恢复原有布局与身份，不按新资产重新生成并重置探索进度。
            if (snapshot.Graph != null)
            {
                var restored = MapSerializer.FromDto(snapshot.Graph);
                var run = new MapRunState(restored);
                run.RestoreFrom(snapshot.CurrentNodeId, snapshot.VisitedNodeIds);
                BindRun(run); return true;
            }
            // 仅种子快照来自旧算法，必须沿旧管线恢复，不能把旧节点身份套到新资产拓扑。
            MapGenerationResult result = MapGenerator.CreateDefault().Generate(Config, Catalog, snapshot.Seed);
            if (!result.Success) return false;

            var legacyRun = new MapRunState(result.Graph);
            legacyRun.RestoreFrom(snapshot.CurrentNodeId, snapshot.VisitedNodeIds);
            BindRun(legacyRun);
            return true;
        }

        public MapRunSnapshot CaptureSnapshot()
        {
            if (Graph == null) return null;
            return MapSerializer.CaptureRun(Graph, RunState, _configAsset != null ? _configAsset.GenerationHash() : Config.ComputeHash());
        }
    }
}
