using System;
using System.Collections.Generic;
using System.Text;
using RoguelikeMap.Model;

namespace RoguelikeMap.Generation
{
    /// <summary>
    /// 把地图打印成控制台 ASCII 图。
    ///
    /// 看起来只是个小工具，但它是"生成器能不能调"的关键：
    /// 不用开 Unity 编辑器就能看结果，可以一次跑 1000 张图肉眼扫异常。
    /// </summary>
    public static class MapGraphDebug
    {
        /// <summary>从最上层（Boss）往下打印。</summary>
        public static string ToAscii(MapGraph graph, int cellWidth = 7)
        {
            if (graph == null) return "(null)";
            if (cellWidth < 4) cellWidth = 4;

            var sb = new StringBuilder();
            int width = graph.Columns * cellWidth;
            var buffer = new char[width];

            for (int row = graph.Rows - 1; row >= 0; row--)
            {
                Fill(buffer, ' ');
                foreach (MapNode node in graph.NodesInRow(row))
                {
                    string label = Abbreviate(node.TypeKey, cellWidth - 1);
                    int start = node.Column * cellWidth + (cellWidth - label.Length) / 2;
                    for (int i = 0; i < label.Length; i++)
                    {
                        int p = start + i;
                        if (p >= 0 && p < width) buffer[p] = label[i];
                    }
                }
                sb.Append("R").Append(row.ToString("00")).Append(" ");
                sb.Append(buffer).Append('\n');

                if (row == 0) continue;

                Fill(buffer, ' ');
                foreach (MapNode node in graph.NodesInRow(row - 1))
                {
                    bool left = false, right = false, straight = false;
                    for (int i = 0; i < node.NextIds.Count; i++)
                    {
                        MapNode to = graph.Get(node.NextIds[i]);
                        if (to == null) continue;
                        if (to.Column < node.Column) left = true;
                        else if (to.Column > node.Column) right = true;
                        else straight = true;
                    }

                    int kinds = (left ? 1 : 0) + (right ? 1 : 0) + (straight ? 1 : 0);
                    char c = kinds > 1 ? '+' : left ? '/' : right ? '\\' : straight ? '|' : ' ';
                    int p = node.Column * cellWidth + cellWidth / 2;
                    if (p >= 0 && p < width) buffer[p] = c;
                }
                sb.Append("    ").Append(buffer).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>一行概览：层数、节点数、路径数、各类型数量。适合批量跑图时统计。</summary>
        public static string Describe(MapGraph graph)
        {
            if (graph == null) return "(null)";

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                string key = graph.Nodes[i].TypeKey;
                int c;
                counts[key] = counts.TryGetValue(key, out c) ? c + 1 : 1;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts)
                parts.Add(pair.Key + "=" + pair.Value);
            parts.Sort(StringComparer.Ordinal);

            return "层数=" + graph.Rows + " 列数=" + graph.Columns +
                   " 节点=" + graph.NodeCount +
                   " 路径=" + graph.CountPaths(int.MaxValue) +
                   " | " + string.Join(" ", parts);
        }

        static void Fill(char[] buffer, char c)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] = c;
        }

        static string Abbreviate(string key, int maxLength)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            if (maxLength < 1) maxLength = 1;
            if (key.Length <= maxLength) return key.ToUpperInvariant();
            return key.Substring(0, maxLength).ToUpperInvariant();
        }
    }
}
