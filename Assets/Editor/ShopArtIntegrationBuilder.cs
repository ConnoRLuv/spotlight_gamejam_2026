using System;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>将主分支商店美术增量接到原交易面板，保留已有对象身份、配置与场景引用。</summary>
    public static class ShopArtIntegrationBuilder
    {
        private const string PanelPath = "Assets/Prefabs/UI/ShopPanel.prefab";
        private const float Width = 1317, Height = 753;
        private static readonly Color Ink = new Color(.92f, .97f, 1f);
        private static readonly Color Cyan = new Color(.43f, .91f, .89f);
        private static readonly Color Blue = new Color(.035f, .06f, .14f);
        private static Font font;

        /// <summary>只更新现有商店预制体的样式和位置，不重建战斗、不写入美术原文件或对话资源。</summary>
        [MenuItem("Spotlight/Shop/Apply Main Artwork To Existing Shop")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var art = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Shop_Popup.prefab");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            if (!art || !font) throw new InvalidOperationException("缺少商店美术或中文字体。");
            var root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                var frame = (RectTransform)root.transform.Find("Layout_16x9");
                var anchorMin = frame.anchorMin; var anchorMax = frame.anchorMax;
                // 保留成员手动调整的根节点激活状态和布局锚点；场景入口仍负责初始隐藏。
                // 留白区域使用不透明底色，避免顶栏衔接处透出战斗背景的亮线。
                root.GetComponent<Image>().color = new Color(.015f, .025f, .055f, 1f);
                frame.GetComponent<AspectRatioFitter>().aspectRatio = Width / Height;
                var background = EnsureRect(frame, "ArtBackground");
                Place(background, new Rect(0, 0, Width, Height));
                var image = EnsureImage(background); image.sprite = art.transform.Find("Background").GetComponent<Image>().sprite;
                image.color = Color.white; image.preserveAspect = true; image.raycastTarget = false; background.SetAsFirstSibling();
                foreach (string name in new[] { "Slogan", "RepairBackground", "ProductsBackground" }) frame.Find(name).gameObject.SetActive(false);

                var heal = frame.Find("HealButton").GetComponent<Button>();
                SkinButton(heal, art.transform.Find("HealButton")); Place((RectTransform)heal.transform, new Rect(62, 102, 211, 84));
                // 图中“治疗”已包含在按钮原图里，费用与恢复量放到右侧，避免重复叠字。
                var healLabel = heal.GetComponentInChildren<Text>(true);
                Style(healLabel, 19, Ink); Place(healLabel.rectTransform, new Rect(220, 0, 185, 70), 211, 84);
                PlaceText(frame.Find("HealthText"), new Rect(283, 176, 190, 25), 19, Ink);
                PlaceText(frame.Find("CurrencyText"), new Rect(552, 97, 245, 28), 20, Cyan);
                PlaceText(frame.Find("ShopTitle"), new Rect(805, 97, 245, 28), 23, Ink);
                PlaceText(frame.Find("RepairTitle"), new Rect(112, 209, 165, 24), 20, Ink);
                PlaceText(frame.Find("RepairHint"), new Rect(277, 210, 165, 22), 14, Cyan);
                frame.Find("RepairHint").GetComponent<Text>().text = "点击义体修复 +1";
                PlaceText(frame.Find("PrototypeHint"), new Rect(536, 647, 510, 43), 16, Ink);
                var exit = frame.Find("ExitButton").GetComponent<Button>();
                SkinButton(exit, art.transform.Find("CancelButton")); Place((RectTransform)exit.transform, new Rect(1175, 10, 82, 83));
                exit.GetComponentInChildren<Text>(true).gameObject.SetActive(false);

                var fields = new SerializedObject(root.GetComponent<ShopPanel>());
                var repairs = fields.FindProperty("repairSlots");
                for (int i = 0; i < repairs.arraySize; i++)
                    StyleRepair((RectTransform)repairs.GetArrayElementAtIndex(i).objectReferenceValue, i);
                var products = fields.FindProperty("productSlots");
                for (int i = 0; i < products.arraySize; i++)
                {
                    var slot = (RectTransform)products.GetArrayElementAtIndex(i).objectReferenceValue;
                    Place(slot, new Rect(551 + i % 3 * 174, 140 + i / 3 * 238, 153, 216));
                    slot.GetComponent<Image>().color = Color.clear;
                    foreach (var label in slot.GetComponentsInChildren<Text>(true))
                        if (!label.GetComponentInParent<CardView>()) Style(label, label.name == "Label" ? 17 : 18, Ink);
                    var button = slot.Find("BuyButton").GetComponent<Button>();
                    button.GetComponent<Image>().color = new Color(.12f, .30f, .37f);
                }
                frame.anchorMin = anchorMin; frame.anchorMax = anchorMax;
                PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("商店美术已接入原 ShopPanel：沿用现有购买、修复、生命恢复与商人对话流程。");
        }

        /// <summary>对齐原图的四个维修框；整块卡面可修复，多型号切换按钮单独处理。</summary>
        private static void StyleRepair(RectTransform slot, int index)
        {
            Place(slot, new Rect(118 + index % 2 * 168, 260 + index / 2 * 187, 135, 183));
            slot.GetComponent<Image>().color = Color.clear;
            PlaceText(slot.Find("Part"), new Rect(6, 6, 123, 22), 18, Cyan, 135, 183);
            PlaceText(slot.Find("Name"), new Rect(4, 96, 127, 22), 16, Ink, 135, 183);
            var icon = EnsureRect(slot, "Icon"); Place(icon, new Rect(33, 31, 69, 62), 135, 183);
            var iconImage = EnsureImage(icon); iconImage.preserveAspect = true; iconImage.raycastTarget = false; iconImage.enabled = false;
            var symbol = EnsureRect(slot, "Symbol"); Place(symbol, new Rect(12, 32, 111, 57), 135, 183);
            Style(EnsureText(symbol), 31, Ink);
            var backdrop = EnsureRect(slot, "DurabilityBackdrop"); Place(backdrop, new Rect(-2, 143, 144, 34), 135, 183);
            var backdropImage = EnsureImage(backdrop); backdropImage.color = Blue; backdropImage.raycastTarget = false;
            PlaceText(slot.Find("Durability"), new Rect(0, 143, 140, 18), 16, Ink, 135, 183);
            slot.Find("Durability").SetAsLastSibling();
            var repair = slot.Find("RepairButton").GetComponent<Button>();
            Place((RectTransform)repair.transform, new Rect(0, 0, 135, 139), 135, 183);
            repair.GetComponent<Image>().color = Color.clear;
            var fee = repair.GetComponentInChildren<Text>(true); Style(fee, 13, Cyan);
            Place(fee.rectTransform, new Rect(-5, 162, 150, 15), 135, 139);
            repair.transform.SetAsLastSibling();
            var cycle = slot.Find("CycleButton");
            Place((RectTransform)cycle, new Rect(11, 118, 113, 19), 135, 183);
            cycle.GetComponent<Image>().color = new Color(.1f, .23f, .31f);
            Style(cycle.GetComponentInChildren<Text>(true), 13, Ink); cycle.SetAsLastSibling();
        }

        /// <summary>复用主分支按钮图像及按压反馈，业务监听仍由 ShopPanel 管理。</summary>
        private static void SkinButton(Button button, Transform source)
        {
            var image = button.GetComponent<Image>(); image.sprite = source.GetComponent<Image>().sprite;
            image.color = Color.white; image.preserveAspect = true;
            var effect = button.GetComponent<ButtonPressEffect>();
            if (!effect) effect = button.gameObject.AddComponent<ButtonPressEffect>();
            EditorUtility.CopySerialized(source.GetComponent<ButtonPressEffect>(), effect);
            var colors = button.colors; colors.normalColor = Color.white; colors.disabledColor = new Color(.42f, .48f, .51f);
            button.colors = colors;
        }

        /// <summary>设置已有文本位置和样式，不替换对象或序列化引用。</summary>
        private static void PlaceText(Transform target, Rect area, int size, Color color, float width = Width, float height = Height)
        { Place((RectTransform)target, area, width, height); Style(target.GetComponent<Text>(), size, color); }

        /// <summary>用左上角原点的设计坐标保存归一化矩形，随面板等比适配分辨率。</summary>
        private static void Place(RectTransform rect, Rect area, float width = Width, float height = Height)
        {
            rect.anchorMin = new Vector2(area.xMin / width, 1 - area.yMax / height);
            rect.anchorMax = new Vector2(area.xMax / width, 1 - area.yMin / height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>统一中文标签的对比度和最小字号，保留原文字内容。</summary>
        private static void Style(Text text, int size, Color color)
        {
            text.font = font; text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; text.resizeTextForBestFit = true; text.resizeTextMinSize = 9; text.resizeTextMaxSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>只补齐缺失节点，重复执行保持已有对象身份。</summary>
        private static RectTransform EnsureRect(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing) return existing;
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false); return rect;
        }

        /// <summary>补齐装饰图片组件。</summary>
        private static Image EnsureImage(RectTransform owner) => owner.GetComponent<Image>() ? owner.GetComponent<Image>() : owner.gameObject.AddComponent<Image>();
        /// <summary>补齐动态文本组件。</summary>
        private static Text EnsureText(RectTransform owner) => owner.GetComponent<Text>() ? owner.GetComponent<Text>() : owner.gameObject.AddComponent<Text>();
    }
}
