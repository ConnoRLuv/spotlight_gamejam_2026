using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>每格代表一次耐久；缓存色块，只有容量增加时创建新格。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class CyberneticDurabilityBar : MonoBehaviour
    {
        [SerializeField] private RectTransform segmentRoot;
        [SerializeField] private List<Image> segments = new List<Image>();
        [SerializeField] private Color availableColor = new Color32(0, 220, 236, 255);
        [SerializeField] private Color consumedColor = new Color32(23, 43, 73, 255);

        /// <summary>空义体隐藏；耗尽义体保留轨道，格数来自配置中的最大耐久。</summary>
        public void Refresh(int current, int maximum)
        {
            gameObject.SetActive(maximum > 0);
            if (maximum <= 0) return;
            EnsureVisuals();
            current = Mathf.Clamp(current, 0, maximum);
            while (segments.Count < maximum)
                segments.Add(CreateImage("Segment_" + segments.Count, segmentRoot));

            for (int i = 0; i < segments.Count; i++)
            {
                var image = segments[i];
                image.gameObject.SetActive(i < maximum);
                if (i >= maximum) continue;
                var rect = image.rectTransform;
                rect.anchorMin = new Vector2((float)i / maximum, 0);
                rect.anchorMax = new Vector2((float)(i + 1) / maximum, 1);
                rect.offsetMin = new Vector2(i == 0 ? 0 : 1, 0);
                rect.offsetMax = new Vector2(i == maximum - 1 ? 0 : -1, 0);
                image.color = i < current ? availableColor : consumedColor;
            }
        }

        private void EnsureVisuals()
        {
            if (segmentRoot) return;
            var border = GetComponent<Image>();
            if (!border) border = gameObject.AddComponent<Image>();
            border.color = new Color32(27, 108, 133, 255);
            border.raycastTarget = false;
            var track = CreateImage("Track", transform);
            track.color = new Color32(7, 21, 39, 255);
            segmentRoot = track.rectTransform;
            segmentRoot.anchorMin = Vector2.zero;
            segmentRoot.anchorMax = Vector2.one;
            segmentRoot.offsetMin = new Vector2(2, 2);
            segmentRoot.offsetMax = new Vector2(-2, -2);
        }

        private static Image CreateImage(string objectName, Transform parent)
        {
            var child = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
    }
}
