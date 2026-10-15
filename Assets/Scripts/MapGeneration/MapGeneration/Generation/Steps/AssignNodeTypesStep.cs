using System.Collections.Generic;
using RoguelikeMap.Core;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;

namespace RoguelikeMap.Generation.Steps
{
    /// <summary>
    /// 第 4 步：分配房间类型。
    ///
    /// 两个关键设计：
    ///
    /// 1. **自底向上分配**。因为边只从 Row 指向 Row+1，处理到第 r 层时，
    ///    所有父节点（第 r-1 层）的类型一定已经定了，规则可以放心读取。
    ///
    /// 2. **拒绝采样 + 逐个排除**。每轮按权重抽一个候选，被规则拒绝就把它记入黑名单，
    ///    下一轮从剩下的类型里再抽。这比"反复重掷同一个分布"收敛快得多。
    ///
    /// 想换成完全不同的策略（回溯搜索、图着色、神经网络），
    /// 只需要写一个新的 IMapGenerationStep 替换本步骤，规则集和校验器都不用动。
    /// </summary>
    public sealed class AssignNodeTypesStep : IMapGenerationStep
    {
        /// <summary>单个节点最多重掷几次，超过就降级。太小会让精英/商店缺失，太大浪费算力。</summary>
        public int MaxRerollsPerNode = 16;

        public string Name => "分配房间类型";

        public void Execute(MapGenerationContext context)
        {
            MapGraph graph = context.Graph;
            ApplyFixedRows(context);

            // 复用缓冲区，避免每个节点都分配两个 List 出来
            var weights = new List<float>(8);
            var keys = new List<string>(8);
            var rejected = new HashSet<string>();

            for (int row = 0; row < graph.Rows; row++)
            {
                foreach (MapNode node in graph.NodesInRow(row))
                {
                    if (node.IsAssigned) continue;
                    AssignOne(context, node, weights, keys, rejected);
                }
            }
        }

        static void ApplyFixedRows(MapGenerationContext context)
        {
            List<Config.FixedRowEntry> fixedRows = context.Config.FixedRows;
            for (int i = 0; i < fixedRows.Count; i++)
            {
                Config.FixedRowEntry entry = fixedRows[i];
                if (!context.Catalog.Contains(entry.TypeKey)) continue;

                foreach (MapNode node in context.Graph.NodesInRow(entry.Row))
                    node.TypeKey = entry.TypeKey;
            }
        }

        void AssignOne(MapGenerationContext context, MapNode node,
            List<float> weights, List<string> keys, HashSet<string> rejected)
        {
            rejected.Clear();

            for (int roll = 0; roll < MaxRerollsPerNode; roll++)
            {
                BuildCandidates(context, node, rejected, weights, keys);
                if (keys.Count == 0) break;

                int pick = context.Random.WeightedIndex(weights);
                if (pick < 0) break;

                string candidate = keys[pick];
                RuleVerdict verdict = context.Rules.Evaluate(node, candidate, context);
                if (verdict.Allowed)
                {
                    node.TypeKey = candidate;
                    return;
                }

                rejected.Add(candidate);
            }

            node.TypeKey = PickFallback(context, node);
        }

        static void BuildCandidates(MapGenerationContext context, MapNode node,
            HashSet<string> rejected, List<float> weights, List<string> keys)
        {
            weights.Clear();
            keys.Clear();

            List<Config.NodeTypeWeightEntry> table = context.Config.Weights;
            for (int i = 0; i < table.Count; i++)
            {
                Config.NodeTypeWeightEntry entry = table[i];
                if (rejected.Contains(entry.TypeKey)) continue;
                if (!context.Catalog.Contains(entry.TypeKey)) continue;   // 配置里写了不存在的类型：跳过而不是崩

                float weight = entry.Evaluate(node.Row);
                if (weight <= 0f) continue;

                weights.Add(weight);
                keys.Add(entry.TypeKey);
            }
        }

        /// <summary>规则全拒绝时的降级路径：按 FallbackOrder 找一个能过的，再不行退回任一合法类型。</summary>
        static string PickFallback(MapGenerationContext context, MapNode node)
        {
            List<string> order = context.Config.FallbackOrder;
            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                if (!context.Catalog.Contains(key)) continue;
                if (context.Rules.Evaluate(node, key, context).Allowed) return key;
            }

            List<NodeTypeDefinition> all = context.Catalog.AllSorted();
            for (int i = 0; i < all.Count; i++)
            {
                string key = all[i].Key;
                if (key == context.Config.BossTypeKey) continue;
                if (key == NodeTypeKeys.Unassigned) continue;
                return key;
            }
            return NodeTypeKeys.Monster;
        }
    }
}
