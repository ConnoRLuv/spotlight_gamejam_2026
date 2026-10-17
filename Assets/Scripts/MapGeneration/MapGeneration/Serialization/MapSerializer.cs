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
        public int StartNodeId = -1;
        public MapAssetLayout AssetLayout;
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
        /// <summary>约束地图保存完整布局，旧种子存档仍保持 null 并沿用旧恢复方式。</summary>
        public MapGraphDto Graph;
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
                ConfigHash = configHash,
                StartNodeId = graph.StartNodeId,
                AssetLayout = CopyLayout(graph.AssetLayout)
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
                Attempt = dto.Attempt,
                StartNodeId = dto.StartNodeId,
                AssetLayout = CopyLayout(dto.AssetLayout)
            };

            if (dto.Nodes == null) return graph;

            // 先补节点，再补边 —— 否则边可能指向还不存在的节点
            for (int i = 0; i < dto.Nodes.Count; i++)
            {
                MapNodeDto n = dto.Nodes[i];
                graph.AddRestoredNode(n.Id, n.Row, n.Column, n.TypeKey);
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
                CurrentNodeId = state != null ? state.CurrentNodeId : MapRunState.NoNode,
                Graph = graph.AssetLayout != null ? ToDto(graph, configHash) : null
            };

            if (state != null)
                foreach (int id in state.VisitedNodeIds) snapshot.VisitedNodeIds.Add(id);

            return snapshot;
        }

        /// <summary>复制布局快照，避免修改 DTO 或存档时反向改变正在探索的地图。</summary>
        private static MapAssetLayout CopyLayout(MapAssetLayout source)
        {
            if (source == null) return null;
            var result = new MapAssetLayout { Version = source.Version, ProfileHash = source.ProfileHash };
            foreach (var n in source.Nodes) result.Nodes.Add(new MapNodePlacement { NodeId = n.NodeId, X = n.X, Y = n.Y });
            foreach (var e in source.Edges) result.Edges.Add(new MapEdgePlacement { FromId = e.FromId, ToId = e.ToId, X = e.X, Y = e.Y,
                TemplateId = e.TemplateId, FootprintId = e.FootprintId, SourceSide = e.SourceSide, TargetSide = e.TargetSide });
            return result;
        }
    }
}
