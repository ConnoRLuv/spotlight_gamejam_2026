using System;
using System.IO;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>增量创建四个空装备槽及共用悬停详情，不改动现有卡牌、弃牌面板和战斗入口。</summary>
    public static class CyberneticEquipmentBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/CyberneticEquipmentTooltip.prefab";
        /// <summary>生成并接入对应的界面资产，配置场景中的组件引用。</summary>
        [MenuItem("Spotlight/Equipment/Create Slots And Wire BattleScene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (scene.path != "Assets/Scenes/BattleScene.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            var references = UnityEngine.Object.FindObjectOfType<BattleSceneReferences>();
            var catalog = AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset");
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            if (!references || !references.GetComponent<BattleHUD>() || !catalog || !font) throw new InvalidOperationException("缺少战斗 UI、卡牌目录或字体。");
            Directory.CreateDirectory("Assets/Prefabs/UI"); AssetDatabase.Refresh();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) prefab = CreateTooltip(catalog, font);
            var frame = references.transform.Find("BattleLayout_16x9");
            var tooltip = frame.GetComponentInChildren<CyberneticEquipmentTooltip>(true);
            if (!tooltip)
            {
                tooltip = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, frame)).GetComponent<CyberneticEquipmentTooltip>();
                var rect = tooltip.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(360f / 1928, 1 - 720f / 1088);
                rect.anchorMax = new Vector2(1040f / 1928, 1 - 340f / 1088);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            tooltip.Hide();
            var area = references.cyberneticDurabilityArea;
            // 保留旧控件与引用，只关闭旧色块/文字，避免无关序列化引用丢失。
            for (int i = 0; i < 4; i++)
            {
                var icon = area.Find("Slot_" + i); if (icon) icon.gameObject.SetActive(false);
                var oldText = area.Find("Durability_" + i); if (oldText) oldText.gameObject.SetActive(false);
            }
            var slots = new CyberneticEquipmentSlot[4];
            for (int i = 0; i < 4; i++)
            {
                var existing = area.Find("EquipmentSlot_" + i);
                if (existing) slots[i] = existing.GetComponent<CyberneticEquipmentSlot>();
                else
                {
                    var row = Rect(area, "EquipmentSlot_" + i);
                    Stretch(row, new Vector2(0, 1 - (i + 1) / 4f), new Vector2(1, 1 - i / 4f), new Vector2(5, 2), new Vector2(-5, -2));
                    row.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
                    var face = Rect(row, "CardFace");
                    Stretch(face, Vector2.zero, new Vector2(0.36f, 1), Vector2.zero, Vector2.zero);
                    var label = Text(row, "Name", font, 12);
                    Stretch(label.rectTransform, new Vector2(0.39f, 0.43f), new Vector2(1, 0.92f), Vector2.zero, Vector2.zero);
                    var durability = Text(row, "Durability", font, 12);
                    Stretch(durability.rectTransform, new Vector2(0.39f, 0.15f), new Vector2(1, 0.4f), Vector2.zero, Vector2.zero);
                    slots[i] = row.gameObject.AddComponent<CyberneticEquipmentSlot>();
                    slots[i].Configure((CyberneticSlot)i, catalog, face, label, durability, tooltip);
                }
                // 为已有槽位增量补齐标题，为部位信息让出独立区域。
                var slotRoot = slots[i].GetComponent<RectTransform>();
                var partTransform = slotRoot.Find("Part");
                var part = partTransform ? partTransform.GetComponent<Text>() : Text(slotRoot, "Part", font, 14);
                part.fontStyle = FontStyle.Bold; part.text = slots[i].SlotName;
                // 中文字体行高大于字号，自动适配标题高度，避免文本整体被截断。
                part.resizeTextForBestFit = true; part.resizeTextMinSize = 8; part.resizeTextMaxSize = 14;
                part.alignment = TextAnchor.MiddleLeft;
                Stretch(part.rectTransform, new Vector2(0.02f, 0.76f), new Vector2(0.98f, 1), Vector2.zero, Vector2.zero);
                Stretch((RectTransform)slotRoot.Find("CardFace"), new Vector2(0, 0.03f), new Vector2(0.36f, 0.74f), Vector2.zero, Vector2.zero);
                Stretch((RectTransform)slotRoot.Find("Name"), new Vector2(0.39f, 0.32f), new Vector2(1, 0.74f), Vector2.zero, Vector2.zero);
                Stretch((RectTransform)slotRoot.Find("Durability"), new Vector2(0.39f, 0.03f), new Vector2(1, 0.28f), Vector2.zero, Vector2.zero);
                var slotFields = new SerializedObject(slots[i]); slotFields.FindProperty("slotText").objectReferenceValue = part;
                slotFields.ApplyModifiedPropertiesWithoutUndo();
            }
            var hud = new SerializedObject(references.GetComponent<BattleHUD>());
            var field = hud.FindProperty("equipmentSlots"); field.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            hud.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("左侧空装备槽和义体大图悬停说明已接入 BattleScene。");
        }
        /// <summary>创建装备详情预制体，配置大卡面、描述和统计文本引用。</summary>
        private static GameObject CreateTooltip(CardPrefabCatalog catalog, Font font)
        {
            var root = Rect(null, "CyberneticEquipmentTooltip"); root.sizeDelta = new Vector2(680, 380);
            try
            {
                root.gameObject.AddComponent<Image>().color = new Color(0.9f, 0.91f, 0.91f, 0.99f);
                var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = group.interactable = false;
                var title = Text(root, "Title", font, 22, false); title.color = new Color(0.15f, 0.18f, 0.2f);
                Stretch(title.rectTransform, new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.98f), Vector2.zero, Vector2.zero);
                var face = Rect(root, "CardFace");
                Stretch(face, new Vector2(0.04f, 0.03f), new Vector2(0.44f, 0.85f), Vector2.zero, Vector2.zero);
                var body = Text(root, "Description", font, 18, false); body.color = new Color(0.18f, 0.21f, 0.23f);
                Stretch(body.rectTransform, new Vector2(0.47f, 0.5f), new Vector2(0.96f, 0.81f), Vector2.zero, Vector2.zero);
                var stats = Text(root, "Details", font, 16, false); stats.color = new Color(0.25f, 0.29f, 0.31f);
                Stretch(stats.rectTransform, new Vector2(0.47f, 0.06f), new Vector2(0.96f, 0.47f), Vector2.zero, Vector2.zero);
                root.gameObject.AddComponent<CyberneticEquipmentTooltip>().Configure(catalog, face, title, body, stats);
                root.gameObject.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }
        /// <summary>创建指定父节点下的 UI 矩形容器。</summary>
        private static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = 5;
            if (parent) rect.SetParent(parent, false); return rect;
        }
        /// <summary>将矩形锚点扩展到整个父容器，并清零边缘偏移。</summary>
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
        /// <summary>创建并设置界面文字组件，绑定项目使用的中文字体。</summary>
        private static Text Text(Transform parent, string name, Font font, int size, bool white = true)
        {
            var text = Rect(parent, name).gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size;
            text.color = white ? Color.white : Color.black; text.raycastTarget = false;
            text.alignment = TextAnchor.UpperLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
    }
}
