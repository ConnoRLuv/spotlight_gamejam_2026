using System;
using System.Collections.Generic;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;

namespace RoguelikeMap.Rules
{
    /// <summary>规则判定的结果。Deny 时带有原因，方便在生成日志里定位问题。</summary>
    public readonly struct RuleVerdict
    {
        public readonly bool Allowed;
        public readonly string Reason;

        RuleVerdict(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public static RuleVerdict Allow => new RuleVerdict(true, null);
        public static RuleVerdict Deny(string reason) => new RuleVerdict(false, reason);
    }

    /// <summary>
    /// 一条房间类型约束。
    ///
    /// 【扩展点】新增约束完全不需要改生成器：实现这个接口，然后 Add 到 MapRuleSet 里。
    /// node 是待判定的节点，candidateTypeKey 是这一次想放进来的类型。
    /// 判定时可以读取 node.PrevIds 指向的父节点（父节点在自底向上的分配里一定已经定好类型了）。
    /// </summary>
    public interface IMapRule
    {
        string Name { get; }
        RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context);
    }

    /// <summary>规则的顺序集合。任何一条 Deny 就整体 Deny。</summary>
    public sealed class MapRuleSet
    {
        readonly List<IMapRule> _rules = new List<IMapRule>(8);

        public IReadOnlyList<IMapRule> Rules => _rules;

        public MapRuleSet Add(IMapRule rule)
        {
            if (rule != null) _rules.Add(rule);
            return this;
        }

        public MapRuleSet Remove(IMapRule rule)
        {
            _rules.Remove(rule);
            return this;
        }

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            // 显式声明跳过规则的节点（例如最后挂上去的 Boss）直接放行。
            // 放在这里而不是散落在每条规则里，是为了保证"豁免"只有一个定义。
            if (node != null && node.IsExemptFromRules) return RuleVerdict.Allow;

            for (int i = 0; i < _rules.Count; i++)
            {
                RuleVerdict verdict = _rules[i].Evaluate(node, candidateTypeKey, context);
                if (!verdict.Allowed) return verdict;
            }
            return RuleVerdict.Allow;
        }

        /// <summary>
        /// 杀戮尖塔那套约束的默认组合。
        /// 注意：这里只是"一套常见配置"，不是硬编码进生成器的规则，
        /// 你完全可以 CreateEmpty() 然后自己拼一套完全不同的规则。
        /// </summary>
        public static MapRuleSet CreateDefault()
        {
            var set = new MapRuleSet();

            // 精英 / 商人 不能在同一条路径上连续出现两次
            set.Add(new NoConsecutiveSameTypeRule(NodeTypeKeys.Elite, NodeTypeKeys.Shop));

            // 前 5 层不出精英和商人（权重表里也设了 MinRow，这里是硬保险）
            set.Add(new MinRowRule(NodeTypeKeys.Elite, 5));
            set.Add(new MinRowRule(NodeTypeKeys.Shop, 5));

            // Boss 只由"挂载 Boss"步骤产生，不允许被随机分配
            set.Add(new AllowedRowsOnlyRule(NodeTypeKeys.Boss, new List<int>()));

            // 数量上限。商人的固定层（Boss 前一整层）也会计入总数，
            // 所以给到 6，既能保证 Boss 前有商人，又允许中段零星再出现一两个。
            set.Add(new MaxTypeCountRule(NodeTypeKeys.Elite, 7));
            set.Add(new MaxTypeCountRule(NodeTypeKeys.Shop, 6));

            return set;
        }

