using System;
using System.Collections.Generic;
using RoguelikeMap.Model;
using RoguelikeMap.Serialization;
using UnityEngine;

namespace RoguelikeMap.Unity
{
    [Serializable]
    public sealed class MapPresetNodeBinding { public int NodeId; public MapNodeView View; public RectTransform PlacementRoot; }
    [Serializable]
    public sealed class MapPresetEdgeBinding { public int FromId, ToId; public MapEdgeView View; }

    /// <summary>完整地图预制体：提前摆好的节点、单图连线及对应的拓扑/房间快照。</summary>
    public sealed class MapPresetPrefab : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string _snapshot;
        [SerializeField] private List<MapPresetNodeBinding> _nodes = new List<MapPresetNodeBinding>();
        [SerializeField] private List<MapPresetEdgeBinding> _edges = new List<MapPresetEdgeBinding>();
        [SerializeField] private bool _useConnectionAnchors;
        [SerializeField] private bool _randomizeRooms;
        private MapPresetDto _data;
        private string _parsedSnapshot;
        public MapPresetDto Data
        {
            get
            {
                if (_data == null || !string.Equals(_parsedSnapshot, _snapshot, StringComparison.Ordinal))
                {
                    _data = JsonUtility.FromJson<MapPresetDto>(_snapshot);
                    _parsedSnapshot = _snapshot;
                }
                return _data;
            }
        }
        public IReadOnlyList<MapPresetNodeBinding> Nodes => _nodes;
        public IReadOnlyList<MapPresetEdgeBinding> Edges => _edges;
        public bool UseConnectionAnchors => _useConnectionAnchors;
        public bool RandomizeRooms => _randomizeRooms;
        public void EnableConnectionAnchors() { _useConnectionAnchors = true; }

        public void Configure(MapPresetDto data, List<MapPresetNodeBinding> nodes, List<MapPresetEdgeBinding> edges)
        {
            _snapshot = JsonUtility.ToJson(data);
            _nodes = nodes; _edges = edges; _data = null; _parsedSnapshot = null;
            _useConnectionAnchors = false;
            _randomizeRooms = false;
        }

        public void ConfigureEmptyLayout(MapPresetDto data)
        {
            _snapshot = JsonUtility.ToJson(data);
            _data = null; _parsedSnapshot = null;
            _randomizeRooms = true;
        }

        /// <summary>存档返回时复用同一预制体；旧快照若不同则使用已有快照显示入口。</summary>
        public bool Matches(MapGraph graph)
        {
            var dto = Data.Graph;
            if (graph == null || graph.Seed != dto.Seed) return false;
            var candidate = MapSerializer.ToDto(graph, dto.ConfigHash);
            if (_randomizeRooms)
                foreach (var node in candidate.Nodes)
                    if (node.Row > 0 && node.Row < candidate.Rows - 1) node.TypeKey = NodeTypeKeys.Unassigned;
            return JsonUtility.ToJson(candidate) == JsonUtility.ToJson(dto);
        }
    }
}
