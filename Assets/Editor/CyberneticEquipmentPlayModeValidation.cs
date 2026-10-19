using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SpotlightGameJam.Editor
{
    /// <summary>保留原义体栏验证菜单，统一运行卡牌长按流程测试，避免继续维护独立悬停验证逻辑。</summary>
    [InitializeOnLoad]
    public static class CyberneticEquipmentPlayModeValidation
    {
        private const string PendingKey = "Spotlight.EquipmentValidation";
        private static readonly TestRunnerApi Api;

        /// <summary>域重载后重新注册结果回调，测试本身由 Unity Test Runner 管理。</summary>
        static CyberneticEquipmentPlayModeValidation()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Results());
        }

        /// <summary>旧菜单兼容入口；统一验证手牌、锁定牌、只读牌堆、义体栏、开始界面及重排候选。</summary>
        [MenuItem("Spotlight/Equipment/Validate Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请从编辑模式运行。");
            SessionState.SetBool(PendingKey, true);
            Api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode,
                testNames = new[] { "SpotlightGameJam.Tests.CardHoldPreviewFlowTests" } }));
        }

        /// <summary>所有卡牌预览共用验证入口，复用相同测试而不另建一套模拟指针流程。</summary>
        [MenuItem("Spotlight/Cards/Validate Hold Preview Play Mode")]
        public static void ValidateAllCardPreviews() => Run();

        /// <summary>仅记录本菜单启动的测试报告，不影响其他 Test Runner 执行。</summary>
        private sealed class Results : ICallbacks
        {
            /// <summary>测试启动无需改写场景或游戏状态。</summary>
            public void RunStarted(ITestAdaptor testsToRun) { }
            /// <summary>单项用例由测试框架初始化。</summary>
            public void TestStarted(ITestAdaptor test) { }
            /// <summary>单项断言与失败详情由 Test Runner 展示。</summary>
            public void TestFinished(ITestResultAdaptor result) { }
            /// <summary>完成后输出汇总至忽略目录，测试用例自行恢复运行设置。</summary>
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(PendingKey, false)) return;
                SessionState.SetBool(PendingKey, false);
                Directory.CreateDirectory("Logs/Equipment");
                string summary = "卡牌长按预览：通过 " + result.PassCount + "，失败 " + result.FailCount + "，跳过 " + result.SkipCount;
                File.WriteAllText("Logs/Equipment/hold-preview-result.txt", summary);
                if (result.FailCount == 0) Debug.Log(summary); else Debug.LogError(summary);
            }
        }
    }
}
