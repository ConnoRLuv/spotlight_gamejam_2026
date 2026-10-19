using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>管理设置面板入口和全屏黑幕，关闭时恢复原有输入，不结算战斗或修改时间倍率。</summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private Button openButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button returnTitleButton;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private string titleScenePath = "Assets/Scenes/StartupScene.unity";
        [SerializeField] private BasePanel panel;
        [SerializeField] private BattleHUD battleHud;
        [SerializeField] private CanvasGroup backgroundInput;
        private bool savedInteractable, savedBlocksRaycasts;
        private GameObject savedSelection;
        private EventSystem inputSystem;
        private bool returningToTitle;
        public bool IsOpen { get; private set; }

        private void OnEnable()
        {
            if (openButton) openButton.onClick.AddListener(Open);
            if (continueButton) continueButton.onClick.AddListener(Close);
            if (returnTitleButton) returnTitleButton.onClick.AddListener(ReturnToTitle);
            GameAudioSettings.ApplySavedVolume();
            if (volumeSlider)
            {
                volumeSlider.SetValueWithoutNotify(GameAudioSettings.MasterVolume);
                volumeSlider.onValueChanged.AddListener(GameAudioSettings.SetMasterVolume);
            }
        }

        private void OnDisable()
        {
            if (openButton) openButton.onClick.RemoveListener(Open);
            if (continueButton) continueButton.onClick.RemoveListener(Close);
            if (returnTitleButton) returnTitleButton.onClick.RemoveListener(ReturnToTitle);
            if (volumeSlider) volumeSlider.onValueChanged.RemoveListener(GameAudioSettings.SetMasterVolume);
            GameAudioSettings.Save(); returningToTitle = false;
            Close();
        }

        private void Update()
        {
            if (IsOpen && !returningToTitle && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnApplicationPause(bool paused) { if (paused) GameAudioSettings.Save(); }
        private void OnApplicationQuit() { GameAudioSettings.Save(); }

        /// <summary>关闭只读牌堆并锁定底层输入；正在选牌、重排或拖拽时不打断当前操作。</summary>
        public void Open()
        {
            if (!isActiveAndEnabled || IsOpen || returningToTitle || !panel || (battleHud && battleHud.IsChoosing)) return;
            if (battleHud) { battleHud.CancelSelection(); battleHud.ClosePilePanels(0); }
            savedInteractable = !backgroundInput || backgroundInput.interactable;
            savedBlocksRaycasts = !backgroundInput || backgroundInput.blocksRaycasts;
            inputSystem = EventSystem.current;
            savedSelection = inputSystem ? inputSystem.currentSelectedGameObject : null;
            if (backgroundInput) backgroundInput.interactable = backgroundInput.blocksRaycasts = false;
            IsOpen = true;
            if (volumeSlider) volumeSlider.SetValueWithoutNotify(GameAudioSettings.MasterVolume);
            panel.transform.SetAsLastSibling();
            panel.Show(.15f);
            if (inputSystem) inputSystem.SetSelectedGameObject(continueButton ? continueButton.gameObject : null);
        }

        /// <summary>继续游戏和 Esc 共用关闭入口，立即撤去黑幕并恢复打开前的输入状态。</summary>
        public void Close()
        {
            if (returningToTitle) return;
            GameAudioSettings.Save();
            if (panel) panel.Hide(0);
            if (!IsOpen) return;
            IsOpen = false;
            if (backgroundInput)
            {
                backgroundInput.interactable = savedInteractable;
                backgroundInput.blocksRaycasts = savedBlocksRaycasts;
            }
            if (inputSystem)
                inputSystem.SetSelectedGameObject(savedSelection && savedSelection.activeInHierarchy ? savedSelection : null);
            savedSelection = null; inputSystem = null;
        }

        /// <summary>结束当前局并异步返回标题；加载期间保留黑幕和输入锁，连续点击只跳转一次。</summary>
        public void ReturnToTitle()
        {
            if (!isActiveAndEnabled || !IsOpen || returningToTitle) return;
            if (!Application.CanStreamedLevelBeLoaded(titleScenePath))
            {
                Debug.LogError("标题场景尚未加入构建列表：" + titleScenePath, this); return;
            }
            returningToTitle = true;
            if (continueButton) continueButton.interactable = false;
            if (returnTitleButton) returnTitleButton.interactable = false;
            if (volumeSlider) volumeSlider.interactable = false;
            GameAudioSettings.Save();
            // 探索会话使用 DontDestroyOnLoad，必须主动移除，避免下一局继承资源、牌组和地图进度。
            if (AdventureSession.Instance) Destroy(AdventureSession.Instance.gameObject);
            SceneManager.LoadSceneAsync(titleScenePath, LoadSceneMode.Single);
        }
    }
}
