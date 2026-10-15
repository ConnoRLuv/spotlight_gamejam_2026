using System;
using System.Collections.Generic;
using RoguelikeMap.Core;
using RoguelikeMap.Model;

namespace RoguelikeMap.Generation.Steps
{
    /// <summary>
    /// 第 1 步：铺路径。
    ///
    /// 有两种模式：
    ///   · HasStartNode = true（默认）：在第 0 层中央放一个唯一的起点节点，
    ///     所有路径都从它出发，向上一层一格（列偏移 -1 / 0 / +1）随机游走。
    ///   · HasStartNode = false：维持"底层多入口"的老行为，起点在横向均匀铺开。
    ///
    /// 走过的格子成为节点，相邻两步之间成为一条边。多条路径在中间自然交汇，
    /// 于是自动形成带分支和合流的分层 DAG。
    ///
    /// 这个走法有三个免费的好处：
    ///   · 每个非底层节点必有入边
    ///   · 每个非顶层节点必有出边
    ///   · 出度天然 ≤ 3（因为只能走列 ±1）
    /// 所以不需要额外的连通性修复。
    /// </summary>
    public sealed class CarvePathsStep : IMapGenerationStep
    {
        public string Name => "铺路径";

        public void Execute(MapGenerationContext context)
        {
            MapGraph graph = context.Graph;
            int rows = graph.Rows;
            int columns = graph.Columns;
            int pathCount = context.Config.PathCount < 1 ? 1 : context.Config.PathCount;

            if (context.Config.HasStartNode)
            {
                int startColumn = columns / 2;
                MapNode start = graph.GetOrCreate(0, startColumn, context.Config.StartTypeKey);
                start.IsExemptFromRules = true;
                graph.StartNodeId = start.Id;

                for (int p = 0; p < pathCount; p++)
                    CarveOne(graph, context.Random, startColumn, rows, columns);

                context.Log("起点节点 #" + start.Id + "，铺出 " + pathCount + " 条路径，节点数 " + graph.NodeCount);
            }
            else
            {
                List<int> starts = BuildStartColumns(context, pathCount);
                for (int p = 0; p < starts.Count; p++)
                    CarveOne(graph, context.Random, starts[p], rows, columns);

                context.Log("铺出 " + starts.Count + " 条路径，节点数 " + graph.NodeCount);
            }
        }

        /// <summary>从 startColumn 出发，向上随机游走到顶层。</summary>
        static void CarveOne(MapGraph graph, IRandomSource rng, int startColumn, int rows, int columns)
        {
            int column = startColumn;
            for (int row = 0; row < rows - 1; row++)
            {
                int next = column + rng.Range(-1, 2);
                if (next < 0) next = 0;
                if (next > columns - 1) next = columns - 1;

                MapNode from = graph.GetOrCreate(row, column);
                MapNode to = graph.GetOrCreate(row + 1, next);
                graph.AddEdge(from.Id, to.Id);

                column = next;
            }
        }

        static List<int> BuildStartColumns(MapGenerationContext context, int pathCount)
        {
            int columns = context.Graph.Columns;
            var starts = new List<int>(pathCount);

            // 起点在横向均匀铺开后加一点抖动
            for (int i = 0; i < pathCount; i++)
            {
                float t = pathCount == 1 ? 0.5f : i / (float)(pathCount - 1);
                int baseColumn = (int)Math.Round(t * (columns - 1));
                int column = baseColumn + context.Random.Range(-1, 2);
                if (column < 0) column = 0;
                if (column > columns - 1) column = columns - 1;
                starts.Add(column);
            }
            return starts;
        }
    }

    /// <summary>
    /// 第 2 步：消除交叉连线。
    ///
    /// 随机游走会产生 X 形交叉。判定方式是：左列节点连到了更右的目标，
    /// 而右列节点连到了更左的目标。修法是把两个目标对调。
    ///
    /// 关键点是：对调只改变"谁连谁"，不改变任何节点的出入度数，
    /// 所以不会凭空制造孤岛。整个过程的本质是对目标列序列做冒泡排序，必然收敛。
    /// </summary>
    public sealed class RemoveCrossingsStep : IMapGenerationStep
    {
        public string Name => "消除交叉连线";

