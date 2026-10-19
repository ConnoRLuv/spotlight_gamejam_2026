using System;
using System.IO;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>在 Unity 内增量应用义体选择美术，不重建开始场景或覆盖其他控件。</summary>
    [InitializeOnLoad]
    public static class StartSelectionArtInstaller
    {
        private const string RequestPath = "Library/CodexStartSelection.request";
        private const string ReportPath = "output/ui/start-scene/install-result.txt";
        private const string ArtPath = "Assets/Art/UI/CyberneticSelection/";

        static StartSelectionArtInstaller() { EditorApplication.delayCall += ApplyRequested; }

        private static void ApplyRequested()
        {
            if (!File.Exists(RequestPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += ApplyRequested; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.isPlaying = false; EditorApplication.delayCall += ApplyRequested; return; }
            try
            {
                if (File.ReadAllText(RequestPath).Trim() == "validate") ValidateAndCapture();
                else Apply();
                File.Delete(RequestPath);
            }
            catch (Exception error)
            {
                File.WriteAllText(ReportPath, error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Spotlight/Start Scene/Apply Selection Component Artwork")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var frames = new[] { "PartTab-Normal", "PartTab-Selected", "DeckOption-Normal", "DeckOption-Selected" }
                .Select(Import).ToArray();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/StartScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity", OpenSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<StartSceneController>(true)).Single();
                var fields = new SerializedObject(controller);
                // 本次测试单独使用精简配置，完整卡组资产和默认出发配置仍保留。
                const string playtestPath = "Assets/GameData/AdventureSetup_CurrentTest.asset";
                if (!AssetDatabase.LoadAssetAtPath<AdventureSetupData>(playtestPath))
                    AssetDatabase.CopyAsset("Assets/GameData/AdventureSetup.asset", playtestPath);
                var playtest = AssetDatabase.LoadAssetAtPath<AdventureSetupData>(playtestPath);
                var excludedDeck = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/TianQiongLegion.asset");
                if (!playtest || !excludedDeck) throw new InvalidOperationException("缺少本次测试的牌组配置。");
                playtest.availableCybernetics = playtest.availableCybernetics.Where(data => !excludedDeck.cybernetics.Contains(data)).ToArray();
                playtest.defaultCybernetics = playtest.defaultCybernetics.Where(data => !excludedDeck.cybernetics.Contains(data)).ToArray();
                EditorUtility.SetDirty(playtest);
                fields.FindProperty("setup").objectReferenceValue = playtest;
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/荆南俊俊体.ttf");
                if (!font) throw new InvalidOperationException("缺少荆南俊俊体。");
                fields.FindProperty("selectionFont").objectReferenceValue = font;
                foreach (var text in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Text>(true)))
                {
                    text.font = font; text.fontStyle = FontStyle.Normal; EditorUtility.SetDirty(text);
                }
                var spriteFields = new[] { "partNormalFrame", "partSelectedFrame", "optionNormalFrame", "optionSelectedFrame" };
                for (int i = 0; i < frames.Length; i++) fields.FindProperty(spriteFields[i]).objectReferenceValue = frames[i];
                var iconNames = new[] { "脑机", "躯干", "手部", "腿部" };
                var icons = fields.FindProperty("partFallbackIcons"); icons.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/义体/" + iconNames[i] + "-新世界.png");
                    if (!sprite) throw new InvalidOperationException("缺少已有义体图标：" + iconNames[i]);
                    icons.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                }
                fields.ApplyModifiedPropertiesWithoutUndo();
                ConfigurePortraitAndBuffPanel(scene, fields, font);
                Canvas.ForceUpdateCanvases();
                var summary = (Text)fields.FindProperty("summary").objectReferenceValue;
                var summaryHost = (RectTransform)summary.transform.parent;
                var assembly = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                    .Single(rect => rect.name == "CyberneticAssemblyPanel");
                var leftFrame = assembly.Find("Background") as RectTransform ?? assembly;
                var leftCorners = new Vector3[4]; leftFrame.GetWorldCorners(leftCorners);
                var oldCenter = summaryHost.TransformPoint(summaryHost.rect.center);
                summaryHost.anchorMin = summaryHost.anchorMax = summaryHost.pivot = new Vector2(.5f, .5f);
                summaryHost.localScale = Vector3.one; summaryHost.sizeDelta = new Vector2(360, 210);
                var summaryPosition = summaryHost.position;
                summaryPosition.x = oldCenter.x;
                summaryPosition.y = leftCorners[1].y - summaryHost.TransformVector(new Vector3(0, 105, 0)).y;
                summaryHost.position = summaryPosition;
                var summaryBackground = summaryHost.Find("Background") as RectTransform;
                if (summaryBackground)
                {
                    summaryBackground.anchorMin = Vector2.zero; summaryBackground.anchorMax = Vector2.one;
                    summaryBackground.localScale = Vector3.one;
                    summaryBackground.offsetMin = summaryBackground.offsetMax = Vector2.zero;
                    summaryBackground.GetComponent<Image>().preserveAspect = false;
                }
                summary.rectTransform.anchorMin = Vector2.zero; summary.rectTransform.anchorMax = Vector2.one;
                summary.rectTransform.offsetMin = new Vector2(24, 18); summary.rectTransform.offsetMax = new Vector2(-24, -18);
                summary.fontSize = 22; summary.lineSpacing = 1.15f; summary.alignment = TextAnchor.MiddleLeft;
                summary.resizeTextForBestFit = false;
                EditorUtility.SetDirty(summary); EditorUtility.SetDirty(summaryHost);
                var buttons = fields.FindProperty("partButtons");
                var parts = Enumerable.Range(0, buttons.arraySize).Select(i =>
                    buttons.GetArrayElementAtIndex(i).objectReferenceValue as Button).ToArray();
                if (parts.Length != 4 || parts.Any(button => !button)) throw new InvalidOperationException("部位按钮引用不完整。");
                var center = parts.Select(button => button.transform.localPosition).Aggregate(Vector3.zero, (a, b) => a + b) / 4;
                for (int i = 0; i < 4; i++)
                {
                    var rect = (RectTransform)parts[i].transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                    rect.localScale = Vector3.one; rect.sizeDelta = new Vector2(112, 123);
                    rect.localPosition = new Vector3(center.x + (i - 1.5f) * 130, center.y, 0);
                }
                var gridRoot = (RectTransform)fields.FindProperty("optionsRoot").objectReferenceValue;
                gridRoot.localScale = Vector3.one;
                gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 274);
                gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 304);
                var grid = gridRoot.GetComponent<GridLayoutGroup>();
                if (!grid) throw new InvalidOperationException("缺少既有卡组网格。");
                grid.cellSize = new Vector2(110, 121); grid.spacing = new Vector2(6, 6);
                grid.padding = new RectOffset(24, 24, 24, 24); grid.childAlignment = TextAnchor.MiddleCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
                var preview = (RectTransform)fields.FindProperty("cardPreviewRoot").objectReferenceValue;
                var info = (Text)fields.FindProperty("detailInfo").objectReferenceValue;
                var infoHost = (RectTransform)info.transform.parent;
                var coordinates = (RectTransform)infoHost.parent;
                var lowerButton = ((Button)fields.FindProperty("unequipButton").objectReferenceValue).GetComponent<RectTransform>();
                var previewCorners = new Vector3[4]; var buttonCorners = new Vector3[4];
                preview.GetWorldCorners(previewCorners); lowerButton.GetWorldCorners(buttonCorners);
                float gapTop = coordinates.InverseTransformPoint(previewCorners[0]).y;
                float gapBottom = coordinates.InverseTransformPoint(buttonCorners[1]).y;
                infoHost.anchorMin = infoHost.anchorMax = infoHost.pivot = new Vector2(.5f, .5f);
                infoHost.sizeDelta = new Vector2(preview.rect.width, Mathf.Clamp(gapTop - gapBottom - 8, 22, 36));
                var previewCenter = coordinates.InverseTransformPoint(preview.TransformPoint(preview.rect.center));
                infoHost.localPosition = new Vector3(previewCenter.x, (gapTop + gapBottom) / 2, 0);
                info.rectTransform.anchorMin = Vector2.zero; info.rectTransform.anchorMax = Vector2.one;
                info.rectTransform.offsetMin = info.rectTransform.offsetMax = Vector2.zero;
                info.alignment = TextAnchor.MiddleCenter; info.fontSize = 18;
                info.resizeTextForBestFit = true; info.resizeTextMinSize = 12; info.resizeTextMaxSize = 18;
                info.horizontalOverflow = HorizontalWrapMode.Wrap; info.verticalOverflow = VerticalWrapMode.Truncate;
                // 由正式控制器创建并连好可点击选项，编辑器预览与运行时用同一条路径。
                InitializePreview(controller);
                controller.SelectPart(CyberneticSlot.Brain);
                Canvas.ForceUpdateCanvases();
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("开始场景保存失败。");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, "SUCCESS\nScene: Assets/Scenes/StartScene.unity\n" +
                    "4 part tabs: 112x123; option tiles: 110x121; spacing: 6; outer grid: 274x304\n" +
                    "Frames: Sprite/Single, alpha transparency, no mipmaps, no compression, bilinear\n" +
                    "Original cybernetic icons reused. Selection sprites wired to runtime controller.\n");
                Debug.Log("StartSelectionArtInstaller SUCCESS: 已接入四种底框及原始义体图标。");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ConfigurePortraitAndBuffPanel(Scene scene, SerializedObject fields, Font font)
        {
            var portrait = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .Single(rect => rect.name == "CharacterPortrait");
            foreach (var name in new[] { "Head", "Body", "LeftArm", "RightArm", "LeftLeg", "RightLeg" })
            {
                var placeholder = portrait.Find(name);
                if (placeholder) placeholder.gameObject.SetActive(false);
            }
            var artwork = portrait.Find("PlayerArtwork");
            if (!artwork)
            {
                artwork = new GameObject("PlayerArtwork", typeof(RectTransform), typeof(Image)).transform;
                artwork.SetParent(portrait, false);
            }
            var image = artwork.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Characters/Hero/玩家.png");
            if (!image.sprite) throw new InvalidOperationException("缺少玩家立绘。");
            image.color = Color.white; image.preserveAspect = true; image.raycastTarget = false;
            var artRect = (RectTransform)artwork;
            artRect.anchorMin = Vector2.zero; artRect.anchorMax = Vector2.one;
            artRect.offsetMin = new Vector2(24, 36); artRect.offsetMax = new Vector2(-24, -36);
            artwork.SetAsLastSibling();

            var body = (Text)fields.FindProperty("buffs").objectReferenceValue;
            var panel = (RectTransform)body.transform.parent;
            // 固定上边缘，内容增多时底板向下伸展。
            var top = panel.TransformPoint(new Vector3(panel.rect.center.x, panel.rect.yMax, 0));
            var portraitCorners = new Vector3[4];
            portrait.GetWorldCorners(portraitCorners);
            top.y = portraitCorners[1].y;
            float width = panel.rect.width;
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, 1);
            panel.sizeDelta = new Vector2(width, panel.rect.height);
            panel.position = top;
            var explore = ((Button)fields.FindProperty("exploreButton").objectReferenceValue).GetComponent<RectTransform>();
            var explorePosition = explore.position;
            explorePosition.x += panel.TransformPoint(panel.rect.center).x - explore.TransformPoint(explore.rect.center).x;
            explore.position = explorePosition;
            var background = panel.Find("Background") as RectTransform;
            if (background)
            {
                background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one;
                background.offsetMin = background.offsetMax = Vector2.zero;
                background.localScale = Vector3.one;
                var ignore = background.GetComponent<LayoutElement>() ?? background.gameObject.AddComponent<LayoutElement>();
                ignore.ignoreLayout = true;
                var frame = background.GetComponent<Image>();
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(frame.sprite)) as TextureImporter;
                if (importer && importer.spriteBorder == Vector4.zero)
                {
                    importer.spriteBorder = new Vector4(40, 40, 40, 40);
                    importer.SaveAndReimport();
                    frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(importer.assetPath);
                }
                frame.preserveAspect = false; frame.type = Image.Type.Sliced;
            }
            var heading = panel.Find("BuffHeading");
            if (!heading)
            {
                heading = new GameObject("BuffHeading", typeof(RectTransform), typeof(Text)).transform;
                heading.SetParent(panel, false);
            }
            var title = heading.GetComponent<Text>();
            title.text = "可触发 buff（同回合打出四部位）";
            title.font = font; title.fontSize = 22; title.fontStyle = FontStyle.Normal;
            title.color = body.color; title.alignment = TextAnchor.UpperLeft;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow; title.raycastTarget = false;
            heading.SetSiblingIndex(background ? 1 : 0);
            body.font = font; body.fontSize = 22; body.fontStyle = FontStyle.Normal;
            body.resizeTextForBestFit = false; body.lineSpacing = 1.15f;
            body.alignment = TextAnchor.UpperLeft; body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            var layout = panel.GetComponent<VerticalLayoutGroup>() ?? panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 40, 40); layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var fitter = panel.GetComponent<ContentSizeFitter>() ?? panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }

        private static Sprite Import(string name)
        {
            var path = ArtPath + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear; importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite) throw new InvalidOperationException("无法导入 " + path);
            return sprite;
        }

        [MenuItem("Spotlight/Start Scene/Validate Selection Artwork")]
        public static void ValidateAndCapture()
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/StartScene.unity");
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("请打开 StartScene。");
            var controller = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<StartSceneController>(true)).Single();
            var fields = new SerializedObject(controller);
            var setup = (AdventureSetupData)fields.FindProperty("setup").objectReferenceValue;
            var excluded = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/TianQiongLegion.asset");
            if (setup.availableCybernetics.Any(data => excluded.cybernetics.Contains(data)))
                throw new InvalidOperationException("本次测试仍包含军用系列。");
            var summaryText = (Text)fields.FindProperty("summary").objectReferenceValue;
            var summaryFrame = (RectTransform)summaryText.transform.parent;
            if (summaryFrame.rect.size != new Vector2(360, 210) || summaryText.fontSize != 22)
                throw new InvalidOperationException("摘要框尺寸或字体大小未更新。");
            var parts = fields.FindProperty("partButtons");
            var grid = (RectTransform)fields.FindProperty("optionsRoot").objectReferenceValue;
            InitializePreview(controller);
            var selection = controller.Selection.ToArray();
            int checks = 0;
            for (int i = 0; i < 4; i++)
            {
                // 编辑模式不执行 MonoBehaviour.OnEnable 的按钮监听绑定；直接走正式切换入口。
                controller.SelectPart((CyberneticSlot)i);
                var tiles = grid.GetComponentsInChildren<CyberneticSelectionTile>(false);
                var candidates = setup.availableCybernetics.Where(data => data && (int)data.slot == i).Distinct().ToArray();
                if (tiles.Length != candidates.Length) throw new InvalidOperationException("候选数量错误：" + i);
                var lastCandidate = candidates.Last(); controller.PreviewCybernetic(lastCandidate);
                tiles = grid.GetComponentsInChildren<CyberneticSelectionTile>(false);
                Canvas.ForceUpdateCanvases();
                foreach (var tile in tiles)
                {
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(grid, tile.transform);
                    if (bounds.min.x < grid.rect.xMin || bounds.max.x > grid.rect.xMax ||
                        bounds.min.y < grid.rect.yMin || bounds.max.y > grid.rect.yMax)
                        throw new InvalidOperationException("卡组选项溢出外框。");
                    foreach (var label in tile.GetComponentsInChildren<Text>(false))
                    {
                        if (label.text.Contains("共享") || label.text.Contains("张"))
                            throw new InvalidOperationException("残留旧计数或耐久文案。");
                        if (label.font.name != "荆南俊俊体") throw new InvalidOperationException("选项字体未统一。");
                    }
                }
                if (tiles.Last().GetComponent<Image>().sprite.name != "DeckOption-Selected")
                    throw new InvalidOperationException("候选选中态未刷新。");
                for (int p = 0; p < 4; p++)
                {
                    var button = (Button)parts.GetArrayElementAtIndex(p).objectReferenceValue;
                    var rect = (RectTransform)button.transform;
                    if (rect.rect.size != new Vector2(112, 123)) throw new InvalidOperationException("页签大小错误。");
                    if (button.image.sprite.name != (p == i ? "PartTab-Selected" : "PartTab-Normal"))
                        throw new InvalidOperationException("部位选中态错误。");
                }
                if (!selection.SequenceEqual(controller.Selection)) throw new InvalidOperationException("预览意外更改了装备。");
                checks++;
            }
            controller.SelectPart(CyberneticSlot.Brain);
            var previewCandidate = setup.availableCybernetics.FirstOrDefault(data => data &&
                data.cyberneticId == "NemesisBrainCard") ?? setup.availableCybernetics.First(data => data && data.slot == CyberneticSlot.Brain);
            controller.PreviewCybernetic(previewCandidate);
            Canvas.ForceUpdateCanvases();
            var previewInfo = (Text)fields.FindProperty("detailInfo").objectReferenceValue;
            var previewTitle = (Text)fields.FindProperty("detailTitle").objectReferenceValue;
            if (!previewInfo.text.Contains("张") || !previewInfo.text.Contains("耐久") || !previewTitle.text.EndsWith(" • 脑机"))
                throw new InvalidOperationException("右侧预览信息格式错误。");
            var previewRoot = (RectTransform)fields.FindProperty("cardPreviewRoot").objectReferenceValue;
            var previewCard = previewRoot.GetComponentInChildren<CardView>();
            var previewButton = previewCard ? previewCard.GetComponent<Button>() : null;
            if (!previewButton || previewButton.interactable || previewButton.transition != Selectable.Transition.None)
                throw new InvalidOperationException("只读预览的禁用态会污染卡面颜色。");
            var canvas = controller.GetComponent<Canvas>();
            var camera = canvas.worldCamera;
            if (camera && canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                var previous = camera.targetTexture; var previousActive = RenderTexture.active;
                var target = RenderTexture.GetTemporary(1920, 1080, 24);
                var texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                    File.WriteAllBytes("output/ui/start-scene/assembled-preview.png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = previous; RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.AppendAllText(ReportPath, "Validated: " + checks + " part switches; candidate highlights; preview leaves loadout unchanged; grid bounds, labels and font verified. Summary 360x210/font22; military set excluded from current playtest.\n");
            Debug.Log("StartSelectionArtInstaller VALIDATED: 4 part switches and candidate highlights.");
        }

        private static void InitializePreview(StartSceneController controller)
        {
            typeof(StartSceneController).GetMethod("Initialize", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
        }
    }
}
