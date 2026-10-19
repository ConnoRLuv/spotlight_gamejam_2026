using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>开始界面可复用的部位/卡组选项，图标、文本与底框独立叠放。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Image))]
    public sealed class CyberneticSelectionTile : MonoBehaviour
    {
        [SerializeField] private Sprite normalFrame, selectedFrame;
        [SerializeField] private Image icon;
        [SerializeField] private Text title, count, durability;

        public void Configure(Sprite normal, Sprite selected, bool partTab, Font font)
        {
            normalFrame = normal; selectedFrame = selected;
            var legacy = transform.Find("Label");
            if (legacy) legacy.gameObject.SetActive(false);
            icon = Child<Image>("Icon"); icon.preserveAspect = true; icon.raycastTarget = false;
            title = Label("TileTitle", font, partTab ? 18 : 14, Color.white);
            count = Label("TileCount", font, 16, partTab ? Color.white : new Color32(88, 231, 239, 255));
            durability = Label("TileDurability", font, 14, new Color32(232, 227, 251, 255));
            Place(icon.rectTransform, new Vector2(.18f, partTab ? .3f : .36f), new Vector2(.82f, .9f));
            Place(title.rectTransform, new Vector2(.1f, partTab ? .125f : .14f),
                new Vector2(.9f, partTab ? .27f : .38f));
            count.text = ""; count.gameObject.SetActive(false);
            Place(durability.rectTransform, new Vector2(.1f, .1f), new Vector2(.9f, .25f));
            durability.text = ""; durability.gameObject.SetActive(false);
            var image = GetComponent<Image>(); image.type = Image.Type.Simple;
            image.preserveAspect = false; image.color = Color.white;
            var button = GetComponent<Button>();
            if (button)
            {
                // 状态由候选选择管理；避免按钮自带的灰色 tint 污染底图颜色。
                button.transition = Selectable.Transition.None;
                button.targetGraphic = image;
            }
        }

        public void SetContent(Sprite artwork, string heading, string quantity, string durabilityText)
        {
            icon.sprite = artwork; icon.enabled = artwork;
            title.text = heading; count.text = ""; durability.text = "";
        }

        public void SetSelected(bool selected)
        {
            GetComponent<Image>().sprite = selected ? selectedFrame : normalFrame;
        }

        private T Child<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (!child)
            {
                var go = new GameObject(childName, typeof(RectTransform), typeof(T));
                go.layer = gameObject.layer; go.transform.SetParent(transform, false); child = go.transform;
            }
            return child.GetComponent<T>();
        }

        private Text Label(string childName, Font font, int size, Color color)
        {
            var label = Child<Text>(childName);
            label.font = font; label.fontSize = size; label.fontStyle = FontStyle.Normal;
            label.color = color; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 10; label.resizeTextMaxSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
