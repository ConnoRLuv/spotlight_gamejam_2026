using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量创建新世界（Type-00）四部位义体与套装配置；只补缺失资产，不覆盖成员后续调整。</summary>
    /// <remarks>
    /// 与普罗米修斯构建器保持同一资产组织方式：效果、卡牌、义体、牌组、套装各自独立资产，
    /// 卡面复用 DefenseCard 模板并注册进卡牌目录。套装在探险出发配置或 GameBootstrap 中挂接。
    /// </remarks>
    public static class NewWorldAssetsBuilder
    {
        private const string SetId = "new-world";
        private const string DeckPath = "Assets/GameData/CyberneticDecks/NewWorld.asset";
        private const string SetBonusPath = "Assets/GameData/SetBonuses/NewWorld.asset";
        private const string CatalogPath = "Assets/GameData/CardPrefabCatalog.asset";
        private static readonly string[] Keys = { "NewWorldBrainCard", "NewWorldTorsoCard", "NewWorldHandsCard", "NewWorldLegsCard" };
        private static readonly string[] Names = { "新世界·脑机", "新世界·躯干", "新世界·手部", "新世界·腿部" };
        private static readonly string[] Colors = { "#A398B8", "#BAAB8F", "#B79A89", "#88AAA7" };
        private static readonly int[] SanityCosts = { 4, 8, 3, 10 };
        private static readonly string[] Descriptions =
        {
            "观看普通牌堆和义体牌堆，并按抽取顺序重排两套抽牌堆。",
            "本回合视为无手牌上限限制。",
            "本回合“过载”牌获得的行动点视为永久行动点（不超过 12）。",
            "获得一个额外的行动回合。"
        };

        /// <summary>菜单与自动化共用入口；只创建缺失资产，BattleScene 与构建场景列表保持原样。</summary>
        [MenuItem("Spotlight/Cards/Create NewWorld Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式创建新世界资源。");
            foreach (var folder in new[] { "Assets/GameData/Cards", "Assets/GameData/Effects", "Assets/GameData/Cybernetics",
                "Assets/GameData/CyberneticDecks", "Assets/GameData/SetBonuses", "Assets/Prefabs/Cards" }) EnsureFolder(folder);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/DefenseCard.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>(CatalogPath);
            if (!template || !catalog) throw new InvalidOperationException("请先创建基础卡牌目录、预制体与卡面目录。");

            var effects = new CardEffectData[]
            {
                Asset<NewWorldBrainEffectData>("Assets/GameData/Effects/NewWorldBrain.asset", value => { }),
                Asset<NewWorldTorsoEffectData>("Assets/GameData/Effects/NewWorldTorso.asset", value => { }),
                Asset<NewWorldHandsEffectData>("Assets/GameData/Effects/NewWorldHands.asset", value => { }),
                Asset<NewWorldLegsEffectData>("Assets/GameData/Effects/NewWorldLegs.asset", value => { })
            };
            var cards = new CardData[4];
            var sources = new CyberneticData[4];
            var pricing = AssetDatabase.LoadAssetAtPath<CyberneticShopPriceData>("Assets/GameData/ShopPrices/NewWorld.asset");
            if (!pricing) throw new InvalidOperationException("缺少新世界系列维修配置。");
            var partNames = new[] { "脑机", "躯干", "手部", "腿部" };
            for (int i = 0; i < 4; i++)
            {
                int part = i;
                cards[i] = Asset<CardData>("Assets/GameData/Cards/" + Keys[i] + ".asset", value =>
                {
                    value.cardId = Keys[i]; value.cardName = Names[part]; value.description = Descriptions[part];
                    value.category = CardCategory.Cybernetic; value.cardType = CardType.Power;
                    value.cost = 0; value.sanityCost = SanityCosts[part]; value.targetType = CardTargetType.Self;
                    value.effects = new[] { effects[part] };
                    value.artwork = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Card/义体牌2-" + (part + 1) + ".png");
                });
                sources[i] = Asset<CyberneticData>("Assets/GameData/Cybernetics/" + Keys[i] + ".asset", value =>
                {
                    value.cyberneticId = Keys[i];
                    value.factionId = "tian-qiong-legion";
                    value.icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/" + partNames[part] + "-新世界.png");
                    value.shopPricing = pricing;
                    value.slot = (CyberneticSlot)part; value.maxDurability = 3;
                    value.cards = Enumerable.Repeat(cards[part], 3).ToArray();
                });
            }

            var deck = Asset<CyberneticDeckData>(DeckPath, value =>
            {
                value.deckId = SetId; value.deckName = "Type-00：新世界（初始义体）"; value.cybernetics = sources;
            });
            var bonus = Asset<NewWorldSetBonusData>(SetBonusPath, value =>
            {
                value.setId = SetId; value.setName = "Type-00：新世界";
                value.description = "同一回合成功打出新世界四部位后，本场战斗每次成功出牌回收一张基本牌；手牌已满则不回收。";
                value.requiredParts = sources;
                value.trigger = NewWorldSetTrigger.OnAnyCardPlayed;
                value.effect = NewWorldSetEffect.ReturnBasicFromDiscard;
                value.returnPolicy = NewWorldSetReturnPolicy.MostRecent;
            });

            for (int i = 0; i < 4; i++) AppendCatalogEntry(catalog, cards[i], MakePrefab(template, i, cards[i], sources[i]));
            RegisterAdventure(sources, bonus);

            AssetDatabase.SaveAssets();
            ValidateCreatedAssets(deck, bonus, cards, sources);
            Debug.Log("新世界四部位、卡面、维修及出发套装配置已接入；BattleScene 未修改。");
        }

        /// <summary>补齐出发候选和套装；旧默认原制式迁移为新世界，保留成员自定义的混搭默认。</summary>
        private static void RegisterAdventure(CyberneticData[] parts, NewWorldSetBonusData bonus)
        {
            var setup = AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            if (!setup) throw new InvalidOperationException("请先创建 StartScene 探索配置。");
            setup.availableCybernetics = (setup.availableCybernetics ?? Array.Empty<CyberneticData>()).Concat(parts).Distinct().ToArray();
            setup.setBonuses = (setup.setBonuses ?? Array.Empty<NewWorldSetBonusData>()).Concat(new[] { bonus }).Distinct().ToArray();
            var original = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/TianQiongLegion.asset");
            if (setup.defaultCybernetics == null || setup.defaultCybernetics.Length == 0 ||
                original && setup.defaultCybernetics.SequenceEqual(original.cybernetics)) setup.defaultCybernetics = parts.ToArray();
            EditorUtility.SetDirty(setup);
            var transformation = AssetDatabase.LoadAssetAtPath<TransformBasicToCyberneticEffectData>("Assets/GameData/Effects/PrometheusTorso.asset");
            if (transformation)
            {
                transformation.candidates = (transformation.candidates ?? Array.Empty<CyberneticData>()).Concat(parts).Distinct().ToArray();
                EditorUtility.SetDirty(transformation);
            }
        }

        /// <summary>递归创建缺失的 Unity 资产目录，不覆盖已有目录和元数据。</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        /// <summary>加载已有配置；仅在不存在时初始化，确保重跑不会重置可调费用、耐久或效果。</summary>
        private static T Asset<T>(string path, Action<T> configure) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing;
            var value = ScriptableObject.CreateInstance<T>();
            configure(value);
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        /// <summary>复制现有卡面结构并绑定新配置，仅在预制体缺失时设置部位占位色和说明。</summary>
        private static CardView MakePrefab(GameObject template, int part, CardData card, CyberneticData source)
        {
            string path = "Assets/Prefabs/Cards/" + Keys[part] + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing) return existing.GetComponent<CardView>();
            var root = UnityEngine.Object.Instantiate(template);
            try
            {
                root.name = Keys[part];
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ColorUtility.TryParseHtmlString(Colors[part], out var color);
                var description = root.transform.Find("Description").GetComponent<Text>();
                description.fontSize = 11;
                description.resizeTextForBestFit = true;
                description.resizeTextMinSize = 10;
                description.resizeTextMaxSize = 12;
                root.GetComponent<CardView>().Configure(card, source, color, root.GetComponent<Image>(),
                    root.GetComponent<Button>(), root.transform.Find("Name").GetComponent<Text>(),
                    root.transform.Find("Cost").GetComponent<Text>(), description, root.transform.Find("Source").GetComponent<Text>());
                // 指定资源是带完整印刷文字的卡面，保留模板字段供动态数据绑定，避免文字重复覆盖。
                if (card.artwork)
                {
                    var image = root.GetComponent<Image>(); image.sprite = card.artwork; image.preserveAspect = true;
                    root.GetComponent<CardView>().Configure(card, source, Color.white, image, root.GetComponent<Button>(),
                        root.transform.Find("Name").GetComponent<Text>(), root.transform.Find("Cost").GetComponent<Text>(), description,
                        root.transform.Find("Source").GetComponent<Text>());
                    foreach (var name in new[] { "Name", "Cost", "Description", "Source", "ColorPlaceholder" })
                    { var child = root.transform.Find(name); if (child) child.gameObject.SetActive(false); }
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<CardView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>为新牌追加唯一预制体映射；已有映射和回退预制体保留。</summary>
        private static void AppendCatalogEntry(CardPrefabCatalog catalog, CardData card, CardView prefab)
        {
            if (!prefab) throw new InvalidOperationException("新卡预制体缺少 CardView：" + card.cardName);
            if (catalog.entries.Any(entry => entry != null && entry.data == card)) return;
            catalog.entries = catalog.entries.Concat(new[] { new CardPrefabCatalog.Entry { data = card, prefab = prefab } }).ToArray();
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>构建后检查配置引用与脚本完整性，及时报告资源配线错误。</summary>
        private static void ValidateCreatedAssets(CyberneticDeckData deck, NewWorldSetBonusData bonus, CardData[] cards, CyberneticData[] sources)
        {
            var errors = deck.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            errors = bonus.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            for (int i = 0; i < 4; i++)
            {
                if (cards[i].Validate().Count > 0 || sources[i].Validate().Count > 0)
                    throw new InvalidOperationException("新世界资源校验失败：" + Keys[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + Keys[i] + ".prefab");
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("新世界预制体丢失脚本：" + Keys[i]);
            }
        }
    }
}
