using System;
using System.Collections.Generic;
using RoguelikeMap.Model;

namespace RoguelikeMap.Generation
{
    public readonly struct MapRouteEndCoverage
    {
        public readonly RouteFitBounds First, Second;
        public MapRouteEndCoverage(RouteFitBounds first, RouteFitBounds second) { First = first; Second = second; }
    }

    public readonly struct MapRouteSocketFit
    {
        public readonly MapRouteTemplate Template;
        public readonly RouteFitBounds SourcePorts, TargetPorts;
        public readonly double Scale, X, Y;
        public MapRouteSocketFit(MapRouteTemplate template, RouteFitBounds sourcePorts, RouteFitBounds targetPorts, double scale, double x, double y)
        { Template = template; SourcePorts = sourcePorts; TargetPorts = targetPorts; Scale = scale; X = x; Y = y; }
    }

    /// <summary>按原图接点选择路线，图片名称中的方向不代表玩家行进方向。</summary>
    public static class MapRouteSocketSelector
    {
        public static bool TrySelect(MapRouteProfile profile, int columnDelta,
            RouteFitBounds sourceNode, RouteFitBounds targetNode, int usedSourceSides, int usedTargetSides,
            out MapRouteSocketFit result, IReadOnlyDictionary<string, MapRouteEndCoverage> endpointCoverage = null)
        {
            result = default;
            double bestScore = double.PositiveInfinity;
            if (profile == null) return false;
            foreach (var template in profile.Templates)
            {
                if (template.ColumnDelta != columnDelta || !HasUsableEndpoints(template)) continue;
                if (columnDelta == 0 && (template.SourceSide != MapSocketSide.Up || template.TargetSide != MapSocketSide.Down)) continue;
                var sourcePorts = PortBounds(template.Source);
                var targetPorts = PortBounds(template.Target);
                if (endpointCoverage != null && endpointCoverage.TryGetValue(template.FootprintId, out var coverage))
                {
                    // 图片名带“下”时，原来的 Source/Target 两组可能需要交换才能向上连接。
                    bool firstIsSource = Distance(coverage.First, sourcePorts) <= Distance(coverage.Second, sourcePorts);
                    sourcePorts = firstIsSource ? coverage.First : coverage.Second;
                    targetPorts = firstIsSource ? coverage.Second : coverage.First;
                }
                if (!MapProportionalRouteFit.TryFit(sourcePorts, targetPorts,
                    SideBounds(sourceNode, template.SourceSide), SideBounds(targetNode, template.TargetSide), 1,
                    out double scale, out double x, out double y)) continue;

                // 分叉允许共用接点；有空闲侧面时优先选它，减少多条线在同一侧叠放。
                double score = ((usedSourceSides & 1 << (int)template.SourceSide) != 0 ? 100 : 0)
                    + ((usedTargetSides & 1 << (int)template.TargetSide) != 0 ? 100 : 0)
                    + (columnDelta != 0 && template.SourceSide == MapSocketSide.Up ? 1 : 0)
                    + Math.Abs(Math.Log(scale));
                if (score >= bestScore) continue;
                bestScore = score;
                result = new MapRouteSocketFit(template, sourcePorts, targetPorts, scale, x, y);
            }
            return result.Template != null;
        }

        public static RouteFitBounds PortBounds(MapRoutePort port) =>
            new RouteFitBounds(port.MinX, port.MinY, port.MaxX, port.MaxY);

        public static bool HasUsableEndpoints(MapRouteTemplate template) => template != null
            && HasUsableLead(template.Source) && HasUsableLead(template.Target);

        static bool HasUsableLead(MapRoutePort port)
        {
            if (port == null) return false;
            // 边界上的长条线身只有很短的垂直厚度，不是真正向图内伸入的双轨开口。
            int span = Math.Max(port.MaxX - port.MinX, port.MaxY - port.MinY) + 1;
            return span > 0 && port.LeadLength > 0 && port.LeadLength * 2L >= span;
        }

        public static RouteFitBounds SideBounds(RouteFitBounds node, MapSocketSide side)
        {
            double x = (node.MinX + node.MaxX) * 0.5, y = (node.MinY + node.MaxY) * 0.5;
            switch (side)
            {
                case MapSocketSide.Up: return new RouteFitBounds(node.MinX, y, node.MaxX, node.MaxY);
                case MapSocketSide.Right: return new RouteFitBounds(x, node.MinY, node.MaxX, node.MaxY);
                case MapSocketSide.Down: return new RouteFitBounds(node.MinX, node.MinY, node.MaxX, y);
                default: return new RouteFitBounds(node.MinX, node.MinY, x, node.MaxY);
            }
        }

        static double Distance(RouteFitBounds a, RouteFitBounds b)
        {
            double x = a.MinX + a.MaxX - b.MinX - b.MaxX, y = a.MinY + a.MaxY - b.MinY - b.MaxY;
            return x * x + y * y;
        }

    }
}
