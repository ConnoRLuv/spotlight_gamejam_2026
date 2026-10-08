using System;
using UnityEngine;

namespace SpotlightGameJam.UI
{
    /// <summary>配置到展示预制体的映射；不存放运行时卡牌身份。</summary>
    [CreateAssetMenu(menuName = "Spotlight/UI/卡牌预制体目录")]
    public sealed class CardPrefabCatalog : ScriptableObject
    {
        /// <summary>一项共享卡牌配置与对应预制体的引用。</summary>
        [Serializable] public sealed class Entry
        {
            public CardData data;
            public CardView prefab;
        }
        public Entry[] entries = Array.Empty<Entry>();
        // 幻痛沿用共用展示结构，由 CardView.Bind 切换内容与灰色主题。
        public CardView fallback;
        /// <summary>查找配置对应的卡牌预制体；未登记的配置使用备用预制体。</summary>
        public CardView Find(CardData data)
        {
            foreach (var entry in entries) if (entry != null && entry.data == data) return entry.prefab;
            return fallback;
        }
    }
}
