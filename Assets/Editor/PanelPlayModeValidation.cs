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
    /// <summary>验证只读牌堆接口、页签、动画、真实点击、滚动及战斗生命周期，不保存运行时修改。</summary>
    [InitializeOnLoad]
    public static class PanelPlayModeValidation
    {
        private const string PendingKey = "Spotlight.PanelPlayValidation";
        private static int stage, frames, nextFrame;
        private static double deadline, nextStep;
        private static float originalTimeScale = 1;
        private static readonly List<string> results = new List<string>();
        /// <summary>域重载后继续本次验证，不在正常编辑时自动进入播放。</summary>
        static PanelPlayModeValidation() { if (SessionState.GetBool(PendingKey, false)) StartWaiting(); }
        /// <summary>菜单启动真实场景验证，结束后退出播放并恢复时间倍率。</summary>
        [MenuItem("Spotlight/Panels/Validate Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请从编辑模式启动验证。");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/BattleScene.unity") throw new InvalidOperationException("请先打开 BattleScene。");
            SessionState.SetBool(PendingKey,true); StartWaiting(); EditorApplication.isPlaying=true;
        }
        /// <summary>只订阅一次更新，设定超时以避免验证任务遗留。</summary>
        private static void StartWaiting()
        {
            stage=frames=nextFrame=0; nextStep=0; results.Clear(); deadline=EditorApplication.timeSinceStartup+90;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        }
        /// <summary>逐帧推进实际 UI 验证，动画使用非缩放时间。</summary>
        private static void Tick()
        {
            if(EditorApplication.timeSinceStartup>deadline){Finish(false,"验证超时");return;}
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<nextStep||Time.frameCount<nextFrame)return;
            var hud=UnityEngine.Object.FindObjectOfType<BattleHUD>(); if(!hud||hud.Battle==null||++frames<4)return;
            try
            {
                var draw=UnityEngine.Object.FindObjectOfType<DrawPilePanel>(true);
                var discard=UnityEngine.Object.FindObjectOfType<DiscardPanel>(true);
                Require(draw&&discard,"两个面板均存在");
                var context=hud.Battle.Context; var refs=hud.GetComponent<BattleSceneReferences>();
                switch(stage)
                {
                    case 0:
                        originalTimeScale=Time.timeScale;Time.timeScale=0;
                        Require(!draw.IsVisible&&!discard.IsVisible,"面板初始隐藏");
                        foreach(var button in new[]{refs.drawPileButton,refs.discardPileButton})
                        {
                            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
                            Click(button.gameObject);
                        }
                        Require(!draw.IsVisible&&!discard.IsVisible,"悬停和点击牌堆不打开面板");
                        hud.ShowDrawPile();Require(draw.IsVisible&&draw.DisplayedCards.SequenceEqual(context.Ordinary.Peek(int.MaxValue)),"显式打开且堆顶优先展示");
                        Next(.3);break;
                    case 1:
                        Require(Mathf.Approximately(draw.GetComponent<CanvasGroup>().alpha,1),"暂停时淡入正常完成");
                        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})Capture(draw,size.x,size.y);
                        var sanity=context.Player.Sanity;var ap=context.ActionPoints.Total;var original=context.Ordinary.DrawPile.Cards.ToArray();
                        Click(draw.transform.Find("CyberneticTab").gameObject);
                        Require(draw.DisplayedCards.SequenceEqual(context.Cybernetic.Peek(int.MaxValue)),"义体页签展示独立牌堆");
                        Click(draw.transform.Find("OrdinaryTab").gameObject);
                        foreach(var view in draw.GetComponentsInChildren<CardView>())Click(view.gameObject);
                        Require(context.Player.Sanity==sanity&&context.ActionPoints.Total==ap&&context.Ordinary.DrawPile.Cards.SequenceEqual(original),"浏览、切页和点击预览不扣资源或移动牌");
                        hud.ShowDiscardPile(0);Require(!draw.IsVisible&&discard.IsVisible&&discard.DisplayedCards.Count==0,"面板互斥及空弃牌状态");
                        discard.Hide(.2f);Require(!discard.GetComponent<CanvasGroup>().blocksRaycasts,"淡出开始即停止射线");discard.Show(0);
                        Next(.3);break;
                    case 2:
                        Require(discard.IsVisible,"旧淡出回调不关闭重新打开的面板");
                        context.Ordinary.Draw(9);
                        var defense=context.Ordinary.Hand.Cards.First(card=>card.Data.cardId=="DefenseCard");
                        Require(hud.Battle.TryPlay(defense).Success,"正常出牌产生普通弃牌");
                        var torso=context.Cybernetic.Hand.Cards.FirstOrDefault(card=>card.Data.cardId=="TorsoCard");
                        if(torso==null){torso=context.Cybernetic.DrawPile.Cards.First(card=>card.Data.cardId=="TorsoCard");Require(context.Cybernetic.TakeFromDrawPile(torso),"准备义体测试手牌");}
                        Require(hud.Battle.TryPlay(torso).Success,"正常出牌产生义体弃牌");
                        Require(discard.DisplayedCards.SequenceEqual(context.Ordinary.DiscardPile.Cards.Reverse()),"打开期间实时更新普通弃牌");
                        Click(discard.transform.Find("CyberneticTab").gameObject);
                        Require(discard.DisplayedCards.SequenceEqual(context.Cybernetic.DiscardPile.Cards.Reverse()),"弃牌义体页签正确");
                        Click(discard.transform.Find("OrdinaryTab").gameObject);
                        foreach(var card in context.Ordinary.Hand.Cards.ToArray())context.Ordinary.Discard(card);
                        discard.RefreshCards();Next(.1);break;
                    case 3:
                        var scroll=discard.GetComponent<ScrollRect>();
                        Require(scroll.content.rect.height>scroll.viewport.rect.height,"多张牌形成滚动列表");
                        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1024,768)})Capture(discard,size.x,size.y);
                        scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1)});
                        Require(scroll.verticalNormalizedPosition<1,"鼠标滚轮可以浏览");
                        Require(hud.Battle.EndTurn(),"推进真实回合");
                        Require(discard.DisplayedCards.SequenceEqual(context.Ordinary.DiscardPile.Cards.Reverse()),"重新洗牌后预览更新");
                        Click(discard.transform.Find("CloseButton").gameObject);
                        Require(!discard.IsVisible&&!discard.GetComponent<CanvasGroup>().blocksRaycasts,"关闭按钮立即停止交互");
                        Next(.3);break;
                    case 4:
                        Require(!discard.gameObject.activeSelf,"淡出结束停用对象");
                        var old=hud.Battle;UnityEngine.Object.FindObjectOfType<GameBootstrap>().StartNewRun();
                        Require(!draw.IsVisible&&!discard.IsVisible,"新战斗关闭两个旧面板");hud.ShowDiscardPile(0);
                        old.Context.Cybernetic.Discard(old.Context.Cybernetic.Hand.Cards[0]);old.EndTurn();
                        Require(discard.DisplayedCards.Count==0,"旧战斗事件不影响新预览");
                        hud.enabled=false;Require(!draw.IsVisible&&!discard.IsVisible,"停用 HUD 关闭并解绑");
                        hud.enabled=true;hud.ShowDrawPile(0);Require(draw.IsVisible,"重新启用绑定当前战斗");
                        hud.ClosePilePanels(0);Require(!draw.gameObject.activeSelf&&!discard.gameObject.activeSelf,"统一关闭接口正确");
                        hud.ShowDrawPile(0);var current=hud.Battle.Context;
                        DamageResolver.Apply(current,current.Player,current.Enemies[0],20000);
                        Require(current.IsFinished&&!draw.IsVisible&&!discard.IsVisible,"战斗结束关闭面板");
                        Finish(true,"只读牌堆面板交互及生命周期验证通过");break;
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(false,e.ToString());}
        }
        /// <summary>发送左键点击，复用真实 Button 的可交互校验和监听。</summary>
        private static void Click(GameObject owner) => ExecuteEvents.Execute(owner,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        /// <summary>等待非缩放时间和实际游戏帧，不阻塞编辑器。</summary>
        private static void Next(double seconds){stage++;nextStep=EditorApplication.timeSinceStartup+seconds;nextFrame=Time.frameCount+2;}
        /// <summary>记录通过项，失败抛出精确说明。</summary>
        private static void Require(bool passed,string message){if(!passed)throw new InvalidOperationException(message);results.Add("PASS "+message);}
        /// <summary>导出不同分辨率并验证网格不会横向溢出。</summary>
        private static void Capture(PilePreviewPanel panel,int width,int height)
        {
            var camera=Camera.main;var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
            var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.aspect=(float)width/height;Canvas.ForceUpdateCanvases();
                panel.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);Canvas.ForceUpdateCanvases();
                var scroll=panel.GetComponent<ScrollRect>();var grid=scroll.content.GetComponent<GridLayoutGroup>();
                Require(grid.constraintCount*grid.cellSize.x+(grid.constraintCount-1)*grid.spacing.x+grid.padding.horizontal<=scroll.viewport.rect.width+1,"网格适应 "+width+"x"+height);
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                Directory.CreateDirectory("Logs/PilePanels");File.WriteAllBytes("Logs/PilePanels/"+panel.GetType().Name+"-"+width+"x"+height+".png",image.EncodeToPNG());
            }
            finally{camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);Canvas.ForceUpdateCanvases();}
        }
        /// <summary>清理验证回调、恢复时间倍率并退出播放，不保存演示状态。</summary>
        private static void Finish(bool passed,string message)
        {
            EditorApplication.update-=Tick;SessionState.SetBool(PendingKey,false);Time.timeScale=originalTimeScale;
            Directory.CreateDirectory("Logs/PilePanels");File.WriteAllLines("Logs/PilePanels/play-mode-validation.txt",new[]{passed?"PASS":"FAIL",message}.Concat(results));
            if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);else EditorApplication.isPlaying=false;
        }
    }
}
