using System;
using System.Collections.Generic;

namespace RoguelikeMap.Model
{
    /// <summary>
    /// 内置房间类型键。
    ///
    /// 【扩展点】新增房间类型**不需要**修改这个类，也不需要改任何 switch：
    /// 只要在 Catalog 里 Register 一个新的 key，再在配置的权重表里加一条即可。
    /// 这里保留常量只是方便代码里引用内置类型。
    /// </summary>
    public static class NodeTypeKeys
    {
        public const string Unassigned = "unassigned";
        public const string Start = "start";
        public const string Monster = "monster";
        public const string Elite = "elite";
        public const string Event = "event";
        public const string Shop = "shop";
        public const string Boss = "boss";
    }

    /// <summary>标签名常量。规则系统通过标签批量命中一组类型，避免硬编码具体类型。</summary>
    public static class NodeTypeTags
    {
        public const string Combat = "combat";
        public const string Safe = "safe";
        public const string Reward = "reward";
        public const string Terminal = "terminal";
        public const string HighRisk = "highRisk";
    }

    /// <summary>一种房间类型的定义。表现层需要的数据（颜色、图标、预制体）挂在 VisualPayload 上。</summary>
    public sealed class NodeTypeDefinition
    {
        public string Key;
        public string DisplayName;

        /// <summary>仅用于稳定排序，保证结果与字典枚举顺序无关。</summary>
        public int SortOrder;

        public readonly List<string> Tags = new List<string>(2);

        /// <summary>表现层数据。核心逻辑不读它。</summary>
        public object VisualPayload;

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        public override string ToString() => Key + "(" + DisplayName + ")";
    }

    /// <summary>
    /// 房间类型注册表。生成流程里所有关于"这个类型是什么"的问题都问它。
    /// 新增类型 = 调用 Add(...)，零代码改动、零编译。
    /// </summary>
    public sealed class NodeTypeCatalog
    {
        readonly Dictionary<string, NodeTypeDefinition> _defs =
            new Dictionary<string, NodeTypeDefinition>(StringComparer.Ordinal);

        public int Count => _defs.Count;

        public NodeTypeCatalog Add(string key, string displayName, params string[] tags)
        {
            var def = new NodeTypeDefinition
            {
                Key = key,
                DisplayName = displayName,
                SortOrder = _defs.Count
            };
            if (tags != null)
                for (int i = 0; i < tags.Length; i++) def.Tags.Add(tags[i]);

            _defs[key] = def;
            return this;
        }

        public NodeTypeCatalog Add(NodeTypeDefinition def)
        {
            if (def != null && !string.IsNullOrEmpty(def.Key)) _defs[def.Key] = def;
            return this;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _defs.ContainsKey(key);
        }

        public NodeTypeDefinition Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            NodeTypeDefinition def;
            return _defs.TryGetValue(key, out def) ? def : null;
        }

        public string GetDisplayName(string key)
        {
            NodeTypeDefinition def = Get(key);
            return def != null ? def.DisplayName : key;
        }

        public bool HasTag(string key, string tag)
        {
            NodeTypeDefinition def = Get(key);
            return def != null && def.HasTag(tag);
        }

        /// <summary>按 SortOrder 稳定排序后返回，保证结果可复现。</summary>
        public List<NodeTypeDefinition> AllSorted()
        {
            var list = new List<NodeTypeDefinition>(_defs.Values);
            list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return list;
        }

        public List<string> AllKeys()
        {
            List<NodeTypeDefinition> list = AllSorted();
            var keys = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++) keys.Add(list[i].Key);
            return keys;
        }

        /// <summary>默认的杀戮尖塔式类型集合。</summary>
        public static NodeTypeCatalog CreateDefault()
        {
            return new NodeTypeCatalog()
                .Add(NodeTypeKeys.Start, "起点")
                .Add(NodeTypeKeys.Monster, "战斗", NodeTypeTags.Combat)
                .Add(NodeTypeKeys.Elite, "精英", NodeTypeTags.Combat, NodeTypeTags.HighRisk)
                .Add(NodeTypeKeys.Event, "事件", NodeTypeTags.Safe)
                .Add(NodeTypeKeys.Shop, "商人", NodeTypeTags.Safe)
                .Add(NodeTypeKeys.Boss, "首领", NodeTypeTags.Combat, NodeTypeTags.Terminal, NodeTypeTags.HighRisk)
                .Add(NodeTypeKeys.Unassigned, "未分配");
        }
    }
}
