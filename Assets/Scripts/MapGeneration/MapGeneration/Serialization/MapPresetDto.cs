using System;
using System.Collections.Generic;
using RoguelikeMap.Config;
using RoguelikeMap.Model;

namespace RoguelikeMap.Serialization
{
    /// <summary>离线完整路径地图；原图布局及房间已确定，运行时只还原快照。</summary>
    [Serializable]
    public sealed class MapPresetDto
    {
        public int Version = 1;
        public int SettingsHash;
        public int PathCount;
        public MapGraphDto Graph;
        public List<MapPresetPath> Paths = new List<MapPresetPath>();
    }

    [Serializable]
    public sealed class MapPresetPath
    {
        public List<int> NodeIds = new List<int>();
    }

    /// <summary>离线文件使用跨运行时稳定的指纹，不依赖 string.GetHashCode。</summary>
    public static class MapPresetFingerprint
    {
        public static int ComputeLayout(MapGenerationConfig config, MapRouteProfile profile, int paths)
        {
            uint h = 2166136261;
            Mix(ref h, config.Rows); Mix(ref h, config.Columns); Mix(ref h, paths);
            Mix(ref h, config.IncludeBossNode ? 1 : 0); Mix(ref h, config.HasStartNode ? 1 : 0);
            Mix(ref h, config.StartTypeKey); Mix(ref h, config.BossTypeKey);
            Mix(ref h, profile == null ? 0 : profile.ComputeHash());
            return unchecked((int)h);
        }

        public static int Compute(MapGenerationConfig config, MapRouteProfile profile, int? pathCount = null)
        {
            uint h = 2166136261;
            Mix(ref h, config.Rows); Mix(ref h, config.Columns); Mix(ref h, pathCount ?? config.PathCount);
            Mix(ref h, config.IncludeBossNode ? 1 : 0); Mix(ref h, config.HasStartNode ? 1 : 0);
            Mix(ref h, config.ConstrainBossApproach ? 1 : 0); Mix(ref h, config.MaxOutDegree);
            Mix(ref h, config.MinPathCount); Mix(ref h, config.MaxPathCount);
            Mix(ref h, config.BossTypeKey); Mix(ref h, config.StartTypeKey);
            foreach (var row in config.FixedRows) { Mix(ref h, row.Row); Mix(ref h, row.TypeKey); }
            foreach (var weight in config.Weights)
            {
                Mix(ref h, weight.TypeKey);
                Mix(ref h, BitConverter.ToInt32(BitConverter.GetBytes(weight.BaseWeight), 0));
                Mix(ref h, BitConverter.ToInt32(BitConverter.GetBytes(weight.WeightPerRow), 0));
                Mix(ref h, weight.MinRow); Mix(ref h, weight.MaxRow);
            }
            foreach (var key in config.FallbackOrder) Mix(ref h, key);
            Mix(ref h, profile == null ? 0 : profile.ComputeHash());
            return unchecked((int)h);
        }

        private static void Mix(ref uint h, int value) { unchecked { h = (h ^ (uint)value) * 16777619; } }
        private static void Mix(ref uint h, string value)
        {
            Mix(ref h, value == null ? -1 : value.Length);
            if (value != null) foreach (char c in value) Mix(ref h, c);
        }
    }
}
