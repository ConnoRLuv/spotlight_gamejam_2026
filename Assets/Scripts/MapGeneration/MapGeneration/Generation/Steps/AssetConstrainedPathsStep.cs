using System;
using System.Collections.Generic;
using RoguelikeMap.Core;
using RoguelikeMap.Model;
using RoguelikeMap.Validation;

namespace RoguelikeMap.Generation.Steps
{
    /// <summary>把完整起点到终点路径逐条加入地图；接点和原图像素冲突时回溯，不事后删掉必要边。</summary>
    public sealed class AssetConstrainedPathsStep : IMapGenerationStep
    {
        private readonly MapRouteProfile profile;
        private readonly bool alignedLayeredPaths;
        private readonly int[] columnGuide;
        public string Name => "资产接点与像素约束铺路";
        /// <summary>接收编辑器预烘焙的原图几何。</summary>
        public AssetConstrainedPathsStep(MapRouteProfile profile, bool alignedLayeredPaths = false, int[] columnGuide = null)
        { this.profile = profile; this.alignedLayeredPaths = alignedLayeredPaths; this.columnGuide = columnGuide == null ? null : (int[])columnGuide.Clone(); }

        /// <summary>联合确定节点、接法和位置；首领直接作为最后一层参与同一搜索。</summary>
        public void Execute(MapGenerationContext context)
        {
            if (!context.Config.HasStartNode) throw new InvalidOperationException("资产约束地图需要唯一的起点。");
            if (profile == null || profile.Templates.Count == 0) throw new InvalidOperationException("路线模板未烘焙。");
            var solver = new Solver(context, profile, alignedLayeredPaths, columnGuide);
            context.Graph = solver.Generate();
            if (alignedLayeredPaths) context.Log("铺出 " + Math.Max(1, context.Config.PathCount) + " 条路径，全部原图通过像素避让。");
            context.Log("资产约束：" + context.Graph.NodeCount + " 节点，" + context.Graph.AssetLayout.Edges.Count + " 条单图连线；所有方向共用接点额度。");
        }

        /// <summary>一次生成尝试的局部搜索状态，失败回滚只影响当前尚未完成的路径。</summary>
        private sealed class Solver
        {
            private readonly MapGenerationContext context;
            private readonly MapRouteProfile profile;
            private readonly bool aligned;
            private readonly int[] columnGuide;
            private bool firstPath;
            private readonly int rows, columns, goal;
            private readonly MapNodePlacement[] points;
            private readonly int[] sockets, references, outgoing, incoming;
            private readonly int[] branchRows;
            private readonly List<Candidate> edges = new List<Candidate>();
            private readonly Dictionary<long, Candidate> byPair = new Dictionary<long, Candidate>();
            private readonly Dictionary<int, List<Candidate>> candidates = new Dictionary<int, List<Candidate>>();
            private readonly Dictionary<long, bool> collisionCache = new Dictionary<long, bool>();
            private int nextCandidateId, budget, preferredBranchRow;

            /// <summary>缓存候选身份用于重复回溯的碰撞结果，避免重复扫描同一对像素行程。</summary>
            private sealed class Candidate { public int Id; public MapEdgePlacement Edge; }

            /// <summary>建立五列范围内的候选坐标；层间和列间允许小幅变化，再按原图锚点选择平移。</summary>
            public Solver(MapGenerationContext context, MapRouteProfile profile, bool aligned, int[] columnGuide)
            {
                this.context = context; this.profile = profile; this.aligned = aligned;
                rows = Math.Max(1, context.Config.Rows) + (context.Config.IncludeBossNode ? 1 : 0);
                if (columnGuide != null && columnGuide.Length != rows) throw new ArgumentException("离线主路径引导必须覆盖每层。");
                this.columnGuide = columnGuide;
                columns = Math.Max(1, context.Config.Columns); goal = (rows - 1) * columns + columns / 2;
                points = new MapNodePlacement[rows * columns]; sockets = new int[points.Length]; references = new int[points.Length]; outgoing = new int[points.Length]; incoming = new int[points.Length];
                int y = 0;
                for (int row = 0; row < rows; row++)
                {
                    if (row > 0) y += profile.RowSpacing - (aligned ? 0 : context.Random.Range(0, 17));
                    for (int col = 0; col < columns; col++)
                    {
                        int id = row * columns + col;
                        points[id] = new MapNodePlacement { NodeId = id, X = (col - columns / 2) * profile.ColumnSpacing + (aligned ? 0 : context.Random.Range(-4, 5)), Y = y };
                    }
                }
                // 在不同深度区间各选一个分支起点，区间内随机，避免每张图都按固定层数重复。
                int sections = Math.Max(1, Math.Min(context.Config.PathCount - 1, rows - 2));
                // 给最晚的分支至少留下三条边，即两层独立节点，再接入首领或已有路线。
                int lastStart = Math.Max(0, rows - 4);
                branchRows = new int[sections];
                for (int i = 0; i < sections; i++)
                {
                    int min = Math.Min(lastStart, 1 + i * lastStart / sections);
                    int max = Math.Max(min, (i + 1) * lastStart / sections);
                    if (i > 0) max = Math.Min(max, branchRows[i - 1] + 4);
                    min = Math.Min(min, max);
                    branchRows[i] = context.Random.Range(min, max + 1);
                }
            }

