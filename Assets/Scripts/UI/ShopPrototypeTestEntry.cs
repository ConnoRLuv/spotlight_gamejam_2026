using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SpotlightGameJam.UI
{
    /// <summary>仅用于 BattleScene 的商店测试流程；未来地图可直接复用两个面板替换本组件。</summary>
    public sealed class ShopPrototypeTestEntry : MonoBehaviour
    {
        [SerializeField] private BattleHUD hud;
        [SerializeField] private Button testButton;
        [SerializeField] private GameObject modalRoot;
        [SerializeField] private DialoguePanel dialogue;
        [SerializeField] private ShopPanel shop;
        [SerializeField] private DialogueSequenceData merchantDialogue;
        [SerializeField] private CanvasGroup battleInputGroup;
        [SerializeField] private RectTransform toolbar;
        [Tooltip("仅商店测试入口启动时补足的余额；设为 0 关闭，不在每次打开商店时重复赠送。")]
        [SerializeField, Min(0)] private int initialTestCoins = 9999;
        private readonly Vector3[] toolbarCorners = new Vector3[4];
        private BattleContext context;
        private Coroutine closing;
        private bool previousInteractable;
        private bool inputLocked;
        public bool IsOpen { get; private set; }

        /// <summary>等待战斗就绪后仅初始化一次测试余额，不更改正式探索的初始货币规则。</summary>
        private IEnumerator Start()
        {
            while (hud && hud.Battle == null) yield return null;
            if (!hud || hud.Battle == null || initialTestCoins <= 0) yield break;
            var run = hud.Battle.Context.Run;
            if (run.Gold < initialTestCoins) run.AddGold(initialTestCoins - run.Gold);
        }

        private void OnEnable()
        {
            if (testButton) testButton.onClick.AddListener(Open);
            if (shop) shop.CloseRequested += Close;
        }

        private void OnDisable()
        {
            if (testButton) testButton.onClick.RemoveListener(Open);
            if (shop) shop.CloseRequested -= Close;
            if (closing != null) StopCoroutine(closing);
            closing = null; CloseImmediately();
        }

        private void Update()
        {
            if (testButton) testButton.interactable = CanOpen();
            if (IsOpen && (!hud || hud.Battle == null || hud.Battle.Context != context || context.IsFinished)) Close();
        }

        private void LateUpdate()
        {
            if (IsOpen || modalRoot && modalRoot.activeSelf) RefreshLayout();
        }

        /// <summary>将面板与父遮罩限制在工具栏下方，跟随真实布局边界适配分辨率。</summary>
        public void RefreshLayout()
        {
            if (!toolbar || !modalRoot) return;
            var modal = modalRoot.transform as RectTransform;
            var parent = modal ? modal.parent as RectTransform : null;
            if (!parent) return;
            toolbar.GetWorldCorners(toolbarCorners);
            float bottom = parent.InverseTransformPoint(toolbarCorners[0]).y;
            // 顶栏美术下沿包含透明留边，遮罩向上衔接 12 个布局单位，避免露出底层战斗背景。
            float topInset = Mathf.Clamp(parent.rect.yMax - bottom - 12f, 0, parent.rect.height);
            modal.anchorMin = Vector2.zero; modal.anchorMax = Vector2.one;
            modal.offsetMin = Vector2.zero;
            modal.offsetMax = new Vector2(0, -topInset);
        }

        /// <summary>检查战斗和选牌状态；淡出结束前保留遮罩，阻止重复打开。</summary>
        private bool CanOpen()
        {
            return hud && hud.Battle != null && !hud.Battle.Context.IsFinished && !hud.IsChoosing &&
                !IsOpen && modalRoot && !modalRoot.activeSelf && dialogue && shop && merchantDialogue && merchantDialogue.IsValid();
        }

        /// <summary>打开测试首句对话，不修改战斗状态、时间倍率或探索数据。</summary>
        public void Open()
        {
            if (!CanOpen()) return;
            context = hud.Battle.Context; hud.ClosePilePanels(0);
            previousInteractable = !battleInputGroup || battleInputGroup.interactable;
            if (battleInputGroup) battleInputGroup.interactable = false;
            inputLocked = true;
            // 入口自身也隐藏，避免四比三屏下浮在露出的工具栏上。
            if (testButton) { testButton.interactable = false; testButton.gameObject.SetActive(false); }
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            // 合并场景后整个流程父节点可能位于战斗布局之前；同时提升父级和内部遮罩才能真正盖住战斗。
            transform.SetAsLastSibling();
            IsOpen = true; modalRoot.SetActive(true); modalRoot.transform.SetAsLastSibling();
            RefreshLayout();
            dialogue.Play(merchantDialogue, OpenShop, Close);
        }

        /// <summary>对话正常完成后进入商店，取消和生命周期清理不会经过此入口。</summary>
        private void OpenShop()
        {
            if (!IsOpen || !hud || hud.Battle == null || hud.Battle.Context != context || context.IsFinished) { Close(); return; }
            shop.Bind(context.Run); shop.Show(0.15f);
        }

        /// <summary>关闭整个测试流程；淡出期间由父遮罩继续阻止底层 UI 接收点击。</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; dialogue.Hide(0.15f); shop.Hide(0.15f);
            if (Application.isPlaying && isActiveAndEnabled) closing = StartCoroutine(FinishClosing());
            else CloseImmediately();
        }

        /// <summary>按真实时间等待淡出，关闭时不依赖或改变 Time.timeScale。</summary>
        private IEnumerator FinishClosing()
        {
            yield return new WaitForSecondsRealtime(0.15f);
            closing = null; CloseImmediately();
        }

        /// <summary>立即清理面板、绑定和遮罩，适用于停用组件或切换场景。</summary>
        private void CloseImmediately()
        {
            IsOpen = false; context = null;
            if (dialogue) dialogue.Hide(0);
            if (shop) { shop.Hide(0); shop.Bind(null); }
            if (modalRoot) modalRoot.SetActive(false);
            if (inputLocked && battleInputGroup) battleInputGroup.interactable = previousInteractable;
            if (inputLocked && testButton) testButton.gameObject.SetActive(true);
            if (inputLocked && hud) hud.RefreshDisplay();
            inputLocked = false;
        }
    }
}
