using System;
using System.Collections.Generic;
namespace SpotlightGameJam
{
    /// <summary>
    /// 封装有独立种子的随机数生成器，供洗牌与随机目标选择共享使用。
    /// </summary>
    /// <remarks>
    /// 同种子且调用顺序相同时可复现结果；不影响 UnityEngine.Random 的全局状态。
    /// </remarks>
    public sealed class RandomUtility
    {
        private readonly Random random;
        /// <summary>以指定种子创建独立随机源，支持复现洗牌和目标选择结果。</summary>
        public RandomUtility(int seed) => random = new Random(seed);
        /// <summary>
        /// 返回从 0 到 exclusiveMax - 1 的随机索引，要求上界为正数。
        /// </summary>
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            return random.Next(exclusiveMax);
        }
        /// <summary>
        /// 从非空候选集合中等概率选择一个元素；调用方负责过滤无效目标。
        /// </summary>
        public T Choose<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0) throw new ArgumentException("候选集合不能为空。",nameof(values));
            return values[Next(values.Count)];
        }
    }
}