            /// <summary>保留已完成路径，后续路径可复用已有边；有效路径数量仍由原校验器决定。</summary>
            public MapGraph Generate()
            {
                int start = columns / 2; references[start] = 1;
                // PathCount 是基础铺路次数；严格接点约束下合流较少，需要允许补足最低有效路径数。
                // 不降低 MinPathCount，也不能因固定铺六次而让所有尝试都稳定卡在六条有效路径。
                int pathLimit = aligned ? Math.Max(1, context.Config.PathCount) : Math.Max(1, Math.Max(context.Config.PathCount, context.Config.MinPathCount));
                for (int path = 0; path < pathLimit; path++)
                {
                    // 把新增分支分散到全图，不能每次都从起点寻找最早可用接点。
                    // 首条路径仍优先直连；后续路径先沿已有边抵达目标深度，再寻找合法分支。
                    preferredBranchRow = path == 0 ? 0 : branchRows[(path - 1) % branchRows.Length];
                    firstPath = path == 0;
                    budget = columnGuide == null ? 4096 : 20000;
                    if (!Walk(start, -1))
                    {
                        if (aligned) throw new InvalidOperationException("第 " + (path + 1) + " 条路径无合法资产接法或发生像素冲突。");
                        if (path == 0) throw new InvalidOperationException("首条路径无合法资产接法或发生像素冲突。");
                        break;
                    }
                    if (!aligned && path + 1 >= Math.Max(1, context.Config.PathCount) && CountCompletePaths(start) >= context.Config.MinPathCount) break;
                }
                var graph = new MapGraph { Rows = rows, Columns = columns, AssetLayout = new MapAssetLayout { ProfileHash = profile.ComputeHash() } };
                var ids = new Dictionary<int, int>();
                for (int i = 0; i < points.Length; i++)
                {
                    if (references[i] == 0) continue;
                    bool boss = context.Config.IncludeBossNode && i == goal;
                    var node = graph.AddNode(i / columns, i % columns, i == start ? context.Config.StartTypeKey : boss ? context.Config.BossTypeKey : NodeTypeKeys.Unassigned);
                    node.IsExemptFromRules = i == start || boss; ids[i] = node.Id;
                    graph.AssetLayout.Nodes.Add(new MapNodePlacement { NodeId = node.Id, X = points[i].X, Y = points[i].Y });
                    if (i == start) graph.StartNodeId = node.Id;
                }
                foreach (var candidate in edges)
                {
                    var e = candidate.Edge; graph.AddEdge(ids[e.FromId], ids[e.ToId]);
                    graph.AssetLayout.Edges.Add(new MapEdgePlacement { FromId = ids[e.FromId], ToId = ids[e.ToId], TemplateId = e.TemplateId,
                        FootprintId = e.FootprintId, SourceSide = e.SourceSide, TargetSide = e.TargetSide, X = e.X, Y = e.Y });
                }
                return graph;
            }

