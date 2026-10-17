using System;
using System.Collections.Generic;

namespace RoguelikeMap.Model
{
    /// <summary>节点的真实接入边；同一方向由入线和出线共同占用。</summary>
    public enum MapSocketSide { Up, Right, Down, Left }

    /// <summary>一组同侧线头的像素范围和直线引入长度，双轨属于同一个接点。</summary>
    [Serializable]
    public sealed class MapRoutePort
    {
        public int MinX, MaxX, MinY, MaxY, LeadLength;
    }

    /// <summary>原图非透明像素的行程编码，每三个整数依次为 y、起始 x、终止 x（不含）。</summary>
    [Serializable]
    public sealed class MapRouteFootprint
    {
        public string Id;
        public int Width, Height;
        public int[] Runs = Array.Empty<int>();
        [NonSerialized] private int[] rowStarts;

        /// <summary>取得指定行的编码起点；缓存只在首次碰撞查询时建立。</summary>
        public int RowStart(int y)
        {
            if (rowStarts == null)
            {
                rowStarts = new int[Height + 1]; int p = 0;
                for (int row = 0; row <= Height; row++)
                { while (p < Runs.Length && Runs[p] < row) p += 3; rowStarts[row] = p; }
            }
            return y < 0 ? 0 : y > Height ? Runs.Length : rowStarts[y];
        }
    }

    /// <summary>一张原图的一种接法；交换端点可反向使用，但不旋转或缩放图片。</summary>
    [Serializable]
    public sealed class MapRouteTemplate
    {
        public string Id, FootprintId;
        public int ColumnDelta;
        public MapSocketSide SourceSide, TargetSide;
        public MapRoutePort Source, Target;
    }

    /// <summary>编辑器烘焙的纯数据布局配置；运行时不读取纹理，核心程序集不依赖 Unity。</summary>
    [Serializable]
    public sealed class MapRouteProfile
    {
        public int Version = 1;
        public int NodeWidth = 130, NodeHeight = 130;
        // 接点仍落在被图标遮盖的中心内核；无关线路按完整原图矩形避让，不能穿过外侧引脚。
        // 旧配置没有该范围时为 0，继续沿用原接点矩形。
        public int NodeClearanceWidth, NodeClearanceHeight;
        public int ClearanceWidth => Math.Max(NodeWidth, NodeClearanceWidth);
        public int ClearanceHeight => Math.Max(NodeHeight, NodeClearanceHeight);
        public int ColumnSpacing = 320, RowSpacing = 560;
        public List<MapRouteFootprint> Footprints = new List<MapRouteFootprint>();
        public List<MapRouteTemplate> Templates = new List<MapRouteTemplate>();

        /// <summary>按稳定标识查找原图占用范围；不存在时返回 null。</summary>
        public MapRouteFootprint FindFootprint(string id) => Footprints.Find(f => f.Id == id);

        /// <summary>按稳定标识查找接法，显示层不再根据坐标猜测图片。</summary>
        public MapRouteTemplate FindTemplate(string id) => Templates.Find(t => t.Id == id);

        /// <summary>生成跨进程稳定的指纹，图片、接点或尺寸变化均会影响新地图配置。</summary>
        public int ComputeHash()
        {
            unchecked
            {
                uint h = 2166136261;
                Mix(ref h, Version); Mix(ref h, NodeWidth); Mix(ref h, NodeHeight); Mix(ref h, ColumnSpacing); Mix(ref h, RowSpacing);
                Mix(ref h, NodeClearanceWidth); Mix(ref h, NodeClearanceHeight);
                foreach (var f in Footprints)
                { Mix(ref h, f.Id); Mix(ref h, f.Width); Mix(ref h, f.Height); foreach (int n in f.Runs) Mix(ref h, n); }
                foreach (var t in Templates)
                {
                    Mix(ref h, t.Id); Mix(ref h, t.FootprintId); Mix(ref h, t.ColumnDelta);
                    Mix(ref h, (int)t.SourceSide); Mix(ref h, (int)t.TargetSide); Mix(ref h, t.Source); Mix(ref h, t.Target);
                }
                return (int)h;
            }
        }

        /// <summary>追加整数到稳定 FNV 指纹。</summary>
        private static void Mix(ref uint h, int value) { unchecked { h = (h ^ (uint)value) * 16777619; } }
        /// <summary>追加标识字符，避免依赖运行时的字符串哈希随机化。</summary>
        private static void Mix(ref uint h, string value) { if (value != null) foreach (char c in value) Mix(ref h, c); }
        /// <summary>追加完整接点范围和引入长度。</summary>
        private static void Mix(ref uint h, MapRoutePort p)
        { Mix(ref h, p.MinX); Mix(ref h, p.MaxX); Mix(ref h, p.MinY); Mix(ref h, p.MaxY); Mix(ref h, p.LeadLength); }
    }

    /// <summary>节点的实际原生像素坐标，与逻辑行列分离。</summary>
    [Serializable]
    public sealed class MapNodePlacement { public int NodeId, X, Y; }

    /// <summary>成功生成的单张连线位置，X/Y 是原图矩形左下角坐标。</summary>
    [Serializable]
    public sealed class MapEdgePlacement
    {
        public int FromId, ToId, X, Y;
        public string TemplateId, FootprintId;
        public MapSocketSide SourceSide, TargetSide;
    }

    /// <summary>随地图保存的布局结果；不包含纹理像素或 Unity 对象。</summary>
    [Serializable]
    public sealed class MapAssetLayout
    {
        public int Version = 1, ProfileHash;
        public List<MapNodePlacement> Nodes = new List<MapNodePlacement>();
        public List<MapEdgePlacement> Edges = new List<MapEdgePlacement>();
        /// <summary>查找节点坐标；旧地图没有布局时由显示层使用旧网格。</summary>
        public MapNodePlacement FindNode(int id) => Nodes.Find(n => n.NodeId == id);
        /// <summary>查找真实边的模板与位置，避免渲染时另行选择接法。</summary>
        public MapEdgePlacement FindEdge(int from, int to) => Edges.Find(e => e.FromId == from && e.ToId == to);
    }
}
