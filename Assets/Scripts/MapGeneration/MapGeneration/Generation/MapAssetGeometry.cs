using System;
using System.Collections.Generic;
using RoguelikeMap.Model;

namespace RoguelikeMap.Generation
{
    /// <summary>单张原图的接点匹配和可见像素碰撞；全部使用整数原生像素坐标。</summary>
    public static class MapAssetGeometry
    {
        /// <summary>求两端接点共同允许的图片平移，优先中间位置，也保留边界位置供回溯避让。</summary>
        public static List<MapEdgePlacement> GetPlacements(MapRouteProfile profile, MapRouteTemplate template, MapNodePlacement from, MapNodePlacement to)
        {
            var result = new List<MapEdgePlacement>();
            var a = PortBounds(profile, from, template.SourceSide, template.Source);
            var b = PortBounds(profile, to, template.TargetSide, template.Target);
            int minX = Math.Max(a[0], b[0]), maxX = Math.Min(a[1], b[1]);
            int minY = Math.Max(a[2], b[2]), maxY = Math.Min(a[3], b[3]);
            if (minX > maxX || minY > maxY) return result;
            int[] xs = { (minX + maxX) / 2, minX, maxX }, ys = { (minY + maxY) / 2, minY, maxY };
            foreach (int x in xs) foreach (int y in ys)
            {
                if (result.Exists(p => p.X == x && p.Y == y)) continue;
                result.Add(new MapEdgePlacement { FromId = from.NodeId, ToId = to.NodeId, X = x, Y = y,
                    TemplateId = template.Id, FootprintId = template.FootprintId, SourceSide = template.SourceSide, TargetSide = template.TargetSide });
            }
            return result;
        }

        /// <summary>沿线头直线段允许少量嵌入节点，不能嵌入超过转折点，否则会从错误方向出线。</summary>
        private static int[] PortBounds(MapRouteProfile profile, MapNodePlacement node, MapSocketSide side, MapRoutePort port)
        {
            int hx = profile.NodeWidth / 2, hy = profile.NodeHeight / 2;
            int minX = node.X - hx + 2, maxX = node.X + hx - 2, minY = node.Y - hy + 2, maxY = node.Y + hy - 2;
            int depth = Math.Max(0, port.LeadLength - 2);
            switch (side)
            {
                case MapSocketSide.Up: minY = node.Y + hy - Math.Min(hy, depth); maxY = node.Y + hy; break;
                case MapSocketSide.Down: minY = node.Y - hy; maxY = minY + Math.Min(hy, depth); break;
                case MapSocketSide.Right: minX = node.X + hx - Math.Min(hx, depth); maxX = node.X + hx; break;
                case MapSocketSide.Left: minX = node.X - hx; maxX = minX + Math.Min(hx, depth); break;
            }
            return new[] { minX - port.MinX, maxX - port.MaxX, minY - port.MinY, maxY - port.MaxY };
        }

        /// <summary>检查两节点完整矩形是否相碰，透明图案不能用来缩小节点的点击区域。</summary>
        public static bool NodesOverlap(MapRouteProfile profile, MapNodePlacement a, MapNodePlacement b) =>
            Math.Abs(a.X - b.X) <= profile.ClearanceWidth && Math.Abs(a.Y - b.Y) <= profile.ClearanceHeight;

        /// <summary>检查原图可见像素是否穿过无关节点，图片透明外框不参与碰撞。</summary>
        public static bool HitsNode(MapRouteProfile profile, MapEdgePlacement edge, MapNodePlacement node)
        {
            var mask = profile.FindFootprint(edge.FootprintId);
            int x0 = node.X - profile.ClearanceWidth / 2, x1 = node.X + profile.ClearanceWidth / 2;
            int y0 = node.Y - profile.ClearanceHeight / 2, y1 = node.Y + profile.ClearanceHeight / 2;
            if (edge.X + mask.Width < x0 || edge.X > x1 || edge.Y + mask.Height < y0 || edge.Y > y1) return false;
            int minRow = Math.Max(0, y0 - edge.Y), maxRow = Math.Min(mask.Height - 1, y1 - edge.Y);
            for (int y = minRow; y <= maxRow; y++)
                for (int p = mask.RowStart(y); p < mask.RowStart(y + 1); p += 3)
                    if (edge.X + mask.Runs[p + 1] <= x1 && edge.X + mask.Runs[p + 2] - 1 >= x0) return true;
            return false;
        }

