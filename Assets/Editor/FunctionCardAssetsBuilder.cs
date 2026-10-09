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
    /// <summary>创建过载配置和低饱和度占位预制体，增量接入现有卡牌目录和 BattleScene。</summary>
    public static class FunctionCardAssetsBuilder
    {
        private const string DataPath = "Assets/GameData/Cards/OverloadCard.asset";
        private const string EffectPath = "Assets/GameData/Effects/Overload.asset";
        private const string PrefabPath = "Assets/Prefabs/Cards/OverloadCard.prefab";

        /// <summary>由明确的策划费用创建新资产；已有资产保留，不覆盖成员调整。</summary>
        public static void Build(int sanityCost)
        {
            if (sanityCost <= 0) throw new ArgumentOutOfRangeException(nameof(sanityCost), "请显式指定正数理智费用。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式创建卡牌。");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (scene.path != "Assets/Scenes/BattleScene.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            var bootstrap = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/DefenseCard.prefab");
            if (!bootstrap || !catalog || !template) throw new InvalidOperationException("请先创建基础卡牌与 BattleScene。");

            var effect = AssetDatabase.LoadAssetAtPath<ActionPointEffectData>(EffectPath);
            if (!effect)
            {
                effect = ScriptableObject.CreateInstance<ActionPointEffectData>(); effect.amount = 1;
                AssetDatabase.CreateAsset(effect, EffectPath);
            }
            var data = AssetDatabase.LoadAssetAtPath<CardData>(DataPath);
            if (!data)
            {
                data = ScriptableObject.CreateInstance<CardData>();
                data.cardId = "OverloadCard"; data.cardName = "过载";
                data.category = CardCategory.Function; data.cardType = CardType.Skill;
                data.cost = 0; data.sanityCost = sanityCost; data.targetType = CardTargetType.None;
                data.requiresEquippedCybernetic = true;
                data.description = "获得 1 点临时行动点。\n回合结束时清除；不消耗义体耐久。";
                data.effects = new CardEffectData[] { effect };
                AssetDatabase.CreateAsset(data, DataPath);
            }
            if (data.Validate().Count > 0) throw new InvalidOperationException(string.Join("\n", data.Validate()));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab)
            {
                // 复用现有卡牌结构与中文字体，生成独立预制体，避免修改基础牌。
                var root = UnityEngine.Object.Instantiate(template);
                try
                {
                    root.name = "OverloadCard";
                    ColorUtility.TryParseHtmlString("#B5AD86", out var color);
                    root.GetComponent<CardView>().Configure(data, null, color, root.GetComponent<Image>(),
                        root.GetComponent<Button>(), root.transform.Find("Name").GetComponent<Text>(),
                        root.transform.Find("Cost").GetComponent<Text>(), root.transform.Find("Description").GetComponent<Text>(),
                        root.transform.Find("Source").GetComponent<Text>());
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            // 增量追加映射；首次接入加入三张过载，已有牌组保留，重复执行不增加副本。
            if (!catalog.entries.Any(entry => entry != null && entry.data == data))
            {
                catalog.entries = catalog.entries.Concat(new[] { new CardPrefabCatalog.Entry { data = data, prefab = prefab.GetComponent<CardView>() } }).ToArray();
                EditorUtility.SetDirty(catalog);
            }
            var serialized = new SerializedObject(bootstrap); var deck = serialized.FindProperty("ordinaryDeck");
            bool exists = false;
            for (int i = 0; i < deck.arraySize; i++) if (deck.GetArrayElementAtIndex(i).objectReferenceValue == data) exists = true;
            if (!exists)
            {
                int index = deck.arraySize; deck.arraySize += 3;
                for (int i = index; i < deck.arraySize; i++) deck.GetArrayElementAtIndex(i).objectReferenceValue = data;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("过载功能牌已接入：理智费用 " + data.sanityCost + "，获得 1 点临时 AP。");
        }

        /// <summary>重新接入已有过载资产；首次创建必须通过 Build 显式传入策划费用。</summary>
        [MenuItem("Spotlight/Cards/Wire Existing Function Card")]
        public static void WireExisting()
        {
            var data = AssetDatabase.LoadAssetAtPath<CardData>(DataPath);
            if (!data) throw new InvalidOperationException("请先调用 FunctionCardAssetsBuilder.Build(理智费用) 创建过载。");
            Build(data.sanityCost);
        }
    }
}
