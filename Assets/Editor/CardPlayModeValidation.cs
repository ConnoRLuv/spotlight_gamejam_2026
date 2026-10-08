using System;
using System.IO;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>用真实场景和 Button 回调验证卡牌交互，另提供七种预制体的色块预览。</summary>
    [InitializeOnLoad]
    public static class CardPlayModeValidation
    {
        private const string PendingKey = "Spotlight.CardPlayValidation";
        private static int readyFrames;
        private static double deadline;
        /// <summary>在编辑器域重载后恢复尚未完成的卡牌验证流程。</summary>
        static CardPlayModeValidation()
        {
            if (SessionState.GetBool(PendingKey, false)) StartWaiting();
        }

        /// <summary>批处理入口，跨域重载继续等待初始化；仅在批处理实例中自动退出。</summary>
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            SessionState.SetBool(PendingKey, true);
            StartWaiting();
            EditorApplication.isPlaying = true;
        }
        /// <summary>订阅编辑器更新，等待进入可执行验证的播放状态。</summary>
        private static void StartWaiting()
        {
            deadline = EditorApplication.timeSinceStartup + 60;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        /// <summary>推进自动验证步骤，等待界面更新并捕获失败信息。</summary>
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "等待战斗初始化超时。"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            if (!hud || hud.Battle == null || ++readyFrames < 4) return;
            EditorApplication.update -= Tick;
            try { ValidatePlayMode(); Finish(true, "七种卡牌、目标选择、选牌取消与确认、转换、额外回合和 UI 射线验证通过。"); }
            catch (Exception exception) { Debug.LogException(exception); Finish(false, exception.ToString()); }
        }
        /// <summary>结束验证并记录结果，解除更新订阅及恢复编辑器状态。</summary>
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(PendingKey, false);
            Directory.CreateDirectory("Logs/Cards");
            File.WriteAllText("Logs/Cards/play-mode-validation.txt", (success ? "PASS\n" : "FAIL\n") + message);
            Debug.Log(message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        /// <summary>在播放模式检查卡牌交互、资源变化与界面展示是否符合规则。</summary>
        [MenuItem("Spotlight/Cards/Validate Play Mode Interaction")]
        public static void ValidatePlayMode()
        {
            Require(Application.isPlaying, "交互验证必须在 Play Mode 中运行。");
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            var battle = hud.Battle; var context = battle.Context;
            var scene = hud.GetComponent<BattleSceneReferences>();
            // 业务回调检查前，确认场景按钮可被 EventSystem 命中且悬停行为正确。
            BattleSceneBuilder.ValidateInteraction();
            var fields = new SerializedObject(hud);
            // 从 HUD 的序列化字段中读取按钮引用，供验证模拟真实界面点击。
            Button ButtonField(string name) => (Button)fields.FindProperty(name).objectReferenceValue;

            // 测试布置补充普通牌，实际出牌始终通过 CardView -> HUD -> BattleController。
            context.Ordinary.Draw(9);
            ButtonField("ordinaryTab").onClick.Invoke();
            ClickHand(hud, "AttackCard");
            Require(hud.PendingCard != null, "攻击未进入目标选择。");
            scene.enemyPortrait.GetComponent<Button>().onClick.Invoke();
            Require(context.Enemies[0].Health == 24 && context.ActionPoints.Total == 1, "攻击目标或费用不正确。");
            ClickHand(hud, "DefenseCard");
            Require(context.Player.Shield == 3 && context.ActionPoints.Total == 0, "防御未结算。");
            int ordinaryCount = context.Ordinary.Hand.Count;
            ClickHand(hud, "HealCard");
            Require(context.Ordinary.Hand.Count == ordinaryCount, "AP 不足时回复牌应保留。");
            scene.endTurnButton.onClick.Invoke();
            Require(battle.TurnNumber == 2 && context.Player.Health == 97, "结束回合未执行敌人攻击。");
            ClickHand(hud, "HealCard");
            Require(context.Player.Health == 100 && context.ActionPoints.Total == 2, "回复或新回合 AP 不正确。");

            PutCyberneticInHand(context, "BrainCard");
            ButtonField("cyberneticTab").onClick.Invoke();
            var originalOrder = context.Cybernetic.DrawPile.Cards.Select(card => card.Id).ToArray();
            var sanity = context.Player.Sanity;
            ClickHand(hud, "BrainCard");
            Require(hud.IsChoosing, "脑机未打开选择面板。");
            scene.endTurnButton.onClick.Invoke();
            Require(battle.TurnNumber == 2, "选择期间不应推进回合。");
            ButtonField("cancelChoiceButton").onClick.Invoke();
            Require(!hud.IsChoosing && context.Player.Sanity == sanity && context.Cybernetic.DrawPile.Cards.Select(card => card.Id).SequenceEqual(originalOrder), "取消选牌修改了资源或顺序。");
            ClickHand(hud, "BrainCard");
            var choicesRoot = (RectTransform)fields.FindProperty("choicesRoot").objectReferenceValue;
            Capture(Camera.main, 1920, 1080, "Logs/Cards/BattleScene-choice-1920x1080.png");
            var choice = choicesRoot.GetComponentsInChildren<CardView>().First();
            var choiceId = choice.Instance.Id;
            choice.GetComponent<Button>().onClick.Invoke();
            Require(!hud.IsChoosing && context.Player.Sanity == sanity - 8 && context.Cybernetic.Hand.Cards.Any(card => card.Id == choiceId), "脑机确认未取出指定实例。");

            PutCyberneticInHand(context, "TorsoCard"); hud.ShowCybernetic(); ClickHand(hud, "TorsoCard");
            int life = context.Player.Health; sanity = context.Player.Sanity;
            DamageResolver.Apply(context, context.Enemies[0], context.Player, 2);
            Require(context.Player.Health == life && context.Player.Sanity == sanity - 2, "生命维持器未转换承伤。");

            PutCyberneticInHand(context, "HandsCard"); hud.ShowCybernetic(); ClickHand(hud, "HandsCard");
            var convert = ButtonField("conversionButton");
            Require(convert.gameObject.activeSelf, "缺少 AP 转换按钮。");
            convert.onClick.Invoke();
            var generated = context.Ordinary.Hand.Cards.Single(card => card.ExpiresAtTurnEnd);
            hud.ShowOrdinary();
            Capture(Camera.main, 1024, 768, "Logs/Cards/BattleScene-conversion-1024x768.png");
            var generatedView = hud.GetComponentsInChildren<CardView>().Single(view => view.Instance == generated);
            generatedView.GetComponent<Button>().onClick.Invoke();
            scene.enemyPortrait.GetComponent<Button>().onClick.Invoke();
            Require(context.Enemies[0].Health == 18 && !context.Ordinary.Hand.Contains(generated) && !context.Ordinary.DiscardPile.Contains(generated), "生成攻击未正确结算或移除。");
            convert.onClick.Invoke();
            var unused = context.Ordinary.Hand.Cards.Single(card => card.ExpiresAtTurnEnd);

            PutCyberneticInHand(context, "LegsCard"); hud.ShowCybernetic(); ClickHand(hud, "LegsCard");
            life = context.Player.Health;
            scene.endTurnButton.onClick.Invoke();
            Require(context.Player.Health == life && battle.TurnNumber == 3 && context.ActionPoints.Total == 4, "额外回合应跳过敌人并完整刷新。");
            Require(!context.Ordinary.Hand.Contains(unused) && !context.Ordinary.DiscardPile.Contains(unused) && !context.TurnEffects.ConversionEnabled, "玩家行动结束后临时牌和转换未清理。");
            Require(context.Usage.GetUses(CyberneticSlot.Legs) == 1 && context.Run.Loadout.Get(CyberneticSlot.Legs).Durability == 2, "额外回合错误重置了战斗次数或耐久。");
            scene.endTurnButton.onClick.Invoke();
            Require(context.Player.Health == life - 6, "额外回合消费后敌人应恢复行动。");
            hud.ShowOrdinary();
            Capture(Camera.main, 1920, 1080, "Logs/Cards/BattleScene-play-1920x1080.png");
            Capture(Camera.main, 1024, 768, "Logs/Cards/BattleScene-play-1024x768.png");
            Capture(Camera.main, 2560, 1080, "Logs/Cards/BattleScene-play-2560x1080.png");
            Debug.Log("BattleScene 七种卡牌交互验证通过。");
        }

        /// <summary>按配置查找当前手牌卡面并模拟点击。</summary>
        private static void ClickHand(BattleHUD hud, string key)
        {
            var view = hud.GetComponentsInChildren<CardView>().FirstOrDefault(value => value.Instance != null && value.Instance.Data.cardId == key);
            Require(view != null, "手牌展示缺失：" + key);
            view.GetComponent<Button>().onClick.Invoke();
        }
        /// <summary>将指定义体牌布置到手牌，为界面验证准备状态。</summary>
        private static void PutCyberneticInHand(BattleContext context, string key)
        {
            if (context.Cybernetic.Hand.Cards.Any(card => card.Data.cardId == key)) return;
            var candidate = context.Cybernetic.DrawPile.Cards.FirstOrDefault(card => card.Data.cardId == key)
                ?? context.Cybernetic.DiscardPile.Cards.First(card => card.Data.cardId == key);
            if (context.Cybernetic.Hand.Count == context.Cybernetic.Hand.Capacity)
                context.Cybernetic.Discard(context.Cybernetic.Hand.Cards[0]);
            if (context.Cybernetic.DrawPile.Contains(candidate)) Require(context.Cybernetic.TakeFromDrawPile(candidate), "测试牌入手失败。");
            else { Require(context.Cybernetic.DiscardPile.TryRemove(candidate), "测试弃牌移出失败。"); Require(context.Cybernetic.Hand.TryAdd(candidate), "测试牌加入失败。"); }
        }
        /// <summary>检查验证条件，不满足时抛出包含原因的异常。</summary>
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        /// <summary>在临时附加场景中展示七种预制体，导出后关闭，不保存或覆盖用户场景。</summary>
        [MenuItem("Spotlight/Cards/Export Seven Card Preview")]
        public static void ExportGallery()
        {
            // 批处理启动时有一个未保存的空场景；加载既有场景后才能创建附加预览。
            if (Application.isBatchMode && string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var gallery = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(gallery);
            try
            {
                var camera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.94f, 0.94f, 0.93f);
                camera.transform.position = new Vector3(0, 0, -10);
                new GameObject("Preview Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
                var canvas = new GameObject("Preview Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600, 500);
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
                PreviewText(canvas.transform, font, "战斗卡牌 · 低饱和度占位", new Rect(98, 30, 1404, 45), 28);
                PreviewText(canvas.transform, font, "攻击 · 防御 · 回复 · 脑机 · 躯干 · 手部 · 腿部", new Rect(98, 78, 1404, 30), 18);
                var keys = new[] { "AttackCard", "DefenseCard", "HealCard", "BrainCard", "TorsoCard", "HandsCard", "LegsCard" };
                for (int i = 0; i < keys.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cards/" + keys[i] + ".prefab");
                    var instance = UnityEngine.Object.Instantiate(prefab, canvas.transform);
                    var rect = instance.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                    rect.anchoredPosition = new Vector2(98 + i * 204, -140);
                }
                PreviewText(canvas.transform, font, "基本牌消耗行动点；义体牌消耗理智，并消耗耐久。", new Rect(98, 390, 1404, 35), 18);
                Capture(camera, 1600, 500, "Logs/Cards/SevenCards-1600x500.png");
            }
            finally
            {
                EditorSceneManager.CloseScene(gallery, true);
                if (originalScene.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene);
            }
        }
        /// <summary>读取预览中的文本内容，用于验证显示信息。</summary>
        private static void PreviewText(Transform parent, Font font, string value, Rect bounds, int size)
        {
            var text = new GameObject("Caption", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var rect = text.rectTransform; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
            text.font = font; text.fontSize = size; text.color = new Color(0.2f, 0.23f, 0.25f); text.text = value;
        }

        /// <summary>导出当前界面截图，供人工检查布局与交互结果。</summary>
        private static void Capture(Camera camera, int width, int height, string path)
        {
            var previous = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24); var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; Canvas.ForceUpdateCanvases();
                var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
                if (hud && Application.isPlaying) hud.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = previousActive; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
