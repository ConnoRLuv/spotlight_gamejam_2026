using System;
using RoguelikeMap.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoguelikeMap.Unity
{
    // 一个节点要显示的全部表现数据。核心层不知道它的存在。
    public struct MapNodeVisual
    {
        public string Label;
        public Color Color;

        // 节点底图，通常就是房间图标本身。留空则用代码生成的圆底。
        public Sprite Badge;

        // 叠加在底图上的小图标，可选。
        public Sprite Icon;

        public static readonly MapNodeVisual Empty = new MapNodeVisual
        {
            Label = string.Empty,
            Color = Color.white,
            Badge = null,
            Icon = null
        };
    }

    // 单个地图节点的表现。挂在节点预制体上，负责显示与响应点击。
    //
    // 预制体需要满足的最小条件：根物体有 UI 矩形（RectTransform）和图片组件（当作底图）。
    // 下面这些子物体/组件都是可选的，填了就用，没填就自动补：
    //   · 名任意、带 Text 的子物体 → 节点文字
    //   · 带按钮（Button）的根物体         → 用按钮（Button）的 onClick 接收点击
    //   · 不带按钮                → 用自己实现的 指针点击接口（IPointerClickHandler）
    //   · _highlight                 → 高亮，留空则运行时用代码生成的圆环
    //
    // 两条点击路径不会同时生效（见 指针点击回调（OnPointerClick） 里的 _useButton 判断），所以一次点击只触发一次。
    public sealed class MapNodeView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image _badge;
        [SerializeField] private Image _icon;
        [SerializeField] private Text _label;
        [SerializeField] private Button _button;
        [Tooltip("可达时显示的高亮。留空则运行时自动生成一个圆环")]
        [SerializeField] private Image _highlight;

        public event Action<MapNode> Clicked;

        public MapNode Node { get; private set; }

        // 类型颜色，用于高亮与无美术时的兜底着色。
        public Color TypeColor { get; private set; } = Color.white;

        Color _badgeColor = Color.white;
        bool _hasBadgeArt;
        bool _available;
        bool _isCurrent;
        bool _useButton;
        bool _labelsRequested;

        // 同一个问题只提醒一次：一屏几十个节点，每个都喊一遍会把 Console 淹掉
        static bool _warnedBrokenArt;

        private void Awake()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            if (_badge == null) _badge = GetComponent<Image>();
            if (_button == null) _button = GetComponent<Button>();
            if (_label == null) _label = GetComponentInChildren<Text>(true);
        }

        public void Bind(MapNode node, MapNodeVisual visual, bool showLabel, bool showHighlight)
        {
            CacheComponents();

            Node = node;
            _useButton = _button != null;
            _labelsRequested = showLabel;
            TypeColor = visual.Color;

            if (_badge != null)
            {
                // 既没图标也没叠加图、预制体自己也没底图时，用代码生成的圆底，至少不是一块白方块
                Sprite badgeSprite = visual.Badge;
                if (!MapSpriteFactory.IsUsable(badgeSprite))
                {
                    WarnArtBrokenOnce("房间图标");
                    badgeSprite = null;
                }

                if (badgeSprite == null && visual.Icon == null && _badge.sprite == null)
                {
                    Sprite fallback = MapSpriteFactory.CircleSprite;
                    badgeSprite = MapSpriteFactory.IsUsable(fallback) ? fallback : null;
                }

                _badge.sprite = badgeSprite;
                _badge.preserveAspect = badgeSprite != null;
                _badge.raycastTarget = true;   // 点击靠它接收射线
                _badgeColor = badgeSprite != null && badgeSprite != MapSpriteFactory.CircleSprite
                    ? Color.white
                    : visual.Color;
                _badge.color = _badgeColor;
                _hasBadgeArt = badgeSprite != null && badgeSprite != MapSpriteFactory.CircleSprite;
            }

            if (_icon != null)
            {
                Sprite iconSprite = MapSpriteFactory.IsUsable(visual.Icon) ? visual.Icon : null;
                _icon.sprite = iconSprite;
                _icon.raycastTarget = false;
                _icon.enabled = iconSprite != null;
            }

            if (_label != null)
            {
                // 预制体里的文字可能没指定字体（比如代码生成的预制体），这里兜一个内置字体，
                // 否则打开文字开关时一个字母都看不到
                if (_label.font == null) _label.font = MapGraphView.ResolveBuiltinFont();

                _label.text = visual.Label;
                _label.raycastTarget = false;
                // 没图标的时候文字是唯一的辨识手段，这时候不管开关都要显示
                _label.gameObject.SetActive(showLabel || !_hasBadgeArt);
            }

            if (showHighlight) EnsureHighlight();

            if (_useButton)
            {
                _button.onClick.RemoveAllListeners();
                _button.onClick.AddListener(OnClick);
            }
        }

        static void WarnArtBrokenOnce(string what)
        {
            if (_warnedBrokenArt) return;
            _warnedBrokenArt = true;
            Debug.LogWarning("[MapNodeView] 配置资产里的" + what + "引用已经失效（美术被删掉或重新导入过），" +
                             "节点先用代码生成的圆底顶着。运行菜单里的 地图 → 生成节点与路线预制体 可以把美术重新挂上。");
        }

        private void EnsureHighlight()
        {
            if (_highlight != null) return;

            var go = new GameObject("Highlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            Vector2 size = GetSize();
            rect.sizeDelta = size * 1.35f;
            rect.SetAsLastSibling();

            _highlight = go.GetComponent<Image>();
            _highlight.sprite = MapSpriteFactory.RingSprite;
            _highlight.raycastTarget = false;
        }

        // 节点自身的尺寸，用来给高亮定大小。
        public Vector2 GetSize()
        {
            var rect = transform as RectTransform;
            if (rect == null) return new Vector2(100f, 100f);
            if (rect.anchorMin != rect.anchorMax) return rect.rect.size;
            return rect.sizeDelta;
        }

        // available 是"这一步能走"，visited 是"走过"，isCurrent 是"玩家现在站在这上面"。
        // 起点从开局第一帧起就是"当前所在地"，所以它不会像普通已走过的房间那样被压暗。
        public void SetState(bool available, bool visited, bool isCurrent, MapViewStyle style)
        {
            _available = available;
            _isCurrent = isCurrent;

            // 有按钮时由按钮挡点击；没有按钮时由指针点击回调（OnPointerClick）里的 _available 挡
            if (_button != null) _button.interactable = available;

            Color c = _badgeColor;
            if (style.TintBadgeWithTypeColor) c = Color.Lerp(c, TypeColor, 0.55f);

            if (isCurrent) c = Color.Lerp(c, Color.white, 0.15f);          // 当前所在地保持亮
            else if (visited) c = Color.Lerp(c, style.VisitedTint, 0.65f);
            else if (!available) c = Color.Lerp(c, style.LockedTint, 0.6f);

            if (_badge != null) _badge.color = c;
            if (_icon != null && _icon.sprite != null) _icon.color = c;
            if (_label != null)
            {
                Color lc = style.LabelColor;
                if ((visited || !available) && !isCurrent) lc.a *= 0.55f;
                _label.color = lc;
            }

            if (_highlight != null)
            {
                bool show = style.ShowAvailableHighlight && (isCurrent || (available && !visited));
                _highlight.enabled = show;
                _highlight.color = isCurrent ? style.CurrentHighlightColor : TypeColor;
                if (show)
                {
                    Vector2 size = GetSize();
                    float scale = isCurrent ? style.CurrentHighlightScale : style.HighlightScale;
                    ((RectTransform)_highlight.transform).sizeDelta = size * scale;
                }
            }
        }

        // 玩家现在是不是站在这上面。
        public bool IsCurrent => _isCurrent;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_useButton) return;   // 已经有 Button 处理了，直接返回，避免同一次点击触发两遍
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (!_available) return;
            OnClick();
        }

        private void OnClick()
        {
            if (Clicked != null && Node != null) Clicked(Node);
        }

        // 运行时可强制显示 / 隐藏文字标签（不改变预制体本身）。
        public void SetLabelVisible(bool visible)
        {
            if (_label == null) return;
            _label.gameObject.SetActive(visible || !_hasBadgeArt);
        }

        public bool LabelsRequested => _labelsRequested;
    }
}
