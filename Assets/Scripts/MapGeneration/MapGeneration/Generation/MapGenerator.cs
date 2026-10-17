using System;
using System.Collections.Generic;
using RoguelikeMap.Config;
using RoguelikeMap.Core;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;
using RoguelikeMap.Validation;

namespace RoguelikeMap.Generation
{
    /// <summary>生成结果。失败时 Graph 依然可用（是最后一次尝试的结果），但 Success = false。</summary>
    public sealed class MapGenerationResult
    {
        public readonly MapGraph Graph;
        public readonly bool Success;
        public readonly IReadOnlyList<string> Diagnostics;

        public MapGenerationResult(MapGraph graph, bool success, IReadOnlyList<string> diagnostics)
        {
            Graph = graph;
            Success = success;
            Diagnostics = diagnostics ?? new List<string>();
        }

        public string DiagnosticsText => Diagnostics.Count == 0 ? "" : string.Join("\n", Diagnostics);
    }

    /// <summary>
    /// 地图生成器 = 一串 Step + 一串 Validator。
    ///
    /// 执行顺序：
    ///   1. 按种子建一个空图
    ///   2. 依次跑所有 Step
    ///   3. 跑所有 Validator
    ///   4. 不通过就换一个派生种子重来，最多 MaxGenerationAttempts 次
    ///
    /// 重试是个关键的工程选择：与其写一堆复杂的局部修复逻辑，
    /// 不如让生成器快速重来。分层 DAG 的生成成本极低，重试几十次也就几毫秒。
    /// </summary>
    public sealed class MapGenerator
    {
        readonly List<IMapGenerationStep> _steps = new List<IMapGenerationStep>(8);
        readonly List<IMapValidator> _validators = new List<IMapValidator>(8);
        readonly MapRuleSet _rules;

        public MapGenerator(MapRuleSet rules)
        {
            _rules = rules ?? MapRuleSet.CreateDefault();
        }

        public IReadOnlyList<IMapGenerationStep> Steps => _steps;
        public IReadOnlyList<IMapValidator> Validators => _validators;
        public MapRuleSet Rules => _rules;

        public MapGenerator AddStep(IMapGenerationStep step)
        {
            if (step != null) _steps.Add(step);
            return this;
        }

        /// <summary>插到指定位置，用于"我想在某一步之前 / 之后插一个新步骤"。</summary>
        public MapGenerator InsertStep(int index, IMapGenerationStep step)
        {
            if (step == null) return this;
            if (index < 0) index = 0;
            if (index > _steps.Count) index = _steps.Count;
            _steps.Insert(index, step);
            return this;
        }

        public MapGenerator AddValidator(IMapValidator validator)
        {
            if (validator != null) _validators.Add(validator);
            return this;
        }

        /// <summary>杀戮尖塔风格的默认管线。想删步骤就自己 new MapGenerator 后逐个 Add。</summary>
        public static MapGenerator CreateDefault()
        {
            return new MapGenerator(MapRuleSet.CreateDefault())
                .AddStep(new Steps.CarvePathsStep(false))
                .AddStep(new Steps.RemoveCrossingsStep())
                .AddStep(new Steps.PruneDanglingNodesStep())
                .AddStep(new Steps.AssignNodeTypesStep())
                .AddStep(new Steps.AttachBossStep())
                .AddValidator(new ConnectivityValidator())
                .AddValidator(new RowOccupancyValidator())
                .AddValidator(new PathCountValidator())
                .AddValidator(new FixedRowValidator())
                .AddValidator(new RuleConsistencyValidator());
        }

        /// <summary>完整路径逐层铺设，生成时排除交叉；沿用房间规则与额外 Boss 层。</summary>
        public static MapGenerator CreateLayered()
        {
            return new MapGenerator(MapRuleSet.CreateDefault())
                .AddStep(new Steps.CarvePathsStep(true, true))
                .AddStep(new Steps.AssignNodeTypesStep(true))
                .AddStep(new Steps.AttachBossStep())
                .AddValidator(new LayeredTopologyValidator())
                .AddValidator(new ConnectivityValidator())
                .AddValidator(new RowOccupancyValidator())
                .AddValidator(new PathCountValidator())
                .AddValidator(new FixedRowValidator())
                .AddValidator(new RuleConsistencyValidator());
        }

        /// <summary>沿用完整路径生成；原图像素避让参与搜索，位置随地图保存，不由视图重新猜测。</summary>
        public static MapGenerator CreateLayered(MapRouteProfile profile, int[] columnGuide = null)
        {
            if (profile == null) return CreateLayered();
            return new MapGenerator(MapRuleSet.CreateDefault())
                .AddStep(new Steps.AssetConstrainedPathsStep(profile, true, columnGuide))
                .AddStep(new Steps.AssignNodeTypesStep(true))
                .AddValidator(new Steps.AssetLayoutValidator(profile))
                .AddValidator(new LayeredTopologyValidator())
                .AddValidator(new ConnectivityValidator())
                .AddValidator(new RowOccupancyValidator())
                .AddValidator(new PathCountValidator())
                .AddValidator(new FixedRowValidator())
                .AddValidator(new RuleConsistencyValidator());
        }