            /// <summary>记录本次分支的起始层，至少经过两个独立节点后才能合流；失败时完整回溯。</summary>
            private bool Walk(int from, int branchStartRow)
            {
                if (--budget < 0) return false;
                bool added = branchStartRow >= 0;
                if (from / columns == rows - 1) return added && (!context.Config.IncludeBossNode || from == goal);
                var choices = new List<Candidate>(GetCandidates(from)); context.Random.Shuffle(choices);
                // 离线烘焙可指定主路径所经列；仍完整执行原图接点和像素避让。
                if (firstPath && columnGuide != null) choices.RemoveAll(c => c.Edge.ToId % columns != columnGuide[c.Edge.ToId / columns]);
                // 先尝试正上方目标，即对目标而言来自正下方的连接；同侧被占后，其他路径只能使用剩余接点。
                // 保留同组的随机顺序，不能用不稳定排序破坏相同种子的可复现性。
                // 抵达本次分支深度前只沿旧边前进；到达后优先新增合法直连，再尝试斜连。
                // 不能从起点一律优先新增，也不能一律沿旧路走到底，否则分支会集中在地图一端。
                var fresh = choices.FindAll(c => !byPair.ContainsKey(Pair(from, c.Edge.ToId)));
                var reused = choices.FindAll(c => byPair.ContainsKey(Pair(from, c.Edge.ToId)));
                if (!added && from / columns < preferredBranchRow) fresh.Clear();
                var ordered = new List<Candidate>();
                if (added)
                {
                    // 分支满足独立长度后才允许合流；合流之后复用已有路线，不重复铺到首领。
                    ordered.AddRange(reused);
                    ordered.AddRange(fresh.FindAll(c => references[c.Edge.ToId] > 0));
                    ordered.AddRange(fresh.FindAll(c => references[c.Edge.ToId] == 0 && c.Edge.ToId % columns == from % columns));
                    ordered.AddRange(fresh.FindAll(c => references[c.Edge.ToId] == 0 && c.Edge.ToId % columns != from % columns));
                }
                else
                {
                    ordered.AddRange(fresh.FindAll(c => c.Edge.ToId % columns == from % columns));
                    ordered.AddRange(fresh.FindAll(c => c.Edge.ToId % columns != from % columns));
                    ordered.AddRange(reused);
                }
                choices = ordered;
                var visitedExisting = new HashSet<int>();
                foreach (var candidate in choices)
                {
                    int to = candidate.Edge.ToId; long pair = Pair(from, to);
                    if (byPair.TryGetValue(pair, out Candidate existing))
                    {
                        if (visitedExisting.Add(to) && Walk(to, branchStartRow)) return true;
                        continue;
                    }
                    int startRow = added ? branchStartRow : from / columns;
                    if (references[to] > 0 && to / columns - startRow < 3) continue;
                    if (!CanAdd(candidate)) continue;
                    Add(candidate);
                    if (Walk(to, startRow)) return true;
                    Remove(candidate);
                    if (budget < 0) break;
                }
                return false;
            }

            /// <summary>按资产锚点求候选平移，不把斜向坐标硬编码为左右接点。</summary>
            private List<Candidate> GetCandidates(int from)
            {
                if (candidates.TryGetValue(from, out var cached)) return cached;
                var list = new List<Candidate>(); candidates[from] = list;
                int row = from / columns, col = from % columns;
                if (row >= rows - 1) return list;
                foreach (var template in profile.Templates)
                {
                    if (aligned && !MapRouteSocketSelector.HasUsableEndpoints(template)) continue;
                    // 同列连入固定占用目标下侧；斜向入线只能使用资产支持的下、左、右接点。
                    if (template.TargetSide == MapSocketSide.Up || template.ColumnDelta == 0 &&
                        (template.SourceSide != MapSocketSide.Up || template.TargetSide != MapSocketSide.Down)) continue;
                    int nextCol = col + template.ColumnDelta;
                    if (nextCol < 0 || nextCol >= columns) continue;
                    if (context.Config.IncludeBossNode && Math.Abs(nextCol - columns / 2) > rows - row - 2) continue;
                    int to = (row + 1) * columns + nextCol;
                    foreach (var placement in MapAssetGeometry.GetPlacements(profile, template, points[from], points[to]))
                        list.Add(new Candidate { Id = nextCandidateId++, Edge = placement });
                }
                return list;
            }

