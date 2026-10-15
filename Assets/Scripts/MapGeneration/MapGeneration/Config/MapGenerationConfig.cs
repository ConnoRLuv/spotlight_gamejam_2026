using System;
using System.Collections.Generic;
using RoguelikeMap.Model;

namespace RoguelikeMap.Config
{
    /// <summary>
    /// 一种房间类型的权重规则。
    /// 权重 = BaseWeight + WeightPerRow * row，并且被 [MinRow, MaxRow] 硬性裁剪。
    [Serializable]
    public struct NodeTypeWeightEntry
    {
        public string TypeKey;
        public float BaseWeight;
        public float WeightPerRow;
        public int MinRow;

        /// <summary>小于 0 表示没有上限。</summary>
        public int MaxRow;

        public float Evaluate(int row)
        {
            if (row < MinRow) return 0f;
            if (MaxRow >= 0 && row > MaxRow) return 0f;
            float w = BaseWeight + WeightPerRow * row;
            return w < 0f ? 0f : w;
        }
    }

    /// <summary>把某一层固定成某个房间类型（例如第 1 层必是战斗、最后一层必是商人）。</summary>
    [Serializable]
    public struct FixedRowEntry
    {
        public int Row;
        public string TypeKey;
    }

    /// <summary>
    /// 地图生成的全部可调参数。这是一个纯数据类，可以直接被 ScriptableObject 序列化。
    /// 想加新参数就往这里加字段，并同步到 Unity 层的 Asset 里，生成流程不用改。
    /// </summary>
    [Serializable]
    public sealed class MapGenerationConfig
    {
        // ---------- 结构 ----------

        /// <summary>可玩层数（不含 Boss 层）。杀戮尖塔是 15。</summary>
        public int Rows = 15;

        /// <summary>横向格子数。杀戮尖塔是 7。</summary>
        public int Columns = 7;

        /// <summary>铺几条路径。路径会自然合并成 DAG，所以这个值越大分叉越多。</summary>
        public int PathCount = 6;

        /// <summary>是否在最后额外挂一个 Boss 节点。</summary>
        public bool IncludeBossNode = true;

        public string BossTypeKey = NodeTypeKeys.Boss;

        /// <summary>是否生成唯一的起点节点（第 0 层只有一个点，所有路径从它分叉出去）。</summary>
        public bool HasStartNode = true;

        /// <summary>起点节点的类型键。起点是"当前位置"标记，不是战斗内容。</summary>
        public string StartTypeKey = NodeTypeKeys.Start;

        /// <summary>单个节点的最大出边数。杀戮尖塔是 3。</summary>
        public int MaxOutDegree = 3;

        // ---------- 随机与重试 ----------

        /// <summary>0 表示每次生成都换一个随机种子。</summary>
        public uint BaseSeed = 0u;

        /// <summary>校验不通过时最多重生成几次。分层 DAG 生成很快，给大一点没关系。</summary>
        public int MaxGenerationAttempts = 64;

        // ---------- 房间类型 ----------

        public List<NodeTypeWeightEntry> Weights = new List<NodeTypeWeightEntry>(8);
        public List<FixedRowEntry> FixedRows = new List<FixedRowEntry>(4);

        /// <summary>
        /// 规则全部拒绝某个节点时的降级顺序。
        /// 例如精英被规则挡住，就退而求其次放战斗，再不行放事件。
        /// </summary>
        public List<string> FallbackOrder = new List<string>(4);

        // ---------- 校验门槛 ----------

        /// <summary>有效路径总数下限。低于这个值说明地图太单薄。</summary>
        public int MinPathCount = 8;

        /// <summary>有效路径总数上限。超过说明地图太乱，玩家无从选择。</summary>
        public int MaxPathCount = 4096;

        // ---------- 工厂方法 ----------

