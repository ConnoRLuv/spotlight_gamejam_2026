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
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
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
                RemoveLabel(heal.transform, "Label");
                RemoveLabel(frame, "HealthText");
                // 两行图标与数值独立于治疗按钮，不受按钮禁用颜色影响。
                var healthIcon = EnsureImage(EnsureRect(frame, "HealingHealthIcon"));
                Place(healthIcon.rectTransform, new Rect(307, 106, 38, 38));
                healthIcon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Healthicon.png");
                healthIcon.color = Color.white; healthIcon.preserveAspect = true; healthIcon.raycastTarget = false;
                var healCoin = EnsureImage(EnsureRect(frame, "HealingCurrencyIcon"));
                Place(healCoin.rectTransform, new Rect(307, 150, 38, 38));
                healCoin.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Goldicon.png");
                healCoin.color = Color.white; healCoin.preserveAspect = true; healCoin.raycastTarget = false;
                var amount = EnsureText(EnsureRect(frame, "HealingAmount")); amount.text = "+" + ShopService.HealingAmount;
                PlaceText(amount.transform, new Rect(350, 106, 92, 38), 22, Ink);
                var price = EnsureText(EnsureRect(frame, "HealingPrice")); price.text = ShopService.HealingPrice.ToString();
                PlaceText(price.transform, new Rect(350, 150, 92, 38), 22, Ink);
                foreach (string label in new[] { "CurrencyText", "ShopTitle", "RepairTitle", "RepairHint" }) RemoveLabel(frame, label);
                RemoveLabel(frame, "PrototypeHint");
                var exit = frame.Find("ExitButton").GetComponent<Button>();
                SkinButton(exit, art.transform.Find("CancelButton")); Place((RectTransform)exit.transform, new Rect(1175, 10, 82, 83));
                exit.GetComponentInChildren<Text>(true).gameObject.SetActive(false);

                var fields = new SerializedObject(root.GetComponent<ShopPanel>());
                fields.FindProperty("healingAmountText").objectReferenceValue = amount;
                fields.FindProperty("healingPriceText").objectReferenceValue = price;
                fields.ApplyModifiedPropertiesWithoutUndo();
                var repairs = fields.FindProperty("repairSlots");
                for (int i = 0; i < repairs.arraySize; i++)
                    StyleRepair((RectTransform)repairs.GetArrayElementAtIndex(i).objectReferenceValue, i);
                var products = fields.FindProperty("productSlots");
                for (int i = 0; i < products.arraySize; i++)
                {
                    var slot = (RectTransform)products.GetArrayElementAtIndex(i).objectReferenceValue;
                    // 卡面下方不再重复显示部位，保留价格与购买状态。
                    RemoveLabel(slot, "Part");
                    // 复用配置中的完整卡面美术，保留 CardView 以继续支持无美术商品的模板展示。
                    var face = EnsureImage(EnsureRect(slot.Find("CardPreview"), "ArtFace"));
                    face.rectTransform.anchorMin = Vector2.zero; face.rectTransform.anchorMax = Vector2.one;
                    face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
                    face.preserveAspect = true; face.raycastTarget = false;
                    face.transform.SetAsLastSibling();
                    var cardRoot = slot.Find("CardPreview");
                    if (!cardRoot.GetComponent<ShopRepairHoldPreview>()) cardRoot.gameObject.AddComponent<ShopRepairHoldPreview>();
                    // 独立透明点击区不受模板底图停用影响，也不覆盖下方购买按钮。
                    var hitArea = EnsureImage(EnsureRect(cardRoot, "HoldHitArea"));
                    hitArea.rectTransform.anchorMin = Vector2.zero; hitArea.rectTransform.anchorMax = Vector2.one;
                    hitArea.rectTransform.offsetMin = hitArea.rectTransform.offsetMax = Vector2.zero;
                    hitArea.color = Color.clear; hitArea.raycastTarget = true; hitArea.transform.SetAsLastSibling();
                    Place(slot, new Rect(565 + i % 3 * 174, 114 + i / 3 * 250, 153, 246));
                    // 收紧卡面与价格、按钮的距离，六个商品共享一致的基线。
                    Place((RectTransform)slot.Find("Price"), new Rect(4, 180, 145, 30), 153, 246);
                    Place((RectTransform)slot.Find("BuyButton"), new Rect(7, 212, 139, 30), 153, 246);
                    // 无完整美术的模板也为 22 号字体提供足够行高。
                    Place((RectTransform)cardRoot.Find("Name"), new Rect(8, 8, 164, 56), 180, 255);
                    Place((RectTransform)cardRoot.Find("Cost"), new Rect(8, 66, 164, 32), 180, 255);
                    Place((RectTransform)cardRoot.Find("ColorPlaceholder"), new Rect(10, 102, 160, 12), 180, 255);
                    Place((RectTransform)cardRoot.Find("Description"), new Rect(10, 119, 160, 92), 180, 255);
                    Place((RectTransform)cardRoot.Find("Source"), new Rect(8, 219, 164, 32), 180, 255);
                    slot.GetComponent<Image>().color = Color.clear;
                    foreach (var label in slot.GetComponentsInChildren<Text>(true))
                        if (!label.GetComponentInParent<CardView>()) Style(label, label.name == "Label" ? 17 : 18, Ink);
                    var button = slot.Find("BuyButton").GetComponent<Button>();
                    button.GetComponent<Image>().color = new Color(.12f, .30f, .37f);
                }
                // 统一剩余文字组件；完整卡图中的字属于美术图片本身。
                foreach (var label in root.GetComponentsInChildren<Text>(true)) Style(label, 22, label.color);
                // 治疗数值按当前界面调整单独放大，其他商店文字维持 22 号。
                Style(amount, 28, Ink); Style(price, 28, Ink);
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
            RemoveLabel(slot, "Part"); RemoveLabel(slot, "Name");
            var icon = EnsureRect(slot, "Icon"); Place(icon, new Rect(17, 17, 101, 94), 135, 183);
            var iconImage = EnsureImage(icon); iconImage.preserveAspect = true; iconImage.raycastTarget = false; iconImage.enabled = false;
            var symbol = EnsureRect(slot, "Symbol"); Place(symbol, new Rect(12, 32, 111, 57), 135, 183);
            Style(EnsureText(symbol), 31, Ink);
            var backdrop = EnsureRect(slot, "DurabilityBackdrop"); Place(backdrop, new Rect(-2, 143, 144, 28), 135, 183);
            var backdropImage = EnsureImage(backdrop); backdropImage.color = Blue; backdropImage.raycastTarget = false;
            PlaceText(slot.Find("Durability"), new Rect(0, 143, 80, 27), 22, Ink, 135, 183);
            var rowFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
            slot.Find("Durability").GetComponent<Text>().font = rowFont;
            var coin = EnsureImage(EnsureRect(slot, "RepairCurrencyIcon"));
            Place(coin.rectTransform, new Rect(81, 147, 18, 19), 135, 183);
            coin.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Goldicon.png");
            coin.color = Color.white; coin.preserveAspect = true; coin.raycastTarget = false;
            slot.Find("Durability").SetAsLastSibling();
            var repair = slot.Find("RepairButton").GetComponent<Button>();
            if (!repair.GetComponent<ShopRepairHoldPreview>()) repair.gameObject.AddComponent<ShopRepairHoldPreview>();
            Place((RectTransform)repair.transform, new Rect(0, 0, 135, 139), 135, 183);
            repair.GetComponent<Image>().color = Color.clear;
            var fee = repair.GetComponentInChildren<Text>(true); Style(fee, 22, Ink);
            fee.font = rowFont;
            Place(fee.rectTransform, new Rect(101, 143, 39, 27), 135, 139);
            repair.transform.SetAsLastSibling();
            var cycle = slot.Find("CycleButton");
            Place((RectTransform)cycle, new Rect(11, 108, 113, 28), 135, 183);
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

        /// <summary>删除已移除的静态标签，允许对新版预制体重复执行构建。</summary>
        private static void RemoveLabel(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child) UnityEngine.Object.DestroyImmediate(child.gameObject);
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
            text.raycastTarget = false; text.resizeTextForBestFit = false;
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
