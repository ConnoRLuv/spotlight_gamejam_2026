using System;
using System.Collections.Generic;

namespace RoguelikeMap.Core
{
    /// <summary>
    /// 随机源抽象。生成流程里所有的随机都必须走这里。
    /// 只要换一个实现（例如接 UnityEngine.Random 或服务器下发的随机流），
    /// 生成逻辑本身一行都不用改。
    /// </summary>
    public interface IRandomSource
    {
        uint NextUInt();
        uint State { get; set; }
    }

    /// <summary>
    /// xorshift32：确定性、零分配、状态只有一个 uint。
    /// 存档时只需要保存 <see cref="State"/>，就能精确还原后续所有随机。
    /// </summary>
    public sealed class XorShiftRandom : IRandomSource
    {
        uint _state;

        public XorShiftRandom(uint seed)
        {
            // xorshift 的 0 是吸收态，必须避开
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        public uint State
        {
            get => _state;
            set => _state = value == 0u ? 0x9E3779B9u : value;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }
    }

    /// <summary>所有生成逻辑共用的随机工具。全部零分配（除了 WeightedIndex 内部的循环）。</summary>
    public static class RandomSourceExtensions
    {
        /// <summary>[minInclusive, maxExclusive) 上的均匀整数。用 Lemire 方法消除取模偏差。</summary>
        public static int Range(this IRandomSource rng, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;

            uint span = (uint)(maxExclusive - minInclusive);
            uint x = rng.NextUInt();
            ulong m = (ulong)x * span;
            uint low = (uint)m;

            if (low < span)
            {
                uint threshold = (uint)((0u - span) % span);
                while (low < threshold)
                {
                    x = rng.NextUInt();
                    m = (ulong)x * span;
                    low = (uint)m;
                }
            }

            return minInclusive + (int)(m >> 32);
        }

        public static float Value01(this IRandomSource rng)
        {
            return rng.NextUInt() / 4294967296f;
        }

        public static bool Chance(this IRandomSource rng, float probability)
        {
            return rng.Value01() < probability;
        }

        /// <summary>
        /// 按权重挑选下标。权重全为 0、列表为空或总和为 0 时返回 -1。
        /// 这是房间类型分配的核心，替换成"先累积和再二分"也只是换这一处。
        /// </summary>
        public static int WeightedIndex(this IRandomSource rng, IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0) return -1;

            double total = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w > 0f) total += w;
            }
            if (total <= 0d) return -1;

            double roll = rng.Value01() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w <= 0f) continue;
                roll -= w;
                if (roll <= 0d) return i;
            }

            // 浮点误差兜底：返回最后一个有效权重
            for (int i = weights.Count - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;
            return -1;
        }

        /// <summary>Fisher-Yates 原地洗牌。注意不要用 OrderBy(random)，那是有偏且会分配的写法。</summary>
        public static void Shuffle<T>(this IRandomSource rng, IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public static T Pick<T>(this IRandomSource rng, IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[rng.Range(0, list.Count)];
        }
    }
}