        public static MapRuleSet CreateEmpty() => new MapRuleSet();
    }

    // ---------------------------------------------------------------------
    // 内置规则。全部是无状态可复用的，想加新规则照着抄一个即可。
    // ---------------------------------------------------------------------

    /// <summary>
    /// 指定类型不能与自己在同一条路径上相邻。
    ///
    /// 必须**双向**检查父节点和子节点。原因：固定层是提前赋值的（例如最后一层全是商人），
    /// 当生成器自底向上处理到倒数第二层时，它的子节点其实已经定好类型了。
    /// 只查父节点会漏掉"我变成商人，而我的孩子已经是商人"这种违规。
    /// </summary>
    public sealed class NoConsecutiveSameTypeRule : IMapRule
    {
        readonly HashSet<string> _types;

        public NoConsecutiveSameTypeRule(params string[] types)
        {
            _types = new HashSet<string>(StringComparer.Ordinal);
            if (types != null)
                for (int i = 0; i < types.Length; i++) _types.Add(types[i]);
        }

        public string Name => "禁止同类相邻";

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            if (!_types.Contains(candidateTypeKey)) return RuleVerdict.Allow;

            for (int i = 0; i < node.PrevIds.Count; i++)
            {
                MapNode parent = context.Graph.Get(node.PrevIds[i]);
                if (parent != null && parent.TypeKey == candidateTypeKey)
                    return RuleVerdict.Deny(candidateTypeKey + " 与父节点 #" + parent.Id + " 同类");
            }

            for (int i = 0; i < node.NextIds.Count; i++)
            {
                MapNode child = context.Graph.Get(node.NextIds[i]);
                if (child != null && child.TypeKey == candidateTypeKey)
                    return RuleVerdict.Deny(candidateTypeKey + " 与子节点 #" + child.Id + " 同类");
            }
            return RuleVerdict.Allow;
        }
    }

    /// <summary>禁止两个类型在同一条路径上相邻，例如"商人旁边不能是精英"。同样是双向检查。</summary>
    public sealed class NoAdjacentPairRule : IMapRule
    {
        readonly string _a;
        readonly string _b;

        public NoAdjacentPairRule(string a, string b)
        {
            _a = a;
            _b = b;
        }

        public string Name => "禁止相邻对 " + _a + "/" + _b;

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            for (int i = 0; i < node.PrevIds.Count; i++)
            {
                MapNode parent = context.Graph.Get(node.PrevIds[i]);
                if (parent == null) continue;
                string p = parent.TypeKey;
                if ((candidateTypeKey == _a && p == _b) || (candidateTypeKey == _b && p == _a))
                    return RuleVerdict.Deny(_a + " 与 " + _b + " 相邻");
            }

            for (int i = 0; i < node.NextIds.Count; i++)
            {
                MapNode child = context.Graph.Get(node.NextIds[i]);
                if (child == null) continue;
                string c = child.TypeKey;
                if ((candidateTypeKey == _a && c == _b) || (candidateTypeKey == _b && c == _a))
                    return RuleVerdict.Deny(_a + " 与 " + _b + " 相邻");
            }
            return RuleVerdict.Allow;
        }
    }

    /// <summary>某类型只能从指定层开始出现。</summary>
    public sealed class MinRowRule : IMapRule
    {
        readonly string _typeKey;
        readonly int _minRow;

        public MinRowRule(string typeKey, int minRow)
        {
            _typeKey = typeKey;
            _minRow = minRow;
        }

        public string Name => _typeKey + " 最早出现在第 " + _minRow + " 层";

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            if (candidateTypeKey != _typeKey) return RuleVerdict.Allow;
            return node.Row >= _minRow
                ? RuleVerdict.Allow
                : RuleVerdict.Deny(_typeKey + " 不允许出现在第 " + node.Row + " 层");
        }
    }

    /// <summary>某类型只允许出现在白名单里的层。</summary>
    public sealed class AllowedRowsOnlyRule : IMapRule
    {
        readonly string _typeKey;
        readonly HashSet<int> _allowedRows;

        public AllowedRowsOnlyRule(string typeKey, IEnumerable<int> allowedRows)
        {
            _typeKey = typeKey;
            _allowedRows = new HashSet<int>();
            if (allowedRows != null)
                foreach (int r in allowedRows) _allowedRows.Add(r);
        }

        public string Name => _typeKey + " 只允许出现在固定层";

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            if (candidateTypeKey != _typeKey) return RuleVerdict.Allow;
            return _allowedRows.Contains(node.Row)
                ? RuleVerdict.Allow
                : RuleVerdict.Deny(_typeKey + " 不允许出现在第 " + node.Row + " 层");
        }
    }

    /// <summary>限制某类型在整张图上的数量上限。</summary>
    public sealed class MaxTypeCountRule : IMapRule
    {
        readonly string _typeKey;
        readonly int _maxCount;

        public MaxTypeCountRule(string typeKey, int maxCount)
        {
            _typeKey = typeKey;
            _maxCount = maxCount;
        }

        public string Name => _typeKey + " 数量不超过 " + _maxCount;

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            if (candidateTypeKey != _typeKey) return RuleVerdict.Allow;

            // 统计时必须排除"待判定的这个节点自己"。
            // 否则在最终校验阶段，一个已经就位的精英会把自己算进去，
            // 于是 count == maxCount 被误判为超限，产生假阳性。
            int count = 0;
            IReadOnlyList<MapNode> nodes = context.Graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TypeKey != _typeKey) continue;
                if (node != null && nodes[i].Id == node.Id) continue;
                count++;
            }

            return count + 1 <= _maxCount
                ? RuleVerdict.Allow
                : RuleVerdict.Deny(_typeKey + " 已达数量上限 " + _maxCount);
        }
    }

    /// <summary>
    /// 用委托临时拼一条规则，适合"这次实验想试个新约束"而不想新建类。
    /// 缺点是委托不能被 ScriptableObject 序列化，长期功能还是应该写成独立类。
    /// </summary>
    public sealed class DelegateRule : IMapRule
    {
        readonly Func<MapNode, string, MapGenerationContext, bool> _predicate;
        readonly string _reason;

        public DelegateRule(string name, string reason,
            Func<MapNode, string, MapGenerationContext, bool> predicate)
        {
            Name = name;
            _reason = reason;
            _predicate = predicate;
        }

        public string Name { get; }

        public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext context)
        {
            return _predicate(node, candidateTypeKey, context)
                ? RuleVerdict.Allow
                : RuleVerdict.Deny(_reason);
        }
    }
}
