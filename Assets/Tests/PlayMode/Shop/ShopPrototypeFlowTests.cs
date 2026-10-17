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
    /// <summary>通过实际场景按钮验证商人对话、商店遮罩和运行时数据只读性。</summary>
    public sealed class ShopPrototypeFlowTests
    {
        private ShopPrototypeTestEntry entry;
        private DialoguePanel dialogue;
        private ShopPanel shop;
        private BattleHUD hud;
        private bool background;
        private float timeScale;

        /// <summary>加载真实 BattleScene，不改 Build Settings；记录临时验证所需的运行设置。</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            timeScale = Time.timeScale;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("BattleScene");
#endif
            yield return null;
            entry = Object.FindObjectOfType<ShopPrototypeTestEntry>();
            hud = Object.FindObjectOfType<BattleHUD>();
            dialogue = entry.GetComponentInChildren<DialoguePanel>(true);
            shop = entry.GetComponentInChildren<ShopPanel>(true);
            Assert.That(hud.Battle, Is.Not.Null);
            Assert.That(hud.Battle.Context.Run.Gold, Is.EqualTo(9999), "测试入口应在启动时提供 9999 塞勒币。");
            // 交易边界用例仍从明确的零余额开始，避免测试补币掩盖余额不足等校验。
            hud.Battle.Context.Run.TrySpendGold(hud.Battle.Context.Run.Gold);
        }

        /// <summary>还原临时设置和测试会话，不保存运行时场景或测试资源变化。</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (entry) entry.enabled = false;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            Time.timeScale = timeScale; Application.runInBackground = background;
            yield return null;
        }

        /// <summary>真实点击完整流程三次，确保淡出期间锁定输入且资源、牌区和耐久完全不变。</summary>
        [UnityTest]
        public IEnumerator DialogueToShopCanRepeatWithoutChangingBattle()
        {
            var before = Snapshot();
            var inputGroup = hud.transform.Find("BattleLayout_16x9").GetComponent<CanvasGroup>();
            for (int i = 0; i < 3; i++)
            {
                Click(hud.GetComponentsInChildren<Button>(true).Single(button => button.name == "TestShopButton"));
                Assert.That(entry.transform.GetSiblingIndex(), Is.GreaterThan(hud.transform.Find("BattleLayout_16x9").GetSiblingIndex()),
                    "商店流程父节点必须显示在战斗界面之上，不能只调整父节点内部的遮罩顺序。");
                Assert.That(entry.IsOpen && dialogue.IsVisible, Is.True);
                Assert.That(shop.IsVisible, Is.False);
                Assert.That(inputGroup.interactable, Is.False);
                Assert.That(hud.GetComponent<BattleSceneReferences>().endTurnButton.IsInteractable(), Is.False);
                Assert.That(dialogue.CurrentLineIndex, Is.Zero);
                Click(dialogue.transform.Find("Layout_16x9/DialogueBox/ContinueButton").GetComponent<Button>());
                Assert.That(dialogue.CurrentLineIndex, Is.EqualTo(1));
                Click(dialogue.transform.Find("Layout_16x9/DialogueBox/ContinueButton").GetComponent<Button>());
                Assert.That(dialogue.IsVisible, Is.False);
                Assert.That(shop.IsVisible, Is.True);
                yield return new WaitForSecondsRealtime(0.2f);
                Click(shop.transform.Find("Layout_16x9/ExitButton").GetComponent<Button>());
                Assert.That(inputGroup.interactable, Is.False, "淡出期间不能点击底层战斗。");
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(inputGroup.interactable, Is.True);
                Assert.That(entry.transform.Find("ModalRoot").gameObject.activeSelf, Is.False);
                Assert.That(Snapshot(), Is.EqualTo(before));
                yield return null;
            }
        }

        /// <summary>取消对话及停用组件均释放遮罩，且不会误打开商店。</summary>
        [UnityTest]
        public IEnumerator CancelAndDisableReleaseInputWithoutOpeningShop()
        {
            entry.Open(); dialogue.Cancel();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(shop.IsVisible || dialogue.IsVisible || entry.IsOpen, Is.False);
            entry.Open(); entry.enabled = false;
            Assert.That(entry.transform.Find("ModalRoot").gameObject.activeSelf, Is.False);
            Assert.That(hud.transform.Find("BattleLayout_16x9").GetComponent<CanvasGroup>().interactable, Is.True);
            yield return null;
        }

        /// <summary>打开前关闭牌堆，正在选择卡牌时拒绝打开；停用未打开的入口也不能影响战斗输入。</summary>
        [UnityTest]
        public IEnumerator OpeningRespectsOtherModalWindows()
        {
            entry.enabled = false;
            var group = hud.transform.Find("BattleLayout_16x9").GetComponent<CanvasGroup>();
            Assert.That(group.interactable, Is.True);
            entry.enabled = true;
            hud.ShowDrawPile(0); entry.Open();
            Assert.That(hud.GetComponentInChildren<DrawPilePanel>(true).IsVisible, Is.False);
            entry.Close(); yield return new WaitForSecondsRealtime(0.2f);
            var choice = (GameObject)typeof(BattleHUD).GetField("choicePanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(hud);
            choice.SetActive(true); entry.Open();
            Assert.That(entry.IsOpen, Is.False);
            choice.SetActive(false);
        }

        /// <summary>验证共享耐久实时刷新、隐藏后退订和切换探索时清空旧展示。</summary>
        [UnityTest]
        public IEnumerator RepairSlotsRefreshSharedDurabilityAndReleaseOldRun()
        {
            var run = new RunState(hud.Battle.Context.Rules);
            var data = hud.Battle.Context.Run.Loadout.Owned.First(card => card.Data.slot == CyberneticSlot.Brain).Data;
            run.Loadout.AddInitial(data);
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            shop.Bind(run);
            var slot = shop.transform.Find("Layout_16x9/RepairSlot_脑机");
            var preview = slot.Find("RepairButton").GetComponent<ShopRepairHoldPreview>();
            var durability = slot.Find("Durability").GetComponent<Text>();
            var source = run.Loadout.Owned[1];
            while (source.TryConsumeDurability()) { }
            Assert.That(preview.DisplayedCard, Is.SameAs(source.Card));
            Assert.That(durability.text, Is.EqualTo("耐久 0/" + data.maxDurability));
            shop.Hide(0); source.Repair(2);
            Assert.That(durability.text, Is.EqualTo("耐久 0/" + data.maxDurability), "隐藏后应解除耐久订阅。");
            shop.Show(0);
            Assert.That(durability.text, Is.EqualTo("耐久 2/" + data.maxDurability));
            source.TryConsumeDurability();
            Assert.That(durability.text, Is.EqualTo("耐久 1/" + data.maxDurability));
            shop.Bind(new RunState(hud.Battle.Context.Rules));
            source.Repair(1);
            Assert.That(preview.DisplayedCard, Is.Null);
            Assert.That(durability.text, Is.Empty);
            Assert.That(slot.Find("RepairButton").gameObject.activeSelf, Is.False);
            entry.Close(); yield return new WaitForSecondsRealtime(0.2f);
        }

        /// <summary>实际购买和修复按钮只扣一次费用，顶部工具栏同步余额，当前牌区保持不变。</summary>
        [UnityTest]
        public IEnumerator PurchaseAndRepairButtonsChargeOnceAndUpdateToolbar()
        {
            var context = hud.Battle.Context; var run = context.Run;
            CyberneticData original = null, product = null;
#if UNITY_EDITOR
            original = UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/BrainCard.asset");
            product = UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/PrometheusBrainCard.asset");
#else
            yield break;
#endif
            run.Loadout.AddInitial(original);
            var source = run.Loadout.Owned.First(card => card.Data == original);
            source.TryConsumeDurability(); source.TryConsumeDurability();
            run.AddGold(1300);
            var zones = context.Cybernetic.DrawPile.Cards.Concat(context.Cybernetic.Hand.Cards).Concat(context.Cybernetic.DiscardPile.Cards).Select(card => card.Id).ToArray();
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            // 真实鼠标射线只在淡入完成、Canvas 已生成图形后验证；立即调用按钮不能覆盖这个时序。
            yield return new WaitForSecondsRealtime(0.2f);
            var buy = shop.transform.Find("Layout_16x9/ProductSlot_0/BuyButton").GetComponent<Button>();
            Click(buy);
            Assert.That(run.Gold, Is.EqualTo(300));
            Assert.That(run.Loadout.Owned.Count(card => card.Data == product), Is.EqualTo(1));
            Assert.That(run.Loadout.Owned.Count(card => card.Data == original), Is.EqualTo(original.cards.Length));
            Assert.That(buy.IsInteractable(), Is.False);
            var slot = shop.transform.Find("Layout_16x9/RepairSlot_脑机");
            var repair = slot.Find("RepairButton").GetComponent<Button>();
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(Camera.main, repair.transform.position)
            };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.EqualTo(repair.gameObject), "维修框必须能实际接收点击，不能被装饰文字或战斗画面盖住。");
            Click(repair);
            Assert.That(run.Gold, Is.EqualTo(150));
            Assert.That(run.Loadout.Owned.Where(card => card.Data == original).Select(card => card.Durability), Is.All.EqualTo(original.maxDurability - 1));
            Click(slot.Find("CycleButton").GetComponent<Button>());
            Assert.That(repair.GetComponent<ShopRepairHoldPreview>().DisplayedCard, Is.SameAs(product.cards[0]));
            Assert.That(repair.IsInteractable(), Is.False, "满耐久不能收费修复。");
            var purchased = run.Loadout.Owned.Single(card => card.Data == product); purchased.TryConsumeDurability();
            Assert.That(repair.IsInteractable(), Is.False, "余额不足不能修复。");
            run.AddGold(150);
            Assert.That(hud.GetComponent<BattleSceneReferences>().goldText.text, Is.EqualTo("塞勒币 300"));
            Click(repair);
            Assert.That(run.Gold, Is.Zero); Assert.That(purchased.Durability, Is.EqualTo(product.maxDurability));
            Assert.That(hud.GetComponent<BattleSceneReferences>().goldText.text, Is.EqualTo("塞勒币 0"));
            Assert.That(context.Cybernetic.DrawPile.Cards.Concat(context.Cybernetic.Hand.Cards).Concat(context.Cybernetic.DiscardPile.Cards).Select(card => card.Id), Is.EqualTo(zones));
            Assert.That(run.Loadout.GetUsableCards().Count(card => card.Source == purchased), Is.EqualTo(1), "下场战斗生成牌组包含购买的唯一新牌。");
            entry.Close(); yield return new WaitForSecondsRealtime(0.2f);
        }

        /// <summary>已有商品仍可买一次，同店重开保持售罄，进入下一商店才恢复按钮。</summary>
        [UnityTest]
        public IEnumerator OwnedProductStockPersistsAcrossReopenAndResetsAtNextShop()
        {
            var run = hud.Battle.Context.Run; run.AddGold(4000); run.Shop.Enter("shop-a");
            var data = run.Loadout.Owned.First(source => source.Data.slot == CyberneticSlot.Brain).Data;
            int count = run.Loadout.Owned.Count(source => source.Data == data);
            var zones = hud.Battle.Context.Cybernetic.DrawPile.Cards.Concat(hud.Battle.Context.Cybernetic.Hand.Cards)
                .Concat(hud.Battle.Context.Cybernetic.DiscardPile.Cards).Select(card => card.Id).ToArray();
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            var buy = shop.transform.Find("Layout_16x9/ProductSlot_0/BuyButton").GetComponent<Button>();
            Assert.That(buy.GetComponentInChildren<Text>().text, Is.EqualTo("购买 ×1"));
            Click(buy);
            Assert.That(run.Gold, Is.EqualTo(3000));
            Assert.That(run.Loadout.Owned.Count(source => source.Data == data), Is.EqualTo(count + 1));
            Assert.That(buy.IsInteractable(), Is.False);
            Assert.That(buy.GetComponentInChildren<Text>().text, Is.EqualTo("已售出"));
            entry.Close(); yield return new WaitForSecondsRealtime(.2f);
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            Assert.That(buy.IsInteractable(), Is.False, "关闭、重新 Bind 同一探索不得刷新限购。");
            run.Shop.Enter("shop-a"); Assert.That(buy.IsInteractable(), Is.False);
            run.Shop.Enter("shop-b"); Assert.That(buy.IsInteractable(), Is.True);
            Click(buy);
            Assert.That(run.Gold, Is.EqualTo(2000));
            Assert.That(run.Loadout.Owned.Count(source => source.Data == data), Is.EqualTo(count + 2));
            Assert.That(hud.Battle.Context.Cybernetic.DrawPile.Cards.Concat(hud.Battle.Context.Cybernetic.Hand.Cards)
                .Concat(hud.Battle.Context.Cybernetic.DiscardPile.Cards).Select(card => card.Id), Is.EqualTo(zones));
            entry.Close(); yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>真实回血按钮每次只收费一次，重复开关不叠加监听，生命和余额即时同步。</summary>
        [UnityTest]
        public IEnumerator HealButtonChargesOnceAndUpdatesVisibleHealth()
        {
            var run = hud.Battle.Context.Run; run.Player.ReceiveDamage(20); run.AddGold(200);
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            var heal = shop.transform.Find("Layout_16x9/HealButton").GetComponent<Button>();
            Click(heal);
            Assert.That(run.Player.Health, Is.EqualTo(90)); Assert.That(run.Gold, Is.EqualTo(100));
            Assert.That(hud.GetComponent<BattleSceneReferences>().healthText.text, Is.EqualTo("生命 90"));
            Assert.That(shop.transform.Find("Layout_16x9/HealthText"), Is.Null);
            Assert.That(shop.transform.Find("Layout_16x9/HealingAmount").GetComponent<Text>().text, Is.EqualTo("+10"));
            Assert.That(shop.transform.Find("Layout_16x9/HealingPrice").GetComponent<Text>().text, Is.EqualTo("100"));
            entry.Close(); yield return new WaitForSecondsRealtime(0.2f);
            entry.Open(); dialogue.Advance(); dialogue.Advance(); Click(heal);
            Assert.That(run.Player.Health, Is.EqualTo(100)); Assert.That(run.Gold, Is.Zero);
            Assert.That(heal.IsInteractable(), Is.False);
            run.AddGold(100);
            Assert.That(heal.IsInteractable(), Is.False, "满血不能收费。");
            ExecuteEvents.Execute(heal.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Assert.That(run.Gold, Is.EqualTo(100));
            run.Player.ReceiveDamage(5); Click(heal);
            Assert.That(run.Player.Health, Is.EqualTo(100)); Assert.That(run.Gold, Is.Zero);
            Assert.That(hud.GetComponent<BattleSceneReferences>().healthText.text, Is.EqualTo("生命 100"));
            Assert.That(hud.GetComponent<BattleSceneReferences>().goldText.text, Is.EqualTo("塞勒币 0"));
            entry.Close(); yield return new WaitForSecondsRealtime(0.2f);
        }

        /// <summary>长按只预览，不扣费；松开和退出收起大图，随后短按仍可修复。</summary>
        [UnityTest]
        public IEnumerator RepairHoldPreviewsWithoutChargingAndShortClickStillRepairs()
        {
            var run = hud.Battle.Context.Run; run.AddGold(1000);
            run.Loadout.Owned[0].TryConsumeDurability();
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            yield return new WaitForSecondsRealtime(.2f);
            var button = shop.transform.Find("Layout_16x9/RepairSlot_脑机/RepairButton").GetComponent<Button>();
            var hold = button.GetComponent<ShopRepairHoldPreview>();
            Assert.That(hold, Is.Not.Null);
            var before = Snapshot();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = -1, eligibleForClick = true };
            // 普通短按阶段不创建可见圆环，也不弹出预览。
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            Assert.That(button.GetComponentInChildren<HoldProgressRing>(), Is.Null);
            yield return new WaitForSecondsRealtime(.05f);
            Assert.That(button.GetComponentInChildren<HoldProgressRing>(), Is.Null);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.2f);
            var ring = button.GetComponentInChildren<HoldProgressRing>();
            Assert.That(ring, Is.Not.Null);
            Assert.That(ring.Progress, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(ring.raycastTarget, Is.False);
            Canvas.ForceUpdateCanvases();
            Assert.That(ring.canvasRenderer, Is.Not.Null);
            Assert.That(ring.canvasRenderer.GetMesh().vertexCount, Is.GreaterThan(0), "圆环必须生成可渲染网格。");
            Assert.That(hold.IsPreviewVisible, Is.False);
            Capture(shop, new Vector2Int(1920, 1080));
            File.Copy("Logs/ShopPrototype/ShopPanel-1920x1080.png", "Logs/ShopPrototype/ShopHoldProgress.png", true);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Assert.That(ring.gameObject.activeSelf, Is.False);
            Assert.That(ring.Progress, Is.Zero);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(ring.gameObject.activeSelf, Is.False);
            Assert.That(hold.IsPreviewVisible, Is.True);
            Assert.That(Snapshot(), Is.EqualTo(before));
            Capture(shop, new Vector2Int(1920, 1080));
            File.Copy("Logs/ShopPrototype/ShopPanel-1920x1080.png", "Logs/ShopPrototype/ShopRepairLongPress.png", true);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(pointer.eligibleForClick, Is.False);
            Assert.That(hold.IsPreviewVisible, Is.False);
            Click(button); // 防御性校验：即使外部直接派发 click，仍不能因长按扣费。
            Assert.That(Snapshot(), Is.EqualTo(before));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            var gold = run.Gold;
            Click(button);
            Assert.That(run.Gold, Is.EqualTo(gold - run.Loadout.Owned[0].Data.shopPricing.repairPrice));
            entry.Close(); yield return new WaitForSecondsRealtime(.2f);
            Assert.That(hold.IsPreviewVisible, Is.False);
        }

        /// <summary>商品完整卡面与模板卡面均支持圆环长按，购买按钮独立且浏览不改变资源。</summary>
        [UnityTest]
        public IEnumerator ProductCardsShareHoldProgressWithoutPurchasing()
        {
            entry.Open(); dialogue.Advance(); dialogue.Advance();
            yield return new WaitForSecondsRealtime(.2f);
            var before = Snapshot();
            foreach (int index in new[] { 0, 4 })
            {
                var card = shop.transform.Find("Layout_16x9/ProductSlot_" + index + "/CardPreview");
                var hold = card.GetComponent<ShopRepairHoldPreview>();
                Assert.That(hold, Is.Not.Null);
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = -1 };
                var canvas = card.GetComponentInParent<Canvas>();
                pointer.position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, ((RectTransform)card).TransformPoint(((RectTransform)card).rect.center));
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject), Is.EqualTo(card.gameObject));
                ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                yield return new WaitForSecondsRealtime(.2f);
                var ring = card.GetComponentInChildren<HoldProgressRing>();
                Assert.That(ring.Progress, Is.GreaterThan(0).And.LessThan(1));
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(hold.IsPreviewVisible, Is.True);
                Assert.That(Snapshot(), Is.EqualTo(before));
                if (index == 0)
                {
                    Capture(shop, new Vector2Int(1920, 1080));
                    File.Copy("Logs/ShopPrototype/ShopPanel-1920x1080.png", "Logs/ShopPrototype/ShopProductLongPress.png", true);
                }
                ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                Assert.That(hold.IsPreviewVisible, Is.False);
                Assert.That(ring.gameObject.activeSelf, Is.False);
            }
            entry.Close(); yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Snapshot(), Is.EqualTo(before));
        }

        /// <summary>检查三个目标分辨率下的文字、卡面与顶部工具栏均正常显示。</summary>
        [UnityTest]
        public IEnumerator LayoutFitsThreeResolutions()
        {
#if UNITY_EDITOR
            var extra = UnityEditor.AssetDatabase.LoadAssetAtPath<CyberneticData>("Assets/GameData/Cybernetics/BrainCard.asset");
            if (!hud.Battle.Context.Run.Loadout.OwnsConfiguration(extra)) hud.Battle.Context.Run.Loadout.AcquireCard(extra, extra.cards[0]);
#endif
            entry.Open(); yield return new WaitForSecondsRealtime(0.2f);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) }) Capture(dialogue, size);
            dialogue.Advance(); dialogue.Advance(); yield return new WaitForSecondsRealtime(0.2f);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) }) Capture(shop, size);
            entry.Close();
        }

        /// <summary>使用实际 Button 点击校验，避免绕过 disabled 或 CanvasGroup 的状态。</summary>
        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable(), Is.True, button.name);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>记录资源、各牌区顺序和所有持久牌耐久，浏览前后逐项比较。</summary>
        private string Snapshot()
        {
            var c = hud.Battle.Context;
            var piles = new[] { c.Ordinary.DrawPile, c.Ordinary.Hand, c.Ordinary.DiscardPile, c.Cybernetic.DrawPile, c.Cybernetic.Hand, c.Cybernetic.DiscardPile };
            return c.Player.Health + "/" + c.Player.Sanity + "/" + c.Player.Shield + "/" + c.Run.Gold + "/" + c.ActionPoints.Total + "/" + c.State + "/" +
                string.Join(";", piles.Select(pile => string.Join(",", pile.Cards.Select(card => card.Id.ToString())))) + "/" +
                string.Join(",", c.Run.Loadout.Owned.Select(card => card.Id + ":" + card.Durability));
        }

        /// <summary>相机离屏渲染复用实际 UI，结束后恢复渲染目标和相机比例。</summary>
        private static void Capture(BasePanel panel, Vector2Int size)
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
            var target = new RenderTexture(size.x, size.y, 24); var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                panel.GetComponentInParent<CanvasScaler>().SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                Object.FindObjectOfType<ShopPrototypeTestEntry>().RefreshLayout();
                Canvas.ForceUpdateCanvases();
                panel.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                var entry = Object.FindObjectOfType<ShopPrototypeTestEntry>();
                var toolbar = (RectTransform)entry.GetComponentInParent<BattleHUD>().transform.Find("BattleLayout_16x9/TopBar");
                var modal = (RectTransform)entry.transform.Find("ModalRoot");
                var toolbarCorners = new Vector3[4]; var modalCorners = new Vector3[4];
                toolbar.GetWorldCorners(toolbarCorners); modal.GetWorldCorners(modalCorners);
                float toolbarBottom = toolbarCorners.Min(corner => entry.transform.InverseTransformPoint(corner).y);
                float modalTop = modalCorners.Max(corner => entry.transform.InverseTransformPoint(corner).y);
                // 覆盖顶栏下沿的透明留边，保证背景不漏光，同时保留顶栏主体。
                Assert.That(modalTop, Is.EqualTo(toolbarBottom + 12f).Within(0.1f), "面板未衔接顶部 toolbar 的透明留边 " + size);
                var frame = (RectTransform)panel.transform.Find("Layout_16x9");
                foreach (var text in frame.GetComponentsInChildren<Text>())
                {
                    var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var local = frame.InverseTransformPoint(corner);
                        Assert.That(local.x, Is.InRange(frame.rect.xMin - 1, frame.rect.xMax + 1), text.name);
                        Assert.That(local.y, Is.InRange(frame.rect.yMin - 1, frame.rect.yMax + 1), text.name);
                    }
                }
                foreach (var card in frame.GetComponentsInChildren<CardView>())
                {
                    var corners = new Vector3[4]; ((RectTransform)card.transform).GetWorldCorners(corners);
                    float bottom = corners.Min(corner => frame.InverseTransformPoint(corner).y);
                    var part = card.transform.parent.Find("Price") as RectTransform;
                    part.GetWorldCorners(corners);
                    float top = corners.Max(corner => frame.InverseTransformPoint(corner).y);
                    Assert.That(bottom, Is.GreaterThanOrEqualTo(top), "商品卡面与价格标签重叠 " + size);
                }
                camera.Render(); RenderTexture.active = target;
                foreach (var text in frame.GetComponentsInChildren<Text>())
                    if (text.isActiveAndEnabled && !string.IsNullOrWhiteSpace(text.text))
                        Assert.That(text.cachedTextGenerator.characterCountVisible, Is.GreaterThan(0), text.name + " 文本被截断 " + size);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs/ShopPrototype");
                File.WriteAllBytes("Logs/ShopPrototype/" + panel.GetType().Name + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
