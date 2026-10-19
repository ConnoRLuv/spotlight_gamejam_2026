using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpotlightGameJam.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>通过真实手牌和指针事件验证目标、取消与输入遮罩，不直接调用出牌接口。</summary>
    public sealed class HandCardDragTests
    {
        private BattleHUD hud;
        private bool background;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            SceneManager.sceneLoaded += DisableShop;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; hud = Object.FindObjectOfType<BattleHUD>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableShop;
            if (hud) hud.CancelSelection();
            Application.runInBackground = background;
            yield return null;
        }

        /// <summary>隔离测试商店，不更改场景资产。</summary>
        private static void DisableShop(Scene scene, LoadSceneMode mode)
        {
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPrototypeTestEntry>(true))) entry.enabled = false;
        }

        /// <summary>获取真实手牌展示，缺少目标牌时从抽牌堆移入。</summary>
        private CardView View(bool attack)
        {
            var deck = hud.Battle.Context.Ordinary;
            var card = deck.Hand.Cards.Concat(deck.DrawPile.Cards).First(value => !value.Data.locksInHand &&
                (value.Data.targetType == CardTargetType.SingleEnemy) == attack);
            if (!deck.Hand.Contains(card)) Assert.That(deck.TakeFromDrawPile(card), Is.True);
            hud.ShowOrdinary(); Canvas.ForceUpdateCanvases();
            return hud.GetComponent<BattleSceneReferences>().handArea.GetComponentsInChildren<CardView>().Single(value => value.Instance == card);
        }

        /// <summary>换算真实屏幕中心，兼容相机画布。</summary>
        private static Vector2 Center(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center));
        }

        /// <summary>发送正式指针事件，未安装拖拽入口时测试应明确失败。</summary>
        private static PointerEventData Begin(CardView view)
        {
            Assert.That(view.GetComponent<ReorderableDragItem>(), Is.Not.Null, "手牌尚未接入独立拖拽组件。");
            var canvas = view.GetComponentInParent<Canvas>().rootCanvas;
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -1, button = PointerEventData.InputButton.Left,
                position = Center((RectTransform)view.transform), eligibleForClick = true,
                pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() } };
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.beginDragHandler);
            Canvas.ForceUpdateCanvases();
            return pointer;
        }

        /// <summary>在指定位置松开，不能额外触发单击出牌。</summary>
        private static void Release(CardView view, PointerEventData pointer, Vector2 position)
        {
            pointer.position = position;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(pointer.eligibleForClick, Is.False);
        }

        /// <summary>攻击仅在敌人区域结算；错误位置和黑幕不能改变战斗资源。</summary>
        [UnityTest]
        public IEnumerator AttackOnlyPlaysOnEnemyAndInvalidDropRestoresEverything()
        {
            var view = View(true);
            // 刷新时旧卡在帧末销毁；先等待实际展示帧，再记录玩家能够抓取的布局索引。
            yield return null;
            var card = view.Instance; var parent = view.transform.parent;
            int index = view.transform.GetSiblingIndex(); var context = hud.Battle.Context;
            int ap = context.ActionPoints.Total, hp = context.Enemies[0].Health;
            var refs = hud.GetComponent<BattleSceneReferences>(); var pointer = Begin(view);
            // 新建 Graphic 的原生深度在渲染帧更新后才可供 GraphicRaycaster 使用。
            yield return null;
            Assert.That(hud.IsChoosing, Is.True, "拖动期间必须锁定其他战斗入口。");
            var backdrop = hud.transform.Find("HandCardDragModal"); Assert.That(backdrop, Is.Not.Null);
            var mask = backdrop.GetComponent<HandDragBackdrop>();
            var shadeCanvas = backdrop.GetComponent<Canvas>();
            File.WriteAllText("Temp/HandHighlight-layers.txt", "shade=" + shadeCanvas.sortingOrder + " override=" + shadeCanvas.overrideSorting +
                " graphic=" + mask.canvas.name + ":" + mask.canvas.sortingOrder + " target=" + refs.enemyPortrait.canvas.sortingOrder);
            Assert.That(refs.enemyPortrait.canvas.sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Assert.That(refs.playerPortrait.canvas.sortingOrder, Is.LessThan(shadeCanvas.sortingOrder));
            Assert.That(refs.handArea.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Assert.That(refs.enemyHealthText.canvas.sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Assert.That(EventSystem.current.sendNavigationEvents, Is.False);
            Assert.That(refs.handArea.GetComponentInParent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(view.GetComponent<Image>().canvasRenderer.GetColor().a, Is.EqualTo(1).Within(.001f), "浮动卡保持原有亮度。");
            var hits = new System.Collections.Generic.List<RaycastResult>();
            pointer.position = Center((RectTransform)refs.endTurnButton.transform); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty, "黑幕应在实际渲染后接管事件射线。");
            Assert.That(hits.First().gameObject, Is.EqualTo(backdrop.gameObject));
            hud.EndTurn(); Assert.That(context.ActionPoints.Total, Is.EqualTo(ap));
            Release(view, pointer, Center(refs.playerPortrait.rectTransform));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.True);
            Assert.That(context.ActionPoints.Total, Is.EqualTo(ap)); Assert.That(context.Enemies[0].Health, Is.EqualTo(hp));
            Assert.That(view.transform.parent, Is.EqualTo(parent)); Assert.That(view.transform.GetSiblingIndex(), Is.EqualTo(index));
            Assert.That(hud.IsChoosing, Is.False);
            Assert.That(refs.handArea.GetComponentInParent<CanvasGroup>().interactable, Is.True);
            yield return null;
            Canvas.ForceUpdateCanvases(); hits.Clear(); pointer.position = Center((RectTransform)view.transform);
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Any(hit => hit.gameObject == view.gameObject), Is.True, "取消高亮后手牌必须继续接收真实指针。");
            pointer = Begin(view); Release(view, pointer, Center(refs.enemyPortrait.rectTransform));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.False);
            Assert.That(context.Enemies[0].Health, Is.LessThan(hp)); Assert.That(hud.PendingCard, Is.Null);
            yield return null;
        }

        /// <summary>自身牌不接受敌人目标；整张 HUD 停用后不遗留浮动卡或输入锁。</summary>
        [UnityTest]
        public IEnumerator SelfCardOnlyPlaysOnPlayerAndDisableCancelsDrag()
        {
            var view = View(false); var card = view.Instance; var context = hud.Battle.Context;
            var refs = hud.GetComponent<BattleSceneReferences>(); var pointer = Begin(view);
            var mask = hud.transform.Find("HandCardDragModal").GetComponent<HandDragBackdrop>();
            var shadeCanvas = mask.GetComponent<Canvas>();
            Assert.That(refs.playerPortrait.canvas.sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Assert.That(refs.enemyPortrait.canvas.sortingOrder, Is.LessThan(shadeCanvas.sortingOrder));
            Assert.That(refs.playerHealthText.canvas.sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Assert.That(refs.sanityText.canvas.sortingOrder, Is.GreaterThan(shadeCanvas.sortingOrder));
            Release(view, pointer, Center(refs.enemyPortrait.rectTransform));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.True);
            pointer = Begin(view); hud.gameObject.SetActive(false); yield return null; yield return null;
            hud.gameObject.SetActive(true); yield return null;
            Assert.That(hud.IsChoosing, Is.False);
            Assert.That(hud.transform.Find("HandCardDragModal").gameObject.activeSelf, Is.False);
            view = View(false); card = view.Instance;
            pointer = Begin(view); Release(view, pointer, Center(refs.playerPortrait.rectTransform));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.False);
            yield return null;
        }

        /// <summary>义体拖放仍复用排序流程，两个模态面板之间不发生输入状态覆盖。</summary>
        [UnityTest]
        public IEnumerator BrainDropOpensDrawPileAndModalLocksDoNotLeak()
        {
            var context = hud.Battle.Context;
            var brain = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards)
                .First(card => card.Source.Data.slot == CyberneticSlot.Brain);
            if (!context.Cybernetic.Hand.Contains(brain)) Assert.That(context.Cybernetic.TakeFromDrawPile(brain), Is.True);
            hud.ShowCybernetic(); Canvas.ForceUpdateCanvases();
            var refs = hud.GetComponent<BattleSceneReferences>();
            var view = refs.handArea.GetComponentsInChildren<CardView>().Single(card => card.Instance == brain);
            int sanity = context.Player.Sanity, durability = brain.Source.Durability;
            var pointer = Begin(view);
            Release(view, pointer, Center(refs.enemyPortrait.rectTransform));
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
            Assert.That(brain.Source.Durability, Is.EqualTo(durability));
            pointer = Begin(view); Release(view, pointer, Center(refs.playerPortrait.rectTransform));
            var drawPanel = hud.GetComponentInChildren<DrawPilePanel>(true);
            Assert.That(drawPanel.IsReordering, Is.True); Assert.That(hud.IsChoosing, Is.True);
            Assert.That(hud.transform.Find("HandCardDragModal").gameObject.activeSelf, Is.False);
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 4));
            Assert.That(brain.Source.Durability, Is.EqualTo(durability - 1));
            drawPanel.GetComponentsInChildren<Button>().Single(button => button.name == "CloseButton").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hud.IsChoosing, Is.False);
            Assert.That(refs.handArea.GetComponentInParent<CanvasGroup>().interactable, Is.True);
        }

        /// <summary>取消与切页保留手牌顺序，普通单击仍能选定攻击目标。</summary>
        [UnityTest]
        public IEnumerator CancelAndRefreshRestoreCardOrderAndLeaveShortClickAvailable()
        {
            var view = View(true); var context = hud.Battle.Context;
            var order = context.Ordinary.Hand.Cards.ToArray(); int ap = context.ActionPoints.Total;
            var parent = view.transform.parent; var position = ((RectTransform)view.transform).anchoredPosition;
            Begin(view); hud.GetComponent<HandCardDragController>().CancelDrag();
            Assert.That(hud.IsChoosing, Is.False);
            Assert.That(view.transform.parent, Is.EqualTo(parent));
            Assert.That(((RectTransform)view.transform).anchoredPosition, Is.EqualTo(position));
            Assert.That(context.Ordinary.Hand.Cards, Is.EqualTo(order)); Assert.That(context.ActionPoints.Total, Is.EqualTo(ap));
            Begin(view); hud.ShowCybernetic(); yield return null;
            Assert.That(hud.IsChoosing, Is.False);
            Assert.That(hud.transform.Find("HandCardDragModal").gameObject.activeSelf, Is.False);
            Assert.That(context.Ordinary.Hand.Cards, Is.EqualTo(order));
            view = View(true); view.GetComponent<Button>().onClick.Invoke();
            Assert.That(hud.PendingCard, Is.EqualTo(view.Instance), "短按应继续走原有选择目标流程。");
        }

        /// <summary>费用不足和报废来源均拒绝开始拖动；判断本身不支付、弃牌或修改使用次数。</summary>
        [UnityTest]
        public IEnumerator UnavailableCardsCannotStartDragAndChecksDoNotPayCosts()
        {
            var view = View(true); var card = view.Instance; var parent = view.transform.parent;
            var context = hud.Battle.Context;
            context.ActionPoints.TrySpend(context.ActionPoints.Total);
            Begin(view);
            Assert.That(hud.IsChoosing, Is.False); Assert.That(view.transform.parent, Is.EqualTo(parent));
            Assert.That(context.Ordinary.Hand.Contains(card), Is.True); Assert.That(context.ActionPoints.Total, Is.Zero);
            context.ActionPoints.BeginTurn(2);
            var brain = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards)
                .First(value => value.Source.Data.slot == CyberneticSlot.Brain);
            if (!context.Cybernetic.Hand.Contains(brain)) context.Cybernetic.TakeFromDrawPile(brain);
            hud.ShowCybernetic(); Canvas.ForceUpdateCanvases();
            view = hud.GetComponent<BattleSceneReferences>().handArea.GetComponentsInChildren<CardView>().Single(value => value.Instance == brain);
            context.Player.TrySpendSanity(context.Player.Sanity);
            int durability = brain.Source.Durability;
            Begin(view); Assert.That(hud.IsChoosing, Is.False);
            Assert.That(context.Player.Sanity, Is.Zero); Assert.That(brain.Source.Durability, Is.EqualTo(durability));
            context.Player.RestoreSanity(context.Player.MaxSanity);
            while (brain.Source.TryConsumeDurability()) { }
            Begin(view); Assert.That(hud.IsChoosing, Is.False);
            Assert.That(context.Cybernetic.Hand.Contains(brain), Is.True);
            Assert.That(context.Usage.GetUses(brain.Source.Data.slot), Is.Zero);
            yield return null;
        }

        /// <summary>从真实运行中的画布导出拖拽画面，覆盖宽屏与四比三布局，不保存场景。</summary>
        [UnityTest]
        public IEnumerator CaptureAttackAndSelfDragAtThreeResolutions()
        {
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
            {
                CaptureDrag(true, size);
                yield return null;
                if (size.x == 1920) CaptureRestored(size);
                CaptureDrag(false, size);
                yield return null;
            }
        }

        /// <summary>离屏渲染当前拖拽及归位画面，结束后恢复相机、输入和画布尺寸。</summary>
        private void CaptureDrag(bool attack, Vector2Int size)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active;
            float aspect = camera.aspect;
            var targetTexture = new RenderTexture(size.x, size.y, 24);
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = targetTexture; camera.aspect = (float)size.x / size.y;
                var scaler = hud.GetComponent<CanvasScaler>();
                if (scaler) scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                hud.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver); Canvas.ForceUpdateCanvases();
                var view = View(attack); var refs = hud.GetComponent<BattleSceneReferences>();
                var pointer = Begin(view);
                pointer.position = Center(attack ? refs.enemyPortrait.rectTransform : refs.playerPortrait.rectTransform) +
                    new Vector2(attack ? -160 : 160, 50);
                ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.dragHandler);
                hud.GetComponentInChildren<HandDragBackdrop>().SetVerticesDirty();
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = targetTexture;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                Directory.CreateDirectory("Temp/HandCardDrag");
                File.WriteAllBytes("Temp/HandCardDrag/" + (attack ? "attack-" : "self-") + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                hud.GetComponent<HandCardDragController>().CancelDrag();
                Assert.That(hud.IsChoosing, Is.False);
            }
            finally
            {
                if (hud) hud.GetComponent<HandCardDragController>().CancelDrag();
                camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active;
                targetTexture.Release(); Object.DestroyImmediate(targetTexture); Object.DestroyImmediate(image);
                Canvas.ForceUpdateCanvases();
            }
        }

        /// <summary>在归位后的实际展示帧导出截图，避免同帧重复相机渲染使用旧的 UI 批次。</summary>
        private void CaptureRestored(Vector2Int size)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active;
            float aspect = camera.aspect;
            var target = new RenderTexture(size.x, size.y, 24);
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                var scaler = hud.GetComponent<CanvasScaler>();
                if (scaler) scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                // 离屏目标切换不一定触发 Graphic 尺寸通知，重建图形后再采集，避免复用旧画布批次。
                foreach (var graphic in hud.GetComponentsInChildren<Graphic>()) graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                var portrait = hud.GetComponent<BattleSceneReferences>().playerPortrait;
                Assert.That(portrait.gameObject.activeInHierarchy && portrait.enabled && portrait.sprite, Is.True);
                Assert.That(portrait.canvasRenderer.GetAlpha(), Is.GreaterThan(0));
                File.WriteAllBytes("Temp/HandCardDrag/restored-1920x1080.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
