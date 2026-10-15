using System.Collections.Generic;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;

namespace RoguelikeMap.Validation
{
    /// <summary>校验结果。只有 Collect 到 0 条错误才算通过。</summary>
    public sealed class MapValidationReport
    {
        public readonly List<string> Errors = new List<string>(4);

        public bool IsValid => Errors.Count == 0;

        public void Add(string error)
        {
            if (!string.IsNullOrEmpty(error)) Errors.Add(error);
        }

        public string Summarize()
        {
            if (Errors.Count == 0) return "通过";
            if (Errors.Count == 1) return Errors[0];
            return Errors[0] + " 等 " + Errors.Count + " 项";
        }
    }

    /// <summary>
    /// 【扩展点】想加新的地图体检项，实现这个接口然后 AddValidator 即可。
    /// 校验器可以决定"什么算合格"，生成器本身不关心。
    /// </summary>
    public interface IMapValidator
    {
        string Name { get; }
        void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report);
    }

    /// <summary>每个节点既要能从起点走到，也要能走到终点。</summary>
    public sealed class ConnectivityValidator : IMapValidator
    {
        public string Name => "连通性";

        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        {
            var starts = new List<int>();
            foreach (MapNode n in graph.NodesInRow(0)) starts.Add(n.Id);
            var ends = new List<int>();
            foreach (MapNode n in graph.NodesInRow(graph.Rows - 1)) ends.Add(n.Id);

            HashSet<int> forward = graph.CollectReachableForward(starts);
            HashSet<int> backward = graph.CollectReachableBackward(ends);

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode node = graph.Nodes[i];
                if (!forward.Contains(node.Id))
                    report.Add("节点 #" + node.Id + new GridCoord(node.Row, node.Column) + " 从起点不可达");
                else if (!backward.Contains(node.Id))
                    report.Add("节点 #" + node.Id + new GridCoord(node.Row, node.Column) + " 无法通向终点");
            }
        }
    }

    /// <summary>每一层都要有节点；每个中间节点都要有进有出；出度不能超过上限。</summary>
    public sealed class RowOccupancyValidator : IMapValidator
    {
        public string Name => "层次完整性";

        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        {
            int maxOut = context != null && context.Config != null ? context.Config.MaxOutDegree : 3;

            for (int r = 0; r < graph.Rows; r++)
            {
                bool any = false;
                foreach (MapNode node in graph.NodesInRow(r))
                {
                    any = true;

                    if (r < graph.Rows - 1 && node.NextIds.Count == 0)
                        report.Add("节点 #" + node.Id + " 在第 " + r + " 层没有出口");
                    if (r > 0 && node.PrevIds.Count == 0)
                        report.Add("节点 #" + node.Id + " 在第 " + r + " 层没有入口");
                    if (node.NextIds.Count > maxOut)
                        report.Add("节点 #" + node.Id + " 出度 " + node.NextIds.Count + " 超过上限 " + maxOut);

                    for (int i = 0; i < node.NextIds.Count; i++)
                    {
                        MapNode to = graph.Get(node.NextIds[i]);
                        if (to == null)
                        {
                            report.Add("节点 #" + node.Id + " 指向了不存在的 #" + node.NextIds[i]);
                        }
                        else if (to.Row != node.Row + 1)
                        {
                            report.Add("节点 #" + node.Id + " 的边跨层：指向第 " + to.Row + " 层");
                        }
                    }
                }

                if (!any) report.Add("第 " + r + " 层没有任何节点");
            }
        }
    }

    /// <summary>把规则集在最终结果上再跑一遍，确认没有漏网的违规。</summary>
    public sealed class RuleConsistencyValidator : IMapValidator
    {
        public string Name => "规则一致性";

        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        {
            if (context == null || context.Rules == null) return;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode node = graph.Nodes[i];
                if (!node.IsAssigned) continue;

                RuleVerdict verdict = context.Rules.Evaluate(node, node.TypeKey, context);
                if (!verdict.Allowed)
                    report.Add("节点 #" + node.Id + " 违反规则：" + verdict.Reason);
            }
        }
    }

    /// <summary>路径总数要落在一个合理区间内。</summary>
    public sealed class PathCountValidator : IMapValidator
    {
        public string Name => "路径数量";

        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        {
            if (context == null || context.Config == null) return;

            int cap = context.Config.MaxPathCount + 1;
            long count = graph.CountPaths(cap);

            if (count < context.Config.MinPathCount)
                report.Add("有效路径只有 " + count + " 条，少于下限 " + context.Config.MinPathCount);
            else if (count > context.Config.MaxPathCount)
                report.Add("有效路径超过 " + context.Config.MaxPathCount + " 条，地图过于破碎");
        }
    }

    /// <summary>固定层必须真的是指定的类型。</summary>
    public sealed class FixedRowValidator : IMapValidator
    {
        public string Name => "固定层";

        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        {
            if (context == null || context.Config == null) return;

            List<Config.FixedRowEntry> fixedRows = context.Config.FixedRows;
            for (int i = 0; i < fixedRows.Count; i++)
            {
                Config.FixedRowEntry entry = fixedRows[i];
                foreach (MapNode node in graph.NodesInRow(entry.Row))
                {
                    if (node.TypeKey != entry.TypeKey)
                        report.Add("第 " + entry.Row + " 层应当是 " + entry.TypeKey +
                                   "，但节点 #" + node.Id + " 是 " + node.TypeKey);
                }
            }
        }
    }
}
