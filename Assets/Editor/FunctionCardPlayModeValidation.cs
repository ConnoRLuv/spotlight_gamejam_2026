using System;
using System.IO;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>通过真实 BattleScene 的手牌按钮验证过载、弃牌预览和临时 AP 过期。</summary>
    [InitializeOnLoad]
    public static class FunctionCardPlayModeValidation
    {
        private const string PendingKey = "Spotlight.FunctionCardValidation";
        private static int stage;
        private static double deadline;
        private static double nextStep;
        private static int nextFrame;
        private static CardInstance played;
        /// <summary>在编辑器域重载后恢复尚未完成的功能牌验证流程。</summary>
        static FunctionCardPlayModeValidation() { if (SessionState.GetBool(PendingKey, false)) StartWaiting(); }

        /// <summary>启动当前工具的 PlayMode 验证，准备进入自动检查流程。</summary>
        [MenuItem("Spotlight/Cards/Validate Function Card Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请从编辑模式启动。");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/BattleScene.unity")
                throw new InvalidOperationException("请先打开 BattleScene。");
            SessionState.SetBool(PendingKey, true); StartWaiting(); EditorApplication.isPlaying = true;
        }
        /// <summary>订阅编辑器更新，等待进入可执行验证的播放状态。</summary>
        private static void StartWaiting()
        {
            stage = nextFrame = 0; nextStep = 0; played = null;
            deadline = EditorApplication.timeSinceStartup + 75;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        /// <summary>推进自动验证步骤，等待界面更新并捕获失败信息。</summary>
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "过载运行验证超时。"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount < nextFrame || EditorApplication.timeSinceStartup < nextStep) return;
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            if (!hud || hud.Battle == null) return;
            try
            {
                var context = hud.Battle.Context; var scene = hud.GetComponent<BattleSceneReferences>();
                var panel = UnityEngine.Object.FindObjectOfType<DiscardPanel>(true);
                switch (stage)
                {
                    case 0:
                        // 抽完演示牌堆确保过载进入手牌，出牌仍通过实际 CardView/HUD 回调。
                        context.Ordinary.Draw(context.Ordinary.DrawPile.Count);
                        played = context.Ordinary.Hand.Cards.Single(card => card.Data.cardId == "OverloadCard");
                        context.ActionPoints.TrySpend(context.ActionPoints.Total); hud.ShowOrdinary();
                        var view = hud.GetComponentsInChildren<CardView>().Single(card => card.Instance == played);
                        Require(view.transform.Find("Source").GetComponent<Text>().text == "功能牌", "手牌分类显示错误。");
                        var durability = context.Run.Loadout.Equipped.Select(item => item.Durability).ToArray();
                        int sanity = context.Player.Sanity;
                        view.GetComponent<Button>().onClick.Invoke();
                        Require(context.ActionPoints.Normal == 0 && context.ActionPoints.Temporary == 1, "零 AP 时过载应获得 1 临时 AP。");
                        Require(context.Player.Sanity == sanity - played.Data.sanityCost, "理智费用不正确。");
                        Require(context.Run.Loadout.Equipped.Select(item => item.Durability).SequenceEqual(durability), "功能牌不应消耗义体耐久。");
                        Require(context.Ordinary.DiscardPile.Contains(played), "过载应进入普通弃牌堆。");
                        Require(scene.actionPointsText.text == "行动点 1", "HUD 行动点未刷新。");
                        ExecuteEvents.Execute(scene.discardPileButton.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                        Next(); break;
                    case 1:
                        Require(panel.IsVisible && panel.DisplayedCards.Contains(played), "弃牌面板缺少已使用的过载。");
                        var preview = panel.GetComponentsInChildren<CardView>().Single(card => card.Instance == played);
                        Require(preview.Data.cardName == "过载" && !preview.GetComponent<Button>().interactable, "过载预览内容或交互不正确。");
                        Capture("Logs/Functions/Overload-discard-1920x1080.png");
                        panel.Hide();
                        // 使用防御牌验证临时 AP 可以支付普通牌费用且优先消耗。
                        var defense = context.Ordinary.Hand.Cards.First(card => card.Data.cardId == "DefenseCard");
                        hud.GetComponentsInChildren<CardView>().Single(card => card.Instance == defense).GetComponent<Button>().onClick.Invoke();
                        Require(context.Player.Shield == 3 && context.ActionPoints.Total == 0, "临时 AP 无法支付基本牌。");
                        // 当前演示只有一张过载，移回手牌用于第二次真实出牌和过期检查。
                        Require(context.Ordinary.DiscardPile.TryRemove(played) && context.Ordinary.Hand.TryAdd(played), "测试牌重新入手失败。");
                        hud.ShowOrdinary();
                        hud.GetComponentsInChildren<CardView>().Single(card => card.Instance == played).GetComponent<Button>().onClick.Invoke();
                        Require(context.ActionPoints.Temporary == 1, "再次过载不应受部位使用次数限制。");
                        scene.endTurnButton.onClick.Invoke();
                        Require(hud.Battle.TurnNumber == 2 && context.ActionPoints.Normal == 3 && context.ActionPoints.Temporary == 0, "临时 AP 未在回合结束清除。");
                        Next(); break;
                    case 2:
                        Capture("Logs/Functions/Overload-next-turn-1920x1080.png");
                        Finish(true, "过载手牌按钮、零 AP 出牌、理智支付、耐久保留、功能牌分类、弃牌悬停预览、临时 AP 支付基本牌及回合结束清除验证通过。");
                        break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(false, exception.ToString()); }
        }
        /// <summary>安排下一验证步骤，留出帧间隔使界面和战斗状态完成更新。</summary>
        private static void Next() { stage++; nextFrame = Time.frameCount + 3; nextStep = EditorApplication.timeSinceStartup + 0.3; }
        /// <summary>检查验证条件，不满足时抛出包含原因的异常。</summary>
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        /// <summary>结束验证并记录结果，解除更新订阅及恢复编辑器状态。</summary>
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(PendingKey, false);
            Directory.CreateDirectory("Logs/Functions"); File.WriteAllText("Logs/Functions/play-mode-validation.txt", (success ? "PASS\n" : "FAIL\n") + message);
            Debug.Log(message); EditorApplication.isPlaying = false; played = null;
        }
        /// <summary>导出当前界面截图，供人工检查布局与交互结果。</summary>
        private static void Capture(string path)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24); var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
