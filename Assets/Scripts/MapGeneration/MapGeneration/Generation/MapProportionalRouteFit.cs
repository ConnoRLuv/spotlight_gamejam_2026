using System;

namespace RoguelikeMap.Generation
{
    /// <summary>二维接点或节点区域的边界，坐标与路线原图或地图一致。</summary>
    public readonly struct RouteFitBounds
    {
        public readonly double MinX, MinY, MaxX, MaxY;
        public RouteFitBounds(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
    }

    /// <summary>只允许统一缩放和平移，确保两组真实线头分别被对应节点覆盖。</summary>
    public static class MapProportionalRouteFit
    {
        public static bool TryFit(RouteFitBounds sourcePorts, RouteFitBounds targetPorts,
            RouteFitBounds sourceNode, RouteFitBounds targetNode, double preferredScale,
            out double scale, out double x, out double y)
        {
            scale = x = y = 0;
            double minScale = 0.0001, maxScale = double.PositiveInfinity;
            var ports = new[] { sourcePorts, targetPorts };
            var nodes = new[] { sourceNode, targetNode };
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                // 每个平移下界均不得超过任何平移上界，约束化为 a * scale <= b。
                if (!Constrain(ports[j].MaxX - ports[i].MinX, nodes[j].MaxX - nodes[i].MinX, ref minScale, ref maxScale)
                    || !Constrain(ports[j].MaxY - ports[i].MinY, nodes[j].MaxY - nodes[i].MinY, ref minScale, ref maxScale))
                    return false;
            }
            if (double.IsNaN(preferredScale) || double.IsInfinity(preferredScale) || preferredScale <= 0) return false;
            scale = Math.Max(minScale, Math.Min(maxScale, preferredScale));
            double minX = Math.Max(sourceNode.MinX - scale * sourcePorts.MinX, targetNode.MinX - scale * targetPorts.MinX);
            double maxX = Math.Min(sourceNode.MaxX - scale * sourcePorts.MaxX, targetNode.MaxX - scale * targetPorts.MaxX);
            double minY = Math.Max(sourceNode.MinY - scale * sourcePorts.MinY, targetNode.MinY - scale * targetPorts.MinY);
            double maxY = Math.Min(sourceNode.MaxY - scale * sourcePorts.MaxY, targetNode.MaxY - scale * targetPorts.MaxY);
            if (minX > maxX + 0.000001 || minY > maxY + 0.000001) return false;
            x = (minX + maxX) * 0.5;
            y = (minY + maxY) * 0.5;
            return true;
        }

        static bool Constrain(double a, double b, ref double minScale, ref double maxScale)
        {
            if (Math.Abs(a) < 0.00000001) return b >= 0;
            if (a > 0) maxScale = Math.Min(maxScale, b / a);
            else minScale = Math.Max(minScale, b / a);
            return minScale <= maxScale;
        }
    }
}