            /// <summary>同时检查两端方向、无关节点和所有既有线路，包括装饰线的占用。</summary>
            private bool CanAdd(Candidate candidate)
            {
                var e = candidate.Edge; int from = e.FromId, to = e.ToId;
                if (outgoing[from] >= context.Config.MaxOutDegree || incoming[to] >= 3 || (sockets[from] & (1 << (int)e.SourceSide)) != 0 || (sockets[to] & (1 << (int)e.TargetSide)) != 0) return false;
                // 已先接斜线的目标不再补同列直线，回溯选择其他合流点，保证正下方直连先占下侧。
                // 分支延后到不同深度时，不能因下口暂时空着就倒置既定接入顺序。
                if (from % columns == to % columns && incoming[to] > 0) return false;
                for (int i = 0; i < points.Length; i++)
                {
                    if (references[i] == 0 || i == from || i == to) continue;
                    if (MapAssetGeometry.HitsNode(profile, e, points[i])) return false;
                    if (references[to] == 0 && MapAssetGeometry.NodesOverlap(profile, points[i], points[to])) return false;
                }
                foreach (var old in edges)
                {
                    var b = old.Edge;
                    if (aligned && b.FromId / columns == from / columns &&
                        (b.FromId % columns - from % columns) * (b.ToId % columns - to % columns) < 0) return false;
                    if (references[to] == 0 && MapAssetGeometry.HitsNode(profile, b, points[to])) return false;
                    int common = b.FromId == from || b.ToId == from ? from : b.FromId == to || b.ToId == to ? to : -1;
                    long key = Pair(Math.Min(candidate.Id, old.Id), Math.Max(candidate.Id, old.Id));
                    if (!collisionCache.TryGetValue(key, out bool collision))
                    { collision = MapAssetGeometry.EdgesConflict(profile, e, b, common < 0 ? null : points[common]); collisionCache[key] = collision; }
                    if (collision) return false;
                }
                return true;
            }

            /// <summary>接受候选，入线和出线使用同一组四方向位标志。</summary>
            private void Add(Candidate candidate)
            {
                var e = candidate.Edge; edges.Add(candidate); byPair.Add(Pair(e.FromId, e.ToId), candidate);
                sockets[e.FromId] |= 1 << (int)e.SourceSide; sockets[e.ToId] |= 1 << (int)e.TargetSide;
                references[e.FromId]++; references[e.ToId]++; outgoing[e.FromId]++; incoming[e.ToId]++;
            }

            /// <summary>回溯撤销当前路径新增的边，释放接点和新节点引用。</summary>
            private void Remove(Candidate candidate)
            {
                var e = candidate.Edge; edges.RemoveAt(edges.Count - 1); byPair.Remove(Pair(e.FromId, e.ToId));
                sockets[e.FromId] &= ~(1 << (int)e.SourceSide); sockets[e.ToId] &= ~(1 << (int)e.TargetSide);
                references[e.FromId]--; references[e.ToId]--; outgoing[e.FromId]--; incoming[e.ToId]--;
            }

            /// <summary>组合两个索引，避免字符串键和运行时哈希影响确定性。</summary>
            private static long Pair(int a, int b) => ((long)a << 32) | (uint)b;

            /// <summary>计数已完成路线，达到基础铺路次数及最低路径数量后及时停止扩展。</summary>
            private long CountCompletePaths(int start)
            {
                var ways = new long[points.Length];
                long cap = (long)Math.Max(context.Config.MaxPathCount, context.Config.MinPathCount) + 1;
                for (int id = points.Length - 1; id >= 0; id--)
                {
                    if (references[id] == 0) continue;
                    if (id / columns == rows - 1) { ways[id] = 1; continue; }
                    foreach (var candidate in edges)
                        if (candidate.Edge.FromId == id) ways[id] = Math.Min(cap, ways[id] + ways[candidate.Edge.ToId]);
                }
                return ways[start];
            }
        }
    }

    /// <summary>最终独立检查接点和像素，不依赖搜索器内部的缓存或判断顺序。</summary>
    public sealed class AssetLayoutValidator : IMapValidator
    {
        private readonly MapRouteProfile profile;
        public string Name => "资产布局几何";
        /// <summary>复用同一烘焙配置校验结果。</summary>
        public AssetLayoutValidator(MapRouteProfile profile) { this.profile = profile; }
        /// <summary>非法布局交给现有生成重试流程处理。</summary>
        public void Validate(MapGraph graph, MapGenerationContext context, MapValidationReport report)
        { if (!MapAssetGeometry.Validate(graph, profile, out string error)) report.Add(error); }
    }
}
