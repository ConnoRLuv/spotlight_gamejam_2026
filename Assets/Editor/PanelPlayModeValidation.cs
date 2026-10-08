using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpotlightGameJam.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpotlightGameJam.Editor
{
    /// <summary>在真实 BattleScene 验证面板动画、悬停、动态弃牌和滚动，结束后退出 Play Mode。</summary>
    [InitializeOnLoad]
    public static class PanelPlayModeValidation
    {
        private const string PendingKey = "Spotlight.PanelPlayValidation";
        private static int stage;
        private static int frames;
        private static double deadline;
        private static double nextStep;
        private static float originalTimeScale = 1;
        private static int nextFrame;
        /// <summary>在编辑器域重载后恢复尚未完成的面板验证流程。</summary>
        static PanelPlayModeValidation() { if (SessionState.GetBool(PendingKey, false)) StartWaiting(); }

        /// <summary>启动当前工具的 PlayMode 验证，准备进入自动检查流程。</summary>
        [MenuItem("Spotlight/Panels/Validate Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请从编辑模式启动验证。");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/BattleScene.unity")
                throw new InvalidOperationException("请先打开已接入弃牌面板的 BattleScene。");
            SessionState.SetBool(PendingKey, true); StartWaiting(); EditorApplication.isPlaying = true;
        }
        /// <summary>订阅编辑器更新，等待进入可执行验证的播放状态。</summary>
        private static void StartWaiting()
        {
            stage = frames = nextFrame = 0; nextStep = 0;
            deadline = EditorApplication.timeSinceStartup + 75;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        /// <summary>推进自动验证步骤，等待界面更新并捕获失败信息。</summary>
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "面板交互验证超时。"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            // 后台编辑器更新不保证推进游戏帧；显式请求 PlayerLoop，并等待实际帧更新。
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < nextStep || Time.frameCount < nextFrame) return;
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            if (!hud || hud.Battle == null || ++frames < 4) return;
            try
            {
                var panel = UnityEngine.Object.FindObjectOfType<DiscardPanel>(true);
                Require(panel, "场景缺少弃牌面板。");
                var group = panel.GetComponent<CanvasGroup>();
                var references = hud.GetComponent<BattleSceneReferences>();
                var pile = references.discardPileButton.gameObject;
                var context = hud.Battle.Context;
                switch (stage)
                {
                    case 0:
                        originalTimeScale = Time.timeScale; Time.timeScale = 0;
                        Require(!panel.gameObject.activeSelf, "弃牌面板应初始隐藏。");
                        AssertRaycast(pile);
                        Enter(pile);
                        Require(panel.IsVisible && group.alpha == 0, "悬停应从透明状态开始淡入。");
                        Require(panel.DisplayedCards.Count == 0, "初始弃牌应为空。");
                        Next(0.3); break;
                    case 1:
                        Require(Mathf.Approximately(group.alpha, 1), "游戏暂停时淡入也应完成。");
                        panel.Hide(0.2f);
                        Require(!group.blocksRaycasts && !panel.IsVisible, "开始淡出应立即停止交互。");
                        panel.Show();
                        Require(group.alpha == 1 && panel.gameObject.activeSelf, "0 秒显示应中断淡出并立即显示。");
                        Next(0.3); break;
                    case 2:
                        Require(panel.gameObject.activeSelf, "旧淡出回调不应关闭重新打开的面板。");
                        panel.Hide(); panel.Show(0.2f); panel.Hide();
                        Require(!panel.gameObject.activeSelf && group.alpha == 0, "0 秒隐藏应中断淡入。");
                        Enter(pile); Next(0.3); break;
                    case 3:
                        Exit(pile); Enter(panel.gameObject);
                        Next(0.3); break;
                    case 4:
                        Require(panel.IsVisible, "从弃牌堆移入面板应保持显示。");
                        Exit(panel.gameObject); Next(0.4); break;
                    case 5:
                        Require(!panel.gameObject.activeSelf, "离开触发区与面板后应淡出关闭。");
                        // 布置足够手牌，通过真实出牌入口产生两种弃牌。
                        context.Ordinary.Draw(9);
                        var defense = context.Ordinary.Hand.Cards.First(card => card.Data.cardId == "DefenseCard");
                        Require(hud.Battle.TryPlay(defense).Success, "防御测试牌出牌失败。");
                        var torso = context.Cybernetic.Hand.Cards.FirstOrDefault(card => card.Data.cardId == "TorsoCard");
                        if (torso == null)
                        {
                            torso = context.Cybernetic.DrawPile.Cards.First(card => card.Data.cardId == "TorsoCard");
                            Require(context.Cybernetic.TakeFromDrawPile(torso), "义体测试牌入手失败。");
                        }
                        Require(hud.Battle.TryPlay(torso).Success, "义体测试牌出牌失败。");
                        Enter(pile); Next(0.3); break;
                    case 6:
                        Require(panel.DisplayedCards.Count == 2 && panel.DisplayedCards.SequenceEqual(CurrentDiscards(context)), "展示应同时包含普通与义体弃牌实例。");
                        Require(panel.GetComponentsInChildren<CardView>().All(view => !view.GetComponent<Button>().interactable), "预览牌不应允许出牌。");
                        AssertRaycast(panel.gameObject);
                        var heal = context.Ordinary.Hand.Cards.First(card => card.Data.cardId == "HealCard");
                        Require(hud.Battle.TryPlay(heal).Success, "回复测试牌出牌失败。");
                        Require(panel.DisplayedCards.Count == 3, "面板打开期间应随弃牌变化刷新。");
                        // 此处只为布置溢出的展示数量，所有牌仍经 CardDeck 正常移区。
                        foreach (var card in context.Ordinary.Hand.Cards.ToArray()) context.Ordinary.Discard(card);
                        panel.RefreshCards(); hud.ShowOrdinary();
                        // 等实际帧完成布局、滚动条和 HUD 更新后再导出截图。
                        Next(0.1); break;
                    case 7:
                        var scroll = panel.GetComponent<ScrollRect>();
                        Require(scroll.content.rect.height > scroll.viewport.rect.height, "多张弃牌应产生可滚动内容。");
                        Capture(1920, 1080); Capture(1024, 768);
                        var pointer = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) };
                        scroll.OnScroll(pointer);
                        Require(scroll.verticalNormalizedPosition < 1, "鼠标滚轮应能浏览下方弃牌。");
                        Require(hud.Battle.EndTurn(), "测试回合推进失败。");
                        Require(context.Ordinary.DiscardPile.Count == 0 && panel.DisplayedCards.SequenceEqual(CurrentDiscards(context)), "洗回抽牌堆的牌应从当前弃牌预览移除。");
                        Next(0.1); break;
                    case 8:
                        var oldBattle = hud.Battle;
                        UnityEngine.Object.FindObjectOfType<GameBootstrap>().StartNewRun();
                        Require(!panel.IsVisible, "新战斗应关闭旧弃牌预览。");
                        Enter(pile); Require(panel.DisplayedCards.Count == 0, "新战斗应绑定新弃牌堆。");
                        oldBattle.Context.Cybernetic.Discard(oldBattle.Context.Cybernetic.Hand.Cards[0]);
                        oldBattle.EndTurn();
                        Require(panel.DisplayedCards.Count == 0, "旧战斗事件不应影响新面板。");
                        var hover = pile.GetComponent<DiscardPileHover>();
                        hover.enabled = false;
                        Require(!panel.gameObject.activeSelf, "禁用悬停入口应立即关闭预览。");
                        hover.enabled = true;
                        for (int i = 0; i < 5; i++) { Enter(pile); Exit(pile); }
                        Next(0.5); break;
                    case 9:
                        Require(!panel.gameObject.activeSelf, "快速进出后不应残留面板或关闭任务。");
                        Finish(true, "面板零秒显隐、暂停时淡入淡出、过渡反转、悬停移入/移出、动态弃牌、洗牌刷新、滚轮、新战斗重新绑定与快速进出验证通过。");
                        break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(false, exception.ToString()); }
        }
        /// <summary>返回当前普通与义体弃牌实例，作为界面预览的对照数据。</summary>
        private static IEnumerable<CardInstance> CurrentDiscards(BattleContext context) => context.Ordinary.DiscardPile.Cards.Concat(context.Cybernetic.DiscardPile.Cards);
        /// <summary>安排下一验证步骤，留出帧间隔使界面和战斗状态完成更新。</summary>
        private static void Next(double seconds)
        { stage++; nextStep = EditorApplication.timeSinceStartup + seconds; nextFrame = Time.frameCount + 3; }
        /// <summary>向指定对象发送鼠标进入事件，模拟悬停操作。</summary>
        private static void Enter(GameObject target) { ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler); }
        /// <summary>向指定对象发送鼠标离开事件，模拟离开触发区域。</summary>
        private static void Exit(GameObject target) { ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler); }
        /// <summary>检查验证条件，不满足时抛出包含原因的异常。</summary>
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        /// <summary>检查指定屏幕位置的射线结果，确认目标控件可接收鼠标事件。</summary>
        private static void AssertRaycast(GameObject target)
        {
            Canvas.ForceUpdateCanvases();
            var rect = target.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(Camera.main, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && (hits[0].gameObject == target || hits[0].gameObject.transform.IsChildOf(target.transform)), "UI 射线无法命中 " + target.name);
        }
        /// <summary>结束验证并记录结果，解除更新订阅及恢复编辑器状态。</summary>
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(PendingKey, false);
            Time.timeScale = originalTimeScale;
            Directory.CreateDirectory("Logs/Panels"); File.WriteAllText("Logs/Panels/play-mode-validation.txt", (success ? "PASS\n" : "FAIL\n") + message);
            Debug.Log(message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
            else EditorApplication.isPlaying = false;
            stage = frames = 0;
        }
        /// <summary>导出当前界面截图，供人工检查布局与交互结果。</summary>
        private static void Capture(int width, int height)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(width, height, 24); var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; Canvas.ForceUpdateCanvases();
                var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
                if (hud) hud.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs/Panels"); File.WriteAllBytes("Logs/Panels/DiscardPanel-" + width + "x" + height + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
