using System;
using System.Collections.Generic;

namespace RoguelikeMap.Model
{
    /// <summary>网格坐标。Row = 0 是最底层（起点），Column 从左到右。</summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int Row;
        public readonly int Column;

        public GridCoord(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public bool Equals(GridCoord other) => Row == other.Row && Column == other.Column;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => (Row * 397) ^ Column;
        public override string ToString() => "(" + Row + "," + Column + ")";
    }

    /// <summary>
    /// 地图节点。注意：节点之间用 **Id 整数**互相引用，而不是对象引用。
    /// 这样序列化、存档、网络同步都只是 int 数组，不用处理引用环。
    /// </summary>
    public sealed class MapNode
    {
        public int Id;
        public int Row;
        public int Column;

        /// <summary>类型键，由 NodeTypeCatalog 解释。生成过程中可能是 NodeTypeKeys.Unassigned。</summary>
        public string TypeKey;

        /// <summary>后继节点 Id（只在 Row+1）。</summary>
        public readonly List<int> NextIds = new List<int>(3);

        /// <summary>前驱节点 Id（只在 Row-1）。由 RebuildAdjacency / AddEdge 维护。</summary>
        public readonly List<int> PrevIds = new List<int>(3);

        /// <summary>
        /// 上层自由挂载的数据：事件内容、商店库存、战斗奖励快照等。
        /// 生成逻辑完全不碰这个字段，所以扩展内容时不用改生成器。
        /// </summary>
        public object Payload;

        /// <summary>
        /// 跳过规则校验。用于"不是通过加权分配产生"的节点（例如最后挂上去的 Boss），
        /// 避免它被房间类型的随机分配规则误伤。
        /// </summary>
        public bool IsExemptFromRules;

        public bool IsAssigned =>
            !string.IsNullOrEmpty(TypeKey) && TypeKey != NodeTypeKeys.Unassigned;

        public override string ToString() => "#" + Id + " " + new GridCoord(Row, Column) + " " + TypeKey;
    }

    /// <summary>
    /// 分层有向无环图。边只从 Row 指向 Row+1。
    /// 这是一个纯数据结构，不依赖 UnityEngine，可以在单元测试里直接构造。
    /// </summary>
    public sealed class MapGraph
    {
        public int Rows;
        public int Columns;
        public uint Seed;

        /// <summary>第几次尝试生成成功，仅用于调试与复现。</summary>
        public int Attempt;

        /// <summary>可选资产约束布局；旧地图保留 null，显示层继续使用原先网格。</summary>
        public MapAssetLayout AssetLayout;

        /// <summary>
        /// 起点节点 Id。单一起点模式下由铺路径步骤设置，否则保持 -1。
        /// 玩家一开始就站在这个节点上，从这里向上一层层分叉。
        /// </summary>
        public int StartNodeId = -1;

        readonly List<MapNode> _nodes = new List<MapNode>(64);
        readonly Dictionary<int, MapNode> _byId = new Dictionary<int, MapNode>(64);
        readonly Dictionary<GridCoord, int> _byCoord = new Dictionary<GridCoord, int>(64);
        int _nextId;

        public IReadOnlyList<MapNode> Nodes => _nodes;
        public int NodeCount => _nodes.Count;

        public MapNode StartNode => Get(StartNodeId);

        public MapNode Get(int id)
        {
            if (id < 0) return null;
            MapNode node;
            return _byId.TryGetValue(id, out node) ? node : null;
        }

        public MapNode GetAt(int row, int column)
        {
            int id;
            return _byCoord.TryGetValue(new GridCoord(row, column), out id) ? _byId[id] : null;
        }

        public bool HasNodeAt(int row, int column)
        {
            return _byCoord.ContainsKey(new GridCoord(row, column));
        }

        public MapNode GetOrCreate(int row, int column, string typeKey = NodeTypeKeys.Unassigned)
        {
            var coord = new GridCoord(row, column);
            int existingId;
            if (_byCoord.TryGetValue(coord, out existingId)) return _byId[existingId];

            var node = new MapNode
            {
                Id = _nextId++,
                Row = row,
                Column = column,
                TypeKey = typeKey
            };
            _nodes.Add(node);
            _byId[node.Id] = node;
            _byCoord[coord] = node.Id;
            return node;
        }

        public MapNode AddNode(int row, int column, string typeKey)
        {
            if (HasNodeAt(row, column))
                throw new InvalidOperationException("格子 " + row + "," + column + " 已经有节点了");
            return GetOrCreate(row, column, typeKey);
        }

        /// <summary>恢复原始身份，兼容删除过节点后出现的不连续 Id，避免探索进度指向错误节点。</summary>
        internal MapNode AddRestoredNode(int id, int row, int column, string typeKey)
        {
            if (id < 0 || _byId.ContainsKey(id) || HasNodeAt(row, column)) throw new InvalidOperationException("存档节点身份或坐标重复。");
            var node = GetOrCreate(row, column, typeKey);
            _byId.Remove(node.Id); node.Id = id; _byId[id] = node; _byCoord[new GridCoord(row, column)] = id;
            _nextId = Math.Max(_nextId, id + 1);
            return node;
        }

