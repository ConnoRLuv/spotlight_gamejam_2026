using System;
using System.Collections.Generic;
using RoguelikeMap.Model;
using RoguelikeMap.Runtime;

namespace RoguelikeMap.Serialization
{
    [Serializable]
    public sealed class MapNodeDto
    {
        public int Id;
        public int Row;
        public int Column;
        public string TypeKey;
        public List<int> NextIds = new List<int>();
    }

    /// <summary>地图的完整快照。字段全部是 List / 基础类型，所以 Unity 的 JsonUtility 能直接吃。</summary>
    [Serializable]
    public sealed class MapGraphDto
    {
        public int Rows;
        public int Columns;
        public uint Seed;
        public int Attempt;
        public int ConfigHash;
        public List<MapNodeDto> Nodes = new List<MapNodeDto>();
    }

    /// <summary>
    /// 精简存档：只存种子 + 进度。
    /// 读档时用同一个种子重新生成地图即可得到完全一致的地形，
    /// 所以这是默认推荐方案 —— 存档体积小到可以忽略。
    /// ConfigHash 用来检测"配置被改过导致地图对不上"。
    /// </summary>
    [Serializable]
    public sealed class MapRunSnapshot
    {
        public uint Seed;
        public int Attempt;
        public int ConfigHash;
        public int CurrentNodeId = MapRunState.NoNode;
        public List<int> VisitedNodeIds = new List<int>();
    }

    /// <summary>
    /// 地图与存档的序列化转换。这里刻意**不依赖 UnityEngine**，
    /// 所以可以脱离 Unity 做单元测试；JSON 那一步交给 Unity 层的 JsonUtility 或 Newtonsoft。
    /// </summary>
    public static class MapSerializer
    {
        public static MapGraphDto ToDto(MapGraph graph, int configHash = 0)
        {
            if (graph == null) return null;

            var dto = new MapGraphDto
            {
                Rows = graph.Rows,
                Columns = graph.Columns,
                Seed = graph.Seed,
                Attempt = graph.Attempt,
                ConfigHash = configHash
            };

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode node = graph.Nodes[i];
                dto.Nodes.Add(new MapNodeDto
                {
                    Id = node.Id,
                    Row = node.Row,
                    Column = node.Column,
                    TypeKey = node.TypeKey,
                    NextIds = new List<int>(node.NextIds)
                });
            }
            return dto;
        }

        public static MapGraph FromDto(MapGraphDto dto)
        {
            if (dto == null) return null;

            var graph = new MapGraph
            {
                Rows = dto.Rows,
                Columns = dto.Columns,
                Seed = dto.Seed,
                Attempt = dto.Attempt
            };

            if (dto.Nodes == null) return graph;

            // 先补节点，再补边 —— 否则边可能指向还不存在的节点
            for (int i = 0; i < dto.Nodes.Count; i++)
            {
                MapNodeDto n = dto.Nodes[i];
                MapNode node = graph.AddNode(n.Row, n.Column, n.TypeKey);
                if (node.Id != n.Id)
                    throw new InvalidOperationException(
                        "节点 Id 与重建顺序不一致，请确认 DTO 是按原始顺序保存的");
            }

            for (int i = 0; i < dto.Nodes.Count; i++)
            {
                MapNodeDto n = dto.Nodes[i];
                if (n.NextIds == null) continue;
                for (int j = 0; j < n.NextIds.Count; j++)
                    graph.AddEdge(n.Id, n.NextIds[j]);
            }

            return graph;
        }

        public static MapRunSnapshot CaptureRun(MapGraph graph, MapRunState state, int configHash = 0)
        {
            if (graph == null) return null;

            var snapshot = new MapRunSnapshot
            {
                Seed = graph.Seed,
                Attempt = graph.Attempt,
                ConfigHash = configHash,
                CurrentNodeId = state != null ? state.CurrentNodeId : MapRunState.NoNode
            };

            if (state != null)
                foreach (int id in state.VisitedNodeIds) snapshot.VisitedNodeIds.Add(id);

            return snapshot;
        }
    }
}
