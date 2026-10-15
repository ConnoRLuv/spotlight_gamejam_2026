using UnityEngine;
using UnityEngine.UI;

namespace RoguelikeMap.Unity
{
    // 路线朝哪个方向走。名字里的"上"指屏幕上的上方（层号 Row 增大的方向）。
    public enum MapRouteDirection
    {
        Up = 0,
        UpRight = 1,
        UpLeft = 2,
        Down = 3,
        DownRight = 4,
        DownLeft = 5
    }

    // 路线段的摆放方式。
    public enum MapRouteLayout
    {
        // 按方向挑一个美术片段，再整体旋转，让两端正好落在父子节点上（不拉伸、不变形）。
        // 手绘曲线最推荐这个：形状保持原样，只是朝向跟着格子走。
        RotateToFit = 0,

        // 按方向挑片段并保持它原本的朝向，只做缩放让两端对上。
        // 适合美术片段本身就是按你的行列间距画的、不想让它转一点角度的情况。
        NativeFit = 1,

        // 只用一张图，拉伸 + 旋转成一条直线段。美术只给了一张线的时候用。
        SimpleStretch = 2
    }

    // 一条路线的表现。挂在路线预制体上。
    //
    // 【预制体的约定】把 UI 矩形（RectTransform）的轴心点放在线段的起点上，
    // 也就是"从父节点长出来"的那一端。摆放时组件会把轴心点直接放到父节点位置，
    // 于是移动 / 旋转 / 缩放都绕着起点发生，端点不会漂。
    // 终点则用 _targetEnd 描述（图片里的归一化坐标，y 轴向上，0 = 图片下边）。
    // MapArtPrefabBuilder 生成预制体时会按图片的透明边自动把这两个值算好。
    public sealed class MapEdgeView : MonoBehaviour
    {
        [SerializeField] private Image _image;

        [Tooltip("终点在线段图片里的归一化位置（y 轴向上，0 = 图片下边，1 = 图片上边）")]
        [SerializeField] private Vector2 _targetEnd = new Vector2(0.5f, 1f);

        [Tooltip("整体长度微调。1 = 刚好铺满两个节点之间")]
        [SerializeField] private float _lengthScale = 1f;

        [Tooltip("两端各向内收多少（0~0.4，按整段长度的比例算），避免线段从节点图标下面露出来")]
        [SerializeField] private float _trim;

        public MapRouteDirection Direction { get; private set; }
        public Image Image => _image;
        public Vector2 TargetEnd => _targetEnd;

        Color _baseColor = Color.white;
        bool _available;

        private void Awake()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            if (_image == null) _image = GetComponent<Image>();
        }

        // 换一张图 / 一套新参数。摆放参数一般由预制体自带，这里只覆盖图片和颜色。
        public void SetSprite(Sprite sprite, Color color)
        {
            CacheComponents();
            if (!MapSpriteFactory.IsUsable(sprite)) sprite = null;   // 引用失效就当没配图，走拉伸兜底

            if (_image != null)
            {
                _image.sprite = sprite;
                _image.raycastTarget = false;
                _image.enabled = true;   // 没图也要保持可见，这样兜底的直线段才画得出来
            }
            _baseColor = color;
            ApplyColor();
        }

        public void Configure(Vector2 targetEnd, float lengthScale, float trim)
        {
            _targetEnd = targetEnd;
            _lengthScale = lengthScale <= 0f ? 1f : lengthScale;
            _trim = Mathf.Clamp(trim, 0f, 0.4f);
        }

        // 只调摆放参数，保留预制体自带的端点信息。
        public void ConfigureLayout(float lengthScale, float trim)
        {
            _lengthScale = lengthScale <= 0f ? 1f : lengthScale;
            _trim = Mathf.Clamp(trim, 0f, 0.4f);
        }

        // 把这条线摆到 from → to 之间。from 是父节点位置，to 是子节点位置，都相对于连线容器的中心。
        public void Place(Vector2 from, Vector2 to, MapRouteLayout layout, float thickness)
        {
            var rect = transform as RectTransform;
            if (rect == null) return;

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f) return;

            // 两端各收一点，线段就不会从节点图标下面钻出来
            if (_trim > 0f)
            {
                Vector2 inset = (delta / length) * (length * _trim * 0.5f);
                from += inset;
                to -= inset;
                delta = to - from;
                length = delta.magnitude;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            Sprite sprite = _image != null ? _image.sprite : null;
            if (layout == MapRouteLayout.SimpleStretch || sprite == null)
            {
                PlaceAsStretch(rect, from, to, delta, length, thickness);
                return;
            }

            // 图片像素尺寸下，起点（轴心点）与终点（_targetEnd）连成的"弦"
            Vector2 size = sprite.rect.size;
            Vector2 chord = Vector2.Scale(_targetEnd - rect.pivot, size);
            float chordLength = chord.magnitude;
            if (chordLength < 0.001f)
            {
                PlaceAsStretch(rect, from, to, delta, length, thickness);
                return;
            }

            float scale = (_lengthScale * length) / chordLength;
            float angle = 0f;

            if (layout == MapRouteLayout.RotateToFit)
            {
                angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg
                      - Mathf.Atan2(chord.y, chord.x) * Mathf.Rad2Deg;
                rect.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                // 不旋转（NativeFit）：保持原来的朝向，两个轴各自缩放，让终点也正好落在子节点上
                float scaleX = Mathf.Abs(chord.x) > 0.001f ? delta.x / chord.x : scale;
                float scaleY = Mathf.Abs(chord.y) > 0.001f ? delta.y / chord.y : scale;
                if (scaleX <= 0f) scaleX = 0.001f;
                if (scaleY <= 0f) scaleY = 0.001f;
                rect.localScale = new Vector3(scaleX, scaleY, 1f);
            }

            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            rect.anchoredPosition = from;   // pivot 就在起点上，所以直接对齐 from
        }

        private void PlaceAsStretch(
            RectTransform rect, Vector2 from, Vector2 to, Vector2 delta, float length, float thickness)
        {
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float width = Mathf.Max(thickness, 0.5f);

            rect.sizeDelta = new Vector2(length, width);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            // 轴心点未必在正中，所以先算轴心点相对矩形中心的偏移，旋转后补回去
            Vector2 pivotOffset = Vector2.Scale(rect.pivot - new Vector2(0.5f, 0.5f), rect.sizeDelta);
            Vector2 rotated = Quaternion.Euler(0f, 0f, angle) * pivotOffset;
            rect.anchoredPosition = (from + to) * 0.5f - rotated;
        }

        public void SetState(bool available, bool visited, MapViewStyle style)
        {
            _available = available;

            if (visited) _baseColor = style.EdgeVisitedTint;
            else if (available) _baseColor = style.EdgeAvailableTint;
            else _baseColor = style.EdgeLockedTint;

            ApplyColor();
        }

        // 不按可达性着色，直接用美术原色。
        public void SetRawColor(Color color)
        {
            _baseColor = color;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_image == null) return;
            Color c = _baseColor;
            if (_image.sprite == null && c == Color.white) c = new Color(0.45f, 0.45f, 0.45f);
            _image.color = c;
        }

        public bool IsAvailable => _available;
    }
}
