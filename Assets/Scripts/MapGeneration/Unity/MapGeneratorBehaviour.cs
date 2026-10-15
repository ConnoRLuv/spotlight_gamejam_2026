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
            _generator = MapGenerator.CreateDefault();
            Catalog = _configAsset != null
                ? _configAsset.BuildCatalog()
                : NodeTypeCatalog.CreateDefault();
        }

        private void Start()
        {
            if (_generateOnStart) Generate();
        }

        public MapGenerationResult Generate()
        {
            return Generate(_useFixedSeed ? unchecked((uint)_fixedSeed) : 0u);
        }

        /// <summary>seed 传 0 表示随机。</summary>
        public MapGenerationResult Generate(uint seed)
        {
            LastResult = _generator.Generate(Config, Catalog, seed);
            Graph = LastResult.Graph;
            RunState = new MapRunState(Graph);

            if (_logDiagnostics && LastResult.Diagnostics.Count > 0)
                Debug.Log("[地图生成]\n" + LastResult.DiagnosticsText);

            if (_logAsciiMap)
                Debug.Log(MapGraphDebug.ToAscii(Graph));

            if (!LastResult.Success)
            {
                Debug.LogError("[地图生成] 校验未通过，请检查配置是否自相矛盾。" +
                               "常见原因是规则互相冲突导致总有节点找不到合法类型。");
            }

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

            int currentHash = Config.ComputeHash();
            if (snapshot.ConfigHash != 0 && snapshot.ConfigHash != currentHash)
            {
                Debug.LogWarning("[地图生成] 存档的地图配置与当前配置不一致，重建出来的地图会不一样。" +
                                 "如果这是一次平衡性改动，请让旧存档作废。");
            }

            MapGenerationResult result = Generate(snapshot.Seed);
            if (!result.Success) return false;

            RunState.RestoreFrom(snapshot.CurrentNodeId, snapshot.VisitedNodeIds);
            return true;
        }

        public MapRunSnapshot CaptureSnapshot()
        {
            if (Graph == null) return null;
            return MapSerializer.CaptureRun(Graph, RunState, Config.ComputeHash());
        }
    }
}
