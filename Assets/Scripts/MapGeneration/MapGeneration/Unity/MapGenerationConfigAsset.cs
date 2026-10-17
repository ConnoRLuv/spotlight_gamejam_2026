using System.Collections.Generic;
using RoguelikeMap.Config;
using RoguelikeMap.Model;
using RoguelikeMap.Generation;
using RoguelikeMap.Serialization;
using UnityEngine;

namespace RoguelikeMap.Unity
{
    /// <summary>烘焙占用标识与显示图片的绑定；纯数据生成器不保存 Sprite 引用。</summary>
    [System.Serializable]
    public sealed class MapConstraintSpriteBinding { public string FootprintId; public Sprite Sprite; }

    // 一种房间类型在编辑器里配置的表现数据。核心逻辑完全不读它。
    [System.Serializable]
    public sealed class NodeTypeVisual
    {
        [Tooltip("必须和核心 NodeTypeKeys 里的键一致")]
        public string TypeKey;

        public string DisplayName;

        [Tooltip("核心规则系统会用到这些标签，例如 combat / safe / reward")]
        public List<string> Tags = new List<string>();

        public Color Color = Color.white;

        [Tooltip("节点底图，一般直接放房间图标。留空则画一个纯色圆")]
        public Sprite Icon;

        [Tooltip("叠加在底图上的小图标，可选")]
        public Sprite OverlayIcon;

        [Tooltip("留空则用 MapGraphView 上的默认节点预制体")]
        public GameObject CustomPrefab;
    }

    // 一条路线（相邻两层之间的一段连线）的表现数据。
    //
    // 两个端点用图片里的归一化坐标描述，y 轴向上（0 = 图片下边）。
    // 这些数值可以手填，也可以让 Tools/地图 菜单里的工具按图片透明边自动算出来。
    [System.Serializable]
    public sealed class MapRouteVisual
    {
        public MapRouteDirection Direction;

        [Tooltip("路线片段图片，按方向各一张")]
        public Sprite Sprite;

        [Tooltip("留空则用 MapGraphView 上的默认连线预制体")]
        public GameObject Prefab;

        [Tooltip("起点端在图片里的归一化位置。只有走代码兜底时才读它（预制体的 pivot 才是准的）")]
        public Vector2 SourceEnd = new Vector2(0.5f, 0f);

        [Tooltip("终点端在图片里的归一化位置")]
        public Vector2 TargetEnd = new Vector2(0.5f, 1f);

        [Tooltip("原尺寸模式的实际像素接线口，由校准工具读取透明边缘生成。")]
        public Vector2[] SourcePorts = System.Array.Empty<Vector2>();
        public Vector2[] TargetPorts = System.Array.Empty<Vector2>();
    }

    // 地图生成配置资产。
    //
    // 所有可调参数都在这里，改数值不需要重新编译。美术（图标、预制体、路线图）也挂在这里，
    // 所以场景里只要引用这一个资产，换整套皮不用动场景。
    //
    // 新增房间类型的完整流程：
    //   1. 在 _visuals 里加一条，填 TypeKey、颜色、图标
    //   2. 在 _config.Weights 里加一条同名的权重
    // 全程不写代码。
    [CreateAssetMenu(menuName = "Roguelike Map/Generation Config", fileName = "MapGenerationConfig")]
    public sealed class MapGenerationConfigAsset : ScriptableObject
    {
        [SerializeField]
        private MapGenerationConfig _config = MapGenerationConfig.CreateDefault();

        [SerializeField]
        private List<NodeTypeVisual> _visuals = new List<NodeTypeVisual>();

        [Header("路线美术")]
        [Tooltip("整张表留空也能跑：会退化成代码画的直线段")]
        [SerializeField]
        private List<MapRouteVisual> _routes = new List<MapRouteVisual>();

        [Header("资产连接约束")]
        [SerializeField] private bool _useAssetConstraints;
        [SerializeField] private MapRouteProfile _routeProfile;
        [SerializeField] private List<MapConstraintSpriteBinding> _constraintSprites = new List<MapConstraintSpriteBinding>();

        public bool UseAssetConstraints => _useAssetConstraints;
        public MapRouteProfile RouteProfile => _routeProfile;

        [Header("离线地图池")]
        [SerializeField] private bool _usePreGeneratedMaps;
        [SerializeField] private List<MapPresetPrefab> _preGeneratedMaps = new List<MapPresetPrefab>();
        public bool UsePreGeneratedMaps => _usePreGeneratedMaps;
        public IReadOnlyList<MapPresetPrefab> PreGeneratedMaps => _preGeneratedMaps;

