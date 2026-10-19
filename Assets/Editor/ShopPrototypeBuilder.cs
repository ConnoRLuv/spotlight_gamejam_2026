using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量创建商店测试预制体和 BattleScene 接线，保留其他场景及已有 UI 的手工修改。</summary>
    public static class ShopPrototypeBuilder
    {
        private const string ScenePath = "Assets/Scenes/BattleScene.unity";
        private const string PrefabFolder = "Assets/Prefabs/UI/";
        private static Font font;
        private static readonly Color Background = new Color(0.94f, 0.94f, 0.92f);
        private static readonly Color Surface = new Color(0.79f, 0.81f, 0.80f);
        private static readonly Color Ink = new Color(0.19f, 0.23f, 0.25f);
        private static readonly Color Accent = new Color(0.39f, 0.47f, 0.49f);
        private static readonly string[] ProductIds = { "PrometheusBrainCard", "PrometheusTorsoCard", "PrometheusHandsCard", "PrometheusLegsCard", "BrainCard", "TorsoCard" };

        /// <summary>创建缺失的资产并连接测试入口；重复执行不会重建面板或新增重复监听。</summary>
        [MenuItem("Spotlight/Shop/Create Prototype And Wire Battle Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            if (!font || !catalog) throw new InvalidOperationException("缺少字体或卡牌预制体目录。");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("BattleScene 有未保存内容，请先保存。");
                var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHUD>(true)).Single();
                var canvas = hud.GetComponent<Canvas>();
                if (!canvas) throw new InvalidOperationException("缺少战斗 Canvas。");
                EnsureFolder("Assets/GameData", "UI"); EnsureFolder("Assets/Prefabs", "UI");
                EnsurePrices();
                var sequence = EnsureDialogue();
                var dialoguePrefab = EnsurePrefab("ShopDialoguePanel", CreateDialogue);
                var shopPrefab = EnsurePrefab("ShopPanel", () => CreateShop(catalog));
                UpgradeShop(shopPrefab, catalog);
                var wrapper = hud.transform.Find("ShopPrototypeTest");
                if (!wrapper) wrapper = Box(hud.transform, "ShopPrototypeTest", new Rect(0, 0, 1920, 1080), null);
                var entry = wrapper.GetComponent<ShopPrototypeTestEntry>();
                if (!entry) entry = wrapper.gameObject.AddComponent<ShopPrototypeTestEntry>();
                // 只有显式运行测试构建菜单才重新启用入口；正式场景默认停用并移除预留按钮。
                entry.enabled = true;
                var controls = hud.transform.Find("BattleLayout_16x9/PilePanelTestControls");
                var button = controls ? controls.Find("TestShopButton") : null;
                if (!button) button = wrapper.Find("TestShopButton");
                if (!button) button = Button(wrapper, "TestShopButton", new Rect(864, 112, 192, 50), "测试商店").transform;
                if (controls)
                {
                    // 与关闭按钮共用父级，使不同屏幕比例下保持同一行和固定间距。
                    var close = controls.Find("ClosePilePanelsButton") as RectTransform;
                    if (close)
                    {
                        var rect = (RectTransform)button; rect.SetParent(controls, false);
                        rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f); rect.pivot = new Vector2(0, 0.5f);
                        rect.sizeDelta = new Vector2(150, close.rect.height);
                        rect.anchoredPosition = close.anchoredPosition + new Vector2(close.rect.width + 12, 0);
                    }
                }
                var modal = wrapper.Find("ModalRoot");
                if (!modal) modal = Box(wrapper, "ModalRoot", new Rect(0, 0, 1920, 1080), new Color(0.15f, 0.18f, 0.19f, 0.4f));
                var dialogue = EnsureInstance<DialoguePanel>(dialoguePrefab, modal);
                var shop = EnsureInstance<ShopPanel>(shopPrefab, modal);
                Set(entry, "hud", hud); Set(entry, "testButton", button.GetComponent<Button>());
                Set(entry, "modalRoot", modal.gameObject); Set(entry, "dialogue", dialogue);
                Set(entry, "shop", shop); Set(entry, "merchantDialogue", sequence);
                var battleLayout = hud.transform.Find("BattleLayout_16x9");
                if (!battleLayout) throw new InvalidOperationException("缺少战斗布局根节点。");
                Set(entry, "toolbar", battleLayout.Find("TopBar") as RectTransform);
                entry.RefreshLayout();
                // Unity 的缺失原生组件可能返回“假 null”，必须使用 Unity 对象的布尔判断。
                var inputGroup = battleLayout.GetComponent<CanvasGroup>();
                if (!inputGroup) inputGroup = battleLayout.gameObject.AddComponent<CanvasGroup>();
                Set(entry, "battleInputGroup", inputGroup);
                dialogue.Hide(0); shop.Hide(0); modal.gameObject.SetActive(false);
                EditorUtility.SetDirty(entry); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("商店已接入 BattleScene：测试商店 → 对话 → 商店。购买获得一张持久牌，修复恢复一点耐久。");
            }
            finally
            {
                // 不切走或保存成员正在编辑的其他场景；增量加载的 BattleScene 用完后卸载。
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>显式给当前测试探索补充塞勒币，不改变正式初始余额或自动赠送货币。</summary>
        [MenuItem("Spotlight/Shop/Grant 3000 Test Coins (Play Mode)")]
        public static void GrantTestCoins()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("该测试菜单只在 Play Mode 使用。");
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            if (!hud || hud.Battle == null) throw new InvalidOperationException("没有当前测试战斗。");
            hud.Battle.Context.Run.AddGold(3000);
        }

        /// <summary>保存四套系列定价，未实现的系列仅保留配置，不伪造卡牌或效果。</summary>
        private static void EnsurePrices()
        {
            EnsureFolder("Assets/GameData", "ShopPrices");
            var standard = EnsurePrice("Standard", "原制式（沿用新世界修复价）", false, 0, 150);
            var prometheus = EnsurePrice("Prometheus", "Type-01：普罗米修斯", true, 1000, 300);
            EnsurePrice("NewWorld", "Type-00：新世界", false, 0, 150);
            EnsurePrice("Nemesis", "Type-02：复仇女神", true, 600, 200);
            foreach (var id in new[] { "BrainCard", "TorsoCard", "HandsCard", "LegsCard", "PrometheusBrainCard", "PrometheusTorsoCard", "PrometheusHandsCard", "PrometheusLegsCard" })
            {
                var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + id + ".asset");
                if (data && !data.shopPricing)
                {
                    data.shopPricing = id.StartsWith("Prometheus", StringComparison.Ordinal) ? prometheus : standard;
                    EditorUtility.SetDirty(data);
                }
            }
        }

        /// <summary>仅创建缺失的价格资产，策划后续调整数值不会被重复构建覆盖。</summary>
        private static CyberneticShopPriceData EnsurePrice(string id, string title, bool purchasable, int purchase, int repair)
        {
            var path = "Assets/GameData/ShopPrices/" + id + ".asset";
            var price = AssetDatabase.LoadAssetAtPath<CyberneticShopPriceData>(path);
            if (price) return price;
            price = ScriptableObject.CreateInstance<CyberneticShopPriceData>();
            price.seriesName = title; price.canPurchase = purchasable; price.purchasePrice = purchase; price.repairPrice = repair;
            AssetDatabase.CreateAsset(price, path); return price;
        }

        /// <summary>为已有商店增量补齐交易引用和型号切换按钮，保留面板根节点与主要布局。</summary>
        private static void UpgradeShop(GameObject prefab, CardPrefabCatalog catalog)
        {
            var path = AssetDatabase.GetAssetPath(prefab); var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panel = root.GetComponent<ShopPanel>(); var fields = new SerializedObject(panel);
                var frame = root.transform.Find("Layout_16x9");
                var heal = frame.Find("HealButton").GetComponent<Button>();
                var healLabel = heal.GetComponentInChildren<Text>(true);
                if (healLabel) healLabel.text = "回血 +" + ShopService.HealingAmount + "\n" + ShopService.HealingPrice + " 塞勒币";
                fields.FindProperty("healButton").objectReferenceValue = heal;
                var repairHint = frame.Find("RepairHint");
                if (repairHint) repairHint.GetComponent<Text>().text = "每次修复恢复 1 点耐久 · 同部位可切换型号";
                var repairs = fields.FindProperty("repairSlots");
                for (int i = 0; i < repairs.arraySize; i++)
                {
                    var slot = (RectTransform)repairs.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (!slot.Find("CycleButton"))
                    {
                        var part = slot.Find("Part") as RectTransform;
                        if (part)
                        {
                            part.anchorMin = new Vector2(10f / 268, 1 - 48f / 274); part.anchorMax = new Vector2(150f / 268, 1 - 10f / 274);
                            part.offsetMin = part.offsetMax = Vector2.zero;
                        }
                        var cycle = Button(slot, "CycleButton", new Rect(156, 12, 102, 32), "切换", 268, 274);
                        cycle.gameObject.SetActive(false);
                    }
                    slot.Find("RepairButton").GetComponentInChildren<Text>(true).text = "修复未定价";
                }
                var definitions = fields.FindProperty("productDefinitions");
                if (definitions.arraySize == 0)
                {
                    definitions.arraySize = ProductIds.Length;
                    for (int i = 0; i < ProductIds.Length; i++) definitions.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + ProductIds[i] + ".asset");
                }
                var slots = fields.FindProperty("productSlots");
                for (int i = 0; i < slots.arraySize && i < definitions.arraySize; i++)
                {
                    var slot = (RectTransform)slots.GetArrayElementAtIndex(i).objectReferenceValue;
                    var data = definitions.GetArrayElementAtIndex(i).objectReferenceValue as CyberneticData;
                    if (!data) continue;
                    var existing = slot.Find("CardPreview");
                    if (!existing || existing.GetComponent<CardView>().Data != data.cards[0])
                    {
                        if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                        var card = UnityEngine.Object.Instantiate(catalog.Find(data.cards[0]), slot); card.name = "CardPreview"; card.SetInteractable(false);
                        var rect = (RectTransform)card.transform; rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1); rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -12);
                        rect.localScale = Vector3.one * Mathf.Min(270 / rect.rect.width, 258 / rect.rect.height);
                    }
                    var partLabel = slot.Find("Part");
                    if (partLabel) partLabel.GetComponent<Text>().text = new[] { "脑机", "躯干", "手部", "腿部" }[(int)data.slot];
                    slot.Find("Price").GetComponent<Text>().text = data.shopPricing && data.shopPricing.canPurchase ? data.shopPricing.purchasePrice + " 塞勒币" : "购买价格未定";
                    slot.Find("BuyButton").GetComponentInChildren<Text>(true).text = "购买 ×1";
                }
                fields.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>创建默认商人台词，已有配置由成员维护，后续执行不覆盖。</summary>
        private static DialogueSequenceData EnsureDialogue()
        {
            const string path = "Assets/GameData/UI/MerchantDialogue.asset";
            var data = AssetDatabase.LoadAssetAtPath<DialogueSequenceData>(path);
            if (data) return data;
            data = ScriptableObject.CreateInstance<DialogueSequenceData>();
            data.leftName = "玩家"; data.rightName = "义体商人"; data.finalContinueLabel = "进入商店";
            data.leftPortrait = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Hero/玩家.png");
            data.rightPortrait = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/NPC/义体医生.png");
            data.lines = new[] {
                new DialogueSequenceData.Line { text = "欢迎，旅人。看看这些义体，也许有你需要的东西。" },
                new DialogueSequenceData.Line { text = "挑一件看看吧。修理和补给也在这里——目前先让你熟悉一下店面。" }
            };
            AssetDatabase.CreateAsset(data, path); return data;
        }

        /// <summary>创建左右人物和底部对话框，台词与结束行为在运行时绑定。</summary>
        private static GameObject CreateDialogue()
        {
            var root = PanelRoot("DialoguePanel"); var panel = root.AddComponent<DialoguePanel>();
            var frame = Frame(root.transform);
            Label(frame, "LocationTitle", new Rect(520, 56, 880, 64), "义体商人的店", 38);
            var left = Box(frame, "PlayerPortrait", new Rect(130, 170, 350, 490), Accent).GetComponent<Image>();
            var right = Box(frame, "MerchantPortrait", new Rect(1440, 170, 350, 490), Accent).GetComponent<Image>();
            Set(panel, "leftPortrait", left); Set(panel, "rightPortrait", right);
            Set(panel, "leftName", Label(frame, "PlayerName", new Rect(130, 675, 350, 48), "玩家", 28));
            Set(panel, "rightName", Label(frame, "MerchantName", new Rect(1440, 675, 350, 48), "商人", 28));
            var dialogue = Box(frame, "DialogueBox", new Rect(100, 754, 1720, 250), Surface);
            Set(panel, "speakerText", Label(dialogue, "Speaker", new Rect(30, 18, 1290, 42), "说话人", 28, TextAnchor.MiddleLeft, 1720, 250));
            Set(panel, "dialogueText", Label(dialogue, "Text", new Rect(30, 70, 1290, 140), "对话文本", 30, TextAnchor.UpperLeft, 1720, 250));
            Set(panel, "progressText", Label(dialogue, "Progress", new Rect(1370, 30, 300, 40), "1 / 2", 24, TextAnchor.MiddleCenter, 1720, 250));
            var next = Button(dialogue, "ContinueButton", new Rect(1370, 120, 300, 80), "继续", 1720, 250);
            Set(panel, "continueButton", next); Set(panel, "continueLabel", next.GetComponentInChildren<Text>());
            Set(panel, "cancelButton", Button(frame, "CancelButton", new Rect(1630, 44, 190, 60), "离开"));
            return root;
        }

        /// <summary>创建左侧补给维修区与右侧三列两行商品区，实际交易资格由运行时服务决定。</summary>
        private static GameObject CreateShop(CardPrefabCatalog catalog)
        {
            var root = PanelRoot("ShopPanel"); var panel = root.AddComponent<ShopPanel>(); var frame = Frame(root.transform);
            var slogan = Box(frame, "Slogan", new Rect(56, 54, 620, 86), Surface);
            Label(slogan, "Text", new Rect(20, 8, 580, 70), "为下一段旅途，留一份可能。", 28, TextAnchor.MiddleCenter, 620, 86);
            var heal = Button(frame, "HealButton", new Rect(56, 164, 280, 84), "回血 +" + ShopService.HealingAmount + "\n" + ShopService.HealingPrice + " 塞勒币"); heal.interactable = false;
            Set(panel, "healButton", heal);
            Box(frame, "RepairBackground", new Rect(56, 274, 620, 736), Surface);
            Label(frame, "RepairTitle", new Rect(76, 288, 580, 54), "带入义体", 34);
            Label(frame, "RepairHint", new Rect(76, 344, 580, 38), "当前带入义体 · 修复功能待接入", 22);
            string[] parts = { "脑机", "躯干", "手部", "腿部" };
            var repairs = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var slot = Box(frame, "RepairSlot_" + parts[i], new Rect(86 + i % 2 * 292, 402 + i / 2 * 294, 268, 274), new Color(0.71f, 0.75f, 0.74f));
                repairs[i] = slot;
                Label(slot, "Part", new Rect(10, 10, 248, 38), parts[i], 28, TextAnchor.MiddleCenter, 268, 274);
                Label(slot, "Name", new Rect(12, 54, 244, 102), "", 26, TextAnchor.MiddleCenter, 268, 274);
                Label(slot, "Durability", new Rect(10, 158, 248, 40), "", 23, TextAnchor.MiddleCenter, 268, 274);
                var repairButton = Button(slot, "RepairButton", new Rect(14, 212, 240, 44), "修复 · 待接入", 268, 274);
                repairButton.interactable = false; repairButton.gameObject.SetActive(false);
            }
            SetArray(panel, "repairSlots", repairs);
            Box(frame, "ProductsBackground", new Rect(714, 54, 1150, 956), Surface);
            Label(frame, "ShopTitle", new Rect(750, 78, 650, 70), "义体商店", 48);
            Set(panel, "exitButton", Button(frame, "ExitButton", new Rect(1690, 72, 150, 68), "退出"));
            var products = new RectTransform[6];
            for (int i = 0; i < products.Length; i++)
            {
                var config = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + ProductIds[i] + ".asset");
                var slot = Box(frame, "ProductSlot_" + i, new Rect(750 + i % 3 * 365, 192 + i / 3 * 400, 345, 380), new Color(0.87f, 0.88f, 0.85f));
                products[i] = slot;
                var card = UnityEngine.Object.Instantiate(catalog.Find(config.cards[0]), slot);
                card.name = "CardPreview"; card.SetInteractable(false);
                var rect = (RectTransform)card.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1); rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -12);
                float scale = Mathf.Min(270 / rect.rect.width, 258 / rect.rect.height); rect.localScale = Vector3.one * scale;
                Label(slot, "Part", new Rect(10, 276, 325, 32), parts[(int)config.slot], 24, TextAnchor.MiddleCenter, 345, 380);
                Label(slot, "Price", new Rect(10, 310, 325, 28), "价格待定", 23, TextAnchor.MiddleCenter, 345, 380);
                Button(slot, "BuyButton", new Rect(24, 344, 297, 32), "购买 · 功能待接入", 345, 380).interactable = false;
            }
            SetArray(panel, "productSlots", products);
            return root;
        }

        /// <summary>创建覆盖全屏的面板根节点，背景与射线范围始终填满屏幕。</summary>
        private static GameObject PanelRoot(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.layer = 5; root.GetComponent<Image>().color = Background;
            var rect = root.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            return root;
        }

        /// <summary>以固定设计比例容纳内容，宽屏和四比三屏均保持控件相对位置。</summary>
        private static RectTransform Frame(Transform parent)
        {
            var frame = Box(parent, "Layout_16x9", new Rect(0, 0, 1920, 1080), null);
            var fitter = frame.gameObject.AddComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fitter.aspectRatio = 16f / 9f;
            return frame;
        }

        /// <summary>创建归一化矩形，坐标采用父级设计尺寸下的左上角原点。</summary>
        private static RectTransform Box(Transform parent, string name, Rect area, Color? color, float width = 1920, float height = 1080)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(area.xMin / width, 1 - area.yMax / height);
            rect.anchorMax = new Vector2(area.xMax / width, 1 - area.yMin / height); rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (color.HasValue) go.AddComponent<Image>().color = color.Value;
            return rect;
        }

        /// <summary>创建中文文本；装饰文字不拦截指针，长文本限制在控件内部。</summary>
        private static Text Label(Transform parent, string name, Rect area, string value, int size, TextAnchor alignment = TextAnchor.MiddleCenter, float width = 1920, float height = 1080)
        {
            var text = Box(parent, name, area, null, width, height).gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = Ink; text.alignment = alignment;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            // CJK 字体实际行高大于字号；短标签在不同画布尺寸下仍需有足够空间绘制。
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Max(10, Mathf.FloorToInt(size * 0.5f)); text.resizeTextMaxSize = size;
            return text;
        }

        /// <summary>创建带标签按钮；交易占位控件由调用方禁用，不绑定任何支付接口。</summary>
        private static Button Button(Transform parent, string name, Rect area, string value, float width = 1920, float height = 1080)
        {
            var rect = Box(parent, name, area, Accent, width, height); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.disabledColor = new Color(0.80f, 0.83f, 0.82f); button.colors = colors;
            var text = Label(rect, "Label", new Rect(0, 0, area.width, area.height), value, area.height < 48 ? 21 : 26, TextAnchor.MiddleCenter, area.width, area.height);
            text.color = Color.white; return button;
        }

        /// <summary>仅创建缺失的预制体，已有资产与 GUID 保持不变。</summary>
        private static GameObject EnsurePrefab(string name, Func<GameObject> create)
        {
            var path = PrefabFolder + name + ".prefab"; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) return prefab;
            var root = create();
            try { root.SetActive(false); return PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>复用场景中已存在的实例，不覆盖成员对面板布局的调整。</summary>
        private static T EnsureInstance<T>(GameObject prefab, Transform parent) where T : Component
        {
            var existing = parent.GetComponentInChildren<T>(true);
            if (existing) return existing;
            return ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).GetComponent<T>();
        }

        /// <summary>通过序列化 API 设置私有引用，保存为正常的 Unity 资产引用。</summary>
        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>配置四个维修位置或六个商品位置的只读控件引用。</summary>
        private static void SetArray(UnityEngine.Object target, string field, RectTransform[] values)
        {
            var serialized = new SerializedObject(target); var property = serialized.FindProperty(field); property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>按需创建资源目录，避免重复创建或移动现有资产。</summary>
        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
