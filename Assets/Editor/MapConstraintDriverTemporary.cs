using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>本次地图调整的临时验证入口，验证后删除，不挂载场景。</summary>
[InitializeOnLoad]
public static class MapConstraintDriverTemporary
{
    [Serializable] private sealed class Job { public string id, action, mode, filter; public int row; }
    [Serializable] private sealed class Result { public int passed, failed, skipped; public List<string> failures = new List<string>(); }
    private const string Key = "MapWavyBranchValidation";
    private static readonly TestRunnerApi Api;
    private static double nextStatus;
    /// <summary>域重载后重新绑定验证回调，任务身份保持在 SessionState 中。</summary>
    static MapConstraintDriverTemporary()
    {
        Api = ScriptableObject.CreateInstance<TestRunnerApi>(); Api.RegisterCallbacks(new Callbacks());
        EditorApplication.update += Tick;
    }
    /// <summary>仅在编辑器空闲时执行本地验证命令。</summary>
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup >= nextStatus)
        {
            nextStatus = EditorApplication.timeSinceStartup + 5;
            File.WriteAllText("Temp/map-driver-status.txt", "playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling +
                " updating=" + EditorApplication.isUpdating + " job=" + SessionState.GetString(Key, ""));
        }
        const string command = "Temp/map-constraint-command.json";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(command)) return;
        var job = JsonUtility.FromJson<Job>(File.ReadAllText(command));
        if (job == null || string.IsNullOrEmpty(job.id) || SessionState.GetString(Key, "") == job.id) return;
        if (job.action != "capture" && (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)) return;
        SessionState.SetString(Key, job.id);
        try
        {
            if (job.action == "test") Api.Execute(new ExecutionSettings(new Filter { testMode = job.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode,
                testNames = string.IsNullOrEmpty(job.filter) ? null : new[] { job.filter } }));
            else if (job.action == "capture") Capture(job);
            else if (job.action == "finish")
            { EditorSettings.enterPlayModeOptionsEnabled = false; PlayerSettings.runInBackground = false; Write(job.id, "{\"settingsReset\":true}"); }
        }
        catch (Exception e) { Write(job.id, JsonUtility.ToJson(new Result { failed = 1, failures = new List<string> { e.ToString() } })); }
    }
    /// <summary>验证结果仅保存到忽略目录。</summary>
    private static void Write(string id, string text)
    { Directory.CreateDirectory("Logs/MapRoutes"); File.WriteAllText("Logs/MapRoutes/" + id + ".json", text); }
    /// <summary>通过内部相机渲染当前地图，保持原尺寸且居中显示五列，不读取桌面画面。</summary>
    private static void Capture(Job job)
    {
        var view = UnityEngine.Object.FindObjectOfType<MapGraphView>();
        if (!view || view.Graph == null) throw new InvalidOperationException("地图尚未生成。");
        var canvas = view.GetComponentInParent<Canvas>();
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldDistance = canvas.planeDistance;
        var owner = new GameObject("Temporary wavy map capture"); var camera = owner.AddComponent<Camera>();
        var target = new RenderTexture(1920, 2160, 24); var pixels = new Texture2D(1920, 2160, TextureFormat.RGB24, false);
        var oldActive = RenderTexture.active;
        try
        {
            owner.transform.position = new Vector3(0, 0, -100); camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f, .14f, .14f);
            camera.cullingMask = 1 << canvas.gameObject.layer; camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 100;
            Canvas.ForceUpdateCanvases(); view.ScrollToNode(view.Graph.Nodes.First(n => n.Row == job.row).Id);
            typeof(MapGraphView).GetField("_horizontalOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(view, 0f);
            view.SendMessage("LateUpdate"); Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1920, 2160), 0, 0); pixels.Apply();
            File.WriteAllBytes("Logs/MapRoutes/" + job.id + ".png", pixels.EncodeToPNG()); Write(job.id, "{\"captured\":true}");
        }
        finally
        {
            RenderTexture.active = oldActive; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
            camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(owner);
        }
    }
    /// <summary>记录通过计数和失败用例。</summary>
    private sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            var summary = new Result { passed = result.PassCount, failed = result.FailCount, skipped = result.SkipCount };
            Collect(result, summary.failures); Write(SessionState.GetString(Key, "unknown"), JsonUtility.ToJson(summary, true));
        }
        /// <summary>只输出失败叶节点，避免套件重复记录同一错误。</summary>
        private static void Collect(ITestResultAdaptor result, List<string> failures)
        {
            if (result.HasChildren) foreach (var child in result.Children) Collect(child, failures);
            else if (result.TestStatus == TestStatus.Failed) failures.Add(result.FullName + ": " + result.Message + "\n" + result.StackTrace);
        }
    }
}
