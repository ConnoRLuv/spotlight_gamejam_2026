using System.Collections;
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
    /// <summary>验证现有抽牌面板的模态拖拽、两套草稿、一次支付和生命周期恢复。</summary>
    public sealed class DrawPileReorderTests
    {
        private BattleHUD hud;
        private bool background;
        private float timeScale;

        /// <summary>加载真实战斗，隔离商店入口与探索会话，不保存运行时修改。</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = Application.runInBackground; timeScale = Time.timeScale;
            Application.runInBackground = true; Time.timeScale = 1;
            SceneManager.sceneLoaded += DisableShop;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            hud = Object.FindObjectOfType<BattleHUD>(); Assert.That(hud.Battle, Is.Not.Null);
        }

        /// <summary>释放本次模态交互和测试设置，避免给下一场景遗留输入遮挡。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableShop;
            if (hud) hud.ClosePilePanels(0);
            Application.runInBackground = background; Time.timeScale = timeScale;
            yield return null;
        }

        /// <summary>关闭测试商店的自动发币与输入控制，不影响正式战斗入口。</summary>
        private static void DisableShop(Scene scene, LoadSceneMode mode)
        {
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPrototypeTestEntry>(true))) entry.enabled = false;
        }

        /// <summary>把正式新世界脑机移入手牌，后续仍通过 HUD 的真实出牌入口使用。</summary>
        private CardInstance Brain()
        {
            var context = hud.Battle.Context;
            var brain = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards)
                .First(card => card.Source.Data.slot == CyberneticSlot.Brain);
            if (!context.Cybernetic.Hand.Contains(brain)) Assert.That(context.Cybernetic.TakeFromDrawPile(brain), Is.True);
            return brain;
        }

        /// <summary>用真实指针处理器拖动；坐标来自画布相机，兼容相机画布与覆盖画布。</summary>
        private static PointerEventData BeginDrag(RectTransform item)
        {
            var canvas = item.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, item.TransformPoint(item.rect.center));
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left, pointerId = -1, position = position,
                pressPosition = position, eligibleForClick = true, dragging = true,
                pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() }
            };
            ExecuteEvents.Execute(item.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(item.gameObject, pointer, ExecuteEvents.beginDragHandler);
            return pointer;
        }

        /// <summary>完成拖动并使目标位置进入同一个排序入口，不直接修改业务草稿。</summary>
        private static void DragTo(RectTransform item, RectTransform target)
        {
            var canvas = item.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var targetPosition = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center));
            var pointer = BeginDrag(item); pointer.position = targetPosition;
            ExecuteEvents.Execute(item.gameObject, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(item.gameObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(pointer.eligibleForClick, Is.False);
        }

        /// <summary>两个页签独立拖动，只有关闭时改真实顺序；费用、耐久与次数只在打开时结算。</summary>
        [UnityTest]
        public IEnumerator DragBothPilesAndCloseCommitsOnceWithBackdropBlockingBattle()
        {
            var context = hud.Battle.Context; var brain = Brain();
            int sanity = context.Player.Sanity, durability = brain.Source.Durability;
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true);
            var oldParent = panel.transform.parent;
            var ordinary = context.Ordinary.GetDrawPileOrder().ToList();
            var cybernetic = context.Cybernetic.GetDrawPileOrder().ToList();
            hud.SelectCard(brain); yield return null;
            Assert.That(panel.IsReordering && hud.IsChoosing, Is.True);
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 4));
            Assert.That(brain.Source.Durability, Is.EqualTo(durability - 1));
            Assert.That(context.Cybernetic.DiscardPile.Contains(brain), Is.True);
            var modal = panel.transform.parent.gameObject;
            var refs = hud.GetComponent<BattleSceneReferences>();
            Assert.That(refs.endTurnButton.interactable, Is.False);
            Assert.That(oldParent.GetComponent<CanvasGroup>().interactable, Is.False, "遮罩之外的键盘导航也应锁定。");
            var canvas = hud.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, refs.endTurnButton.transform.position) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.First().gameObject, Is.EqualTo(modal), "黑幕应拦截面板之外的操作。");
            var views = panel.GetComponentsInChildren<CardView>();
            DragTo((RectTransform)views[0].transform, (RectTransform)views[1].transform);
            var first = ordinary[0]; ordinary.RemoveAt(0); ordinary.Insert(1, first);
            Assert.That(panel.DisplayedCards, Is.EqualTo(ordinary));
            Assert.That(context.Ordinary.GetDrawPileOrder(), Is.Not.EqualTo(ordinary), "拖动不能提前改真实牌堆。");
            panel.ShowCybernetic(); yield return null;
            views = panel.GetComponentsInChildren<CardView>();
            DragTo((RectTransform)views[0].transform, (RectTransform)views[1].transform);
            first = cybernetic[0]; cybernetic.RemoveAt(0); cybernetic.Insert(1, first);
            Assert.That(panel.DisplayedCards, Is.EqualTo(cybernetic));
            panel.ShowOrdinary(); Assert.That(panel.DisplayedCards, Is.EqualTo(ordinary));
            panel.GetComponentsInChildren<Button>().Single(button => button.name == "CloseButton").onClick.Invoke();
            Assert.That(panel.IsReordering || hud.IsChoosing, Is.False);
            Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(ordinary));
            Assert.That(context.Cybernetic.GetDrawPileOrder(), Is.EqualTo(cybernetic));
            Assert.That(context.Player.Sanity, Is.EqualTo(sanity - 4)); Assert.That(brain.Source.Durability, Is.EqualTo(durability - 1));
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(panel.transform.parent, Is.EqualTo(oldParent)); Assert.That(modal.activeSelf, Is.False);
            Assert.That(refs.endTurnButton.interactable, Is.True);
            Assert.That(oldParent.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        /// <summary>战斗重绑时撤销正在拖动的浮层，释放黑幕，不让上一战草稿写入新战斗。</summary>
        [UnityTest]
        public IEnumerator RebindingBattleCancelsDragAndRestoresReadOnlyPanel()
        {
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true); var originalParent = panel.transform.parent;
            hud.SelectCard(Brain()); yield return null;
            var view = panel.GetComponentsInChildren<CardView>().First();
            BeginDrag((RectTransform)view.transform);
            var list = panel.GetComponentInChildren<ReorderableDragList>(true); Assert.That(list.IsDragging, Is.True);
            Assert.That(Object.FindObjectOfType<GameBootstrap>().StartNewRun(), Is.True);
            yield return null;
            Assert.That(list.IsDragging || panel.IsReordering || hud.IsChoosing, Is.False);
            Assert.That(panel.transform.parent, Is.EqualTo(originalParent));
            Assert.That(hud.GetComponentInParent<Canvas>().rootCanvas.GetComponentsInChildren<Image>()
                .Any(image => image.name == "DrawPileReorderModal" && image.gameObject.activeInHierarchy), Is.False);
            hud.ShowDrawPile(0); yield return null;
            Assert.That(panel.IsVisible, Is.True); Assert.That(panel.IsReordering, Is.False);
            view = panel.GetComponentsInChildren<CardView>().First();
            Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(view.gameObject), Is.EqualTo(panel.GetComponentInChildren<ScrollRect>().gameObject));
        }

        /// <summary>整个 HUD 停用时也要释放浮层和黑幕，重新启用后不残留输入锁或旧排序会话。</summary>
        [UnityTest]
        public IEnumerator DisablingHudDuringDragReleasesModalAndRestoresInput()
        {
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true); var originalParent = panel.transform.parent;
            hud.SelectCard(Brain()); yield return null;
            var modal = panel.transform.parent.gameObject;
            BeginDrag((RectTransform)panel.GetComponentsInChildren<CardView>().First().transform);
            hud.gameObject.SetActive(false); yield return null;
            Assert.That(panel.IsReordering, Is.False); Assert.That(modal.activeSelf, Is.False);
            Assert.That(originalParent.GetComponent<CanvasGroup>().interactable, Is.True);
            hud.gameObject.SetActive(true); yield return null;
            Assert.That(panel.IsVisible || hud.IsChoosing, Is.False);
            Assert.That(panel.transform.parent, Is.EqualTo(originalParent));
        }

        /// <summary>拖动未松开时切页应撤销本次拖动，不能把普通牌混入义体页或遗留浮动对象。</summary>
        [UnityTest]
        public IEnumerator SwitchingTabDuringDragCancelsOnlyTheUnfinishedDrag()
        {
            var context = hud.Battle.Context; var expected = context.Ordinary.GetDrawPileOrder().ToArray();
            hud.SelectCard(Brain()); yield return null;
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true);
            var views = panel.GetComponentsInChildren<CardView>();
            var pointer = BeginDrag((RectTransform)views[0].transform);
            var canvas = hud.GetComponentInParent<Canvas>().rootCanvas;
            pointer.position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, views[1].transform.position);
            ExecuteEvents.Execute(views[0].gameObject, pointer, ExecuteEvents.dragHandler);
            panel.ShowCybernetic(); yield return null;
            Assert.That(panel.IsCyberneticPage, Is.True);
            Assert.That(panel.transform.parent.Find("DragLayer").childCount, Is.Zero);
            panel.ShowOrdinary(); Assert.That(panel.DisplayedCards, Is.EqualTo(expected));
            panel.Close(); Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(expected));
        }

        /// <summary>拖动靠近下沿时用真实时间滚动，暂停时间倍率也能把卡牌拖往列表下方。</summary>
        [UnityTest]
        public IEnumerator DraggingNearViewportEdgeScrollsWhenTimeScaleIsZero()
        {
            hud.SelectCard(Brain()); yield return null;
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true);
            var scroll = panel.GetComponentInChildren<ScrollRect>(); scroll.verticalNormalizedPosition = 1;
            var view = panel.GetComponentsInChildren<CardView>().First();
            var pointer = BeginDrag((RectTransform)view.transform);
            var viewport = scroll.viewport; var canvas = hud.GetComponentInParent<Canvas>().rootCanvas;
            pointer.position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                viewport.TransformPoint(new Vector3(viewport.rect.center.x, viewport.rect.yMin + 2)));
            Time.timeScale = 0;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.dragHandler);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(.99f));
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.endDragHandler);
            Assert.That(scroll.vertical, Is.True); panel.Close();
        }
    }
}
