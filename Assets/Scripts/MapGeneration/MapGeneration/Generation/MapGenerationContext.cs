using System.Collections.Generic;
using RoguelikeMap.Config;
using RoguelikeMap.Core;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;

namespace RoguelikeMap.Generation
{
    /// <summary>
    /// 生成过程中所有步骤共享的上下文。
    /// 以后要加新的共享状态（生物群系、美术主题、难度参数……），
    /// 加字段到这里，所有步骤都能直接读到，不用改任何方法签名。
    /// </summary>
    public sealed class MapGenerationContext
    {
        public MapGenerationConfig Config;
        public NodeTypeCatalog Catalog;
        public IRandomSource Random;
        public MapRuleSet Rules;
        public MapGraph Graph;

        /// <summary>生成日志。校验失败时会带出去，方便定位是哪一步出的问题。</summary>
        public readonly List<string> Diagnostics = new List<string>(8);

        public void Log(string message)
        {
            if (!string.IsNullOrEmpty(message)) Diagnostics.Add(message);
        }
    }

    /// <summary>
    /// 生成流水线的一环。
    ///
    /// 【扩展点】整个地图生成就是若干 Step 顺序执行。想加"生物群系""地图修饰器"
    /// "对称结构"之类的功能，写一个新 Step 插进管线即可，已有步骤一行都不用动。
    /// </summary>
    public interface IMapGenerationStep
    {
        string Name { get; }
        void Execute(MapGenerationContext context);
    }
}
