using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量创建复仇女神四部位、完整美术、价格及统一套装入口，不打开或保存 BattleScene。</summary>
    public static class NemesisAssetsBuilder
    {
        private static readonly string[] Parts = { "Brain", "Torso", "Hands", "Legs" };
        private static readonly string[] Names = { "脑机", "躯干", "手部", "腿部" };
        private static readonly int[] Costs = { 2, 4, 6, 10 };
        private static readonly int[] Durabilities = { 3, 3, 3, 2 };
        private static readonly int[] Copies = { 3, 3, 3, 2 };
        private static readonly string[] Descriptions =
        {
            "将当前普通手牌中的所有非攻击牌临时转为攻击牌，幻痛不变；本回合结束恢复原牌。",
            "本回合敌方造成的生命和理智伤害均转为攻击者承受的生命伤害；主动费用照常支付。",
            "观看普通及义体弃牌堆，选择并回收一张攻击牌。",
            "本回合使用基本牌无需消耗 AP。"
        };

        /// <summary>创建缺失配置并注册出发候选；重复执行保留已存在的玩法参数和自定义默认牌组。</summary>
        [MenuItem("Spotlight/Cards/Create Nemesis Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式创建复仇女神资源。");
            foreach (var folder in new[] { "Assets/GameData/Cards", "Assets/GameData/Cybernetics", "Assets/GameData/Effects",
                "Assets/GameData/CyberneticDecks", "Assets/GameData/Buffs", "Assets/Prefabs/Cards" }) EnsureFolder(folder);
            var attack = AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/DefenseCard.prefab");
            var pricing = AssetDatabase.LoadAssetAtPath<CyberneticShopPriceData>("Assets/GameData/ShopPrices/Nemesis.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            var setup = AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
            if (!attack || !template || !pricing || !catalog || !setup) throw new InvalidOperationException("缺少基础卡牌、目录、价格或探索配置。");
            var effects = new CardEffectData[]
            {
                Asset<NemesisBrainEffectData>("Assets/GameData/Effects/NemesisBrain.asset", value => value.attackCard = attack),
                Asset<NemesisTorsoEffectData>("Assets/GameData/Effects/NemesisTorso.asset", value => { }),
                Asset<NemesisHandsEffectData>("Assets/GameData/Effects/NemesisHands.asset", value => { }),
                // 复用已实现的基本牌免 AP 效果，不再开发第二套相同结算。
                Asset<FreeBasicCardsEffectData>("Assets/GameData/Effects/NemesisLegs.asset", value => { })
            };
            var sources = new CyberneticData[4];
            for (int i = 0; i < Parts.Length; i++)
            {
                int part = i; string key = "Nemesis" + Parts[i] + "Card";
                var artwork = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Card/义体牌3-" + (i + 1) + ".png");
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/" + Names[i] + "-复仇女神.png");
                if (!artwork || !icon) throw new InvalidOperationException("缺少复仇女神美术：" + Names[i]);
                var card = Asset<CardData>("Assets/GameData/Cards/" + key + ".asset", value =>
                {
                    value.cardId = key; value.cardName = "复仇女神·" + Names[part]; value.description = Descriptions[part];
                    value.category = CardCategory.Cybernetic; value.cardType = CardType.Power;
                    value.cost = 0; value.sanityCost = Costs[part]; value.targetType = CardTargetType.Self;
                    value.effects = new[] { effects[part] }; value.artwork = artwork;
                });
                sources[i] = Asset<CyberneticData>("Assets/GameData/Cybernetics/" + key + ".asset", value =>
                {
                    value.cyberneticId = key; value.factionId = "waste-construct-coalition";
                    value.slot = (CyberneticSlot)part; value.maxDurability = Durabilities[part];
                    value.cards = Enumerable.Repeat(card, Copies[part]).ToArray();
                    value.icon = icon; value.shopPricing = pricing;
                });
                var prefab = MakePrefab(template, card, sources[i], key);
                if (!catalog.entries.Any(entry => entry != null && entry.data == card))
                    catalog.entries = catalog.entries.Concat(new[] { new CardPrefabCatalog.Entry { data = card, prefab = prefab } }).ToArray();
            }
            var deck = Asset<CyberneticDeckData>("Assets/GameData/CyberneticDecks/Nemesis.asset", value =>
            { value.deckId = "nemesis"; value.deckName = "Type-02：复仇女神"; value.cybernetics = sources; });
            var buff = Asset<RunBuffData>("Assets/GameData/Buffs/Nemesis.asset", value =>
            {
                value.buffId = "nemesis"; value.buffName = "Type-02：复仇女神";
                value.description = "同一回合成功打出四部位后，本场战斗每次实际受到伤害，从抽牌堆获得一张基本牌；满手不获得。";
                value.requiredCybernetics = sources; value.effect = RunBuffEffect.DrawBasicOnDamage; value.duration = RunBuffDuration.Battle;
            });
            setup.availableCybernetics = (setup.availableCybernetics ?? Array.Empty<CyberneticData>()).Concat(sources).Distinct().ToArray();
            setup.buffs = (setup.buffs ?? Array.Empty<RunBuffData>()).Concat(new[] { buff }).Distinct().ToArray();
            EditorUtility.SetDirty(setup); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            if (deck.Validate().Count > 0 || buff.Validate().Count > 0) throw new InvalidOperationException("复仇女神卡组配置校验失败。");
            Debug.Log("复仇女神四部位、11张牌、卡面及套装已接入出发配置；BattleScene 未修改。");
        }

        /// <summary>加载已有资产；只初始化缺失项，避免重跑覆盖成员调整。</summary>
        private static T Asset<T>(string path, Action<T> configure) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing) return existing;
            var value = ScriptableObject.CreateInstance<T>(); configure(value); AssetDatabase.CreateAsset(value, path); return value;
        }

        /// <summary>复用基础卡面层级，使用完整卡面并保持比例，保留原字段引用而关闭重复印刷内容。</summary>
        private static CardView MakePrefab(GameObject template, CardData card, CyberneticData source, string key)
        {
            string path = "Assets/Prefabs/Cards/" + key + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing.GetComponent<CardView>();
            var root = UnityEngine.Object.Instantiate(template);
            try
            {
                root.name = key;
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var image = root.GetComponent<Image>(); image.sprite = card.artwork; image.preserveAspect = true;
                root.GetComponent<CardView>().Configure(card, source, Color.white, image, root.GetComponent<Button>(),
                    root.transform.Find("Name").GetComponent<Text>(), root.transform.Find("Cost").GetComponent<Text>(),
                    root.transform.Find("Description").GetComponent<Text>(), root.transform.Find("Source").GetComponent<Text>());
                foreach (var name in new[] { "Name", "Cost", "Description", "Source", "ColorPlaceholder" })
                { var child = root.transform.Find(name); if (child) child.gameObject.SetActive(false); }
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<CardView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>递归补齐资产目录，不覆盖现有目录或元数据。</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/'); string parent = path.Substring(0, separator);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
