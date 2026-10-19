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
    /// <summary>验证同页手牌排布、抽打牌过渡、悬停让位与无限手牌兼容性。</summary>
    public sealed class HandPresentationTests
    {
        private BattleHUD hud;
        private bool background;
        private float timeScale;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = Application.runInBackground; Application.runInBackground = true; timeScale = Time.timeScale;
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
            Time.timeScale = timeScale; Application.runInBackground = background; yield return null;
        }

        /// <summary>从真实抽牌堆补充手牌，仅用于排布测试，不创建额外卡牌。</summary>
        private void Fill(int count)
        {
            var deck = hud.Battle.Context.Ordinary;
            while (deck.Hand.Count < count && deck.DrawPile.Count > 0)
                Assert.That(deck.TakeFromDrawPile(deck.DrawPile.Cards[0]), Is.True);
            hud.ShowOrdinary();
        }

        /// <summary>取得正式手牌内容节点，并要求已安装独立展示组件。</summary>
        private RectTransform Content()
        {
            var view = hud.GetComponent<BattleSceneReferences>().handArea.GetComponentInChildren<CardView>();
            var root = (RectTransform)view.transform.parent;
            Assert.That(root.GetComponent("HandCardLayout"), Is.Not.Null, "尚未接入同页手牌布局。");
            return root;
        }

        /// <summary>满手牌在宽屏及四比三下均留在视口中，滚轮不会移动内容。</summary>
        [UnityTest]
        public IEnumerator FullHandFitsEveryResolutionWithoutScrolling()
        {
            Fill(13); var root = Content(); var scroll = root.GetComponentInParent<ScrollRect>();
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
            {
                var camera = Camera.main; var previous = camera.targetTexture; float aspect = camera.aspect;
                var target = new RenderTexture(size.x, size.y, 24);
                try
                {
                    camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                    hud.GetComponent<CanvasScaler>().SendMessage("Update"); Canvas.ForceUpdateCanvases(); hud.SendMessage("LateUpdate");
                    yield return new WaitForSecondsRealtime(.35f); Canvas.ForceUpdateCanvases();
                    Assert.That(scroll.horizontal || scroll.vertical, Is.False);
                    AssertInside(root, scroll.viewport);
                    var before = root.anchoredPosition;
                    ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -5) }, ExecuteEvents.scrollHandler);
                    Assert.That(root.anchoredPosition, Is.EqualTo(before));
                    Capture(size, camera, "full");
                }
                finally { camera.targetTexture = previous; camera.aspect = aspect; target.Release(); Object.Destroy(target); }
            }
        }

        /// <summary>打牌保留其余卡牌实例并平滑展开，补牌后重新收紧，不改变权威手牌顺序。</summary>
        [UnityTest]
        public IEnumerator PlayingAndDrawingReuseViewsAndAdjustSpacing()
        {
            Fill(8); var root = Content(); yield return new WaitForSecondsRealtime(.35f);
            var before = root.GetComponentsInChildren<CardView>().ToDictionary(view => view.Instance);
            var survivor = hud.Battle.Context.Ordinary.Hand.Cards.Last();
            var survivorRect = (RectTransform)before[survivor].transform;
            float oldX = survivorRect.anchoredPosition.x;
            var attack = hud.Battle.Context.Ordinary.Hand.Cards.First(card => card.Data.cardType == CardType.Attack);
            Assert.That(hud.Battle.TryPlay(attack, hud.Battle.Context.Enemies[0]).Success, Is.True);
            Assert.That(root.GetComponentsInChildren<CardView>().Single(view => view.Instance == survivor), Is.SameAs(before[survivor]));
            Assert.That(survivorRect.anchoredPosition.x, Is.EqualTo(oldX).Within(.01f), "应从当前位置开始过渡。");
            yield return new WaitForSecondsRealtime(.35f);
            float playedStep = Step(root);
            Fill(8); yield return new WaitForSecondsRealtime(.35f);
            Assert.That(Step(root), Is.LessThan(playedStep), "补牌后中心间距应更紧凑。");
            CollectionAssert.AreEqual(hud.Battle.Context.Ordinary.Hand.Cards,
                root.GetComponentsInChildren<CardView>().OrderBy(view => view.transform.localPosition.x).Select(view => view.Instance));
        }

        /// <summary>暂停时悬停仍抬起当前牌并让两邻牌退开，移出后恢复原位且不改变手牌内容。</summary>
        [UnityTest]
        public IEnumerator HoverLiftsCardAndMovesNeighborsWithoutChangingHand()
        {
            Fill(8); var root = Content(); yield return new WaitForSecondsRealtime(.35f);
            var views = root.GetComponentsInChildren<CardView>().OrderBy(view => view.transform.localPosition.x).ToArray();
            var selected = (RectTransform)views[3].transform;
            var left = (RectTransform)views[2].transform; var right = (RectTransform)views[4].transform;
            var original = selected.anchoredPosition; float leftX = left.anchoredPosition.x, rightX = right.anchoredPosition.x;
            var canvas = selected.GetComponentInParent<Canvas>().rootCanvas;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, selected.position),
                pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() } };
            Time.timeScale = 0;
            ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(selected.anchoredPosition.y, Is.GreaterThan(original.y + 5));
            Assert.That(left.anchoredPosition.x, Is.LessThan(leftX - 2));
            Assert.That(right.anchoredPosition.x, Is.GreaterThan(rightX + 2));
            AssertInside(root, root.GetComponentInParent<ScrollRect>().viewport);
            Capture(new Vector2Int(1920, 1080), Camera.main, "hover");
            pointer.position = new Vector2(-1000, -1000);
            ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(selected.anchoredPosition.y, Is.EqualTo(original.y).Within(.1f));
            Assert.That(left.anchoredPosition.x, Is.EqualTo(leftX).Within(.1f));
            Assert.That(hud.Battle.Context.Ordinary.Hand.Count, Is.EqualTo(8));
            // 验证七张普通牌与五张义体牌都只抬起当前牌，八张时仍保留上述让位行为。
            hud.Battle.Context.Ordinary.Discard(hud.Battle.Context.Ordinary.Hand.Cards.Last());
            hud.RefreshDisplay(); yield return new WaitForSecondsRealtime(.35f);
            yield return HoverWithoutNeighborMovement(root);
            var cybernetic = hud.Battle.Context.Cybernetic;
            while (cybernetic.Hand.Count < 5 && cybernetic.DrawPile.Count > 0)
                Assert.That(cybernetic.TakeFromDrawPile(cybernetic.DrawPile.Cards[0]), Is.True);
            hud.ShowCybernetic(); yield return new WaitForSecondsRealtime(.35f);
            yield return HoverWithoutNeighborMovement(root);
        }

        /// <summary>少量手牌仍抬起当前牌，但其余所有牌的横向位置保持不变。</summary>
        private static IEnumerator HoverWithoutNeighborMovement(RectTransform root)
        {
            var views = root.GetComponentsInChildren<CardView>().OrderBy(view => view.transform.localPosition.x).ToArray();
            Assert.That(views.Length, Is.LessThanOrEqualTo(7));
            var positions = views.Select(view => ((RectTransform)view.transform).anchoredPosition).ToArray();
            int index = views.Length / 2;
            var canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, views[index].transform.position) };
            ExecuteEvents.Execute(views[index].gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(((RectTransform)views[index].transform).anchoredPosition.y, Is.GreaterThan(positions[index].y + 5));
            for (int i = 0; i < views.Length; i++)
                Assert.That(((RectTransform)views[i].transform).anchoredPosition.x, Is.EqualTo(positions[i].x).Within(.1f));
            pointer.position = new Vector2(-1000, -1000);
            ExecuteEvents.Execute(views[index].gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>新世界躯干突破普通手牌上限后仍保持单页。</summary>
        [UnityTest]
        public IEnumerator UnlimitedHandAlsoFitsOnOnePage()
        {
            var context = hud.Battle.Context;
            var torso = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards).First(card => card.Source.Data.slot == CyberneticSlot.Torso);
            if (!context.Cybernetic.Hand.Contains(torso)) Assert.That(context.Cybernetic.TakeFromDrawPile(torso), Is.True);
            Assert.That(hud.Battle.TryPlay(torso).Success, Is.True);
            // 默认普通牌组只有十三张；仅在运行时补足独立测试实例，实际经过躯干的无上限规则抽取。
            var basic = context.Ordinary.Hand.Cards.First(card => card.Data.category == CardCategory.Basic).Data;
            while (context.Ordinary.Hand.Count + context.Ordinary.DrawPile.Count < 15)
                Assert.That(context.Ordinary.DrawPile.TryAdd(new CardInstance(basic)), Is.True);
            Fill(15); var root = Content(); yield return new WaitForSecondsRealtime(.35f);
            Assert.That(context.Ordinary.Hand.Count, Is.EqualTo(15));
            AssertInside(root, root.GetComponentInParent<ScrollRect>().viewport);
            while (context.Cybernetic.Hand.Count < 5)
                Assert.That(context.Cybernetic.TakeFromDrawPile(context.Cybernetic.DrawPile.Cards[0]), Is.True);
            hud.ShowCybernetic(); yield return new WaitForSecondsRealtime(.35f);
            Assert.That(root.GetComponentsInChildren<CardView>().Length, Is.EqualTo(5));
            AssertInside(root, root.GetComponentInParent<ScrollRect>().viewport);
        }

        /// <summary>比较相邻卡牌中心间距。</summary>
        private static float Step(RectTransform root)
        {
            var cards = root.GetComponentsInChildren<CardView>().OrderBy(view => view.transform.localPosition.x).ToArray();
            return cards[1].transform.localPosition.x - cards[0].transform.localPosition.x;
        }

        /// <summary>逐张检查完整卡牌矩形都处于裁剪区域内。</summary>
        private static void AssertInside(RectTransform root, RectTransform viewport)
        {
            var corners = new Vector3[4];
            foreach (var view in root.GetComponentsInChildren<CardView>())
            {
                ((RectTransform)view.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var local = viewport.InverseTransformPoint(corner);
                    Assert.That(local.x, Is.InRange(viewport.rect.xMin - 1, viewport.rect.xMax + 1));
                    Assert.That(local.y, Is.InRange(viewport.rect.yMin - 1, viewport.rect.yMax + 1));
                }
            }
        }

        /// <summary>导出真实手牌画面用于检查交叠、间距和悬停效果。</summary>
        private static void Capture(Vector2Int size, Camera camera, string state)
        {
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = previous ? previous : new RenderTexture(size.x, size.y, 24);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                foreach (var graphic in Object.FindObjectsOfType<Graphic>()) graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes("Temp/HandPresentation-" + state + "-" + target.width + "x" + target.height + ".png", image.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Object.Destroy(image); if (!previous) { target.Release(); Object.Destroy(target); } }
        }
    }
}