        /// <summary>
        /// 一套可以直接开跑的"杀戮尖塔风格"默认参数。
        /// 数值只是起点，拿去做手感测试后再调。
        /// </summary>
        public static MapGenerationConfig CreateDefault()
        {
            var cfg = new MapGenerationConfig();

            // 第 1 层是起点后的第一场战斗；最后一层固定商人，保证"商人必在 Boss 前"。
            // 第 0 层是起点节点，由铺路径步骤单独创建，不在这里固定。
            cfg.FixedRows.Add(new FixedRowEntry { Row = 1, TypeKey = NodeTypeKeys.Monster });
            cfg.FixedRows.Add(new FixedRowEntry { Row = 14, TypeKey = NodeTypeKeys.Shop });

            cfg.Weights.Add(new NodeTypeWeightEntry
            {
                TypeKey = NodeTypeKeys.Monster,
                BaseWeight = 45f, WeightPerRow = -0.6f, MinRow = 2, MaxRow = -1
            });
            cfg.Weights.Add(new NodeTypeWeightEntry
            {
                TypeKey = NodeTypeKeys.Event,
                BaseWeight = 22f, WeightPerRow = 0.3f, MinRow = 2, MaxRow = -1
            });
            cfg.Weights.Add(new NodeTypeWeightEntry
            {
                TypeKey = NodeTypeKeys.Elite,
                BaseWeight = 0f, WeightPerRow = 1.5f, MinRow = 5, MaxRow = -1
            });
            cfg.Weights.Add(new NodeTypeWeightEntry
            {
                TypeKey = NodeTypeKeys.Shop,
                BaseWeight = 4f, WeightPerRow = 0.25f, MinRow = 5, MaxRow = -1
            });

            cfg.FallbackOrder.Add(NodeTypeKeys.Monster);
            cfg.FallbackOrder.Add(NodeTypeKeys.Event);
            cfg.FallbackOrder.Add(NodeTypeKeys.Shop);

            return cfg;
        }

        public MapGenerationConfig Clone()
        {
            var copy = new MapGenerationConfig
            {
                Rows = Rows,
                Columns = Columns,
                PathCount = PathCount,
                IncludeBossNode = IncludeBossNode,
                BossTypeKey = BossTypeKey,
                HasStartNode = HasStartNode,
                StartTypeKey = StartTypeKey,
                MaxOutDegree = MaxOutDegree,
                BaseSeed = BaseSeed,
                MaxGenerationAttempts = MaxGenerationAttempts,
                MinPathCount = MinPathCount,
                MaxPathCount = MaxPathCount,
                Weights = new List<NodeTypeWeightEntry>(Weights),
                FixedRows = new List<FixedRowEntry>(FixedRows),
                FallbackOrder = new List<string>(FallbackOrder)
            };
            return copy;
        }

        /// <summary>
        /// 配置指纹。存档里带上它，读档时如果配置被改动过就能给出提示，
        /// 而不是静默地生成出一张完全不同的地图。
        /// </summary>
        public int ComputeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Rows;
                h = h * 31 + Columns;
                h = h * 31 + PathCount;
                h = h * 31 + (IncludeBossNode ? 1 : 0);
                h = h * 31 + (HasStartNode ? 1 : 0);
                h = h * 31 + MaxOutDegree;
                h = h * 31 + MinPathCount;
                h = h * 31 + MaxPathCount;
                h = h * 31 + (BossTypeKey != null ? BossTypeKey.GetHashCode() : 0);
                h = h * 31 + (StartTypeKey != null ? StartTypeKey.GetHashCode() : 0);

                for (int i = 0; i < FixedRows.Count; i++)
                {
                    h = h * 31 + FixedRows[i].Row;
                    string k = FixedRows[i].TypeKey;
                    h = h * 31 + (k != null ? k.GetHashCode() : 0);
                }
                for (int i = 0; i < Weights.Count; i++)
                {
                    NodeTypeWeightEntry w = Weights[i];
                    h = h * 31 + (w.TypeKey != null ? w.TypeKey.GetHashCode() : 0);
                    h = h * 31 + w.BaseWeight.GetHashCode();
                    h = h * 31 + w.WeightPerRow.GetHashCode();
                    h = h * 31 + w.MinRow;
                    h = h * 31 + w.MaxRow;
                }
                return h;
            }
        }
    }
}
