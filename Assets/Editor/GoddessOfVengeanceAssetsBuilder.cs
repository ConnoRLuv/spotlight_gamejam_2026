using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量创建复仇女神（Type-02）四部位义体与套装配置；只补缺失资产，不覆盖成员后续调整。</summary>
    /// <remarks>
    /// 与普罗米修斯/新世界构建器保持同一资产组织方式：效果、卡牌、义体、牌组、套装各自独立资产，
    /// 卡面复用 DefenseCard 模板并注册进卡牌目录。套装在探险出发配置或 GameBootstrap 中挂接。
    /// </remarks>
    public static class GoddessOfVengeanceAssetsBuilder
    {
        private const string SetId = "goddess-of-vengeance";
        private const string FactionId = "scrap-coalition";
        private const string DeckPath = "Assets/GameData/CyberneticDecks/GoddessOfVengeance.asset";
        private const string SetBonusPath = "Assets/GameData/SetBonuses/GoddessOfVengeance.asset";
        private const string CatalogPath = "Assets/GameData/CardPrefabCatalog.asset";
        private const string AttackCardPath = "Assets/GameData/Cards/AttackCard.asset";
        private static readonly string[] Keys = { "GoddessBrainCard", "GoddessTorsoCard", "GoddessHandsCard", "GoddessLegsCard" };
        private static readonly string[] Names = { "复仇女神·脑机", "复仇女神·躯干", "复仇女神·手部", "复仇女神·腿部" };
        private static readonly string[] Colors = { "#8A4B7E", "#B85C4A", "#B08A3E", "#4B7E8A" };
        private static readonly int[] SanityCosts = { 2, 4, 6, 10 };
        private static readonly int[] Durabilities = { 3, 3, 3, 2 };
        private static readonly int[] CardCounts = { 3, 3, 3, 2 };
        private static readonly string[] Descriptions =
        {
            "把所有非攻击牌转化为攻击牌。",
            "本回合你受到的伤害由敌人代为承受（即完全的伤害转移）。",
            "观看普通与义体弃牌堆，并回收一张攻击牌。",
            "本回合使用基本牌无需消耗行动点。"
        };

        /// <summary>菜单与自动化共用入口；只创建缺失资产，BattleScene 与构建场景列表保持原样。</summary>
        [MenuItem("Spotlight/Cards/Create Goddess of Vengeance Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式创建复仇女神资源。");
            foreach (var folder in new[] { "Assets/GameData/Cards", "Assets/GameData/Effects", "Assets/GameData/Cybernetics",
                "Assets/GameData/CyberneticDecks", "Assets/GameData/SetBonuses", "Assets/Prefabs/Cards" }) EnsureFolder(folder);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/DefenseCard.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>(CatalogPath);
            var attackCard = AssetDatabase.LoadAssetAtPath<CardData>(AttackCardPath);
            if (!template || !catalog || !attackCard) throw new InvalidOperationException("请先创建基础卡牌目录、预制体、卡面目录与攻击牌。");

            var effects = new CardEffectData[]
            {
                Asset<GoddessOfVengeanceBrainEffectData>("Assets/GameData/Effects/GoddessBrain.asset", value => { value.attack = attackCard; }),
                Asset<GoddessOfVengeanceTorsoEffectData>("Assets/GameData/Effects/GoddessTorso.asset", value => { }),
                Asset<GoddessOfVengeanceHandsEffectData>("Assets/GameData/Effects/GoddessHands.asset", value => { value.attack = attackCard; }),
                Asset<GoddessOfVengeanceLegsEffectData>("Assets/GameData/Effects/GoddessLegs.asset", value => { })
            };
            var cards = new CardData[4];
            var sources = new CyberneticData[4];
            for (int i = 0; i < 4; i++)
            {
                int part = i;
                cards[i] = Asset<CardData>("Assets/GameData/Cards/" + Keys[i] + ".asset", value =>
                {
                    value.cardId = Keys[i]; value.cardName = Names[part]; value.description = Descriptions[part];
                    value.category = CardCategory.Cybernetic; value.cardType = CardType.Power;
                    value.cost = 0; value.sanityCost = SanityCosts[part]; value.targetType = CardTargetType.Self;
                    value.effects = new[] { effects[part] };
                });
                sources[i] = Asset<CyberneticData>("Assets/GameData/Cybernetics/" + Keys[i] + ".asset", value =>
                {
                    value.cyberneticId = Keys[i]; value.factionId = FactionId;
                    value.slot = (CyberneticSlot)part; value.maxDurability = Durabilities[part];
                    value.cards = Enumerable.Repeat(cards[part], CardCounts[part]).ToArray();
                });
            }

            var deck = Asset<CyberneticDeckData>(DeckPath, value =>
            {
                value.deckId = SetId; value.deckName = "Type-02：复仇女神"; value.cybernetics = sources;
            });
            var bonus = Asset<GoddessOfVengeanceSetBonusData>(SetBonusPath, value =>
            {
                value.setId = SetId; value.setName = "Type-02：复仇女神";
                value.description = "穿戴一整套后，每当你受到一次伤害，从普通抽牌堆获得一张基本牌；手牌已满则不再获得。";
                value.requiredParts = sources;
                value.trigger = GoddessOfVengeanceSetTrigger.OnPlayerDamaged;
                value.effect = GoddessOfVengeanceSetEffect.DrawBasicFromDrawPile;
            });

            for (int i = 0; i < 4; i++) AppendCatalogEntry(catalog, cards[i], MakePrefab(template, i, cards[i], sources[i]));

            AssetDatabase.SaveAssets();
            ValidateCreatedAssets(deck, bonus, cards, sources);
            Debug.Log("复仇女神四部位、牌组与套装配置已创建；请把牌组挂到出发配置/GameBootstrap，并把套装挂到 goddessSetBonuses。");
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
        private static void ValidateCreatedAssets(CyberneticDeckData deck, GoddessOfVengeanceSetBonusData bonus, CardData[] cards, CyberneticData[] sources)
        {
            var errors = deck.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            errors = bonus.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            for (int i = 0; i < 4; i++)
            {
                if (cards[i].Validate().Count > 0 || sources[i].Validate().Count > 0)
                    throw new InvalidOperationException("复仇女神资源校验失败：" + Keys[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + Keys[i] + ".prefab");
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("复仇女神预制体丢失脚本：" + Keys[i]);
            }
        }
    }
}