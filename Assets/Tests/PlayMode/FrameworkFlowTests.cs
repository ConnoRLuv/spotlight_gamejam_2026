using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpotlightGameJam.UI;
using RoguelikeMap.Unity;
using RoguelikeMap.Generation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证面板真实按钮、淡出生命周期以及地图往返时的探索状态。</summary>
    public sealed class FrameworkFlowTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        /// <summary>跨场景资源用例不应领取商店测试补助；只在测试运行时隔离该入口。</summary>
        [SetUp]
        public void IsolateShopTestFunding() => SceneManager.sceneLoaded += DisableShopTestFunding;

        /// <summary>场景加载后、Start 发放测试币前停用入口，不改写场景和正式探索资源。</summary>
        private static void DisableShopTestFunding(Scene scene, LoadSceneMode mode)
        {
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPrototypeTestEntry>(true)))
                entry.enabled = false;
        }

        /// <summary>登记测试对象，测试完成后销毁，避免残留会话或配置。</summary>
        private T Keep<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }

        /// <summary>清理独立测试对象；不写入场景、配置资产或构建设置。</summary>
        [TearDown]
        public void Cleanup()
        {
            SceneManager.sceneLoaded -= DisableShopTestFunding;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        /// <summary>创建无义体初选的合法探索，允许实际场景复用同一份玩家状态。</summary>
        private AdventureSetupData Setup()
        {
            var setup = Keep(ScriptableObject.CreateInstance<AdventureSetupData>());
            setup.rules = Keep(ScriptableObject.CreateInstance<BattleRules>());
            setup.phantomPain = Keep(ScriptableObject.CreateInstance<CardData>());
            setup.phantomPain.cardId = "framework-phantom";
            setup.phantomPain.category = CardCategory.Special; setup.phantomPain.locksInHand = true;
            return setup;
        }

        /// <summary>编辑器测试直接按路径加载已有场景，避免为验证改变构建场景列表。</summary>
        private static AsyncOperation LoadScene(string name)
        {
#if UNITY_EDITOR
            return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/" + name + ".unity",
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            return SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
#endif
        }

        [UnityTest]
        public IEnumerator Map01AnchorPrefabRestoresPositionsAndProgress()
        {
            yield return LoadScene("MapScene");
            yield return null;
            if (AdventureSession.Instance) Keep(AdventureSession.Instance.gameObject);
            var view = UnityEngine.Object.FindObjectOfType<MapGraphView>();
            var generator = view.Generator;
            Assert.That(generator.Generate(1u).Success, Is.True);
            yield return null;
            var preset = view.GetComponentInChildren<MapPresetPrefab>();
            Assert.That(preset.UseConnectionAnchors, Is.True);
            var positions = preset.Nodes.ToDictionary(n => n.NodeId, n => view.GetPosition(view.Graph.Get(n.NodeId)));
            foreach (var binding in preset.Nodes)
                Assert.That(positions[binding.NodeId], Is.EqualTo(binding.PlacementRoot.anchoredPosition));
            foreach (var edge in preset.Edges)
            {
                var placement = view.Graph.AssetLayout.FindEdge(edge.FromId, edge.ToId);
                string a = AnchorName(placement.SourceSide), b = AnchorName(placement.TargetSide);
                var from = preset.Nodes.Single(n => n.NodeId == edge.FromId).PlacementRoot;
                var to = preset.Nodes.Single(n => n.NodeId == edge.ToId).PlacementRoot;
                Assert.That(Vector3.Distance(edge.View.transform.Find(a).position, from.Find(a).position), Is.LessThan(.02f));
                Assert.That(Vector3.Distance(edge.View.transform.Find(b).position, to.Find(b).position), Is.LessThan(.02f));
            }
            Assert.That(generator.RunState.MoveTo(generator.RunState.GetOptions()[0].Id), Is.True);
            var snapshot = generator.CaptureSnapshot();
            var roomTypes = generator.Graph.Nodes.Select(n => n.TypeKey).ToArray();
            Assert.That(generator.TryRestore(snapshot), Is.True);
            yield return null;
            Assert.That(generator.RunState.CurrentNodeId, Is.EqualTo(snapshot.CurrentNodeId));
            CollectionAssert.AreEqual(roomTypes, generator.Graph.Nodes.Select(n => n.TypeKey), "还原不能重新随机房间。");
            Assert.That(view.GetComponentInChildren<MapPresetPrefab>().UseConnectionAnchors, Is.True);
            foreach (var node in view.Graph.Nodes) Assert.That(view.GetPosition(node), Is.EqualTo(positions[node.Id]));
        }

        [UnityTest]
        public IEnumerator AllEmptyLayoutsFillRoomPrefabsOnlyAtRuntime()
        {
            yield return LoadScene("MapScene");
            yield return null;
            if (AdventureSession.Instance) Keep(AdventureSession.Instance.gameObject);
            var view = UnityEngine.Object.FindObjectOfType<MapGraphView>();
            for (uint map = 1; map <= 10; map++)
            {
                Assert.That(view.Generator.Generate(map).Success, Is.True);
                yield return null;
                var preset = view.GetComponentInChildren<MapPresetPrefab>();
                Assert.That(preset.RandomizeRooms, Is.True);
                Assert.That(preset.UseConnectionAnchors, Is.True, preset.name);
                Assert.That(preset.Nodes.Count, Is.EqualTo(view.Graph.NodeCount));
                foreach (var binding in preset.Nodes)
                {
                    Assert.That(binding.View, Is.Not.Null);
                    Assert.That(binding.View.Node, Is.SameAs(view.Graph.Get(binding.NodeId)));
                    Assert.That(binding.View.transform.parent, Is.SameAs(binding.PlacementRoot));
                }
                foreach (var edge in preset.Edges)
                {
                    var placement = view.Graph.AssetLayout.FindEdge(edge.FromId, edge.ToId);
                    string a = AnchorName(placement.SourceSide), b = AnchorName(placement.TargetSide);
                    var from = preset.Nodes.Single(n => n.NodeId == edge.FromId).PlacementRoot;
                    var to = preset.Nodes.Single(n => n.NodeId == edge.ToId).PlacementRoot;
                    Assert.That(Vector3.Distance(edge.View.transform.Find(a).position, from.Find(a).position), Is.LessThan(.02f), preset.name);
                    Assert.That(Vector3.Distance(edge.View.transform.Find(b).position, to.Find(b).position), Is.LessThan(.02f), preset.name);
                }
                var source = view.ConfigAsset.PreGeneratedMaps[(int)map - 1];
                foreach (var binding in source.Nodes)
                {
                    var node = source.Data.Graph.Nodes.Single(n => n.Id == binding.NodeId);
                    if (node.Row > 0 && node.Row < 14) Assert.That(binding.View, Is.Null, "运行时填充不能污染源预制体。");
                }
            }
        }

        private static string AnchorName(RoguelikeMap.Model.MapSocketSide side) => side == RoguelikeMap.Model.MapSocketSide.Up ? "top" :
            side == RoguelikeMap.Model.MapSocketSide.Down ? "bottom" : side == RoguelikeMap.Model.MapSocketSide.Left ? "left" : "right";

        /// <summary>真实地图→战斗→地图往返保持进度与资源，桥不主动跳转场景。</summary>
        [UnityTest]
        public IEnumerator MapBattleMapRetainsSessionProgressAndResources()
        {
            var owner = Keep(new GameObject("Persistent exploration flow test"));
            var session = owner.AddComponent<AdventureSession>();
            Assert.That(session.TryBegin(Setup(), Array.Empty<CyberneticData>()), Is.True);
            yield return LoadScene("MapScene");
            yield return null;
            Assert.That(session.Map, Is.Not.Null);
            var view = UnityEngine.Object.FindObjectOfType<MapGraphView>();
            Assert.That(view.Graph.Columns, Is.EqualTo(5));
            Assert.That(view.Graph.AssetLayout, Is.Not.Null);
            var presetInstance = view.GetComponentInChildren<MapPresetPrefab>();
            Assert.That(presetInstance, Is.Not.Null, "应实例化完整地图预制体。");
            Assert.That(view.Graph.Nodes.Select(n => n.Column).Distinct().Count(), Is.EqualTo(5));
            uint presetSeed = view.Graph.Seed;
            Assert.That(MapAssetGeometry.Validate(view.Graph, view.ConfigAsset.RouteProfile, out string layoutError), Is.True, layoutError);
            Assert.That(view.Graph.Rows, Is.EqualTo(15), "总共 15 层，包含起点和 Boss。");
            Assert.That(view.ConfigAsset.Config.PathCount, Is.EqualTo(4));
            Assert.That(view.ConfigAsset.GetRoute(MapRouteDirection.UpRight).SourceEnd.x, Is.LessThan(0.2f),
                "重新进入运行模式后，斜线路线必须保留校准起点。");
            foreach (var edge in view.GetComponentsInChildren<MapEdgeView>())
            {
                var ids = edge.name.Split('_');
                var rect = (RectTransform)edge.transform;
                Assert.That(rect.rect.size, Is.EqualTo(edge.Image.sprite.rect.size), edge.name);
                Assert.That(Quaternion.Angle(rect.localRotation, Quaternion.identity), Is.LessThan(0.01f), edge.name);
                Assert.That(rect.localScale.x, Is.GreaterThan(0f), edge.name);
                Assert.That(rect.localScale.y, Is.GreaterThan(0f), edge.name);
                Assert.That(rect.localScale.x, Is.EqualTo(rect.localScale.y).Within(0.001f), "连接线必须保持原图比例。");
                Assert.That(rect.parent.localScale.x, Is.EqualTo(rect.parent.localScale.y), "地图必须整体等比显示。");
                Assert.That(edge.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(1), "每条边只能使用单张路线图。");
                var from = view.Graph.Get(int.Parse(ids[1]));
                var to = view.Graph.Get(int.Parse(ids[2]));
                Assert.That(to.Row, Is.EqualTo(from.Row + 1));
                Assert.That(Math.Abs(to.Column - from.Column), Is.LessThanOrEqualTo(1));
                Assert.That(edge.SocketTemplate, Is.Not.Null, "应按实际节点接点选图。");
                foreach (var node in new[] { from, to })
                {
                    var face = view.GetComponentsInChildren<MapNodeView>().Single(v => v.Node == node);
                    var nodeRect = (RectTransform)face.transform;
                    var portGroup = node == from ? edge.SourcePortBounds : edge.TargetPortBounds;
                    var ports = new[] { new Vector2((float)portGroup.MinX, (float)portGroup.MinY), new Vector2((float)portGroup.MaxX, (float)portGroup.MaxY) };
                    foreach (var port in ports)
                    {
                        var worldPort = rect.TransformPoint(port);
                        var localPort = (Vector2)nodeRect.InverseTransformPoint(worldPort);
                        Assert.That(nodeRect.rect.Contains(localPort), Is.True, "真实线头必须接入对应节点内部。");
                        var side = node == from ? edge.SocketTemplate.SourceSide : edge.SocketTemplate.TargetSide;
                        var profile = view.ConfigAsset.RouteProfile;
                        var connectionSize = presetInstance.UseConnectionAnchors ?
                            presetInstance.Nodes.Single(n => n.NodeId == node.Id).PlacementRoot.rect.size : new Vector2(profile.NodeWidth, profile.NodeHeight);
                        bool horizontal = side == RoguelikeMap.Model.MapSocketSide.Left || side == RoguelikeMap.Model.MapSocketSide.Right;
                        Assert.That(Mathf.Abs(localPort.x), Is.LessThanOrEqualTo(connectionSize.x / 2f - (horizontal ? 0 : 2) + 0.01f), "线头需进入中心接点范围。");
                        Assert.That(Mathf.Abs(localPort.y), Is.LessThanOrEqualTo(connectionSize.y / 2f - (horizontal ? 2 : 0) + 0.01f), "线头需进入中心接点范围。");
                        if (side == RoguelikeMap.Model.MapSocketSide.Left) Assert.That(localPort.x, Is.LessThanOrEqualTo(0.01f));
                        if (side == RoguelikeMap.Model.MapSocketSide.Right) Assert.That(localPort.x, Is.GreaterThanOrEqualTo(-0.01f));
                        if (side == RoguelikeMap.Model.MapSocketSide.Up) Assert.That(localPort.y, Is.GreaterThanOrEqualTo(-0.01f));
                        if (side == RoguelikeMap.Model.MapSocketSide.Down) Assert.That(localPort.y, Is.LessThanOrEqualTo(0.01f));
                    }
                }
            }
            var map = session.Map;
            Assert.That(map.MoveTo(map.GetOptions()[0].Id), Is.True);
            int current = map.CurrentNodeId;
            var run = session.Run;
            run.AddGold(321); run.Player.ReceiveDamage(7); run.Player.TrySpendSanity(3);
            int hp = run.Player.Health, sanity = run.Player.Sanity;
            yield return LoadScene("BattleScene");
            yield return null;
            Assert.That(session.CurrentBattle, Is.Not.Null);
            Assert.That(session.CurrentBattle.Context.Run, Is.SameAs(run));
            yield return LoadScene("MapScene");
            yield return null;
            Assert.That(session.Map, Is.SameAs(map));
            Assert.That(session.Map.CurrentNodeId, Is.EqualTo(current));
            Assert.That(UnityEngine.Object.FindObjectOfType<MapGraphView>().GetComponentInChildren<MapPresetPrefab>(), Is.Not.Null);
            Assert.That(session.Map.Graph.Seed, Is.EqualTo(presetSeed), "战斗返回不能重新随机地图。");
            Assert.That(run.Gold, Is.EqualTo(321));
            Assert.That(run.Player.Health, Is.EqualTo(hp));
            Assert.That(run.Player.Sanity, Is.EqualTo(sanity));
        }

        /// <summary>重排取消不扣费、淡出立即释放鼠标；反复打开确认只结算一次。</summary>
        [UnityTest]
        public IEnumerator ReorderCancelAndConfirmUseSharedPanelLifecycle()
        {
            var setup = Setup();
            var basic = Keep(ScriptableObject.CreateInstance<CardData>());
            basic.cardId = "framework-basic"; basic.category = CardCategory.Basic;
            basic.effects = new CardEffectData[] { Keep(ScriptableObject.CreateInstance<HealEffectData>()) };
            basic.targetType = CardTargetType.Self; basic.cost = 1;
            var brain = Keep(ScriptableObject.CreateInstance<CardData>());
            brain.cardId = "framework-brain"; brain.category = CardCategory.Cybernetic; brain.sanityCost = 4;
            brain.effects = new CardEffectData[] { Keep(ScriptableObject.CreateInstance<NewWorldBrainEffectData>()) };
            var data = Keep(ScriptableObject.CreateInstance<CyberneticData>());
            data.cyberneticId = "framework-brain-source"; data.cards = new[] { brain }; data.maxDurability = 3;
            var run = new RunState(setup.rules); run.Loadout.AddInitial(data);
            var random = new RandomUtility(1);
            using (var context = new BattleContext(run, setup.rules, new[] { new CombatantState("enemy", 9999) },
                new CardDeck(13, Enumerable.Range(0, 6).Select(_ => new CardInstance(basic)), random),
                new CardDeck(5, run.Loadout.GetUsableCards(), random, true), setup.phantomPain, random))
            {
                var battle = new BattleController(context);
                Assert.That(battle.StartBattle(), Is.True, battle.LastError);
                var card = context.Cybernetic.Hand.Cards.Single();
                var root = Keep(new GameObject("Reorder test", typeof(RectTransform), typeof(CanvasGroup)));
                var panel = root.AddComponent<NewWorldBrainPanel>();
                var pool = Keep(new GameObject("Pool", typeof(RectTransform))).GetComponent<RectTransform>();
                var order = Keep(new GameObject("Order", typeof(RectTransform))).GetComponent<RectTransform>();
                pool.SetParent(root.transform); order.SetParent(root.transform);
                var confirm = Keep(new GameObject("Confirm", typeof(RectTransform), typeof(Button))).GetComponent<Button>();
                var cancel = Keep(new GameObject("Cancel", typeof(RectTransform), typeof(Button))).GetComponent<Button>();
                var catalog = Keep(ScriptableObject.CreateInstance<CardPrefabCatalog>());
                // 使用简化卡面，仅验证真实按钮的选择流程，避免依赖项目美术资源。
                var template = Keep(new GameObject("Card template", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CardView)));
                template.SetActive(false);
                var templateView = template.GetComponent<CardView>();
                templateView.Configure(basic, null, Color.gray, template.GetComponent<Image>(), template.GetComponent<Button>(), null, null, null, null);
                catalog.fallback = templateView;
                template.SetActive(true);
                panel.Configure(catalog, null, null, pool, order, null, null, confirm, cancel, null);
                int callbacks = 0, sanity = run.Player.Sanity;
                panel.Open(battle, card, _ => callbacks++);
                cancel.onClick.Invoke();
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                Assert.That(run.Player.Sanity, Is.EqualTo(sanity));
                Assert.That(card.Source.Durability, Is.EqualTo(3));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(root.activeSelf, Is.False);
                panel.Open(battle, card, _ => callbacks++);
                for (int i = 0; i < 3; i++)
                {
                    pool.GetComponentsInChildren<CardView>(true).First(v => v.gameObject.activeSelf).GetComponent<Button>().onClick.Invoke();
                }
                Assert.That(confirm.interactable, Is.True);
                // 模拟真实 HUD：结算通知会先关闭面板，仍应准确收到成功回调。
                context.Changed += _ => { if (!context.Cybernetic.Hand.Contains(card)) panel.Close(0); };
                confirm.onClick.Invoke();
                Assert.That(callbacks, Is.EqualTo(2));
                Assert.That(run.Player.Sanity, Is.EqualTo(sanity - 4));
                Assert.That(card.Source.Durability, Is.EqualTo(2));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(root.activeSelf, Is.False);
            }
        }
    }
}
