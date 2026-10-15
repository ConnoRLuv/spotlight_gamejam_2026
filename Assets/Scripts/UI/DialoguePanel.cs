using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>可复用的双人顺序对话面板；仅负责展示和推进，通过回调通知调用方。</summary>
    public sealed class DialoguePanel : BasePanel
    {
        [SerializeField] private Text leftName;
        [SerializeField] private Text rightName;
        [SerializeField] private Image leftPortrait;
        [SerializeField] private Image rightPortrait;
        [SerializeField] private Text speakerText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text progressText;
        [SerializeField] private Button continueButton;
        [SerializeField] private Text continueLabel;
        [SerializeField] private Button cancelButton;
        private DialogueSequenceData sequence;
        private Action completed;
        private Action cancelled;
        public int CurrentLineIndex { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (continueButton) continueButton.onClick.AddListener(Advance);
            if (cancelButton) cancelButton.onClick.AddListener(Cancel);
        }

        protected override void OnDisable()
        {
            if (continueButton) continueButton.onClick.RemoveListener(Advance);
            if (cancelButton) cancelButton.onClick.RemoveListener(Cancel);
            ClearCallbacks();
            base.OnDisable();
        }

        private void Update()
        {
            if (IsVisible && Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        /// <summary>从首句播放指定对话；完成和取消互斥，每轮最多通知一次。</summary>
        public void Play(DialogueSequenceData value, Action onCompleted, Action onCancelled)
        {
            if (!value || !value.IsValid()) throw new ArgumentException("对话配置必须包含非空台词。", nameof(value));
            // 替换播放内容时清除旧回调，外部停用对象也不会误触发商店跳转。
            ClearCallbacks(); sequence = value; CurrentLineIndex = 0;
            completed = onCompleted; cancelled = onCancelled;
            leftName.text = value.leftName; rightName.text = value.rightName;
            SetPortrait(leftPortrait, value.leftPortrait); SetPortrait(rightPortrait, value.rightPortrait);
            RefreshLine();
            Show(Application.isPlaying ? 0.15f : 0f);
            if (continueButton) continueButton.Select();
        }

        /// <summary>推进一句；最后确认时先关闭并解除回调，再通知调用方，避免重复点击。</summary>
        public void Advance()
        {
            if (!IsVisible || !sequence) return;
            if (CurrentLineIndex + 1 < sequence.lines.Length) { CurrentLineIndex++; RefreshLine(); return; }
            var callback = completed; ClearCallbacks(); Hide(0); callback?.Invoke();
        }

        /// <summary>取消本轮对话，不触发完成回调；Esc 和取消按钮共用该入口。</summary>
        public void Cancel()
        {
            if (!IsVisible) return;
            var callback = cancelled; ClearCallbacks(); Hide(Application.isPlaying ? 0.15f : 0f); callback?.Invoke();
        }

        /// <summary>刷新当前说话人、正文、进度及末句按钮文案。</summary>
        private void RefreshLine()
        {
            var line = sequence.lines[CurrentLineIndex];
            speakerText.text = line.rightSpeaker ? sequence.rightName : sequence.leftName;
            dialogueText.text = line.text;
            progressText.text = (CurrentLineIndex + 1) + " / " + sequence.lines.Length;
            continueLabel.text = CurrentLineIndex == sequence.lines.Length - 1 ? sequence.finalContinueLabel : "继续";
        }

        /// <summary>应用可选立绘；未配置立绘时使用低饱和度色块和角色名称占位。</summary>
        private static void SetPortrait(Image image, Sprite sprite)
        {
            if (!image) return;
            image.sprite = sprite; image.preserveAspect = true;
            image.color = sprite ? Color.white : new Color(0.39f, 0.43f, 0.45f);
        }

        /// <summary>释放本轮对话和调用方引用，防止关闭或再次播放时残留回调。</summary>
        private void ClearCallbacks() { completed = null; cancelled = null; sequence = null; }
    }
}
