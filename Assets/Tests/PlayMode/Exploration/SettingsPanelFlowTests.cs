using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpotlightGameJam.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>通过实际场景按钮验证设置面板的遮罩、重复开关和输入恢复。</summary>
    public sealed class SettingsPanelFlowTests
    {
        private BattleHUD hud;
        private bool background;
        private const string VolumeKey = "SpotlightGameJam.Audio.MasterVolume";
        private bool hadVolume;
        private float savedVolume, savedPreference;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            hadVolume = PlayerPrefs.HasKey(VolumeKey); savedPreference = PlayerPrefs.GetFloat(VolumeKey, 1);
            savedVolume = AudioListener.volume; PlayerPrefs.SetFloat(VolumeKey, .64f);
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; hud = Object.FindObjectOfType<BattleHUD>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.runInBackground = background;
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            if (hadVolume) PlayerPrefs.SetFloat(VolumeKey, savedPreference); else PlayerPrefs.DeleteKey(VolumeKey);
            PlayerPrefs.Save(); AudioListener.volume = savedVolume;
            yield return null;
        }

        /// <summary>重复打开只显示一个设置面板，黑幕拦截空白处，关闭后恢复战斗且不改变资源。</summary>
        [UnityTest]
        public IEnumerator SettingsButtonOpensModalAndContinueRestoresBattle()
        {
            var controller = hud.GetComponent("SettingsPanelController");
            Assert.That(controller, Is.Not.Null, "尚未接入设置面板控制器。");
            var panel = hud.transform.Find("SettingsModal").GetComponent<BasePanel>();
            var backdrop = panel.transform.Find("Blackout").GetComponent<Image>();
            var window = panel.transform.Find("SettingPanel");
            var resume = window.Find("Button_1").GetComponent<Button>();
            var input = hud.transform.Find("BattleLayout_16x9").GetComponent<CanvasGroup>();
            var scene = hud.GetComponent<BattleSceneReferences>();
            var context = hud.Battle.Context;
            var before = context.Player.Health + "/" + context.Player.Sanity + "/" + context.Run.Gold + "/" + hud.Battle.TurnNumber;
            var timeScale = Time.timeScale;
            bool interactable = input.interactable, blocks = input.blocksRaycasts;
            Assert.That(panel.gameObject.activeSelf, Is.False);
            for (int i = 0; i < 3; i++)
            {
                Click(scene.settingsButton);
                yield return new WaitForSecondsRealtime(.25f);
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.IsVisible, Is.True);
                Assert.That(hud.IsChoosing, Is.True);
                Assert.That(input.interactable || input.blocksRaycasts, Is.False);
                Assert.That(backdrop.color.r, Is.Zero);
                Assert.That(backdrop.color.a, Is.InRange(.5f, .8f));
                Assert.That(resume.IsInteractable(), Is.True);
                var raycasts = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(8, 8) }, raycasts);
                Assert.That(raycasts.Count, Is.GreaterThan(0));
                Assert.That(raycasts[0].gameObject, Is.EqualTo(backdrop.gameObject));
                hud.EndTurn();
                Assert.That(context.Player.Health + "/" + context.Player.Sanity + "/" + context.Run.Gold + "/" + hud.Battle.TurnNumber, Is.EqualTo(before));
                Assert.That(Time.timeScale, Is.EqualTo(timeScale));
                Click(resume);
                yield return null;
                Assert.That(panel.gameObject.activeSelf, Is.False);
                Assert.That(hud.IsChoosing, Is.False);
                Assert.That(input.interactable, Is.EqualTo(interactable));
                Assert.That(input.blocksRaycasts, Is.EqualTo(blocks));
            }
            Assert.That(hud.GetComponentsInChildren<BasePanel>(true).Count(value => value.name == "SettingsModal"), Is.EqualTo(1));
        }

        /// <summary>组件停用时立即释放遮罩和输入锁，重新启用后仍可打开。</summary>
        [UnityTest]
        public IEnumerator DisablingSettingsControllerReleasesInput()
        {
            var controller = hud.GetComponent("SettingsPanelController") as Behaviour;
            Assert.That(controller, Is.Not.Null, "尚未接入设置面板控制器。");
            var input = hud.transform.Find("BattleLayout_16x9").GetComponent<CanvasGroup>();
            var panel = hud.transform.Find("SettingsModal").GetComponent<BasePanel>();
            var button = hud.GetComponent<BattleSceneReferences>().settingsButton;
            Click(button); yield return null;
            Assert.That(input.interactable, Is.False);
            controller.enabled = false; yield return null;
            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(input.interactable && input.blocksRaycasts, Is.True);
            controller.enabled = true;
            Click(button); yield return null;
            Assert.That(panel.IsVisible, Is.True);
            controller.SendMessage("Close");
        }

        /// <summary>滑条实时控制总音量，支持静音和最大音量，关闭或切换场景后保留选择。</summary>
        [UnityTest]
        public IEnumerator VolumeSliderAppliesAndPersistsAcrossSceneReload()
        {
            var controller = hud.GetComponent<SettingsPanelController>();
            var slider = hud.transform.Find("SettingsModal/SettingPanel/VolumeSlider")?.GetComponent<Slider>();
            Assert.That(slider, Is.Not.Null, "尚未接入音量滑条。");
            Click(hud.GetComponent<BattleSceneReferences>().settingsButton);
            Assert.That(slider.value, Is.EqualTo(.64f).Within(.001f));
            slider.value = 0; Assert.That(AudioListener.volume, Is.Zero);
            slider.value = 1; Assert.That(AudioListener.volume, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(.2f); Canvas.ForceUpdateCanvases();
            Assert.That(slider.handleRect.rect.height, Is.EqualTo(30).Within(.01f), "滑块应限制在音量栏内。");
            var area = (RectTransform)slider.handleRect.parent;
            var canvas = slider.GetComponentInParent<Canvas>().rootCanvas;
            var position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                area.TransformPoint(new Vector3(area.rect.xMin + area.rect.width * .23f, 0, 0)));
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position,
                pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() } };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.First().gameObject, Is.EqualTo(slider.gameObject), "音量区域必须可以接收指针。");
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(AudioListener.volume, Is.EqualTo(.23f).Within(.001f));
            CaptureSettings(); controller.Close(); controller.Open();
            Assert.That(slider.value, Is.EqualTo(.23f).Within(.001f));
            Assert.That(PlayerPrefs.GetFloat(VolumeKey), Is.EqualTo(.23f).Within(.001f));
            controller.enabled = false;
            slider.value = .8f;
            Assert.That(AudioListener.volume, Is.EqualTo(.23f).Within(.001f), "停用时必须解绑滑条回调。");
            controller.enabled = true; controller.Open();
            Assert.That(slider.value, Is.EqualTo(.23f).Within(.001f));
            controller.Close();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; hud = Object.FindObjectOfType<BattleHUD>();
            Click(hud.GetComponent<BattleSceneReferences>().settingsButton);
            slider = hud.transform.Find("SettingsModal/SettingPanel/VolumeSlider").GetComponent<Slider>();
            Assert.That(slider.value, Is.EqualTo(.23f).Within(.001f));
            Assert.That(AudioListener.volume, Is.EqualTo(.23f).Within(.001f));
        }

        /// <summary>离屏保存设置面板实际画面，用于检查滑条未遮挡原美术。</summary>
        private static void CaptureSettings()
        {
            var camera = Camera.main; var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
            var target = new RenderTexture(1920, 1080, 24); var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.aspect = 1920f / 1080;
                foreach (var graphic in Object.FindObjectsOfType<Graphic>()) graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes("Temp/SettingsAudio-open.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active;
                target.Release(); Object.Destroy(target); Object.Destroy(image);
            }
        }

        /// <summary>返回标题结束当前探索，移除会话与战斗，连续点击只执行一次场景跳转。</summary>
        [UnityTest]
        public IEnumerator ReturnToTitleClearsRunAndLoadsStartupOnce()
        {
            int titleLoads = 0;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> loaded = (scene, mode) => { if (scene.name == "StartupScene") titleLoads++; };
            SceneManager.sceneLoaded += loaded;
            try
            {
#if UNITY_EDITOR
                var setup = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureSetupData>("Assets/GameData/AdventureSetup.asset");
                var session = AdventureSession.GetOrCreate();
                Assert.That(session.TryBegin(setup, setup.defaultCybernetics), Is.True, session.LastError);
                Assert.That(Object.FindObjectOfType<GameBootstrap>().StartSessionEncounter(), Is.True);
                session.Run.AddGold(180);
#endif
                Click(hud.GetComponent<BattleSceneReferences>().settingsButton);
                var button = hud.transform.Find("SettingsModal/SettingPanel/Button_2").GetComponent<Button>();
                Click(button); button.onClick.Invoke();
                float deadline = Time.realtimeSinceStartup + 12;
                while (SceneManager.GetActiveScene().name != "StartupScene" && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("StartupScene"));
                Assert.That(titleLoads, Is.EqualTo(1));
                Assert.That(AdventureSession.Instance, Is.Null, "返回标题后不应保留上一局会话。");
                Assert.That(Object.FindObjectOfType<GameBootstrap>(), Is.Null);
                Assert.That(Object.FindObjectsOfType<SettingsPanelController>().Length, Is.Zero, "黑幕与输入锁应随战斗场景一起释放。");
                Assert.That(AudioListener.volume, Is.EqualTo(.64f).Within(.001f));
            }
            finally { SceneManager.sceneLoaded -= loaded; }
        }

        /// <summary>模拟真实按钮事件，要求按钮当前能够交互。</summary>
        private static void Click(Button button)
        {
            Assert.That(button.IsInteractable(), Is.True, button.name);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
    }
}
