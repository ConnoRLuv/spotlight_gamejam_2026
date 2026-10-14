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
    /// <summary>增量创建普罗米修斯四部位卡组、低饱和度预制体，保留已有资产参数。</summary>
    public static class PrometheusAssetsBuilder
    {
        private const string FactionId = "tian-qiong-legion";
        private const string DeckPath = "Assets/GameData/CyberneticDecks/Prometheus.asset";
        private const string CatalogPath = "Assets/GameData/CardPrefabCatalog.asset";
        private static readonly string[] Parts = { "Brain", "Torso", "Hands", "Legs" };
        private static readonly string[] Names = { "篡改者", "改造驱动", "生命涌现", "持久变量" };
        private static readonly string[] Colors = { "#A49CB6", "#B6A58F", "#91AAA0", "#96A5B5" };
        private static readonly int[] SanityCosts = { 8, 40, 2, 30 };
        private static readonly int[] Durabilities = { 4, 1, 3, 1 };
        private static readonly int[] Copies = { 3, 1, 3, 1 };
        private static readonly string[] Descriptions =
        {
            "下个玩家回合改从对应弃牌堆随机抽 3 张普通牌、1 张义体牌；不足从抽牌堆补足。",
            "选择 1 张基本手牌永久移除，随机获得 1 张未拥有的天穹军团义体牌；报废亦算拥有。新牌有独立耐久，跨战斗保留。",
            "本玩家回合打出所有基本牌无需行动点，包括之后新获得的基本牌。过载和义体牌不受影响。",
            "本场战斗后续义体牌改用生命支付，并恢复等量理智，上限不变。支付无视护盾且须保留 1 生命；过载费用另由全局 buff 决定。"
        };

        /// <summary>菜单与自动化共用入口；只创建缺失资产，原 BattleScene 和构建场景列表保持原样。</summary>
        [MenuItem("Spotlight/Cards/Create Prometheus Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在编辑模式创建普罗米修斯资源。");
            foreach (var folder in new[] { "Assets/GameData/Cards", "Assets/GameData/Effects", "Assets/GameData/Cybernetics",
                "Assets/GameData/CyberneticDecks", "Assets/Prefabs/Cards", "Assets/Scenes" }) EnsureFolder(folder);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/DefenseCard.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>(CatalogPath);
            if (!template || !catalog || !AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/BattleScene.unity"))
                throw new InvalidOperationException("请先创建基础卡牌目录、预制体和 BattleScene。");

            var effects = new CardEffectData[]
            {
                Asset<DiscardDrawNextTurnEffectData>("Assets/GameData/Effects/PrometheusBrain.asset", value => { }),
                Asset<TransformBasicToCyberneticEffectData>("Assets/GameData/Effects/PrometheusTorso.asset", value => value.factionId = FactionId),
                Asset<FreeBasicCardsEffectData>("Assets/GameData/Effects/PrometheusHands.asset", value => { }),
                Asset<HealthPaymentEffectData>("Assets/GameData/Effects/PrometheusLegs.asset", value => { })
            };
            var cards = new CardData[4];
            var sources = new CyberneticData[4];
            for (int i = 0; i < Parts.Length; i++)
            {
                int part = i;
                string key = "Prometheus" + Parts[i] + "Card";
                cards[i] = Asset<CardData>("Assets/GameData/Cards/" + key + ".asset", value =>
                {
                    value.cardId = key; value.cardName = Names[part]; value.description = Descriptions[part];
                    value.category = CardCategory.Cybernetic; value.cardType = CardType.Power;
                    value.cost = 0; value.sanityCost = SanityCosts[part]; value.targetType = CardTargetType.Self;
                    value.effects = new[] { effects[part] };
                });
                sources[i] = Asset<CyberneticData>("Assets/GameData/Cybernetics/" + key + ".asset", value =>
                {
                    value.cyberneticId = key; value.factionId = FactionId; value.slot = (CyberneticSlot)part;
                    value.maxDurability = Durabilities[part]; value.cards = Enumerable.Repeat(cards[part], Copies[part]).ToArray();
                });
            }

            // 同阵营的已有四种制式义体也是改造候选；显式序列化阵营，避免只依赖字段默认值。
            var legacy = Parts.Select(part => AssetDatabase.LoadAssetAtPath<CyberneticData>(
                "Assets/GameData/Cybernetics/" + part + "Card.asset")).ToArray();
            if (legacy.Any(value => !value)) throw new InvalidOperationException("原天穹军团制式义体配置缺失。");
            foreach (var source in legacy)
            {
                source.factionId = FactionId;
                EditorUtility.SetDirty(source);
            }
            var transformation = (TransformBasicToCyberneticEffectData)effects[1];
            if (transformation.candidates == null || transformation.candidates.Length == 0)
            {
                // 只补充尚未配置的引用，重跑构建不覆盖成员后来调整的候选范围。
                transformation.candidates = legacy.Concat(sources).ToArray();
                EditorUtility.SetDirty(transformation);
            }
            var deck = Asset<CyberneticDeckData>(DeckPath, value =>
            {
                value.deckId = "prometheus"; value.deckName = "Type-01：普罗米修斯"; value.cybernetics = sources;
                value.reservedSetBonusDescription = "过载从消耗理智值变为消耗血量（预留，未启用）";
            });
            for (int i = 0; i < Parts.Length; i++)
                AppendCatalogEntry(catalog, cards[i], MakePrefab(template, i, cards[i], sources[i]));

            AssetDatabase.SaveAssets();
            ValidateCreatedAssets(deck, cards, sources);
            Debug.Log("普罗米修斯四部位、8 张义体牌资源已接入；探索全局 buff 由出发配置负责。");
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
            string key = "Prometheus" + Parts[part] + "Card";
            string path = "Assets/Prefabs/Cards/" + key + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing) return existing.GetComponent<CardView>();
            var root = UnityEngine.Object.Instantiate(template);
            try
            {
                root.name = key;
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ColorUtility.TryParseHtmlString(Colors[part], out var color);
                var description = root.transform.Find("Description").GetComponent<Text>();
                // 改造和生命支付文字较长，沿用卡面区域并适度缩小字号，确保描述完整可读。
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
        private static void ValidateCreatedAssets(CyberneticDeckData deck, CardData[] cards, CyberneticData[] sources)
        {
            var errors = deck.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].Validate().Count > 0 || sources[i].Validate().Count > 0)
                    throw new InvalidOperationException("普罗米修斯资源校验失败：" + Parts[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/Prometheus" + Parts[i] + "Card.prefab");
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("普罗米修斯预制体丢失脚本：" + Parts[i]);
            }
        }
    }
}
