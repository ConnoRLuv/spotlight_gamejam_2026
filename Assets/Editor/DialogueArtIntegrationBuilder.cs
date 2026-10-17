using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>将现有对话美术接入可复用的商人对话预制体，保留流程脚本及序列化引用。</summary>
    public static class DialogueArtIntegrationBuilder
    {
        private const string PanelPath = "Assets/Prefabs/UI/ShopDialoguePanel.prefab";
        private static readonly Color Ink = new Color(.92f, .97f, 1f);

        /// <summary>增量更新现有控件的图片、位置和文字，重复执行不会创建重复节点。</summary>
        [MenuItem("Spotlight/Shop/Apply Existing Dialogue Artwork")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var art = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DialoguePanel.prefab");
            if (!art) throw new InvalidOperationException("缺少已有对话美术预制体。");
            var root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                root.GetComponent<Image>().color = new Color(.015f, .025f, .055f, 1f);
                var frame = root.transform.Find("Layout_16x9");
                var background = EnsureImage(frame, "ArtBackground");
                background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Background/地下城.png");
                background.color = new Color(.48f, .48f, .58f, 1f);
                background.preserveAspect = true; background.raycastTarget = false;
                Place(background.rectTransform, 0, 0, 1920, 1080);
                background.transform.SetAsFirstSibling();
                frame.Find("LocationTitle").gameObject.SetActive(false);
                // 玩家立绘缩至原来的 75%，保持底部位置与图片宽高比例。
                Place((RectTransform)frame.Find("PlayerPortrait"), 235, 242.5f, 300, 532.5f);
                Place((RectTransform)frame.Find("MerchantPortrait"), 1280, 100, 455, 710);
                // 人物标签由下方说话人框统一呈现，保留字段供通用对话脚本正常赋值。
                frame.Find("PlayerName").gameObject.SetActive(false);
                frame.Find("MerchantName").gameObject.SetActive(false);
                var box = frame.Find("DialogueBox");
                Skin(box.GetComponent<Image>(), art.transform.Find("DialogBox").GetComponent<Image>().sprite);
                Place((RectTransform)box, 62, 808, 1796, 255);
                Style(box.Find("Text").GetComponent<Text>(), 29, TextAnchor.UpperLeft);
                Place((RectTransform)box.Find("Text"), 85, 51, 1450, 138, 1796, 255);
                Style(box.Find("Progress").GetComponent<Text>(), 20, TextAnchor.MiddleRight);
                Place((RectTransform)box.Find("Progress"), 1500, 30, 170, 35, 1796, 255);
                var name = EnsureImage(frame, "SpeakerNameBackground");
                Skin(name, art.transform.Find("Namebox").GetComponent<Image>().sprite);
                Place(name.rectTransform, 100, 726, 246, 109);
                // 原说话人文本保留对象身份，只调整为覆盖美术姓名框。
                var fields = new SerializedObject(root.GetComponent<SpotlightGameJam.UI.DialoguePanel>());
                var speaker = ((Text)fields.FindProperty("speakerText").objectReferenceValue).transform;
                speaker.SetParent(name.transform, false);
                Place((RectTransform)speaker, 14, 12, 216, 78, 246, 109);
                Style(speaker.GetComponent<Text>(), 27, TextAnchor.MiddleCenter);
                var next = box.Find("ContinueButton");
                Skin(next.GetComponent<Image>(), name.sprite);
                Place((RectTransform)next, 1465, 157, 245, 75, 1796, 255);
                var nextLabel = next.GetComponentInChildren<Text>(true);
                Style(nextLabel, 24, TextAnchor.MiddleCenter);
                Place(nextLabel.rectTransform, 0, 0, 245, 75, 245, 75);
                // 对齐按钮可见内框，文字在几何居中的基础上再略微上移。
                nextLabel.rectTransform.anchoredPosition += new Vector2(0, 4f);
                // 中文字体按实际字形边界对齐，避免字体基线留白使文字看起来偏下。
                nextLabel.alignByGeometry = true;
                var cancel = frame.Find("CancelButton");
                Skin(cancel.GetComponent<Image>(), AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CancelButton.png"));
                Place((RectTransform)cancel, 1770, 30, 95, 95);
                cancel.GetComponentInChildren<Text>(true).gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>复用已有装饰节点；首次接入时才补充 Image。</summary>
        private static Image EnsureImage(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (!child) { child = new GameObject(name, typeof(RectTransform), typeof(Image)).transform; child.SetParent(parent, false); child.gameObject.layer = 5; }
            return child.GetComponent<Image>();
        }

        /// <summary>以设计坐标设置归一化锚点，跟随现有等比布局缩放。</summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height, float parentWidth = 1920, float parentHeight = 1080)
        {
            rect.anchorMin = new Vector2(x / parentWidth, 1 - (y + height) / parentHeight);
            rect.anchorMax = new Vector2((x + width) / parentWidth, 1 - y / parentHeight);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        /// <summary>图片保持原始比例，装饰不拦截鼠标，按钮继续接收点击。</summary>
        private static void Skin(Image image, Sprite sprite)
        {
            image.sprite = sprite; image.color = Color.white; image.preserveAspect = true;
            image.raycastTarget = image.GetComponent<Button>();
        }

        /// <summary>使用浅色中文文本匹配深色美术底板，限制字号以兼容窄屏。</summary>
        private static void Style(Text text, int size, TextAnchor alignment)
        {
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
            text.color = Ink; text.fontSize = size; text.alignment = alignment;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }
    }
}
