using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpotlightGameJam.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证卡牌统一长按：延迟圆环、放大预览、点击抑制、只读牌区及取消清理。</summary>
    public sealed class CardHoldPreviewFlowTests
    {
        private float originalTimeScale;
        private bool originalBackground;
        private BattleHUD hud;
        private GameObject extra;

        /// <summary>校验回调不可添加运行时组件；真正启用卡牌后仍自动安装长按预览。</summary>
        [UnityTest]
        public IEnumerator EditorValidationDoesNotInstallRuntimeHoldComponents()
        {
            extra = new GameObject("ValidationOnlyCard", typeof(RectTransform)); extra.SetActive(false);
            var view = extra.AddComponent<CardView>();
#if UNITY_EDITOR
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>("Assets/GameData/Cards/AttackCard.asset");
            typeof(CardView).GetField("cardData", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, data);
#endif
            typeof(CardView).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            Assert.That(view.GetComponent<CardHoldPreview>(), Is.Null, "OnValidate 不应安装运行时组件。");
            extra.SetActive(true); yield return null;
            Assert.That(view.GetComponent<CardHoldPreview>(), Is.Not.Null, "OnEnable 应正常初始化长按预览。");
        }

        /// <summary>加载真实战斗并隔离商店测试入口，验证过程不保存场景或资源。</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalTimeScale = Time.timeScale; originalBackground = Application.runInBackground;
            Time.timeScale = 1; Application.runInBackground = true;
            SceneManager.sceneLoaded += DisableShopEntry;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; hud = Object.FindObjectOfType<BattleHUD>(); Assert.That(hud.Battle, Is.Not.Null);
        }

        /// <summary>还原暂停和后台设置，释放临时卡牌及场景监听。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableShopEntry;
            if (extra) Object.Destroy(extra);
            Time.timeScale = originalTimeScale; Application.runInBackground = originalBackground;
            yield return null;
        }

        /// <summary>阻止旧测试入口发币或遮挡当前测试中的卡牌。</summary>
        private static void DisableShopEntry(Scene scene, LoadSceneMode mode)
        {
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPrototypeTestEntry>(true))) entry.enabled = false;
        }

        /// <summary>读取公共预览状态；使用脚本名可在功能尚未接入前得到明确的失败断言。</summary>
        private static Component Hold(CardView view)
        {
            var hold = view.GetComponent("CardHoldPreview"); Assert.That(hold, Is.Not.Null, "CardView 应自动接入共用长按预览。"); return hold;
        }

        /// <summary>读取共用组件对外公开的预览状态，避免依赖内部计时字段。</summary>
        private static bool IsVisible(Component hold) => (bool)hold.GetType().GetProperty("IsPreviewVisible").GetValue(hold);

        /// <summary>创建同一主指针，用于模拟短按、长按和滚动取消。</summary>
        private static PointerEventData Pointer() => new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left, pointerId = -1, eligibleForClick = true };

        /// <summary>长按必须先显示进度再放大，暂停时仍推进；长按不触发选牌，下一次短按只触发一次。</summary>
        [UnityTest]
        public IEnumerator EveryCardViewSupportsDelayedHoldAndSuppressesLongClick()
        {
            var template = hud.GetComponentsInChildren<CardView>().First();
            extra = Object.Instantiate(template.gameObject, hud.GetComponentInParent<Canvas>().transform);
            var view = extra.GetComponent<CardView>(); view.Bind(template.Instance); view.SetInteractable(true);
            int selected = 0; view.Selected += _ => selected++;
            var hold = Hold(view); var pointer = Pointer(); Time.timeScale = 0;
            ExecuteEvents.Execute(extra, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(extra.GetComponentInChildren<HoldProgressRing>(), Is.Null);
            yield return new WaitForSecondsRealtime(.17f);
            var ring = extra.GetComponentInChildren<HoldProgressRing>(); Assert.That(ring, Is.Not.Null);
            Assert.That(ring.Progress, Is.GreaterThan(0).And.LessThan(1)); Assert.That(IsVisible(hold), Is.False);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(IsVisible(hold), Is.True); Assert.That(ring.gameObject.activeSelf, Is.False);
            ExecuteEvents.Execute(extra, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(IsVisible(hold), Is.False); Assert.That(pointer.eligibleForClick, Is.False);
            view.GetComponent<Button>().onClick.Invoke(); Assert.That(selected, Is.Zero);
            pointer = Pointer(); ExecuteEvents.Execute(extra, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(extra, pointer, ExecuteEvents.pointerUpHandler);
            view.GetComponent<Button>().onClick.Invoke(); Assert.That(selected, Is.EqualTo(1));
        }

        /// <summary>只读牌堆卡仍接收长按，但不出牌、不移牌；拖动、离开和停用均取消预览。</summary>
        [UnityTest]
        public IEnumerator ReadOnlyPileCardsAllowPreviewAndCancelOnScrollExitOrDisable()
        {
            hud.ShowDrawPile(0);
            // 激活后的首帧才完成 Graphic 注册与遮罩更新，射线必须在真实可显示帧验证。
            yield return null;
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true); Assert.That(panel.IsVisible, Is.True);
            var view = panel.GetComponentsInChildren<CardView>().First(); var hold = Hold(view);
            Assert.That(view.GetComponent<Button>().interactable, Is.False);
            Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(view.gameObject), Is.EqualTo(panel.GetComponentInChildren<ScrollRect>().gameObject));
            Canvas.ForceUpdateCanvases();
            var canvas = view.GetComponentInParent<Canvas>();
            var hitPointer = Pointer();
            hitPointer.position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                ((RectTransform)view.transform).TransformPoint(((RectTransform)view.transform).rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(hitPointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject), Is.EqualTo(view.gameObject));
            var context = hud.Battle.Context;
            var before = context.Ordinary.GetDrawPileOrder().Select(card => card.Id).ToArray();
            var pointer = Pointer(); ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            pointer.dragging = true; yield return new WaitForSecondsRealtime(.5f);
            Assert.That(IsVisible(hold), Is.False);
            pointer = Pointer(); ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(hold), Is.True);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerExitHandler); Assert.That(IsVisible(hold), Is.False);
            pointer = Pointer(); ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(hold), Is.True);
            panel.Hide(0); Assert.That(IsVisible(hold), Is.False);
            Assert.That(context.Ordinary.GetDrawPileOrder().Select(card => card.Id), Is.EqualTo(before));
            Assert.That(hud.PendingCard, Is.Null);
        }

        /// <summary>真实手牌长按不出牌，幻痛等锁定牌仍可预览，并且不会改变牌区和行动资源。</summary>
        [UnityTest]
        public IEnumerator BattleHandAndLockedCardsPreviewWithoutPlaying()
        {
            var context = hud.Battle.Context; var view = hud.GetComponentsInChildren<CardView>().First();
            var hold = Hold(view); var pointer = Pointer();
            var hand = context.Ordinary.Hand.Cards.ToArray(); int sanity = context.Player.Sanity, ap = context.ActionPoints.Total;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(hold), Is.True);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerUpHandler); view.GetComponent<Button>().onClick.Invoke();
            Assert.That(context.Ordinary.Hand.Cards, Is.EqualTo(hand)); Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
            Assert.That(context.ActionPoints.Total, Is.EqualTo(ap)); Assert.That(hud.PendingCard, Is.Null);
            context.Player.TrySpendSanity(context.Player.Sanity); hud.RefreshDisplay();
            var phantom = hud.GetComponentsInChildren<CardView>().Single(card => card.Data.locksInHand);
            var phantomHold = Hold(phantom); Assert.That(phantom.GetComponent<Button>().interactable, Is.False);
            pointer = Pointer(); ExecuteEvents.Execute(phantom.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(phantomHold), Is.True);
            ExecuteEvents.Execute(phantom.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(IsVisible(phantomHold), Is.False); Assert.That(context.HasPhantomPain, Is.True);
        }

        /// <summary>义体栏不再悬停即放大；长按显示同一公共大图，新战斗重绑立即关闭并清空旧牌。</summary>
        [UnityTest]
        public IEnumerator EquipmentRowsShareHoldPreviewAndNewBattleClearsIt()
        {
            var context = hud.Battle.Context;
            var torso = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards).First(card => card.Source.Data.slot == CyberneticSlot.Torso);
            if (!context.Cybernetic.Hand.Contains(torso)) context.Cybernetic.TakeFromDrawPile(torso);
            Assert.That(hud.Battle.TryPlay(torso).Success, Is.True);
            var slot = hud.GetComponentsInChildren<CyberneticEquipmentSlot>().Single(value => value.Slot == CyberneticSlot.Torso);
            var icon = slot.transform.Find("CardFace").GetComponent<Image>();
            Assert.That(icon, Is.Not.Null, "义体栏应显示部件图标，而不是卡面缩略图。");
            Assert.That(icon.sprite, Is.EqualTo(torso.Source.Data.icon));
            Assert.That(icon.preserveAspect, Is.True);
            Assert.That(slot.GetComponentsInChildren<CardView>(true), Is.Empty);
            var hold = slot.GetComponent<CardHoldPreview>(); Assert.That(hold, Is.Not.Null);
            ExecuteEvents.Execute(slot.gameObject, Pointer(), ExecuteEvents.pointerEnterHandler);
            Assert.That(hold.IsPreviewVisible, Is.False);
            var pointer = Pointer(); ExecuteEvents.Execute(slot.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(hold.IsPreviewVisible, Is.True);
            Assert.That(hud.GetComponentInParent<Canvas>().rootCanvas.GetComponentsInChildren<CardPreviewOverlay>(true).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectOfType<GameBootstrap>().StartNewRun(), Is.True);
            Assert.That(hold.IsPreviewVisible, Is.False); Assert.That(slot.DisplayedCard, Is.Null);
            Assert.That(icon.sprite, Is.Null); Assert.That(icon.enabled, Is.False);
        }

        /// <summary>开始界面只读卡面可长按，换部位关闭旧图；放大层保持比例且不拦截鼠标。</summary>
        [UnityTest]
        public IEnumerator StartPreviewCanHoldAndSwitchingPartClosesOldCard()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/StartScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var controller = Object.FindObjectOfType<StartSceneController>(); controller.SelectPart(CyberneticSlot.Brain);
            var root = (RectTransform)controller.GetType().GetField("cardPreviewRoot", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            var view = root.GetComponentInChildren<CardView>(); var hold = Hold(view); var pointer = Pointer();
            Assert.That(view.GetComponent<Image>().raycastTarget, Is.True); Assert.That(view.GetComponent<Button>().interactable, Is.False);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(hold), Is.True);
            var overlay = Object.FindObjectOfType<CardPreviewOverlay>(); Assert.That(overlay.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            var face = (RectTransform)overlay.transform.Find("CardFace");
            Assert.That(face.GetComponent<Image>().preserveAspect, Is.True);
            Assert.That(face.rect.width, Is.LessThan(((RectTransform)overlay.transform).rect.width));
            Assert.That(face.rect.height, Is.LessThan(((RectTransform)overlay.transform).rect.height));
            controller.SelectPart(CyberneticSlot.Torso); Assert.That(overlay.IsVisible, Is.False);
        }

        /// <summary>重排候选卡长按不选牌；松开后下一次短按才把卡牌移入待确认顺序，过程不扣费。</summary>
        [UnityTest]
        public IEnumerator ReorderCandidatesPreviewWithoutChangingOrderOrPayingAgain()
        {
            var context = hud.Battle.Context;
#if UNITY_EDITOR
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/NewWorldBrainCard.asset");
            var acquired = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards).First(card => card.Source.Data == data);
            if (!context.Cybernetic.Hand.Contains(acquired)) Assert.That(context.Cybernetic.TakeFromDrawPile(acquired), Is.True);
            hud.SelectCard(acquired);
#endif
            var panel = hud.GetComponentInChildren<DrawPilePanel>(true); Assert.That(panel.IsReordering, Is.True);
            var view = panel.GetComponentsInChildren<CardView>().First(); var hold = Hold(view); var pointer = Pointer();
            var before = context.Ordinary.GetDrawPileOrder().ToArray(); int sanity = context.Player.Sanity;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f); Assert.That(IsVisible(hold), Is.True);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerUpHandler); view.GetComponent<Button>().onClick.Invoke();
            Assert.That(context.Ordinary.GetDrawPileOrder(), Is.EqualTo(before)); Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
            pointer = Pointer(); ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.pointerUpHandler); view.GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.DisplayedCards, Is.EqualTo(before)); Assert.That(context.Player.Sanity, Is.EqualTo(sanity));
            panel.Close(); Assert.That(panel.IsReordering, Is.False);
        }
    }
}
