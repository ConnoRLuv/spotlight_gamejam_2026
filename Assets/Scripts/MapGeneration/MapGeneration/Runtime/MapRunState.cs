using System;
using System.Collections.Generic;
using RoguelikeMap.Model;

namespace RoguelikeMap.Runtime
{
    /// <summary>
    /// 一局游戏里"玩家在地图上的进度"。
    ///
    /// 刻意和 MapGraph 分开：Graph 是这一局不变的地形，RunState 是会随进度变化的状态。
    /// 这样存档可以只存 RunState（很小），地图本身用种子重算出来。
    /// </summary>
    public sealed class MapRunState
    {
        /// <summary>还没进入地图时的当前节点 Id。</summary>
        public const int NoNode = -1;

        public MapGraph Graph { get; private set; }
        public int CurrentNodeId { get; private set; }

        readonly HashSet<int> _visited = new HashSet<int>();
        readonly List<MapNode> _optionBuffer = new List<MapNode>(4);

        /// <summary>进入某个节点时触发。UI、战斗系统、奖励系统都挂在这里。</summary>
        public event Action<MapNode> NodeEntered;

        /// <summary>走到终点（Boss）时触发。</summary>
        public event Action<MapRunState> RunCompleted;

        public MapRunState(MapGraph graph)
        {
            Graph = graph ?? throw new ArgumentNullException("graph");
            Reset();
        }

        public IReadOnlyCollection<int> VisitedNodeIds => _visited;
        public int Depth => _visited.Count;
        public bool HasStarted => CurrentNodeId != NoNode;
        public bool IsCompleted { get; private set; }

        public bool HasVisited(int nodeId) => _visited.Contains(nodeId);

        public MapNode CurrentNode => Graph.Get(CurrentNodeId);

        /// <summary>回到起点。开始新一局、或者重算地图后调用。</summary>
        public void Reset(MapGraph graph = null)
        {
            if (graph != null) Graph = graph;
            CurrentNodeId = NoNode;
            _visited.Clear();
            IsCompleted = false;

            // 有起点节点时，玩家一开始就站在起点上，并把它记为已访问。
            // 这样第一次 GetOptions() 直接返回起点的分支，而不是让玩家先"点进"起点。
            if (Graph != null && Graph.StartNode != null)
            {
                CurrentNodeId = Graph.StartNode.Id;
                _visited.Add(Graph.StartNode.Id);
            }
        }

        /// <summary>
        /// 当前可以去的节点。
        /// 还没出发时返回第 0 层所有节点；否则返回当前节点的后继。
        /// 返回的是内部复用列表，调用方不要长期持有 —— 需要缓存请自己 Copy。
        /// </summary>
        public IReadOnlyList<MapNode> GetOptions()
        {
            _optionBuffer.Clear();

            if (IsCompleted) return _optionBuffer;

            if (CurrentNodeId == NoNode)
            {
                foreach (MapNode node in Graph.NodesInRow(0)) _optionBuffer.Add(node);
                return _optionBuffer;
            }

            MapNode current = Graph.Get(CurrentNodeId);
            if (current == null) return _optionBuffer;

            for (int i = 0; i < current.NextIds.Count; i++)
            {
                MapNode next = Graph.Get(current.NextIds[i]);
                if (next != null) _optionBuffer.Add(next);
            }
            return _optionBuffer;
        }

        public bool CanMoveTo(int nodeId)
        {
            IReadOnlyList<MapNode> options = GetOptions();
            for (int i = 0; i < options.Count; i++)
                if (options[i].Id == nodeId) return true;
            return false;
        }

        /// <summary>移动到指定节点。非法移动返回 false 且不改变任何状态。</summary>
        public bool MoveTo(int nodeId)
        {
            if (!CanMoveTo(nodeId)) return false;

            CurrentNodeId = nodeId;
            _visited.Add(nodeId);

            MapNode node = Graph.Get(nodeId);
            NodeEntered?.Invoke(node);

            if (node != null && node.NextIds.Count == 0)
            {
                IsCompleted = true;
                RunCompleted?.Invoke(this);
            }
            return true;
        }

        /// <summary>从存档恢复：直接指定当前节点和已访问集合，不重新触发事件。</summary>
        public void RestoreFrom(int currentNodeId, IEnumerable<int> visitedNodeIds)
        {
            CurrentNodeId = currentNodeId;
            _visited.Clear();
            if (visitedNodeIds != null)
                foreach (int id in visitedNodeIds) _visited.Add(id);

            MapNode node = Graph.Get(currentNodeId);
            IsCompleted = node != null && node.NextIds.Count == 0;
        }

        /// <summary>按类型统计已经走过的节点，用于结算界面或成就系统。</summary>
        public Dictionary<string, int> CountVisitedByType()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int id in _visited)
            {
                MapNode node = Graph.Get(id);
                if (node == null) continue;
                int c;
                counts[node.TypeKey] = counts.TryGetValue(node.TypeKey, out c) ? c + 1 : 1;
            }
            return counts;
        }
    }
}
