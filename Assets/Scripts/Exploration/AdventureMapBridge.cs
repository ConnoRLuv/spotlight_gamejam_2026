using System;
using RoguelikeMap.Generation;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace SpotlightGameJam
{
    /// <summary>连接地图表现与持久探索会话；节点只发布流程请求，由外部决定场景或面板。</summary>
    /// <remarks>不创建第二份玩家资源，不自动开战或切场景；商店与事件可以接到同一份 RunState。</remarks>
    [DisallowMultipleComponent]
    public sealed class AdventureMapBridge : MonoBehaviour
    {
        [SerializeField] private MapGeneratorBehaviour generator;
        [SerializeField] private MapGraphView view;
        [SerializeField] private UnityEvent combatRequested = new UnityEvent();
        [SerializeField] private UnityEvent shopRequested = new UnityEvent();
        [SerializeField] private UnityEvent eventRequested = new UnityEvent();
        private AdventureSession session;
        public event Action<MapNode> NodeRequested;
        public RunState Run => session ? session.Run : null;

        private void OnEnable() { if (Application.isPlaying) Connect(); }
        private void OnDisable() { Disconnect(); }

        /// <summary>由增量构建工具配置已有地图组件；重复配置先解绑旧监听。</summary>
        public void Configure(MapGeneratorBehaviour mapGenerator, MapGraphView mapView)
        {
            Disconnect(); generator = mapGenerator; view = mapView;
            if (Application.isPlaying && isActiveAndEnabled) Connect();
        }

        /// <summary>绑定会话及地图事件，优先采用探索中保存的进度；没有探索时保留独立地图演示。</summary>
        private void Connect()
        {
            Disconnect();
            if (!generator || !view) return;
            session = AdventureSession.GetOrCreate();
            session.ExplorationRequested += OnExplorationRequested;
            generator.Generated += OnGenerated;
            view.NodeEntered += OnNodeEntered;
            if (session.Map != null) generator.BindRun(session.Map);
            else if (generator.RunState != null && session.Run != null) session.TryBindMap(generator.RunState);
        }

        /// <summary>解除成对监听，不释放会话持有的地图、生命、理智、塞勒币或牌组。</summary>
        private void Disconnect()
        {
            if (session) session.ExplorationRequested -= OnExplorationRequested;
            if (generator) generator.Generated -= OnGenerated;
            if (view) view.NodeEntered -= OnNodeEntered;
            session = null;
        }

        /// <summary>开始新探索后生成该局地图；普通返回地图不会经过此入口。</summary>
        private void OnExplorationRequested(RunState run) => generator.Generate();

        /// <summary>首次生成登记状态；已有路线时拒绝刷新覆盖，并让表现层恢复会话中的原地图。</summary>
        private void OnGenerated(MapGenerationResult result)
        {
            if (!session || session.Run == null || !result.Success) return;
            if (session.Map == null) session.TryBindMap(generator.RunState);
            else if (generator.RunState != session.Map) generator.BindRun(session.Map);
        }

        /// <summary>仅对真实进入的节点分发请求；战斗入口可加载 BattleScene，再复用会话开始遭遇。</summary>
        private void OnNodeEntered(MapNode node)
        {
            if (!session || session.Run == null || session.Map != generator.RunState || node == null) return;
            NodeRequested?.Invoke(node);
            switch (node.TypeKey)
            {
                case NodeTypeKeys.Monster:
                case NodeTypeKeys.Elite:
                case NodeTypeKeys.Boss: combatRequested.Invoke(); break;
                case NodeTypeKeys.Shop: shopRequested.Invoke(); break;
                case NodeTypeKeys.Event: eventRequested.Invoke(); break;
            }
        }
    }
}
