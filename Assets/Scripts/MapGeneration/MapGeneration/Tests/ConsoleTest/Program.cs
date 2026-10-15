using System;
using System.Collections.Generic;
using RoguelikeMap.Config;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Runtime;
using RoguelikeMap.Rules;
using RoguelikeMap.Serialization;

namespace MapGenTest
{
    /// <summary>
    /// 核心层的自检程序。跑一遍就知道生成器有没有坏。
    /// 调数值时改 MapGenerationConfig.CreateDefault()，然后重跑这里看分布。
    /// </summary>
    public static class Program
    {
        static int _failures;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            MapGenerationConfig config = MapGenerationConfig.CreateDefault();
            NodeTypeCatalog catalog = NodeTypeCatalog.CreateDefault();
            MapGenerator generator = MapGenerator.CreateDefault();

            TestSingleMap(config, catalog, generator);
            TestBulkSuccessRate(config, catalog, generator);
            TestDeterminism(config, catalog, generator);
            TestSerialization(config, catalog, generator);
            TestRunNavigation(config, catalog, generator);
            TestCustomRoomType(config, catalog, generator);

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "==== 全部通过 ====" : "==== 失败 " + _failures + " 项 ====");
            return _failures == 0 ? 0 : 1;
        }

        static void TestSingleMap(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("1. 生成一张地图（种子 12345）");

            MapGenerationResult result = generator.Generate(config, catalog, 12345u);
            Console.WriteLine(MapGraphDebug.ToAscii(result.Graph));
            Console.WriteLine(MapGraphDebug.Describe(result.Graph));
            Console.WriteLine("成功=" + result.Success);
            Console.WriteLine(result.DiagnosticsText);

            Check(result.Success, "单张地图生成应当成功");
        }

        static void TestBulkSuccessRate(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("2. 批量生成 2000 张，统计成功率与分布");

            int ok = 0;
            var typeTotals = new Dictionary<string, int>(StringComparer.Ordinal);
            long nodeTotal = 0;
            long pathTotal = 0;
            int minNodes = int.MaxValue, maxNodes = 0;
            int worstAttempts = 0;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (uint seed = 1; seed <= 2000u; seed++)
            {
                MapGenerationResult r = generator.Generate(config, catalog, seed);
                if (r.Success) ok++; else _failures++;

                MapGraph g = r.Graph;
                nodeTotal += g.NodeCount;
                pathTotal += g.CountPaths(int.MaxValue);
                if (g.NodeCount < minNodes) minNodes = g.NodeCount;
                if (g.NodeCount > maxNodes) maxNodes = g.NodeCount;
                if (g.Attempt > worstAttempts) worstAttempts = g.Attempt;

                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    string k = g.Nodes[i].TypeKey;
                    int c;
                    typeTotals[k] = typeTotals.TryGetValue(k, out c) ? c + 1 : 1;
                }
            }
            sw.Stop();

            Console.WriteLine("成功 " + ok + "/2000，耗时 " + sw.ElapsedMilliseconds + " ms" +
                              "（平均 " + (sw.Elapsed.TotalMilliseconds / 2000.0).ToString("F3") + " ms/张）");
            Console.WriteLine("节点数区间 " + minNodes + "~" + maxNodes + "，平均 " + (nodeTotal / 2000.0).ToString("F1"));
            Console.WriteLine("路径数平均 " + (pathTotal / 2000.0).ToString("F1") + "，最大重试次数 " + worstAttempts);

            var keys = new List<string>(typeTotals.Keys);
            keys.Sort(StringComparer.Ordinal);
            Console.Write("类型分布：");
            for (int i = 0; i < keys.Count; i++)
            {
                double pct = typeTotals[keys[i]] * 100.0 / nodeTotal;
                Console.Write(keys[i] + "=" + pct.ToString("F1") + "%  ");
            }
            Console.WriteLine();

            Check(ok == 2000, "2000 张图全部应当生成成功");
        }

        static void TestDeterminism(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("3. 确定性：同一种子必须生成完全相同的图");

            MapGraph a = generator.Generate(config, catalog, 777u).Graph;
            MapGraph b = generator.Generate(config, catalog, 777u).Graph;

            bool same = a.NodeCount == b.NodeCount;
            for (int i = 0; i < a.Nodes.Count && same; i++)
            {
                MapNode na = a.Nodes[i], nb = b.Nodes[i];
                if (na.Id != nb.Id || na.Row != nb.Row || na.Column != nb.Column || na.TypeKey != nb.TypeKey)
                    same = false;
                else if (na.NextIds.Count != nb.NextIds.Count) same = false;
                else
                    for (int j = 0; j < na.NextIds.Count; j++)
                        if (na.NextIds[j] != nb.NextIds[j]) { same = false; break; }
            }

            MapGraph c = generator.Generate(config, catalog, 778u).Graph;
            bool differs = c.NodeCount != a.NodeCount;
            for (int i = 0; i < a.Nodes.Count && !differs; i++)
                if (a.Nodes[i].TypeKey != c.Nodes[i].TypeKey || a.Nodes[i].Column != c.Nodes[i].Column)
                    differs = true;

            Console.WriteLine("同种子一致=" + same + "，不同种子有差异=" + differs);
            Check(same, "同一种子必须完全一致");
            Check(differs, "不同种子应当产生不同地图");
        }

        static void TestSerialization(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("4. 序列化往返");

            MapGraph original = generator.Generate(config, catalog, 999u).Graph;
            MapGraphDto dto = MapSerializer.ToDto(original, config.ComputeHash());
            MapGraph restored = MapSerializer.FromDto(dto);

            bool same = original.NodeCount == restored.NodeCount && original.Rows == restored.Rows;
            for (int i = 0; i < original.Nodes.Count && same; i++)
            {
                MapNode o = original.Nodes[i], r = restored.Nodes[i];
                if (o.TypeKey != r.TypeKey || o.Row != r.Row || o.Column != r.Column) same = false;
                if (o.NextIds.Count != r.NextIds.Count) same = false;
                else
                    for (int j = 0; j < o.NextIds.Count; j++)
                        if (o.NextIds[j] != r.NextIds[j]) { same = false; break; }
            }

            Console.WriteLine("DTO 节点数=" + dto.Nodes.Count + "，往返一致=" + same);
            Check(same, "序列化往返后地图应当完全一致");
        }

        static void TestRunNavigation(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("5. 走图：从起点一路走到 Boss");

            MapGraph graph = generator.Generate(config, catalog, 4242u).Graph;
            var run = new MapRunState(graph);

            var visitedTypes = new List<string>();
            run.NodeEntered += node => visitedTypes.Add(node.TypeKey);

            int steps = 0;
            int guard = 0;
            while (!run.IsCompleted && guard++ < 100)
            {
                IReadOnlyList<MapNode> options = run.GetOptions();
                if (options.Count == 0) break;

                // 简单走法：优先非战斗节点
                int chosen = 0;
                for (int i = 0; i < options.Count; i++)
                    if (options[i].TypeKey != NodeTypeKeys.Monster) { chosen = i; break; }

                if (!run.MoveTo(options[chosen].Id)) { _failures++; break; }
                steps++;
            }

            Console.WriteLine("走了 " + steps + " 步，完成=" + run.IsCompleted);
            Console.WriteLine("路径：" + string.Join(" -> ", visitedTypes));
            Check(run.IsCompleted, "应当能一路走到终点");

            MapRunSnapshot snap = MapSerializer.CaptureRun(graph, run, config.ComputeHash());
            var restoredRun = new MapRunState(MapSerializer.FromDto(MapSerializer.ToDto(graph)));
            restoredRun.RestoreFrom(snap.CurrentNodeId, snap.VisitedNodeIds);
            Console.WriteLine("读档后当前位置=" + snap.CurrentNodeId +
                              "，已访问=" + snap.VisitedNodeIds.Count +
                              "，深度一致=" + (restoredRun.Depth == run.Depth));
            Check(restoredRun.Depth == run.Depth, "读档后进度应当一致");
        }

        static void TestCustomRoomType(MapGenerationConfig config, NodeTypeCatalog catalog, MapGenerator generator)
        {
            Section("6. 扩展测试：不写一行新代码，只加数据就引入新房间类型");

            catalog.Add("forge", "锻造台", "safe");

            MapGenerationConfig custom = config.Clone();
            custom.Weights.Add(new NodeTypeWeightEntry
            {
                TypeKey = "forge",
                BaseWeight = 0f,
                WeightPerRow = 0.8f,
                MinRow = 6,
                MaxRow = -1
            });
            custom.FallbackOrder.Insert(1, "forge");

            generator.Rules.Add(new NoConsecutiveSameTypeRule("forge"));

            MapGenerationResult r = generator.Generate(custom, catalog, 31337u);
            int forgeCount = 0;
            for (int i = 0; i < r.Graph.Nodes.Count; i++)
                if (r.Graph.Nodes[i].TypeKey == "forge") forgeCount++;

            Console.WriteLine("新类型 'forge' 出现次数=" + forgeCount + "，生成成功=" + r.Success);
            Check(r.Success && forgeCount > 0, "新增房间类型应当无需改动生成器即可生效");
        }

        static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("=======================================================");
            Console.WriteLine(title);
            Console.WriteLine("=======================================================");
        }

        static void Check(bool condition, string message)
        {
            if (condition) return;
            _failures++;
            Console.WriteLine("  [失败] " + message);
        }
    }
}
