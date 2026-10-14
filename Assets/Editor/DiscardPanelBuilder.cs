using UnityEditor;
namespace SpotlightGameJam.Editor
{
    /// <summary>保留原菜单入口，统一由新的牌堆面板构建工具升级场景。</summary>
    public static class DiscardPanelBuilder
    {
        /// <summary>增量创建抽牌和弃牌面板并移除旧悬停组件，不重建战斗界面。</summary>
        [MenuItem("Spotlight/Panels/Create Discard Panel And Wire Scene")]
        public static void Build() => PilePanelBuilder.Build();
    }
}
