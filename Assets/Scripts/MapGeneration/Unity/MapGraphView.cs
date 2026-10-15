using System;
using System.Collections.Generic;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoguelikeMap.Unity
{
    // 地图外观参数。只影响"长什么样"，不参与任何游戏规则。
    [Serializable]
    public sealed class MapViewStyle
    {
        [Header("间距")]
        [Tooltip("相邻两列的横向间距。路线美术是按竖长的格子画的，行距取列距的 2.3 倍左右时线条不用旋转")]
        public float ColumnSpacing = 150f;

        [Tooltip("相邻两层的纵向间距")]
        public float RowSpacing = 350f;

        [Header("路线")]
        [Tooltip("没有连线预制体、只能靠代码画线时的线宽")]
        public float EdgeThickness = 14f;

        [Tooltip("没有连线预制体、只能靠代码画线时的颜色")]
        public Color EdgeColor = new Color(0.77f, 0.93f, 1f);

        [Tooltip("选哪一种摆放方式，见 MapRouteLayout 的注释")]
        public MapRouteLayout RouteLayout = MapRouteLayout.RotateToFit;

        [Tooltip("两端各向内收多少（按整段长度的比例），1 = 收掉半段")]
        [Range(0f, 0.4f)]
        public float RouteTrim = 0.04f;

        [Tooltip("路线长度微调，1 = 刚好铺满两个节点之间")]
        public float RouteLengthScale = 1f;

        public Color EdgeAvailableTint = Color.white;
        public Color EdgeVisitedTint = new Color(0.55f, 0.72f, 0.85f);
        public Color EdgeLockedTint = new Color(0.26f, 0.3f, 0.34f);

        [Header("节点状态")]
        public Color VisitedTint = new Color(0.35f, 0.35f, 0.35f);
        public Color LockedTint = new Color(0.18f, 0.18f, 0.18f);
        public Color LabelColor = new Color(0.12f, 0.12f, 0.12f);

        [Tooltip("在节点上叠一层房间名文字。节点有自己的图标时一般不需要")]
        public bool ShowLabels;

        [Tooltip("在可达节点、以及当前所在地节点上套一个高亮圈")]
        public bool ShowAvailableHighlight = true;

        [Tooltip("高亮圈相对节点尺寸的倍率")]
        public float HighlightScale = 1.35f;

        [Tooltip("当前所在地（玩家脚下这个节点）的高亮圈颜色")]
        public Color CurrentHighlightColor = new Color(1f, 0.86f, 0.4f);

        [Tooltip("当前所在地高亮圈的倍率，一般比可达节点再大一圈")]
        public float CurrentHighlightScale = 1.7f;

        [Tooltip("用房间类型颜色给图标染色。图标本身有颜色时建议关掉")]
        public bool TintBadgeWithTypeColor;
    }

    // 地图的可视化层。挂上它、指定配置资产，运行就能看到地图。
    //
    // 使用前提：本组件要挂在画布（Canvas）下面的某个 UI 矩形（RectTransform）上。
    // 想让它铺满屏幕，把这个 UI 矩形（RectTransform）的锚点设成四角拉伸（拉伸-拉伸）。
    //
    // 两种布局，靠"自动缩放"开关切换：
    //   · 自动缩放打开 → 整张图缩到一屏里（小地图、总览用）
    //   · 自动缩放关掉 → 地图保持原生大小，上下滚动看（长地图推荐；底纹会跟着一起滚）
    //
    // 层叠顺序（从下到上）：背景插画（固定不动）→ 底纹（跟着地图滚）→ 路线 → 节点。
    // 前两层直接用配置资产里的图，留空就不画。
    //
    // 它会自动做三件事，所以不需要你写任何额外的启动脚本：
    //   1. 订阅 MapGeneratorBehaviour.Generated，地图生成完自动绘制
    //   2. 没填容器时自动创建 背景 / 底纹 / 连线 / 节点
    //   3. 按"房间类型的预制体 → 本组件的默认预制体 → 代码生成"的顺序兜底
    [RequireComponent(typeof(RectTransform))]
    public sealed class MapGraphView : MonoBehaviour
    {
        [Header("数据来源")]
        [Tooltip("留空会自动找同一个物体或父物体上的 MapGeneratorBehaviour，找不到就自己加一个")]
        [SerializeField] private MapGeneratorBehaviour _generator;

        [Header("容器（留空自动创建）")]
        [SerializeField] private RectTransform _nodeRoot;
        [SerializeField] private RectTransform _edgeRoot;

        [Header("预制体（留空则用配置资产里的，还是没有就用代码生成的默认样子）")]
        [Tooltip("需要带 RectTransform，MapNodeView 会自动补上")]
        [SerializeField] private GameObject _nodePrefab;
        [Tooltip("一个带 Image 的 RectTransform，MapEdgeView 会自动补上")]
        [SerializeField] private GameObject _edgePrefab;

        [Header("外观")]
        [SerializeField] private MapViewStyle _style = new MapViewStyle();
        [Tooltip("节点尺寸。用自定义预制体时请把这里改成预制体的实际大小，算边距时才准")]
        [SerializeField] private Vector2 _nodeSize = new Vector2(130f, 130f);
        [SerializeField] private MapGenerationConfigAsset _configAsset;

        [Header("布局")]
        [Tooltip("让整张图自动缩放到刚好放进这个 RectTransform（一屏总览）。关掉就是原生大小 + 上下滚动")]
        [SerializeField] private bool _autoFitToViewport;
        [SerializeField] private float _fitPadding = 40f;

        [Header("滚动（关掉自动缩放后生效）")]
        [Tooltip("鼠标滚轮速度。设成 0 就不响应滚轮，只留 ScrollToNode 这类接口给外部控制")]
        [SerializeField] private float _scrollSpeed = 150f;

        [Header("交互")]
        [Tooltip("点击可达节点时自动前进；关掉则只发事件，由外部决定")]
        [SerializeField] private bool _autoMoveOnClick = true;
        [Tooltip("玩家走到新节点后，自动把镜头滚到它身上")]
        [SerializeField] private bool _scrollToCurrentOnEnter = true;
        [Tooltip("调试用：按这个键重新生成一张地图。不需要就设成 None")]
        [SerializeField] private KeyCode _regenerateKey = KeyCode.R;

        struct EdgeRecord
        {
            public MapEdgeView View;
            public int FromId;
            public int ToId;

            public EdgeRecord(MapEdgeView view, int fromId, int toId)
            {
                View = view;
                FromId = fromId;
                ToId = toId;
            }
        }

        readonly Dictionary<int, MapNodeView> _nodeViews = new Dictionary<int, MapNodeView>();
        readonly List<EdgeRecord> _edgeViews = new List<EdgeRecord>();
        readonly List<MapNode> _availableBuffer = new List<MapNode>(8);

        MapGraph _graph;
        NodeTypeCatalog _catalog;
        MapRunState _run;
        bool _subscribed;

        RectTransform _backdropRoot;
        Image _background;
        RawImage _backdrop;
        float _scrollOffset;
        float _contentMinY;
        float _contentMaxY;
        float _mapWidth = 1f;
        bool _layoutPending;

        // 一屏几十个节点，这个提示喊一次就够了
        static bool _warnedArtNotWired;

        // 节点被点击时触发（无论是否真的移动了）。
        public event Action<MapNode> NodeClicked;

        // 玩家真的移动到了新节点时触发，适合在这里加载战斗或事件。
        public event Action<MapNode> NodeEntered;

        public MapGeneratorBehaviour Generator => _generator;
        public MapRunState RunState => _run;
        public MapGraph Graph => _graph;

        // 当前滚动位置（容器在本地空间里的 y 偏移）。读到边界就知道有没有滚到头。
        public float ScrollOffset => _scrollOffset;

        // 美术配置（图标、预制体、路线图、背景与底纹）。留空则回退到生成器上的那一份。
        public MapGenerationConfigAsset ConfigAsset
        {
            get
            {
                if (_configAsset != null) return _configAsset;
                return _generator != null ? _generator.ConfigAsset : null;
            }
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            ResolveGenerator();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Start()
        {
            // 覆盖"生成发生在订阅之前"的情况（例如生成器先跑了 Awake（挂载时））
            if (_generator != null && _generator.Graph != null && _nodeViews.Count == 0)
                RenderFromGenerator();
        }

        private void Update()
        {
            HandleScrollInput();

            if (_regenerateKey == KeyCode.None) return;
            if (!Input.GetKeyDown(_regenerateKey)) return;
            if (_generator != null) _generator.Generate();
        }

        // 第一帧画布还没算出 UI 矩形（RectTransform）的实际大小，这时候量不出滚动范围。
        // LateUpdate（布局之后）里再补一次，所以挂在这儿补一次，玩家第一眼看到的位置就是对的。
        private void LateUpdate()
        {
            if (!_layoutPending) return;
            _layoutPending = false;
            LayoutBackdrop();
            ApplyViewTransform();
        }

        private void HandleScrollInput()
        {
            if (_autoFitToViewport) return;
            if (_scrollSpeed <= 0f) return;
            if (_graph == null || _nodeViews.Count == 0) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.001f) return;

            // 滚轮往上 → 内容往下走 → 看见地图更上面的一层
            _scrollOffset -= wheel * _scrollSpeed;
            ApplyViewTransform();
        }

        private void ResolveGenerator()
        {
            if (_generator != null) return;

            _generator = GetComponent<MapGeneratorBehaviour>();
            if (_generator == null) _generator = GetComponentInParent<MapGeneratorBehaviour>();

            if (_generator == null)
            {
                _generator = gameObject.AddComponent<MapGeneratorBehaviour>();
                Debug.Log("[MapGraphView] 没有找到 MapGeneratorBehaviour，已自动添加一个。" +
                          "它现在用的是默认配置；想用你自己配的数值和颜色，请把 MapGenerationConfig 资产拖到它的 Config Asset 上。");
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _generator == null) return;
            _generator.Generated += HandleGenerated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _generator == null) return;
            _generator.Generated -= HandleGenerated;
            _subscribed = false;
        }

        private void HandleGenerated(MapGenerationResult result)
        {
            RenderFromGenerator();
        }

        // 从当前生成器取数据并重绘。也可以在外面生成完手动调用。
        [ContextMenu("重新绘制地图")]
        public void RenderFromGenerator()
        {
            if (_generator == null) ResolveGenerator();
            if (_generator == null || _generator.Graph == null) return;

            Render(_generator.Graph, _generator.Catalog, _generator.RunState);
        }

        [ContextMenu("重新生成地图")]
        public void RegenerateFromMenu()
        {
            if (_generator == null) ResolveGenerator();
            if (_generator != null) _generator.Generate();
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------

        public void Render(MapGraph graph, NodeTypeCatalog catalog, MapRunState run)
        {
            Clear();

            _graph = graph;
            _catalog = catalog;
            _run = run;

            if (_graph == null) return;

            EnsureContainers();

            for (int i = 0; i < _graph.Nodes.Count; i++)
                CreateNode(_graph.Nodes[i]);

            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                MapNode node = _graph.Nodes[i];
                Vector2 from = GetPosition(node);
                for (int j = 0; j < node.NextIds.Count; j++)
                {
                    MapNode to = _graph.Get(node.NextIds[j]);
                    if (to != null) CreateEdge(node, to, from, GetPosition(to));
                }
            }

            MeasureContent();
            LayoutBackdrop();

            // 长地图：镜头先对准玩家当前所在的节点（开局就是起点）；没有起点节点时才回到底部
            if (!_autoFitToViewport)
            {
                if (_run != null && _run.CurrentNodeId != MapRunState.NoNode) ScrollToNode(_run.CurrentNodeId);
                else _scrollOffset = float.MaxValue;
            }
            ApplyViewTransform();

            RefreshInteractivity();
        }

        public void Clear()
        {
            foreach (KeyValuePair<int, MapNodeView> pair in _nodeViews)
                if (pair.Value != null) Destroy(pair.Value.gameObject);
            _nodeViews.Clear();

            for (int i = 0; i < _edgeViews.Count; i++)
                if (_edgeViews[i].View != null) Destroy(_edgeViews[i].View.gameObject);
            _edgeViews.Clear();

            _run = null;
        }

        private void EnsureContainers()
        {
            var self = transform as RectTransform;
            MapGenerationConfigAsset asset = ConfigAsset;

            // 顺序就是绘制顺序：背景插画 → 底纹 → 路线 → 节点
            if (asset != null && asset.BackgroundSprite != null) EnsureBackground(self, asset);

            if (_backdropRoot == null) _backdropRoot = CreateContainer("Backdrop", self);
            if (_edgeRoot == null) _edgeRoot = CreateContainer("Edges", self);
            if (_nodeRoot == null) _nodeRoot = CreateContainer("Nodes", self);
        }

        private static RectTransform CreateContainer(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.SetAsLastSibling();
            return rect;
        }

        // 背景插画：铺满这个 UI 矩形（RectTransform），不跟着地图滚。
        private void EnsureBackground(RectTransform parent, MapGenerationConfigAsset asset)
        {
            if (_background == null)
            {
                var go = new GameObject("Background",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.SetAsFirstSibling();

                _background = go.GetComponent<Image>();
                _background.raycastTarget = false;
            }

            // 图被删掉或重新导入过之后引用会失效，这种空壳赋给图片组件会直接抛异常，所以先验一道。
            // 没图就整块关掉，不然一张白图会把屏幕盖住。
            Sprite sprite = MapSpriteFactory.IsUsable(asset.BackgroundSprite) ? asset.BackgroundSprite : null;
            _background.gameObject.SetActive(sprite != null);
            if (sprite == null) return;

            if (_background.sprite != sprite) _background.sprite = sprite;
            _background.preserveAspect = asset.BackgroundPreserveAspect;
            _background.color = asset.BackgroundTint;
        }

        // 节点在这个 UI 矩形（RectTransform）局部空间里的位置。改成世界空间只要换这一个方法。
        public Vector2 GetPosition(MapNode node)
        {
            if (_graph == null) return Vector2.zero;
            float x = (node.Column - (_graph.Columns - 1) * 0.5f) * _style.ColumnSpacing;
            float y = node.Row * _style.RowSpacing;
            return new Vector2(x, y);
        }

        // 一条边该用哪个方向的路线片段：先看横向偏移有多大，再看朝上还是朝下。
        public static MapRouteDirection ResolveDirection(Vector2 delta)
        {
            float ax = Mathf.Abs(delta.x);
            float ay = Mathf.Abs(delta.y);

            // 横向偏移不到纵向的 1/3，当作竖直连线
            if (ax * 3f <= ay) return delta.y >= 0f ? MapRouteDirection.Up : MapRouteDirection.Down;

            if (delta.y >= 0f) return delta.x >= 0f ? MapRouteDirection.UpRight : MapRouteDirection.UpLeft;
            return delta.x >= 0f ? MapRouteDirection.DownRight : MapRouteDirection.DownLeft;
        }

        // ------------------------------------------------------------------
        // 尺寸与布局
        // ------------------------------------------------------------------

        // 节点实际占的尺寸：优先用实例的 rect，预制体比 _nodeSize 小时也不会算错。
        private Vector2 ResolveNodeLayoutSize()
        {
            Vector2 size = _nodeSize;
            foreach (KeyValuePair<int, MapNodeView> pair in _nodeViews)
            {
                MapNodeView view = pair.Value;
                if (view == null) continue;
                size = Vector2.Max(size, view.GetSize());
            }
            return size;
        }

        // 量一遍所有节点的包围盒，滚动范围、底纹大小、缩放都靠它。
        private void MeasureContent()
        {
            Vector2 half = ResolveNodeLayoutSize() * 0.5f;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                Vector2 p = GetPosition(_graph.Nodes[i]);
                min = Vector2.Min(min, p - half);
                max = Vector2.Max(max, p + half);
            }

            _mapWidth = Mathf.Max(max.x - min.x, 1f);

            MapGenerationConfigAsset asset = ConfigAsset;
            float pad = asset != null ? Mathf.Max(asset.BackdropPadding, 0f) : 0f;
            _contentMinY = min.y - pad;
            _contentMaxY = max.y + pad;
        }

        // 底纹：按地图范围铺一张可平铺的图，跟着地图一起滚。
        private void LayoutBackdrop()
        {
            if (_backdropRoot == null) return;

            MapGenerationConfigAsset asset = ConfigAsset;
            Sprite sprite = asset != null ? asset.BackdropSprite : null;
            if (sprite != null && !MapSpriteFactory.IsUsable(sprite)) sprite = null;

            if (sprite == null)
            {
                if (_backdrop != null) _backdrop.gameObject.SetActive(false);
                return;
            }

            Texture texture = sprite.texture;
            if (!MapSpriteFactory.IsUsable(texture))
            {
                if (_backdrop != null) _backdrop.gameObject.SetActive(false);
                return;
            }

            if (_backdrop == null)
            {
                var go = new GameObject("BackdropImage",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(_backdropRoot, false);
                _backdrop = go.GetComponent<RawImage>();
                _backdrop.raycastTarget = false;
            }

            _backdrop.gameObject.SetActive(true);

            // 只铺地图这一列，两边留出背景插画 —— 底纹像一块跟着滚的"地垫"
            float width = _mapWidth + Mathf.Max(asset.BackdropPadding, 0f) * 2f;
            float height = _contentMaxY - _contentMinY;

            var rect = (RectTransform)_backdrop.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, (_contentMinY + _contentMaxY) * 0.5f);

            // 贴图坐标超过 1 就是平铺；贴图要设成循环平铺（Repeat）模式，不然接缝会被拉成一条线
            if (texture.wrapMode != TextureWrapMode.Repeat)
                texture.wrapMode = TextureWrapMode.Repeat;

            float tileScale = Mathf.Max(asset.BackdropTileScale, 0.01f);
            float tileWidth = Mathf.Max(sprite.rect.width * tileScale, 8f);
            float tileHeight = Mathf.Max(sprite.rect.height * tileScale, 8f);

            _backdrop.texture = texture;
            _backdrop.uvRect = new Rect(0f, 0f, width / tileWidth, height / tileHeight);
            _backdrop.color = new Color(1f, 1f, 1f, asset.BackdropAlpha);
        }

        private void ApplyViewTransform()
        {
            _layoutPending = false;

            if (_autoFitToViewport)
            {
                ApplyFit();
                return;
            }

            var viewport = (RectTransform)transform;
            float viewportHeight = viewport.rect.height;
            if (viewportHeight <= 1f)
            {
                _layoutPending = true;   // 布局还没算出来，等 LateUpdate 再来一次
                return;
            }

            // 横向装不下就整体缩一点，纵向靠滚动，所以不参与缩放
            float scale = Mathf.Clamp(viewport.rect.width / _mapWidth, 0.05f, 1f);

            float top = viewportHeight * 0.5f - _contentMaxY * scale;      // 顶到地图上面
            float bottom = -viewportHeight * 0.5f - _contentMinY * scale;  // 顶到地图下面

            // 内容比视口还矮就居中，不然夹在两头之间
            if (top >= bottom) _scrollOffset = (top + bottom) * 0.5f;
            else _scrollOffset = Mathf.Clamp(_scrollOffset, top, bottom);

            var offset = new Vector2(0f, _scrollOffset);
            ApplyContainer(_backdropRoot, scale, offset);
            ApplyContainer(_edgeRoot, scale, offset);
            if (_nodeRoot != _edgeRoot) ApplyContainer(_nodeRoot, scale, offset);
        }

        // 把整张图缩放并居中到当前 UI 矩形（RectTransform）的范围里（一屏总览）。
        // 地图是 16 层 × 350 间距 ≈ 5600 单位高，不缩放的话屏幕根本装不下。
        private void ApplyFit()
        {
            if (_graph == null || _nodeViews.Count == 0) return;
            if (_nodeRoot == null) return;

            Vector2 half = ResolveNodeLayoutSize() * 0.5f;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                Vector2 p = GetPosition(_graph.Nodes[i]);
                min = Vector2.Min(min, p - half);
                max = Vector2.Max(max, p + half);
            }

            Vector2 size = max - min;
            Vector2 center = (min + max) * 0.5f;

            var viewport = transform as RectTransform;
            float vw = viewport.rect.width - _fitPadding * 2f;
            float vh = viewport.rect.height - _fitPadding * 2f;

            // 布局还没算出来的时候 rect 可能是 0，这时候保持原样，下一帧用户手动重绘即可
            if (vw <= 1f || vh <= 1f)
            {
                _layoutPending = true;
                return;
            }

            float scale = Mathf.Min(vw / Mathf.Max(size.x, 1f), vh / Mathf.Max(size.y, 1f));
            scale = Mathf.Clamp(scale, 0.05f, 2f);

            Vector2 offset = -center * scale;

            ApplyContainer(_backdropRoot, scale, offset);
            ApplyContainer(_edgeRoot, scale, offset);
            if (_nodeRoot != _edgeRoot) ApplyContainer(_nodeRoot, scale, offset);
        }

        private static void ApplyContainer(RectTransform rect, float scale, Vector2 offset)
        {
            if (rect == null) return;
            rect.localScale = new Vector3(scale, scale, 1f);
            rect.anchoredPosition = offset;
        }

        // ------------------------------------------------------------------
        // 滚动接口
        // ------------------------------------------------------------------

        // 滚到最下面（起点）。
        public void ScrollToBottom()
        {
            _scrollOffset = float.MaxValue;
            ApplyViewTransform();
        }

        // 滚到最上面（Boss）。
        public void ScrollToTop()
        {
            _scrollOffset = float.MinValue;
            ApplyViewTransform();
        }

        // 把某个节点滚到视口中间，适合"走完一步之后镜头跟过去"。
        public void ScrollToNode(int nodeId)
        {
            if (_autoFitToViewport || _graph == null) return;
            MapNode node = _graph.Get(nodeId);
            if (node == null) return;

            var viewport = (RectTransform)transform;
            if (viewport.rect.height <= 1f) return;

            float scale = Mathf.Clamp(viewport.rect.width / _mapWidth, 0.05f, 1f);
            _scrollOffset = -GetPosition(node).y * scale;
            ApplyViewTransform();
        }

        // ------------------------------------------------------------------
        // 节点
        // ------------------------------------------------------------------

        private void CreateNode(MapNode node)
        {
            NodeTypeVisual visual = ResolveTypeVisual(node.TypeKey);
            GameObject prefab = visual != null && visual.CustomPrefab != null ? visual.CustomPrefab : _nodePrefab;

            if (visual != null && visual.Icon == null && visual.CustomPrefab == null) WarnArtNotWiredOnce();

            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, _nodeRoot, false);
            }
            else
            {
                go = BuildDefaultNode();
                go.transform.SetParent(_nodeRoot, false);
            }

            go.name = "Node_" + node.Id + "_" + node.TypeKey;

            var view = go.GetComponent<MapNodeView>();
            if (view == null) view = go.AddComponent<MapNodeView>();

            view.Bind(node, BuildNodeVisual(node, visual), _style.ShowLabels, _style.ShowAvailableHighlight);
            view.Clicked += HandleNodeClicked;
            _nodeViews[node.Id] = view;

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = GetPosition(node);
            }
        }

        private MapNodeVisual BuildNodeVisual(MapNode node, NodeTypeVisual visual)
        {
            string label = _catalog != null ? _catalog.GetDisplayName(node.TypeKey) : node.TypeKey;
            if ((label == null || label.Length == 0) && visual != null) label = visual.DisplayName;

            return new MapNodeVisual
            {
                Label = label,
                Color = visual != null ? visual.Color : FallbackColor(node.TypeKey),
                Badge = visual != null ? visual.Icon : null,
                Icon = visual != null ? visual.OverlayIcon : null
            };
        }

        private NodeTypeVisual ResolveTypeVisual(string typeKey)
        {
            MapGenerationConfigAsset asset = ConfigAsset;
            return asset != null ? asset.GetVisual(typeKey) : null;
        }

        static void WarnArtNotWiredOnce()
        {
            if (_warnedArtNotWired) return;
            _warnedArtNotWired = true;
            Debug.LogWarning("[MapGraphView] 配置资产里的美术还没挂上（图标 / 路线图 / 背景 / 底纹都是空的），" +
                             "现在先用代码生成的兜底图形顶着。打开工程时会自动挂一次，" +
                             "也可以手动运行菜单里的 地图 → 生成节点与路线预制体。");
        }

        private GameObject BuildDefaultNode()
        {
            var go = new GameObject("DefaultNode",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = _nodeSize;

            var image = go.GetComponent<Image>();
            image.sprite = MapSpriteFactory.CircleSprite;
            image.color = Color.white;
            image.raycastTarget = true;

            var labelGo = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelGo.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = _style.LabelColor;
            text.fontSize = 20;
            text.font = ResolveBuiltinFont();
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return go;
        }

        // 内置字体在 Unity 2022 之后改名了：旧版是 Arial.ttf，新版是 LegacyRuntime.ttf。
        // 这里按版本顺序尝试，避免"节点画出来了但一个字都看不到"。
        public static Font ResolveBuiltinFont()
        {
            Font font = null;

            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { }

            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (Exception) { }
            }

            if (font == null)
            {
                try { font = Font.CreateDynamicFontFromOSFont("Arial", 16); }
                catch (Exception) { }
            }

            if (font == null)
                Debug.LogWarning("[MapGraphView] 找不到内置字体，请把节点预制体上 Text 组件的 Font 手动指定一个。");

            return font;
        }

        // ------------------------------------------------------------------
        // 路线
        // ------------------------------------------------------------------

        private void CreateEdge(MapNode from, MapNode to, Vector2 fromPos, Vector2 toPos)
        {
            MapRouteDirection direction = ResolveDirection(toPos - fromPos);
            MapRouteVisual route = ResolveRoute(direction);
            Sprite sprite = route != null ? route.Sprite : null;

            GameObject prefab = null;
            if (route != null && route.Prefab != null) prefab = route.Prefab;
            else prefab = _edgePrefab;

            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, _edgeRoot, false);
            }
            else
            {
                go = BuildDefaultEdge(sprite, route);
                go.transform.SetParent(_edgeRoot, false);
            }

            go.name = "Edge_" + from.Id + "_" + to.Id + "_" + direction;

            var view = go.GetComponent<MapEdgeView>();
            if (view == null) view = go.AddComponent<MapEdgeView>();

            if (route != null) view.Configure(route.TargetEnd, _style.RouteLengthScale, _style.RouteTrim);
            else view.ConfigureLayout(_style.RouteLengthScale, _style.RouteTrim);

            view.SetSprite(sprite, _style.EdgeColor);
            view.Place(fromPos, toPos, _style.RouteLayout, _style.EdgeThickness);

            _edgeViews.Add(new EdgeRecord(view, from.Id, to.Id));
        }

        private MapRouteVisual ResolveRoute(MapRouteDirection direction)
        {
            MapGenerationConfigAsset asset = ConfigAsset;
            return asset != null ? asset.GetRoute(direction) : null;
        }

        private GameObject BuildDefaultEdge(Sprite sprite, MapRouteVisual route)
        {
            var go = new GameObject("Edge",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            var rect = (RectTransform)go.transform;
            if (sprite != null)
            {
                rect.sizeDelta = sprite.rect.size;
                rect.pivot = route != null && route.SourceEnd != Vector2.zero
                    ? route.SourceEnd
                    : new Vector2(0.5f, 0f);
            }
            else
            {
                rect.pivot = new Vector2(0.5f, 0.5f);
            }

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = false;
            image.raycastTarget = false;
            image.color = _style.EdgeColor;

            return go;
        }

        // ------------------------------------------------------------------
        // 交互
        // ------------------------------------------------------------------

        // 根据 RunState 刷新"哪些能点、哪些走过"。
        public void RefreshInteractivity()
        {
            if (_run == null) return;

            _availableBuffer.Clear();
            IReadOnlyList<MapNode> options = _run.GetOptions();
            for (int i = 0; i < options.Count; i++) _availableBuffer.Add(options[i]);

            foreach (KeyValuePair<int, MapNodeView> pair in _nodeViews)
            {
                int id = pair.Key;
                bool available = Contains(id);

                MapNodeView view = pair.Value;
                if (view != null) view.SetState(available, _run.HasVisited(id), id == _run.CurrentNodeId, _style);
            }

            // 连线跟着节点一起变化：通向可选节点的线亮起来，走过的线留在后面，其余压暗
            for (int i = 0; i < _edgeViews.Count; i++)
            {
                EdgeRecord edge = _edgeViews[i];
                if (edge.View == null) continue;

                bool available = Contains(edge.ToId);
                bool visited = _run.HasVisited(edge.FromId) && _run.HasVisited(edge.ToId);
                edge.View.SetState(available, visited, _style);
            }
        }

        private bool Contains(int nodeId)
        {
            for (int i = 0; i < _availableBuffer.Count; i++)
                if (_availableBuffer[i].Id == nodeId) return true;
            return false;
        }

        private void HandleNodeClicked(MapNode node)
        {
            bool moved = false;
            if (_autoMoveOnClick && _run != null && _run.CanMoveTo(node.Id))
            {
                _run.MoveTo(node.Id);
                moved = true;
                RefreshInteractivity();
            }

            if (NodeClicked != null) NodeClicked(node);

            if (moved)
            {
                if (_scrollToCurrentOnEnter) ScrollToNode(node.Id);
                if (NodeEntered != null) NodeEntered(node);
            }
        }

        // 取某个节点的表现组件（没有则返回 null）。外面想加动画/换图可以用它。
        public MapNodeView GetNodeView(int nodeId)
        {
            MapNodeView view;
            return _nodeViews.TryGetValue(nodeId, out view) ? view : null;
        }

        private Color FallbackColor(string typeKey)
        {
            // 没配置颜色时用键的哈希散出一个稳定颜色，至少能区分开
            unchecked
            {
                int h = typeKey != null ? typeKey.GetHashCode() : 0;
                float hue = Mathf.Abs(h % 360) / 360f;
                return Color.HSVToRGB(hue, 0.45f, 0.9f);
            }
        }
    }
}
