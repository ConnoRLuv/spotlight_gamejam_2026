using System;
using System.Collections.Generic;

namespace SpotlightGameJam
{
    /// <summary>按商店节点保存已购买的型号；与面板和服务实例无关，返回同一商店不会补货。</summary>
    public sealed class ShopVisitState
    {
        private readonly Dictionary<string, HashSet<string>> purchases = new Dictionary<string, HashSet<string>>();
        public string CurrentId { get; private set; } = "battle-test-shop";
        public event Action Changed;

        /// <summary>地图进入商店前调用；新节点使用独立库存，重复进入原节点保留购买记录。</summary>
        public void Enter(string shopId)
        {
            if (string.IsNullOrWhiteSpace(shopId)) throw new ArgumentException("商店节点身份不能为空。", nameof(shopId));
            if (CurrentId == shopId) return;
            CurrentId = shopId; Changed?.Invoke();
        }

        /// <summary>查询当前商店是否已经购买此型号，不读取玩家原有牌组。</summary>
        public bool HasPurchased(CyberneticData data) => data && purchases.TryGetValue(CurrentId, out var items) && items.Contains(data.cyberneticId);

        /// <summary>扣款前预占库存，防止余额事件重入或新建服务绕过限购。</summary>
        internal bool Reserve(string shopId, CyberneticData data)
        {
            if (!purchases.TryGetValue(shopId, out var items)) purchases.Add(shopId, items = new HashSet<string>());
            return items.Add(data.cyberneticId);
        }

        /// <summary>扣款失败时释放本次预占，不消耗购买资格。</summary>
        internal void Release(string shopId, CyberneticData data)
        {
            if (purchases.TryGetValue(shopId, out var items)) items.Remove(data.cyberneticId);
        }
    }
}
