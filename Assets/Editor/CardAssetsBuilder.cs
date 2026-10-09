using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>生成七种卡牌资产并接入现有场景；已有配置和预制体保留，不覆盖成员修改。</summary>
    public static class CardAssetsBuilder
    {
        private static readonly string[] Keys = { "AttackCard", "DefenseCard", "HealCard", "BrainCard", "TorsoCard", "HandsCard", "LegsCard" };
        private static readonly string[] Names = { "攻击", "防御", "回复", "军用战术协调矩阵", "军用生命维持器系统", "军用脉冲发射器", "军用战术推进器" };
        private static readonly string[] Colors = { "#B98C88", "#8D9EB5", "#91AA99", "#A398B8", "#BAAB8F", "#B79A89", "#88AAA7" };
        private static readonly int[] Sanity = { 0, 0, 0, 8, 4, 2, 8 };
        private static readonly string[] Descriptions = {
            "对一名敌人造成 6 点伤害。", "获得 3 点护盾。\n下个玩家回合开始时清除。", "恢复 3 点生命，不超过生命上限。",
            "查看义体抽牌堆顶最多 3 张，选择 1 张加入手牌。",
            "下次伤害先扣护盾，再消耗理智；理智不足的部分扣生命。",
            "本回合每 1 AP 生成一张免费攻击，造成 6 点伤害。\n生成牌用后或回合结束时移除。",
            "结束回合时跳过敌人，进入一个完整的新玩家回合。" };
        private static Font font;
        private static readonly Color Ink = new Color(0.17f, 0.19f, 0.21f);

        /// <summary>菜单和批处理共用入口；资产均由 Unity 原生 API 创建。</summary>
        [MenuItem("Spotlight/Cards/Create Assets And Wire BattleScene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式创建资产。");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            if (!font) throw new InvalidOperationException("中文字体未导入。");
            foreach (var path in new[] { "Assets/GameData/Cards", "Assets/GameData/Effects", "Assets/GameData/Cybernetics", "Assets/Prefabs/Cards" })
                Directory.CreateDirectory(path);
            AssetDatabase.Refresh();

            var cards = new CardData[7];
            var effects = new CardEffectData[7];
            effects[0] = Asset<DamageEffectData>("Effects/Attack", value => value.amount = 6);
            effects[1] = Asset<ShieldEffectData>("Effects/Defense", value => value.amount = 3);
            effects[2] = Asset<HealEffectData>("Effects/Heal", value => value.amount = 3);
            for (int i = 0; i < 3; i++) cards[i] = MakeCard(i, effects[i]);
            effects[3] = Asset<CyberneticPeekEffectData>("Effects/Brain", value => value.count = 3);
            effects[4] = Asset<DamageToSanityEffectData>("Effects/Torso", value => { });
            effects[5] = Asset<AttackConversionEffectData>("Effects/Hands", value => value.attack = cards[0]);
            effects[6] = Asset<ExtraTurnEffectData>("Effects/Legs", value => { });
            for (int i = 3; i < 7; i++) cards[i] = MakeCard(i, effects[i]);
            var cybernetics = new CyberneticData[4];
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                cybernetics[i] = Asset<CyberneticData>("Cybernetics/" + Keys[i + 3], value => {
                    value.cyberneticId = Keys[slot + 3]; value.slot = (CyberneticSlot)slot;
                    value.maxDurability = slot < 2 ? 5 : 3;
                    value.cards = Enumerable.Repeat(cards[slot + 3], 3).ToArray();
                });
            }
            var prefabs = new CardView[7];
            for (int i = 0; i < 7; i++) prefabs[i] = MakePrefab(i, cards[i], i < 3 ? null : cybernetics[i - 3]);
            var catalog = Asset<CardPrefabCatalog>("CardPrefabCatalog", value => {
                value.entries = cards.Select((card, index) => new CardPrefabCatalog.Entry { data = card, prefab = prefabs[index] }).ToArray();
                value.fallback = prefabs[0];
            });
            var rules = Asset<BattleRules>("BattleRules", value => { });
            var phantom = Asset<CardData>("Cards/PhantomPain", value => {
                value.cardId = "phantom-pain"; value.cardName = "幻痛"; value.category = CardCategory.Special;
                value.locksInHand = true; value.targetType = CardTargetType.None;
                value.description = "无法使用或弃置，占用普通手牌位置。\n跨战斗保留，理智恢复到大于 0 时移除。\n玩家造成的伤害目标随机，包含玩家自身。";
            });
            AssetDatabase.SaveAssets();
            WireScene(cards, cybernetics, catalog, rules, phantom);
            ValidateAssets();
            Debug.Log("七种卡牌预制体、效果配置与 BattleScene 接入完成。");
        }

        /// <summary>加载已有配置资产；仅在资产缺失时创建并应用初始化配置。</summary>
        private static T Asset<T>(string key, Action<T> configure) where T : ScriptableObject
        {
            var path = "Assets/GameData/" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing;
            var value = ScriptableObject.CreateInstance<T>(); configure(value);
            AssetDatabase.CreateAsset(value, path); return value;
        }
        /// <summary>创建或复用卡牌配置，关联费用、描述、目标和效果。</summary>
        private static CardData MakeCard(int index, CardEffectData effect) => Asset<CardData>("Cards/" + Keys[index], value => {
            value.cardId = Keys[index]; value.cardName = Names[index]; value.description = Descriptions[index];
            value.category = index < 3 ? CardCategory.Basic : CardCategory.Cybernetic;
            value.cost = index < 3 ? 1 : 0; value.sanityCost = Sanity[index];
            value.targetType = index == 0 ? CardTargetType.SingleEnemy : CardTargetType.Self;
            value.cardType = index == 0 ? CardType.Attack : index < 3 ? CardType.Skill : CardType.Power;
            value.effects = new[] { effect };
        });

        /// <summary>创建或复用卡牌预制体，配置占位外观及卡面脚本引用。</summary>
        private static CardView MakePrefab(int index, CardData data, CyberneticData cybernetic)
        {
            var path = "Assets/Prefabs/Cards/" + Keys[index] + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing) return existing.GetComponent<CardView>();
            var root = new GameObject(Keys[index], typeof(RectTransform), typeof(Image), typeof(Button), typeof(CardView));
            root.layer = 5;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 210);
            try
            {
                ColorUtility.TryParseHtmlString(Colors[index], out var color);
                var image = root.GetComponent<Image>(); image.color = color;
                var button = root.GetComponent<Button>(); button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                var outline = root.AddComponent<Outline>(); outline.effectColor = new Color(0.2f, 0.23f, 0.27f, 0.5f); outline.effectDistance = new Vector2(1, -1);
                var title = TextAt(root.transform, "Name", new Rect(8, 8, 164, 43), 18);
                title.fontStyle = FontStyle.Bold;
                var cost = TextAt(root.transform, "Cost", new Rect(8, 53, 164, 22), 14);
                var strip = Rect(root.transform, "ColorPlaceholder", new Rect(10, 79, 160, 17)).gameObject.AddComponent<Image>();
                strip.color = new Color(1, 1, 1, 0.2f); strip.raycastTarget = false;
                var description = TextAt(root.transform, "Description", new Rect(10, 102, 160, 82), 12);
                description.alignment = TextAnchor.UpperLeft;
                var source = TextAt(root.transform, "Source", new Rect(8, 187, 164, 18), 11);
                var layout = root.AddComponent<LayoutElement>(); layout.preferredWidth = 180; layout.preferredHeight = 210;
                root.GetComponent<CardView>().Configure(data, cybernetic, color, image, button, title, cost, description, source);
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<CardView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>调整七种默认预制体的说明区域，保证最长的手部效果文字完整显示。</summary>
        [MenuItem("Spotlight/Cards/Fit Default Description Text")]
        public static void FitDescriptionText()
        {
            foreach (var key in Keys)
            {
                var path = "Assets/Prefabs/Cards/" + key + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var text = root.transform.Find("Description").GetComponent<Text>();
                    text.fontSize = 12;
                    text.rectTransform.sizeDelta = new Vector2(160, 82);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>将新增场景控件锚定到参考布局，避免宽高比变化时固定像素坐标偏移。</summary>
        [MenuItem("Spotlight/Cards/Fit Current Scene Layout")]
        public static void FitSceneLayout()
        {
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            if (!references || !references.GetComponent<BattleHUD>()) throw new InvalidOperationException("请先打开已接入卡牌的 BattleScene。");
            var frame = references.transform.Find("BattleLayout_16x9");
            AnchorBounds((RectTransform)frame.Find("ConvertApButton"), new Rect(181, 932, 183, 38), 1928, 1088);
            var label = frame.Find("ConvertApButton/Label").GetComponent<Text>();
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 10; label.resizeTextMaxSize = 18;
            AnchorBounds((RectTransform)frame.Find("BattleStatus"), new Rect(650, 746, 610, 40), 1928, 1088);
            for (int i = 0; i < 4; i++)
            {
                var area = references.cyberneticDurabilityArea;
                AnchorBounds((RectTransform)area.Find("Slot_" + i), new Rect(12, 26 + i * 73, 30, 40), 163, 344);
                AnchorBounds((RectTransform)area.Find("Durability_" + i), new Rect(47, 18 + i * 73, 109, 60), 163, 344);
            }
            EditorSceneManager.MarkSceneDirty(references.gameObject.scene);
        }

        /// <summary>批处理布局修正入口，仅保存已生成的 BattleScene。</summary>
        public static void FitBattleSceneLayout()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            FitSceneLayout(); EditorSceneManager.SaveScene(scene);
        }
        /// <summary>设置矩形的归一化锚点与偏移，使卡面元素随容器缩放。</summary>
        private static void AnchorBounds(RectTransform rect, Rect bounds, float width, float height)
        {
            rect.anchorMin = new Vector2(bounds.xMin / width, 1 - bounds.yMax / height);
            rect.anchorMax = new Vector2(bounds.xMax / width, 1 - bounds.yMin / height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>仅新增必要 UI 和入口；不重建已有角色、顶部状态栏或相机。</summary>
        private static void WireScene(CardData[] cards, CyberneticData[] cybernetics, CardPrefabCatalog catalog, BattleRules rules, CardData phantom)
        {
            if (!Application.isBatchMode && UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("当前场景有未保存修改，请先保存后再接入。");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            if (!references) throw new InvalidOperationException("BattleScene 缺少 UI 引用。");
            if (references.GetComponent<BattleHUD>()) return;
            var canvas = references.GetComponent<Canvas>();
            var frame = references.transform.Find("BattleLayout_16x9");
            var bootstrap = new GameObject("BattleGame", typeof(GameBootstrap)).GetComponent<GameBootstrap>();
            var boot = new SerializedObject(bootstrap);
            Set(boot, "rules", rules); Set(boot, "phantomPain", phantom);
            // 普通牌按总牌组洗牌后顺序抽取，不保证每种各一张；过载由功能牌入口追加三张。
            SetArray(boot, "ordinaryDeck", cards.Take(3).SelectMany((value, index) => Enumerable.Repeat(value, index == 0 ? 4 : 3))
                .Cast<UnityEngine.Object>().ToArray());
            SetArray(boot, "initialCybernetics", cybernetics.Cast<UnityEngine.Object>().ToArray());
            boot.ApplyModifiedPropertiesWithoutUndo();

            references.handArea.Find("Placeholder").gameObject.SetActive(false);
            var originalCards = references.handArea.Find("Cards");
            var viewport = Rect(references.handArea, "Viewport", new Rect(0, 0, 1, 1));
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6, 4); viewport.offsetMax = new Vector2(-6, -31);
            var viewportImage = viewport.gameObject.AddComponent<Image>(); viewportImage.color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            originalCards.SetParent(viewport, false);
            var content = (RectTransform)originalCards;
            content.anchorMin = content.anchorMax = new Vector2(0, 0.5f); content.pivot = new Vector2(0, 0.5f);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 184);
            Horizontal(content, 12, true);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = references.handArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var ordinaryTab = MakeButton(references.handArea, "OrdinaryTab", new Rect(8, 3, 112, 25), "普通牌", 14);
            var cyberTab = MakeButton(references.handArea, "CyberneticTab", new Rect(128, 3, 112, 25), "义体牌", 14);
            var conversion = MakeButton(frame, "ConvertApButton", new Rect(181, 932, 183, 38), "1 AP → 攻击", 18);
            conversion.gameObject.SetActive(false);
            var enemyButton = references.enemyPortrait.gameObject.AddComponent<Button>();
            enemyButton.targetGraphic = references.enemyPortrait;
            var status = TextAt(frame, "BattleStatus", new Rect(650, 746, 610, 40), 18);
            status.text = "点击手牌出牌";

            // 用实际四部位展示替换线框图中的两行占位。
            var durability = references.cyberneticDurabilityArea;
            for (int i = durability.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(durability.GetChild(i).gameObject);
            var durabilityTexts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var icon = Rect(durability, "Slot_" + i, new Rect(12, 26 + i * 73, 30, 40)).gameObject.AddComponent<Image>();
                ColorUtility.TryParseHtmlString(Colors[i + 3], out var color); icon.color = color; icon.raycastTarget = false;
                durabilityTexts[i] = TextAt(durability, "Durability_" + i, new Rect(47, 18 + i * 73, 109, 60), 12);
                durabilityTexts[i].color = Color.white;
                durabilityTexts[i].text = new[] { "脑机", "躯干", "手部", "腿部" }[i] + "\n耐久 " + cybernetics[i].maxDurability;
            }

            var overlay = new GameObject("CardChoiceOverlay", typeof(RectTransform), typeof(Image));
            overlay.layer = 5; var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.SetParent(canvas.transform, false); overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
            var window = Rect(overlay.transform, "ChoiceWindow", new Rect(0, 0, 720, 340));
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f); window.anchoredPosition = Vector2.zero;
            window.gameObject.AddComponent<Image>().color = new Color(0.87f, 0.88f, 0.89f);
            TextAt(window, "Title", new Rect(20, 6, 680, 42), 20).text = "选择一张义体牌";
            var choices = Rect(window, "Choices", new Rect(40, 59, 640, 210)); Horizontal(choices, 24, false);
            var cancel = MakeButton(window, "CancelChoice", new Rect(270, 286, 180, 38), "取消（不扣费）", 16);
            overlay.SetActive(false);

            var hud = references.gameObject.AddComponent<BattleHUD>();
            var serialized = new SerializedObject(hud);
            Set(serialized, "bootstrap", bootstrap); Set(serialized, "scene", references); Set(serialized, "catalog", catalog);
            Set(serialized, "cardsRoot", content); Set(serialized, "handScroll", scroll);
            Set(serialized, "ordinaryTab", ordinaryTab); Set(serialized, "cyberneticTab", cyberTab);
            Set(serialized, "conversionButton", conversion); Set(serialized, "enemyButton", enemyButton);
            Set(serialized, "choicePanel", overlay); Set(serialized, "choicesRoot", choices); Set(serialized, "cancelChoiceButton", cancel);
            Set(serialized, "statusText", status); SetArray(serialized, "durabilityTexts", durabilityTexts.Cast<UnityEngine.Object>().ToArray());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            references.playerHealthText.fontSize = 14;
            FitSceneLayout();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>检查生成的卡牌配置及预制体引用，报告不完整的资产。</summary>
        [MenuItem("Spotlight/Cards/Validate Assets")]
        public static void ValidateAssets()
        {
            foreach (var key in Keys)
            {
                var data = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/" + key + ".asset");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + key + ".prefab");
                if (!data || data.Validate().Count != 0 || !prefab || !prefab.GetComponent<CardView>())
                    throw new InvalidOperationException("资产无效：" + key);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("预制体丢失脚本：" + key);
                foreach (var text in prefab.GetComponentsInChildren<Text>(true))
                    if (!text.font) throw new InvalidOperationException("预制体字体丢失：" + key);
            }
            foreach (var slot in Keys.Skip(3))
            {
                var data = AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/" + slot + ".asset");
                if (!data || data.Validate().Count != 0 || data.cards.Length != 3) throw new InvalidOperationException("义体配置无效：" + slot);
            }
            Debug.Log("7 个预制体、卡牌效果、4 个义体及中文字体引用校验通过。");
        }

        /// <summary>通过序列化属性设置对象引用，并提交修改。</summary>
        private static void Set(SerializedObject obj, string field, UnityEngine.Object value) { obj.FindProperty(field).objectReferenceValue = value; }
        /// <summary>通过序列化属性写入对象引用数组，并提交修改。</summary>
        private static void SetArray(SerializedObject obj, string field, UnityEngine.Object[] values)
        {
            var property = obj.FindProperty(field); property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        /// <summary>创建指定父节点下的 UI 矩形容器。</summary>
        private static RectTransform Rect(Transform parent, string name, Rect bounds)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = 5;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size; return rect;
        }
        /// <summary>在卡面指定区域创建文本并设置显示样式。</summary>
        private static Text TextAt(Transform parent, string name, Rect bounds, int size)
        {
            var text = Rect(parent, name, bounds).gameObject.AddComponent<Text>(); text.font = font;
            text.fontSize = size; text.color = Ink; text.raycastTarget = false; text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        /// <summary>创建按钮及标签，用于战斗界面的操作入口。</summary>
        private static Button MakeButton(Transform parent, string name, Rect bounds, string label, int size)
        {
            var rect = Rect(parent, name, bounds); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.63f, 0.66f, 0.68f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var text = TextAt(rect, "Label", new Rect(0, 0, bounds.width, bounds.height), size); text.text = label;
            return button;
        }
        /// <summary>为容器配置水平布局，按顺序排列卡牌或按钮。</summary>
        private static void Horizontal(RectTransform root, float spacing, bool scale)
        {
            var group = root.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft; group.childControlHeight = group.childControlWidth = false;
            group.childForceExpandHeight = group.childForceExpandWidth = false;
            group.childScaleWidth = group.childScaleHeight = scale;
        }
    }
}
