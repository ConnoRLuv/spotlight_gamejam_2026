using System;
using UnityEngine;

namespace SpotlightGameJam.UI
{
    /// <summary>顺序对话配置；不包含商店跳转、奖励或分支逻辑，可供事件对话复用。</summary>
    [CreateAssetMenu(menuName = "Spotlight/UI/顺序对话")]
    public sealed class DialogueSequenceData : ScriptableObject
    {
        /// <summary>一句对话的角色位置和正文；右侧通常为交谈对象。</summary>
        [Serializable]
        public sealed class Line
        {
            public bool rightSpeaker = true;
            [TextArea(2, 5)] public string text;
        }

        public string leftName = "玩家";
        public string rightName = "商人";
        public Sprite leftPortrait;
        public Sprite rightPortrait;
        public string finalContinueLabel = "结束对话";
        public Line[] lines = Array.Empty<Line>();

        /// <summary>检查必需文本，避免空对话打开后无法推进。</summary>
        public bool IsValid()
        {
            if (lines == null || lines.Length == 0) return false;
            foreach (var line in lines)
                if (line == null || string.IsNullOrWhiteSpace(line.text)) return false;
            return true;
        }
    }
}