        public void AddEdge(int fromId, int toId)
        {
            if (fromId == toId) return;
            MapNode from = Get(fromId);
            MapNode to = Get(toId);
            if (from == null || to == null) return;
            if (from.NextIds.Contains(toId)) return;

            from.NextIds.Add(toId);
            to.PrevIds.Add(fromId);
        }

        public bool HasEdge(int fromId, int toId)
        {
            MapNode from = Get(fromId);
            return from != null && from.NextIds.Contains(toId);
        }

        public void RemoveEdge(int fromId, int toId)
        {
            MapNode from = Get(fromId);
            MapNode to = Get(toId);
            if (from != null) from.NextIds.Remove(toId);
            if (to != null) to.PrevIds.Remove(fromId);
        }

        /// <summary>整体替换某个节点的后继集合，并保持 PrevIds 的对称性。</summary>
        public void SetOutgoing(int fromId, IList<int> targets)
        {
            MapNode from = Get(fromId);
            if (from == null) return;

            for (int i = 0; i < from.NextIds.Count; i++)
            {
                MapNode to = Get(from.NextIds[i]);
                if (to != null) to.PrevIds.Remove(fromId);
            }
            from.NextIds.Clear();

            if (targets == null) return;
            for (int i = 0; i < targets.Count; i++) AddEdge(fromId, targets[i]);
        }

        /// <summary>
        /// 当外部直接改动过 NextIds（例如消除交叉线时交换目标）之后调用，重建全部 PrevIds。
        /// </summary>
        public void RebuildAdjacency()
        {
            for (int i = 0; i < _nodes.Count; i++) _nodes[i].PrevIds.Clear();
            for (int i = 0; i < _nodes.Count; i++)
            {
                MapNode node = _nodes[i];
                for (int j = 0; j < node.NextIds.Count; j++)
                {
                    MapNode to = Get(node.NextIds[j]);
                    if (to != null) to.PrevIds.Add(node.Id);
                }
            }
        }

        public bool RemoveNode(int id)
        {
            MapNode node = Get(id);
            if (node == null) return false;

            for (int i = 0; i < node.NextIds.Count; i++)
            {
                MapNode to = Get(node.NextIds[i]);
                if (to != null) to.PrevIds.Remove(id);
            }
            for (int i = 0; i < node.PrevIds.Count; i++)
            {
                MapNode from = Get(node.PrevIds[i]);
                if (from != null) from.NextIds.Remove(id);
            }

            _nodes.Remove(node);
            _byId.Remove(id);
            _byCoord.Remove(new GridCoord(node.Row, node.Column));
            return true;
        }

        public IEnumerable<MapNode> NodesInRow(int row)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].Row == row) yield return _nodes[i];
        }

        /// <summary>只沿 NextIds 方向做一次 BFS，返回可达节点集合。</summary>
        public HashSet<int> CollectReachableForward(IEnumerable<int> startIds)
        {
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (int id in startIds)
                if (visited.Add(id)) stack.Push(id);

            while (stack.Count > 0)
            {
                MapNode node = Get(stack.Pop());
                if (node == null) continue;
                for (int i = 0; i < node.NextIds.Count; i++)
                    if (visited.Add(node.NextIds[i])) stack.Push(node.NextIds[i]);
            }
            return visited;
        }

        /// <summary>只沿 PrevIds 方向做一次 BFS。</summary>
        public HashSet<int> CollectReachableBackward(IEnumerable<int> startIds)
        {
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (int id in startIds)
                if (visited.Add(id)) stack.Push(id);

            while (stack.Count > 0)
            {
                MapNode node = Get(stack.Pop());
                if (node == null) continue;
                for (int i = 0; i < node.PrevIds.Count; i++)
                    if (visited.Add(node.PrevIds[i])) stack.Push(node.PrevIds[i]);
            }
            return visited;
        }

        /// <summary>
        /// 数一下从起点层到终点层有多少条不同路径（DP，超上限就截断）。
        /// 用于校验"路径数不会少得可怜"，比枚举所有路径快得多。
        /// </summary>
        public long CountPaths(int cap)
        {
            var ways = new Dictionary<int, long>(_nodes.Count);

            for (int r = Rows - 1; r >= 0; r--)
            {
                foreach (MapNode node in NodesInRow(r))
                {
                    if (node.NextIds.Count == 0)
                    {
                        ways[node.Id] = 1;
                        continue;
                    }

                    long sum = 0;
                    for (int i = 0; i < node.NextIds.Count; i++)
                    {
                        long w;
                        sum += ways.TryGetValue(node.NextIds[i], out w) ? w : 0;
                        if (sum >= cap) { sum = cap; break; }
                    }
                    ways[node.Id] = sum;
                }
            }

            long total = 0;
            foreach (MapNode node in NodesInRow(0))
            {
                long w;
                total += ways.TryGetValue(node.Id, out w) ? w : 0;
                if (total >= cap) return cap;
            }
            return total;
        }
    }
}