        /// <summary>烘焙全部成功后一次绑定，启用后缺失地图也不退回实时生成。</summary>
        public void ConfigurePreGeneratedMaps(List<MapPresetPrefab> maps)
        {
            if (maps == null || maps.Count != 10 || maps.Exists(m => m == null))
                throw new System.ArgumentException("离线地图池必须包含 10 张完整地图。");
            _preGeneratedMaps = new List<MapPresetPrefab>(maps);
            _usePreGeneratedMaps = true;
        }

        /// <summary>0 随机选择；非零选择器稳定映射到地图编号。每次还原独立对象，不进行铺路。</summary>
        public MapGenerationResult LoadPreGeneratedMap(uint selector = 0u, uint roomSeed = 0u)
        {
            if (_preGeneratedMaps.Count != 10)
                return new MapGenerationResult(null, false, new[] { "离线地图池需要 10 张地图，不进行实时生成。" });
            int index = selector == 0u ? Random.Range(0, _preGeneratedMaps.Count) : (int)((selector - 1u) % (uint)_preGeneratedMaps.Count);
            try
            {
                var source = _preGeneratedMaps[index];
                var preset = source != null ? source.Data : null;
                int expectedPaths = preset != null && preset.PathCount > 0 ? preset.PathCount : Config.PathCount;
                int settingsHash = source != null && source.RandomizeRooms ? MapPresetFingerprint.ComputeLayout(Config, _routeProfile, expectedPaths) :
                    MapPresetFingerprint.Compute(Config, _routeProfile, expectedPaths);
                if (preset == null || preset.Version != 1 || preset.Graph == null || preset.Graph.AssetLayout == null ||
                    _routeProfile == null || preset.SettingsHash != settingsHash ||
                    preset.Graph.AssetLayout.ProfileHash != _routeProfile.ComputeHash() ||
                    preset.Paths.Count != expectedPaths)
                    throw new System.InvalidOperationException("地图缺失或配置已变更，请重新烘焙。");
                if (source.RandomizeRooms)
                {
                    uint seed = roomSeed == 0 ? (uint)Random.Range(1, int.MaxValue) : roomSeed;
                    var assigned = MapRoomDistributor.Assign(preset.Graph, Config, BuildCatalog(), seed);
                    return new MapGenerationResult(assigned.Graph, assigned.Success, new[] { "读取空白地图 " + (index + 1) + "/10：" + source.name,
                        "房间分布种子=" + seed, assigned.DiagnosticsText });
                }
                return new MapGenerationResult(MapSerializer.FromDto(preset.Graph), true, new[] { "读取离线地图 " + (index + 1) + "/10：" + source.name });
            }
            catch (System.Exception error)
            {
                return new MapGenerationResult(null, false, new[] { "离线地图读取失败：" + error.Message });
            }
        }

        public MapPresetPrefab FindPreGeneratedMapPrefab(MapGraph graph)
        {
            foreach (var prefab in _preGeneratedMaps)
                if (prefab != null && prefab.Matches(graph)) return prefab;
            return null;
        }

        /// <summary>一次接收完整烘焙结果，避免 JSON 覆写与序列化缓存混用后丢失像素或模板列表。</summary>
        public void ConfigureAssetConstraints(MapRouteProfile profile, List<MapConstraintSpriteBinding> sprites)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (sprites == null) throw new System.ArgumentNullException(nameof(sprites));
            _routeProfile = profile;
            _constraintSprites = new List<MapConstraintSpriteBinding>(sprites);
            _useAssetConstraints = true;
            _config.Columns = 5;
        }

        /// <summary>用稳定占用标识取得原图，显示层不再按空间方向猜测资源。</summary>
        public Sprite FindConstraintSprite(string id) => _constraintSprites.Find(b => b.FootprintId == id)?.Sprite;

        /// <summary>使用具备足够引入长度的真实端点组，排除贴着图片边缘的长线身。</summary>
        public Dictionary<string, MapRouteEndCoverage> BuildRouteEndpointCoverage()
        {
            var result = new Dictionary<string, MapRouteEndCoverage>();
            if (_routeProfile == null) return result;
            foreach (var template in _routeProfile.Templates)
            {
                if (!MapRouteSocketSelector.HasUsableEndpoints(template) || result.ContainsKey(template.FootprintId)) continue;
                result[template.FootprintId] = new MapRouteEndCoverage(
                    MapRouteSocketSelector.PortBounds(template.Source), MapRouteSocketSelector.PortBounds(template.Target));
            }
            return result;
        }

        /// <summary>可选资产接点管线；关闭时使用逐层铺路、生成时禁止交叉的管线。</summary>
        public RoguelikeMap.Generation.MapGenerator CreateGenerator() => _useAssetConstraints
            ? RoguelikeMap.Generation.MapGenerator.CreateAssetConstrained(_routeProfile)
            : RoguelikeMap.Generation.MapGenerator.CreateLayered(_routeProfile);