        /// <summary>检查两条边的非透明像素重叠或接触；只豁免共同端点节点内部的连接区域。</summary>
        public static bool EdgesConflict(MapRouteProfile profile, MapEdgePlacement a, MapEdgePlacement b, MapNodePlacement common)
        {
            var am = profile.FindFootprint(a.FootprintId); var bm = profile.FindFootprint(b.FootprintId);
            if (a.X > b.X + bm.Width || b.X > a.X + am.Width || a.Y > b.Y + bm.Height || b.Y > a.Y + am.Height) return false;
            int minY = Math.Max(a.Y, b.Y - 1), maxY = Math.Min(a.Y + am.Height - 1, b.Y + bm.Height);
            for (int worldY = minY; worldY <= maxY; worldY++)
            {
                int ay = worldY - a.Y;
                if (ay < 0 || ay >= am.Height) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int by = worldY + dy - b.Y;
                    if (by < 0 || by >= bm.Height) continue;
                    int ap = am.RowStart(ay), ae = am.RowStart(ay + 1), bp = bm.RowStart(by), be = bm.RowStart(by + 1);
                    while (ap < ae && bp < be)
                    {
                        int ax0 = a.X + am.Runs[ap + 1], ax1 = a.X + am.Runs[ap + 2] - 1;
                        int bx0 = b.X + bm.Runs[bp + 1], bx1 = b.X + bm.Runs[bp + 2] - 1;
                        if (ax0 <= bx1 + 1 && bx0 <= ax1 + 1)
                        {
                            // 相邻像素接触也必须检查两边实际像素，不能只因其中一边在节点内就豁免。
                            // 对称计算可避免调换两条边的顺序后得到不同碰撞结果。
                            int left = Math.Min(Math.Max(ax0, bx0 - 1), Math.Max(bx0, ax0 - 1));
                            int right = Math.Max(Math.Min(ax1, bx1 + 1), Math.Min(bx1, ax1 + 1));
                            if (!InsideCommon(profile, common, left, right, worldY, worldY + dy)) return true;
                        }
                        if (ax1 < bx1) ap += 3; else bp += 3;
                    }
                }
            }
            return false;
        }

        /// <summary>整段接触区都在共同节点内部时才允许，不豁免节点外紧邻的重叠。</summary>
        private static bool InsideCommon(MapRouteProfile profile, MapNodePlacement node, int left, int right, int y0, int y1) =>
            node != null && left >= node.X - profile.NodeWidth / 2 && right <= node.X + profile.NodeWidth / 2 &&
            Math.Min(y0, y1) >= node.Y - profile.NodeHeight / 2 && Math.Max(y0, y1) <= node.Y + profile.NodeHeight / 2;

        /// <summary>独立复核最终地图的接点、几何、边布局完整性，供生成器和测试共用。</summary>
        public static bool Validate(MapGraph graph, MapRouteProfile profile, out string error)
        {
            error = null;
            if (graph.AssetLayout == null || graph.AssetLayout.Version != 1) { error = "缺少有效资产布局"; return false; }
            var layout = graph.AssetLayout; var ports = new Dictionary<int, int>(); int edgeCount = 0;
            foreach (var node in graph.Nodes)
            {
                if (layout.FindNode(node.Id) == null) { error = "节点缺少位置：" + node.Id; return false; }
                edgeCount += node.NextIds.Count;
                if (node.PrevIds.Count > 3) { error = "节点入边超过三条：" + node.Id; return false; }
            }
            if (edgeCount != layout.Edges.Count || graph.NodeCount != layout.Nodes.Count) { error = "布局数量与地图不一致"; return false; }
            var pairs = new HashSet<long>();
            foreach (var edge in layout.Edges)
            {
                var from = graph.Get(edge.FromId); var to = graph.Get(edge.ToId);
                var template = profile.FindTemplate(edge.TemplateId);
                long pair = ((long)edge.FromId << 32) | (uint)edge.ToId;
                if (from == null || to == null || !graph.HasEdge(from.Id, to.Id) || !pairs.Add(pair) || to.Row != from.Row + 1 || Math.Abs(to.Column - from.Column) > 1 ||
                    template == null || template.ColumnDelta != to.Column - from.Column || template.FootprintId != edge.FootprintId || template.SourceSide != edge.SourceSide || template.TargetSide != edge.TargetSide)
                { error = "非法连线或模板：" + edge.FromId + "→" + edge.ToId; return false; }
                if (edge.TargetSide == MapSocketSide.Up || from.Column == to.Column &&
                    (edge.SourceSide != MapSocketSide.Up || edge.TargetSide != MapSocketSide.Down))
                { error = "正下方连入必须占下侧，入线只能使用下、左、右接点"; return false; }
                int a = 1 << (int)edge.SourceSide, b = 1 << (int)edge.TargetSide;
                ports.TryGetValue(from.Id, out int pa); ports.TryGetValue(to.Id, out int pb);
                if ((pa & a) != 0 || (pb & b) != 0) { error = "同侧接点被多次占用：" + from.Id + "→" + to.Id; return false; }
                ports[from.Id] = pa | a; ports[to.Id] = pb | b;
                var allowed = GetPlacements(profile, template, layout.FindNode(from.Id), layout.FindNode(to.Id));
                if (!allowed.Exists(p => p.X == edge.X && p.Y == edge.Y)) { error = "连线未对准指定接点"; return false; }
                foreach (var node in layout.Nodes)
                    if (node.NodeId != from.Id && node.NodeId != to.Id && HitsNode(profile, edge, node))
                    { error = "连线穿过无关节点：" + node.NodeId; return false; }
            }
            for (int i = 0; i < layout.Nodes.Count; i++) for (int j = i + 1; j < layout.Nodes.Count; j++)
                if (NodesOverlap(profile, layout.Nodes[i], layout.Nodes[j])) { error = "节点重叠"; return false; }
            for (int i = 0; i < layout.Edges.Count; i++) for (int j = i + 1; j < layout.Edges.Count; j++)
            {
                var a = layout.Edges[i]; var b = layout.Edges[j];
                int common = a.FromId == b.FromId || a.FromId == b.ToId ? a.FromId : a.ToId == b.FromId || a.ToId == b.ToId ? a.ToId : -1;
                if (EdgesConflict(profile, a, b, layout.FindNode(common))) { error = "线路重叠或接触：" + a.FromId + "→" + a.ToId + " / " + b.FromId + "→" + b.ToId; return false; }
            }
            return true;
        }
    }
}
