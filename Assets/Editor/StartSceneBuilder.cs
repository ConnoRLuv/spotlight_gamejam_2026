using System;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace SpotlightGameJam.Editor
{
    /// <summary>按开始界面参考图生成可编辑 uGUI 场景及探索配置，不修改启动顺序。</summary>
    public static class StartSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/StartScene.unity";
        private const string SetupPath = "Assets/GameData/AdventureSetup.asset";
        private static Font font;
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector2> referenceSizes = new System.Collections.Generic.Dictionary<Transform, Vector2>();
        /// <summary>创建缺失资源；已有 StartScene 不重建，保护成员手工调整。</summary>
        [MenuItem("Spotlight/Start Scene/Create Layout And Exploration Setup")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式构建。");
            var setup = CreateSetup();
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) CreateScene(setup);
            RemoveDedicatedScene();
            AssetDatabase.SaveAssets();
        }
        /// <summary>复用 BattleScene 的普通牌、规则及幻痛，创建可配置的组合 buff。</summary>
        private static AdventureSetupData CreateSetup()
        {
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            if (!font) throw new InvalidOperationException("缺少中文字体。");
            var original = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/TianQiongLegion.asset");
            var prometheus = AssetDatabase.LoadAssetAtPath<CyberneticDeckData>("Assets/GameData/CyberneticDecks/Prometheus.asset");
            if (!original || !prometheus) throw new InvalidOperationException("缺少已实现义体牌组。");
            if (!AssetDatabase.IsValidFolder("Assets/GameData/Buffs")) AssetDatabase.CreateFolder("Assets/GameData", "Buffs");
            var buff = AssetDatabase.LoadAssetAtPath<RunBuffData>("Assets/GameData/Buffs/Prometheus.asset");
            if (!buff)
            {
                buff = ScriptableObject.CreateInstance<RunBuffData>(); buff.buffId = "prometheus-overload";
                buff.buffName = "普罗米修斯";
                buff.description = "同一玩家回合成功打出普罗米修斯四部位后，本场战斗过载改为消耗 5 点生命；无视护盾，不回复理智，支付后须保留至少 1 生命。";
                buff.duration = RunBuffDuration.Battle;
                buff.requiredCybernetics = prometheus.cybernetics.ToArray(); buff.effect = RunBuffEffect.OverloadPaysHealth;
                AssetDatabase.CreateAsset(buff, "Assets/GameData/Buffs/Prometheus.asset");
            }
            var existing = AssetDatabase.LoadAssetAtPath<AdventureSetupData>(SetupPath);
            if (existing) return existing;
            var sourceScene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (opened) sourceScene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var bootstrap = sourceScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true)).Single();
                var data = new SerializedObject(bootstrap);
                var setup = ScriptableObject.CreateInstance<AdventureSetupData>();
                setup.rules = data.FindProperty("rules").objectReferenceValue as BattleRules;
                setup.phantomPain = data.FindProperty("phantomPain").objectReferenceValue as CardData;
                var ordinary = data.FindProperty("ordinaryDeck");
                setup.ordinaryDeck = Enumerable.Range(0, ordinary.arraySize).Select(i => ordinary.GetArrayElementAtIndex(i).objectReferenceValue as CardData).ToArray();
                setup.availableCybernetics = original.cybernetics.Concat(prometheus.cybernetics).ToArray();
                setup.defaultCybernetics = original.cybernetics.ToArray(); setup.buffs = new[] { buff };
                setup.randomSeed = data.FindProperty("randomSeed").intValue;
                AssetDatabase.CreateAsset(setup, SetupPath); return setup;
            }
            finally { if (opened) EditorSceneManager.CloseScene(sourceScene, true); }
        }
        /// <summary>以附加方式创建场景，不丢弃成员当前打开场景的未保存内容。</summary>
        private static void CreateScene(AdventureSetupData setup, string scenePath = ScenePath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(0,0,-10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.97f,.97f,.97f);
            camera.orthographic = true;
            var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional;
            var canvas = new GameObject("StartCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.gameObject.layer = 5; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            var frame = new GameObject("StartLayout", typeof(RectTransform), typeof(AspectRatioFitter)).GetComponent<RectTransform>();
            frame.SetParent(canvas.transform, false); frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f,.5f);
            frame.sizeDelta = new Vector2(1920,1080);
            referenceSizes.Clear(); referenceSizes[frame] = new Vector2(1920,1080);
            var fitter = frame.GetComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fitter.aspectRatio = 16f/9f;
            var panel = Panel("CyberneticAssemblyPanel", frame,114,190,611,767,.86f);
            Panel("Body",panel,18,79,578,661,.74f);
            var header = Panel("Header",panel,0,0,611,79,.46f); Label("Title",header,"义体组装模块",32,Color.white);
            var parts = new Button[4]; var names = new[]{"脑机","躯干","手部","腿部"};
            for(int i=0;i<4;i++) parts[i] = Button("Part"+i,panel,41+i*141,108,107,117,names[i],28);
            LabelAt("PartHint",panel,24,235,563,28,"点击部位选择义体牌组",22,Color.white);
            var assembly = Panel("AssemblyWindow",panel,42,280,526,432,.62f);
            var inventory = Panel("InventoryGrid",assembly,25,25,283,265,.58f);
            var grid = inventory.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(111,112);
            grid.spacing = new Vector2(26,16); grid.padding = new RectOffset(17,17,12,12); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            var title = LabelAt("SelectedCyberneticName",assembly,326,25,175,32,"义体名字",22,Color.white);
            var preview = Rect("CardPreview",assembly,326,65,175,215);
            var info = LabelAt("DetailInfo",assembly,326,285,175,45,"同种义体共享耐久",15,Color.white);
            var replace = Button("ReplaceButton",assembly,25,305,283,65,"更换",28);
            var unequip = Button("UnequipButton",assembly,326,340,175,45,"卸下此部位",20);
            var clear = Button("ClearButton",panel,42,722,526,32,"清空四部位",18);
            var character = Panel("CharacterPreviewPanel",frame,813,192,356,765,.86f);
            var summaryRoot = Panel("EquippedSummary",character,29,26,299,165,.46f);
            var summary = Label("Summary",summaryRoot,"",17,Color.white); summary.alignment = TextAnchor.MiddleLeft;
            var portrait = Panel("CharacterPortrait",character,29,207,299,465,.64f);
            Panel("Head",portrait,111,55,78,88,.46f); Panel("Body",portrait,89,150,122,170,.46f);
            Panel("LeftArm",portrait,53,155,30,160,.50f); Panel("RightArm",portrait,218,155,30,160,.50f);
            Panel("LeftLeg",portrait,89,327,51,108,.46f); Panel("RightLeg",portrait,160,327,51,108,.46f);
            LabelAt("PortraitHint",character,29,675,299,40,"角色立绘占位",20,Color.white);
            var buffRoot = Panel("GlobalBuffPanel",frame,1264,580,553,200,.86f);
            var buffs = Label("GlobalBuffs",buffRoot,"可触发 buff：无完整组合",22,new Color(.25f,.25f,.25f)); buffs.alignment = TextAnchor.MiddleLeft;
            var settings = Button("SettingsButton",frame,1768,51,102,102,"设置",36);
            var story = Button("StoryBookButton",frame,1768,195,102,102,"故事书",23);
            var explore = Button("ExploreButton",frame,1264,827,553,134,"出发探索",30);
            var status = LabelAt("Status",frame,1264,978,553,42,"选择义体牌组，也可留空出发",20,new Color(.3f,.3f,.3f));
            new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            new GameObject("AdventureSession").AddComponent<AdventureSession>();
            var controller = canvas.gameObject.AddComponent<StartSceneController>();
            controller.Configure(setup,AssetDatabase.LoadAssetAtPath<CardPrefabCatalog>("Assets/GameData/CardPrefabCatalog.asset"),
                parts,replace,unequip,clear,explore,settings,story,inventory,preview,title,info,summary,buffs,status);
            Canvas.ForceUpdateCanvases(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene,scenePath)) throw new InvalidOperationException("StartScene 保存失败。");
            referenceSizes.Clear();
        }
        /// <summary>移除旧的独立普罗米修斯测试场景，不删除卡牌配置或预制体。</summary>
        private static void RemoveDedicatedScene()
        {
            const string path = "Assets/Scenes/PrometheusBattleScene.unity";
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty) throw new InvalidOperationException("独立测试场景有未保存修改，请先保存。");
                EditorSceneManager.CloseScene(scene,true);
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) AssetDatabase.DeleteAsset(path);
        }
        /// <summary>将参考坐标转换为父节点比例锚点，适应不同屏幕比例。</summary>
        private static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); rect.gameObject.layer=5;
            Vector2 size;
            if (!referenceSizes.TryGetValue(parent, out size)) size = ((RectTransform)parent).rect.size;
            referenceSizes[rect] = new Vector2(w,h);
            rect.anchorMin = new Vector2(x/size.x,1-(y+h)/size.y); rect.anchorMax = new Vector2((x+w)/size.x,1-y/size.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        /// <summary>创建不拦截鼠标的灰阶背景。</summary>
        private static RectTransform Panel(string name,Transform parent,float x,float y,float w,float h,float gray)
        { var rect=Rect(name,parent,x,y,w,h); var image=rect.gameObject.AddComponent<Image>(); image.color=new Color(gray,gray,gray); image.raycastTarget=false; return rect; }
        /// <summary>创建中文文本并关闭射线，允许中文字体行高溢出。</summary>
        private static Text Label(string name,RectTransform parent,string content,int size,Color color)
        {
            Vector2 bounds;
            if (!referenceSizes.TryGetValue(parent,out bounds)) bounds=parent.rect.size;
            var rect=Rect(name,parent,8,2,bounds.x-16,bounds.y-4); var text=rect.gameObject.AddComponent<Text>();
            text.font=font; text.text=content; text.fontSize=size; text.color=color; text.alignment=TextAnchor.MiddleCenter;
            text.raycastTarget=false; text.verticalOverflow=VerticalWrapMode.Overflow; return text;
        }
        /// <summary>在参考矩形中创建文本。</summary>
        private static Text LabelAt(string name,Transform parent,float x,float y,float w,float h,string content,int size,Color color)
        { var rect=Rect(name,parent,x,y,w,h); return Label("Label",rect,content,size,color); }
        /// <summary>创建具备鼠标悬停和按下反馈的按钮。</summary>
        private static Button Button(string name,Transform parent,float x,float y,float w,float h,string content,int size)
        {
            var rect=Panel(name,parent,x,y,w,h,.46f); var image=rect.GetComponent<Image>(); image.raycastTarget=true;
            var button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=image; Label("Label",rect,content,size,Color.white); return button;
        }
    }
}