        public void Execute(MapGenerationContext context)
        {
            MapGraph graph = context.Graph;
            int guard = 0;
            int maxPasses = graph.Rows * graph.Columns * graph.Columns + 16;
            int swaps = 0;
            bool changed = true;

            while (changed && guard++ < maxPasses)
            {
                changed = false;

                for (int row = 0; row < graph.Rows - 1; row++)
                {
                    for (int left = 0; left < graph.Columns; left++)
                    {
                        MapNode leftNode = graph.GetAt(row, left);
                        if (leftNode == null || leftNode.NextIds.Count == 0) continue;

                        for (int right = left + 1; right < graph.Columns; right++)
                        {
                            MapNode rightNode = graph.GetAt(row, right);
                            if (rightNode == null || rightNode.NextIds.Count == 0) continue;

                            for (int i = 0; i < leftNode.NextIds.Count; i++)
                            {
                                for (int j = 0; j < rightNode.NextIds.Count; j++)
                                {
                                    MapNode toLeft = graph.Get(leftNode.NextIds[i]);
                                    MapNode toRight = graph.Get(rightNode.NextIds[j]);
                                    if (toLeft == null || toRight == null) continue;
                                    if (toLeft.Column <= toRight.Column) continue;

                                    int tmp = leftNode.NextIds[i];
                                    leftNode.NextIds[i] = rightNode.NextIds[j];
                                    rightNode.NextIds[j] = tmp;

                                    swaps++;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }

            DeduplicateEdges(graph);
            graph.RebuildAdjacency();

            context.Log("消除交叉连线：交换 " + swaps + " 次");
        }

        static void DeduplicateEdges(MapGraph graph)
        {
            var seen = new HashSet<int>();
            for (int n = 0; n < graph.Nodes.Count; n++)
            {
                MapNode node = graph.Nodes[n];
                seen.Clear();
                for (int i = node.NextIds.Count - 1; i >= 0; i--)
                    if (!seen.Add(node.NextIds[i])) node.NextIds.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 第 3 步：清理悬空节点。
    /// 标准走法不会产生悬空节点，这一步是防御性的 —— 万一你自定义了 CarvePathsStep，
    /// 或者加了会删边的修饰步骤，它能保证后面的步骤拿到的是干净图。
    /// </summary>
    public sealed class PruneDanglingNodesStep : IMapGenerationStep
    {
        public string Name => "清理悬空节点";

        public void Execute(MapGenerationContext context)
        {
            MapGraph graph = context.Graph;
            int removed = 0;
            bool changed = true;
            int guard = 0;

            while (changed && guard++ < 32)
            {
                changed = false;
                for (int i = graph.Nodes.Count - 1; i >= 0; i--)
                {
                    MapNode node = graph.Nodes[i];
                    bool needsEntry = node.Row > 0;
                    bool needsExit = node.Row < graph.Rows - 1;
                    bool bad = (needsEntry && node.PrevIds.Count == 0)
                               || (needsExit && node.NextIds.Count == 0);
                    if (!bad) continue;

                    graph.RemoveNode(node.Id);
                    removed++;
                    changed = true;
                }
            }

            if (removed > 0) context.Log("清理悬空节点 " + removed + " 个");
        }
    }

    /// <summary>
    /// 第 5 步：挂载 Boss 节点。
    /// 把层数 +1 交给 Boss，并让原来最顶层的所有节点都指向它（顶层通常是一排商人）。
    /// Boss 标记为 IsExemptFromRules，不参与房间类型的随机分配。
    /// </summary>
    public sealed class AttachBossStep : IMapGenerationStep
    {
        public string Name => "挂载 Boss";

        public void Execute(MapGenerationContext context)
        {
            MapGraph graph = context.Graph;
            if (!context.Config.IncludeBossNode) return;

            int bossRow = graph.Rows;
            int bossColumn = graph.Columns / 2;

            graph.Rows = bossRow + 1;

            MapNode boss = graph.GetOrCreate(bossRow, bossColumn, context.Config.BossTypeKey);
            boss.IsExemptFromRules = true;

            var entryNodes = new List<MapNode>();
            foreach (MapNode node in graph.NodesInRow(bossRow - 1)) entryNodes.Add(node);

            for (int i = 0; i < entryNodes.Count; i++)
                graph.AddEdge(entryNodes[i].Id, boss.Id);

            context.Log("挂载 Boss：第 " + bossRow + " 层，入口 " + entryNodes.Count + " 个");
        }
    }
}
