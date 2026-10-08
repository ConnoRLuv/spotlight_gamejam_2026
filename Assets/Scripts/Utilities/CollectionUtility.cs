using System;
using System.Collections.Generic;
namespace SpotlightGameJam
{
    /// <summary>
    /// 提供不依赖 Unity 全局随机状态的集合操作。
    /// </summary>
    /// <remarks>
    /// 当前使用 Fisher–Yates 对传入列表原地洗牌；随机源由调用方注入，便于复现测试与战斗过程。
    /// </remarks>
    public static class CollectionUtility
    {
        /// <summary>
        /// 使用注入的随机源原地洗牌；修改传入列表，不创建新列表。
        /// </summary>
        public static void Shuffle<T>(IList<T> values, RandomUtility random)
        {
            if (values == null || random == null) throw new ArgumentNullException();
            // Fisher–Yates：从未固定的前缀均匀选一项交换到尾部，保证排列等概率。
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1); T value = values[i];
                values[i] = values[j]; values[j] = value;
            }
        }
    }
}