        /// <summary>资产约束管线：拓扑和布局共同生成，首领已包含在内，不再换边、补边或事后删路径。</summary>
        public static MapGenerator CreateAssetConstrained(MapRouteProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return new MapGenerator(MapRuleSet.CreateDefault())
                .AddStep(new Steps.AssetConstrainedPathsStep(profile))
                .AddStep(new Steps.AssignNodeTypesStep())
                .AddValidator(new Steps.AssetLayoutValidator(profile))
                .AddValidator(new ConnectivityValidator())
                .AddValidator(new RowOccupancyValidator())
                .AddValidator(new PathCountValidator())
                .AddValidator(new FixedRowValidator())
                .AddValidator(new RuleConsistencyValidator());
        }

        public MapGenerationResult Generate(MapGenerationConfig config, NodeTypeCatalog catalog)
        {
            return Generate(config, catalog, 0u);
        }

        /// <summary>
        /// seed 传 0 表示"随机"（依次回退到 config.BaseSeed，再回退到时间戳）。
        /// 传具体值则完全可复现，这是存档只存一个 uint 的基础。
        /// </summary>
        public MapGenerationResult Generate(MapGenerationConfig config, NodeTypeCatalog catalog, uint seed)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (catalog == null) catalog = NodeTypeCatalog.CreateDefault();

            uint baseSeed = ResolveSeed(config, seed);
            var diagnostics = new List<string>(8);
            int attempts = config.MaxGenerationAttempts < 1 ? 1 : config.MaxGenerationAttempts;

            MapGraph lastGraph = null;
            MapValidationReport lastReport = null;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var context = new MapGenerationContext
                {
                    Config = config,
                    Catalog = catalog,
                    Rules = _rules,
                    Random = new XorShiftRandom(DeriveSeed(baseSeed, attempt)),
                    Graph = new MapGraph
                    {
                        Rows = config.Rows < 1 ? 1 : config.Rows,
                        Columns = config.Columns < 1 ? 1 : config.Columns
                    }
                };

                string stepFailure = null;
                for (int i = 0; i < _steps.Count; i++)
                {
                    try
                    {
                        _steps[i].Execute(context);
                    }
                    catch (Exception ex)
                    {
                        stepFailure = "[步骤异常] " + _steps[i].Name + "：" + ex.Message;
                        context.Log(stepFailure);
                        break;
                    }
                }

                context.Graph.Seed = baseSeed;
                context.Graph.Attempt = attempt;

                MapValidationReport report = RunValidators(context);
                if (stepFailure != null) report.Add(stepFailure);
                lastGraph = context.Graph;
                lastReport = report;

                if (report.IsValid)
                {
                    diagnostics.Add("[成功] 第 " + (attempt + 1) + " 次尝试通过全部校验，种子 " + baseSeed);
                    for (int i = 0; i < context.Diagnostics.Count; i++) diagnostics.Add(context.Diagnostics[i]);
                    return new MapGenerationResult(lastGraph, true, diagnostics);
                }

                diagnostics.Add("[重试] 第 " + (attempt + 1) + " 次失败：" + report.Summarize());
            }

            diagnostics.Add("[警告] 连续 " + attempts + " 次都没通过校验，返回最后一次结果。请检查配置是否自相矛盾。");
            if (lastReport != null && !lastReport.IsValid)
                for (int i = 0; i < lastReport.Errors.Count; i++) diagnostics.Add("[未解决] " + lastReport.Errors[i]);

            return new MapGenerationResult(lastGraph, false, diagnostics);
        }

        MapValidationReport RunValidators(MapGenerationContext context)
        {
            var report = new MapValidationReport();
            for (int i = 0; i < _validators.Count; i++)
                _validators[i].Validate(context.Graph, context, report);
            return report;
        }

        static uint ResolveSeed(MapGenerationConfig config, uint seed)
        {
            if (seed != 0u) return seed;
            if (config.BaseSeed != 0u) return config.BaseSeed;
            unchecked
            {
                uint s = (uint)DateTime.UtcNow.Ticks;
                return s == 0u ? 1u : s;
            }
        }

        /// <summary>
        /// 把 (种子, 尝试次数) 混合成一个新种子。
        /// 直接写 seed + attempt 会让相邻尝试产生高度相关的随机序列，
        /// 所以必须做一次雪崩混合，这是很多"重试生成"实现容易忽略的坑。
        /// </summary>
        static uint DeriveSeed(uint seed, int attempt)
        {
            unchecked
            {
                uint x = seed + (uint)attempt * 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x == 0u ? 1u : x;
            }
        }
    }
}
