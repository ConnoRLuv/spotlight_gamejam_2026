using System;

namespace SpotlightGameJam
{
    /// <summary>统一校验并结算购买、单点修复和生命恢复，修改当前探索的持久状态。</summary>
    public sealed class ShopService
    {
        public const int HealingPrice = 100;
        public const int HealingAmount = 10;
        private readonly RunState run;
        private bool transacting;
        /// <summary>绑定已有探索，不创建牌组，也不操作当前战斗的任何牌区。</summary>
        public ShopService(RunState run) { this.run = run ?? throw new ArgumentNullException(nameof(run)); }

        /// <summary>存活、生命未满且至少拥有100塞勒币时允许回血，不提供复活。</summary>
        public bool CanHeal(out string reason)
        {
            reason = null;
            if (!run.Player.IsAlive) reason = "无法为已死亡角色恢复生命。";
            else if (run.Player.Health >= run.Player.MaxHealth) reason = "生命已满。";
            else if (run.Gold < HealingPrice) reason = "塞勒币不足。";
            return reason == null;
        }

        /// <summary>每次支付固定100塞勒币，最多恢复10点生命；接近上限时只恢复缺少部分。</summary>
        public bool TryHeal(out string message)
        {
            if (transacting) { message = "交易正在处理中。"; return false; }
            if (!CanHeal(out message)) return false;
            transacting = true;
            try
            {
                if (!run.TrySpendGold(HealingPrice)) { message = "塞勒币不足。"; return false; }
                int before = run.Player.Health;
                run.Player.Heal(HealingAmount);
                message = "生命恢复 " + (run.Player.Health - before) + " 点。";
                return true;
            }
            finally { transacting = false; }
        }

        /// <summary>检查购买资格和余额；同型号持有包括零耐久，避免用购买重建满耐久。</summary>
        public bool CanPurchase(CyberneticData data, out string reason)
        {
            reason = null;
            if (!data || data.Validate().Count > 0) reason = "商品配置无效。";
            else if (!data.shopPricing || !data.shopPricing.canPurchase || data.shopPricing.purchasePrice < 0) reason = "该型号暂未开放购买。";
            else if (run.Loadout.OwnsConfiguration(data)) reason = "已拥有该型号义体。";
            else if (run.Gold < data.shopPricing.purchasePrice) reason = "塞勒币不足。";
            return reason == null;
        }

        /// <summary>成功支付后仅获得一张满耐久持久牌，保留同部位原型号，下场战斗自然入堆。</summary>
        public bool TryPurchase(CyberneticData data, out string message)
        {
            if (transacting) { message = "交易正在处理中。"; return false; }
            if (!CanPurchase(data, out message)) return false;
            transacting = true;
            try
            {
                // 扣费事件可能触发 UI 刷新；交易锁阻止回调重入本服务。
                if (!run.TrySpendGold(data.shopPricing.purchasePrice)) { message = "塞勒币不足。"; return false; }
                run.Loadout.AcquireCard(data, data.cards[0]);
                message = "已购买 " + data.cards[0].cardName + " ×1，下场战斗加入义体牌堆。";
                return true;
            }
            finally { transacting = false; }
        }

        /// <summary>检查待修复牌归属、系列定价、耐久缺口和余额，不允许给其他探索的牌付费。</summary>
        public bool CanRepair(CyberneticInstance source, out string reason)
        {
            reason = null;
            if (!run.Loadout.Owns(source)) reason = "该义体不属于当前探索。";
            else if (!source.Data.shopPricing || source.Data.shopPricing.repairPrice < 0) reason = "该型号暂未配置修复价格。";
            else if (source.Durability >= source.Data.maxDurability) reason = "耐久已满。";
            else if (run.Gold < source.Data.shopPricing.repairPrice) reason = "塞勒币不足。";
            return reason == null;
        }

        /// <summary>支付一次价格，仅恢复该型号的一点共享耐久，不改变使用次数或直接触发 buff。</summary>
        public bool TryRepair(CyberneticInstance source, out string message)
        {
            if (transacting) { message = "交易正在处理中。"; return false; }
            if (!CanRepair(source, out message)) return false;
            transacting = true;
            try
            {
                if (!run.TrySpendGold(source.Data.shopPricing.repairPrice)) { message = "塞勒币不足。"; return false; }
                source.Repair(1);
                message = source.Card.cardName + " 耐久 +1。";
                return true;
            }
            finally { transacting = false; }
        }
    }
}
