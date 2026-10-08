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
    /// <summary>用真实义体牌按钮验证四个装备槽、大图悬停、脑机取消、数值刷新和战斗重新绑定。</summary>
    [InitializeOnLoad]
    public static class CyberneticEquipmentPlayModeValidation
    {
        private const string PendingKey = "Spotlight.EquipmentValidation";
        private static int stage, nextFrame;
        private static double deadline, nextStep;
        /// <summary>在编辑器域重载后恢复尚未完成的装备栏验证流程。</summary>
        static CyberneticEquipmentPlayModeValidation() { if (SessionState.GetBool(PendingKey, false)) StartWaiting(); }
        /// <summary>启动当前工具的 PlayMode 验证，准备进入自动检查流程。</summary>
        [MenuItem("Spotlight/Equipment/Validate Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请从编辑模式运行。");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/BattleScene.unity") throw new InvalidOperationException("请打开 BattleScene。");
            SessionState.SetBool(PendingKey, true); StartWaiting(); EditorApplication.isPlaying = true;
        }
        /// <summary>订阅编辑器更新，等待进入可执行验证的播放状态。</summary>
        private static void StartWaiting()
        {
            stage = nextFrame = 0; nextStep = 0; deadline = EditorApplication.timeSinceStartup + 75;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        /// <summary>推进自动验证步骤，等待界面更新并捕获失败信息。</summary>
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "装备栏验证超时。"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount < nextFrame || EditorApplication.timeSinceStartup < nextStep) return;
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>(); if (!hud || hud.Battle == null) return;
            try
            {
                var slots = hud.GetComponentsInChildren<CyberneticEquipmentSlot>(true).OrderBy(slot => slot.Slot).ToArray();
                var tooltip = hud.GetComponentInChildren<CyberneticEquipmentTooltip>(true);
                var context = hud.Battle.Context; var torso = slots.Single(slot => slot.Slot == CyberneticSlot.Torso);
                switch (stage)
                {
                    case 0:
                        Require(slots.Length == 4 && slots.All(slot => slot.DisplayedCard == null &&
                            slot.transform.Find("Name").GetComponent<Text>().text == "" && slot.transform.Find("Durability").GetComponent<Text>().text == ""), "初始装备内容应为空。");
                        Require(slots.Select(slot => slot.transform.Find("Part").GetComponent<Text>().text).SequenceEqual(new[] { "脑机", "躯干", "手部", "腿部" }), "四个部位标题应常显。");
                        foreach (var slot in slots) Enter(slot.gameObject);
                        Require(!tooltip.IsVisible, "空槽悬停不应显示详情。");
                        Capture(1920, 1080, "empty");
                        Require(slots.All(slot => slot.transform.Find("Part").GetComponent<Text>().cachedTextGenerator.vertexCount > 4), "部位标题必须实际生成可见文字。");
                        Click(hud, "TorsoCard");
                        Require(torso.DisplayedCard != null && torso.DisplayedCard.Source.Durability == 4, "躯干使用后未填入正确卡牌及耐久。");
                        Require(slots.Count(slot => slot.DisplayedCard != null) == 1, "只应填入已使用部位。");
                        Next(); break;
                    case 1:
                        AssertRaycast(torso.gameObject); Enter(torso.gameObject); Next(); break;
                    case 2:
                        Require(tooltip.IsVisible && tooltip.DisplayedCard == torso.DisplayedCard, "悬停详情不是该部位卡牌。");
                        Require(!tooltip.GetComponent<CanvasGroup>().blocksRaycasts, "悬停说明不应遮挡触发区。");
                        Require(tooltip.GetComponentsInChildren<CardView>().Single().transform.localScale.x > 1, "悬停应显示放大卡面。");
                        Require(tooltip.transform.Find("Description").GetComponent<Text>().text == torso.DisplayedCard.Data.description, "说明内容错误。");
                        Require(tooltip.transform.Find("Details").GetComponent<Text>().text.Contains("部位：躯干"), "大图说明应明确标注部位。");
                        Capture(1920, 1080, "torso-hover"); Capture(1024, 768, "torso-hover");
                        Exit(torso.gameObject); Next(); break;
                    case 3:
                        Require(!tooltip.gameObject.activeSelf, "移出部位后应隐藏大图。");
                        Click(hud, "BrainCard"); Require(hud.IsChoosing, "脑机应进入选牌阶段。");
                        Require(slots[0].DisplayedCard == null, "选牌待确认时脑机槽应为空。");
                        var fields = new SerializedObject(hud);
                        ((Button)fields.FindProperty("cancelChoiceButton").objectReferenceValue).onClick.Invoke();
                        Require(!hud.IsChoosing && slots[0].DisplayedCard == null, "取消脑机不应装备或消耗。");
                        Click(hud, "BrainCard");
                        var choices = (RectTransform)fields.FindProperty("choicesRoot").objectReferenceValue;
                        choices.GetComponentsInChildren<CardView>().First().GetComponent<Button>().onClick.Invoke();
                        Require(slots[0].DisplayedCard != null, "脑机确认后应填入。");
                        Click(hud, "HandsCard"); Click(hud, "LegsCard");
                        Require(slots.All(slot => slot.DisplayedCard != null), "四个部位未完整填入。");
                        Next(); break;
                    case 4:
                        foreach (var slot in slots)
                        {
                            AssertRaycast(slot.gameObject); Enter(slot.gameObject);
                            Require(tooltip.Owner == slot && tooltip.DisplayedCard == slot.DisplayedCard, "部位切换显示了错误大图。");
                            Exit(slot.gameObject);
                        }
                        Enter(slots[2].gameObject); Next(); break;
                    case 5:
                        Capture(1920, 1080, "all-slots-hands-hover"); Capture(1024, 768, "all-slots-hands-hover");
                        Exit(torso.gameObject);
                        Require(tooltip.IsVisible && tooltip.Owner == slots[2], "旧部位的移出事件不应关闭新部位详情。");
                        hud.enabled = false;
                        Require(!tooltip.IsVisible && slots.All(slot => slot.DisplayedCard == null), "HUD 禁用应清空槽并关闭说明。");
                        hud.enabled = true; Require(slots.All(slot => slot.DisplayedCard != null), "HUD 重新启用应恢复本场已使用的义体。");
                        Require(hud.Battle.EndTurn() && slots.All(slot => slot.DisplayedCard != null), "新回合应保留本场装备显示。");
                        while (torso.DisplayedCard.Source.TryConsumeDurability()) { }
                        hud.ShowCybernetic(); Enter(torso.gameObject);
                        Require(tooltip.transform.Find("Details").GetComponent<Text>().text.Contains("耐久 0/5") && tooltip.transform.Find("Details").GetComponent<Text>().text.Contains("已报废"), "耐久变更未刷新悬停说明。");
                        var oldBattle = hud.Battle;
                        Require(UnityEngine.Object.FindObjectOfType<GameBootstrap>().StartNewRun(), "新冒险启动失败。");
                        Require(slots.All(slot => slot.DisplayedCard == null) && !tooltip.IsVisible, "新战斗应重新清空装备栏和说明。");
                        oldBattle.EndTurn(); Require(slots.All(slot => slot.DisplayedCard == null), "旧战斗事件不应影响新装备栏。");
                        Next(); break;
                    case 6:
                        Finish(true, "初始空装备栏、四部位卡面/名称/耐久、脑机取消与确认、悬停大图和描述、射线命中、快速切换、HUD 禁用恢复、跨回合保留、报废耐久刷新及新战斗清空验证通过。"); break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(false, exception.ToString()); }
        }
        /// <summary>模拟指定界面按钮的点击事件。</summary>
        private static void Click(BattleHUD hud, string id)
        {
            var deck = hud.Battle.Context.Cybernetic;
            var card = deck.Hand.Cards.FirstOrDefault(value => value.Data.cardId == id);
            if (card == null)
            {
                card = deck.DrawPile.Cards.First(value => value.Data.cardId == id);
                Require(deck.TakeFromDrawPile(card), "测试义体入手失败。");
            }
            hud.ShowCybernetic(); hud.GetComponentsInChildren<CardView>().Single(view => view.Instance == card).GetComponent<Button>().onClick.Invoke();
        }
        /// <summary>向指定对象发送鼠标进入事件，模拟悬停操作。</summary>
        private static void Enter(GameObject target) => ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        /// <summary>向指定对象发送鼠标离开事件，模拟离开触发区域。</summary>
        private static void Exit(GameObject target) => ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        /// <summary>安排下一验证步骤，留出帧间隔使界面和战斗状态完成更新。</summary>
        private static void Next() { stage++; nextFrame = Time.frameCount + 3; nextStep = EditorApplication.timeSinceStartup + 0.3; }
        /// <summary>检查验证条件，不满足时抛出包含原因的异常。</summary>
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        /// <summary>检查指定屏幕位置的射线结果，确认目标控件可接收鼠标事件。</summary>
        private static void AssertRaycast(GameObject target)
        {
            Canvas.ForceUpdateCanvases(); var rect = target.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(Camera.main, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && (hits[0].gameObject == target || hits[0].gameObject.transform.IsChildOf(target.transform)), "装备槽无法接收悬停射线。");
        }
        /// <summary>结束验证并记录结果，解除更新订阅及恢复编辑器状态。</summary>
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(PendingKey, false);
            Directory.CreateDirectory("Logs/Equipment"); File.WriteAllText("Logs/Equipment/play-mode-validation.txt", (success ? "PASS\n" : "FAIL\n") + message);
            Debug.Log(message); EditorApplication.isPlaying = false;
        }
        /// <summary>导出当前界面截图，供人工检查布局与交互结果。</summary>
        private static void Capture(int width, int height, string name)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(width, height, 24); var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; Canvas.ForceUpdateCanvases();
                UnityEngine.Object.FindObjectOfType<BattleHUD>().SendMessage("LateUpdate");
                var tooltip = UnityEngine.Object.FindObjectOfType<CyberneticEquipmentTooltip>();
                if (tooltip) tooltip.SendMessage("LateUpdate");
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs/Equipment"); File.WriteAllBytes("Logs/Equipment/" + name + "-" + width + "x" + height + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