        /// <summary>资产约束配置的指纹包含烘焙图片数据，旧地图配置的指纹不变。</summary>
        public int GenerationHash() => _routeProfile != null
            ? unchecked(Config.ComputeHash() * 31 + _routeProfile.ComputeHash()) : Config.ComputeHash();

        [Header("背景与底纹")]
        [Tooltip("背景插画：铺满整个地图区域，不跟着地图滚动")]
        public Sprite BackgroundSprite;

        [Tooltip("背景插画保持原比例（多余的地方留空）。关掉就是拉满")]
        public bool BackgroundPreserveAspect = true;

        public Color BackgroundTint = Color.white;

        [Tooltip("底纹：单张拉伸铺满整个地图区域，跟着地图滚动，不重复平铺。")]
        public Sprite BackdropSprite;

        [Tooltip("旧版重复平铺参数，仅保留序列化兼容；当前单张铺满模式不读取。")]
        [Range(0.02f, 1f)]
        public float BackdropTileScale = 0.25f;

        [Tooltip("底纹比地图四周各大出多少像素（也是两边留给背景插画的宽度）")]
        public float BackdropPadding = 150f;

        [Range(0f, 1f)]
        public float BackdropAlpha = 1f;

        public MapGenerationConfig Config => _config;
        public IReadOnlyList<NodeTypeVisual> Visuals => _visuals;
        public IReadOnlyList<MapRouteVisual> Routes => _routes;

        // 用编辑器里配置的类型表构建运行时目录。
        public NodeTypeCatalog BuildCatalog()
        {
            var catalog = new NodeTypeCatalog();

            for (int i = 0; i < _visuals.Count; i++)
            {
                NodeTypeVisual v = _visuals[i];
                if (string.IsNullOrEmpty(v.TypeKey)) continue;

                var def = new NodeTypeDefinition
                {
                    Key = v.TypeKey,
                    DisplayName = string.IsNullOrEmpty(v.DisplayName) ? v.TypeKey : v.DisplayName,
                    SortOrder = i,
                    VisualPayload = v
                };
                if (v.Tags != null)
                    for (int t = 0; t < v.Tags.Count; t++) def.Tags.Add(v.Tags[t]);

                catalog.Add(def);
            }

            // 编辑器里配漏了内置类型会导致生成器找不到候选，这里兜底补齐
            NodeTypeCatalog fallback = NodeTypeCatalog.CreateDefault();
            List<NodeTypeDefinition> all = fallback.AllSorted();
            for (int i = 0; i < all.Count; i++)
                if (!catalog.Contains(all[i].Key)) catalog.Add(all[i]);

            return catalog;
        }

        public NodeTypeVisual GetVisual(string typeKey)
        {
            for (int i = 0; i < _visuals.Count; i++)
                if (_visuals[i].TypeKey == typeKey) return _visuals[i];
            return null;
        }

        public MapRouteVisual GetRoute(MapRouteDirection direction)
        {
            for (int i = 0; i < _routes.Count; i++)
                if (_routes[i].Direction == direction) return _routes[i];
            return null;
        }

        // 把六个方向补齐，跑生成工具前用。
        public void EnsureRouteSlots()
        {
            for (int d = 0; d <= (int)MapRouteDirection.DownLeft; d++)
            {
                var direction = (MapRouteDirection)d;
                if (GetRoute(direction) == null)
                    _routes.Add(new MapRouteVisual { Direction = direction });
            }
        }

        // 第一次创建资产时把默认类型表填好，省得手动录入。
        private void Reset()
        {
            _visuals.Clear();
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Start, DisplayName = "起点", Color = new Color(0.3f, 0.85f, 0.4f) });
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Monster, DisplayName = "战斗", Tags = new List<string> { NodeTypeTags.Combat }, Color = new Color(0.75f, 0.75f, 0.75f) });
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Elite, DisplayName = "精英", Tags = new List<string> { NodeTypeTags.Combat, NodeTypeTags.HighRisk }, Color = new Color(0.85f, 0.3f, 0.3f) });
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Event, DisplayName = "事件", Tags = new List<string> { NodeTypeTags.Safe }, Color = new Color(0.4f, 0.7f, 0.9f) });
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Shop, DisplayName = "商人", Tags = new List<string> { NodeTypeTags.Safe }, Color = new Color(0.95f, 0.8f, 0.35f) });
            _visuals.Add(new NodeTypeVisual { TypeKey = NodeTypeKeys.Boss, DisplayName = "首领", Tags = new List<string> { NodeTypeTags.Combat, NodeTypeTags.Terminal }, Color = new Color(0.6f, 0.2f, 0.6f) });

            _routes.Clear();
            EnsureRouteSlots();
        }
    }
}
