using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace SpotlightGameJam.UI
{
    /// <summary>探索前的四部位牌组配置界面；确认选择与出发事件不承担地图跳转。</summary>
    public sealed class StartSceneController : MonoBehaviour
    {
        [SerializeField] private AdventureSetupData setup;
        [SerializeField] private CardPrefabCatalog catalog;
        [SerializeField] private Button[] partButtons;
        [SerializeField] private Button replaceButton, unequipButton, clearButton, exploreButton;
        [SerializeField] private Button settingsButton, storyButton;
        [SerializeField] private RectTransform optionsRoot, cardPreviewRoot;
        [SerializeField] private Text detailTitle, detailInfo, summary, buffs, status;
        private readonly CyberneticData[] selected = new CyberneticData[4];
        private readonly List<Button> options = new List<Button>();
        private CyberneticSlot part;
        private CyberneticData candidate;
        private bool initialized;
        private CardView previewCard;
        private Vector2 previewSize;
        public IReadOnlyList<CyberneticData> Selection => selected.Where(value => value).ToArray();
        public event Action<RunState> ExplorationRequested;
        private void OnEnable()
        {
            if (partButtons != null && partButtons.Length == 4)
            {
                partButtons[0].onClick.AddListener(ShowBrain); partButtons[1].onClick.AddListener(ShowTorso);
                partButtons[2].onClick.AddListener(ShowHands); partButtons[3].onClick.AddListener(ShowLegs);
            }
            if (replaceButton) replaceButton.onClick.AddListener(ConfirmSelection);
            if (unequipButton) unequipButton.onClick.AddListener(UnequipPart);
            if (clearButton) clearButton.onClick.AddListener(ClearSelection);
            if (exploreButton) exploreButton.onClick.AddListener(PrepareExploration);
            if (settingsButton) settingsButton.onClick.AddListener(SettingsPlaceholder);
            if (storyButton) storyButton.onClick.AddListener(StoryPlaceholder);
            Initialize();
        }
        private void OnDisable()
        {
            if (partButtons != null && partButtons.Length == 4)
            {
                partButtons[0].onClick.RemoveListener(ShowBrain); partButtons[1].onClick.RemoveListener(ShowTorso);
                partButtons[2].onClick.RemoveListener(ShowHands); partButtons[3].onClick.RemoveListener(ShowLegs);
            }
            if (replaceButton) replaceButton.onClick.RemoveListener(ConfirmSelection);
            if (unequipButton) unequipButton.onClick.RemoveListener(UnequipPart);
            if (clearButton) clearButton.onClick.RemoveListener(ClearSelection);
            if (exploreButton) exploreButton.onClick.RemoveListener(PrepareExploration);
            if (settingsButton) settingsButton.onClick.RemoveListener(SettingsPlaceholder);
            if (storyButton) storyButton.onClick.RemoveListener(StoryPlaceholder);
        }
        private void LateUpdate()
        {
            if (previewCard && cardPreviewRoot && previewSize != cardPreviewRoot.rect.size) ScalePreview();
        }
        /// <summary>构建场景时显式连接引用，不依赖运行时查找对象名。</summary>
        public void Configure(AdventureSetupData data, CardPrefabCatalog prefabs, Button[] parts,
            Button replace, Button unequip, Button clear, Button explore, Button settings, Button story,
            RectTransform list, RectTransform preview, Text title, Text info, Text equipped, Text bonus, Text feedback)
        {
            setup = data; catalog = prefabs; partButtons = parts;
            replaceButton = replace; unequipButton = unequip; clearButton = clear; exploreButton = explore;
            settingsButton = settings; storyButton = story; optionsRoot = list; cardPreviewRoot = preview;
            detailTitle = title; detailInfo = info; summary = equipped; buffs = bonus; status = feedback;
            Initialize();
        }
        /// <summary>首次加载采用默认选择；重新启用面板不覆盖玩家已修改的选择。</summary>
        private void Initialize()
        {
            if (!setup) return;
            if (!initialized)
            {
                foreach (var data in setup.defaultCybernetics ?? Array.Empty<CyberneticData>())
                    if (data && Enum.IsDefined(typeof(CyberneticSlot), data.slot)) selected[(int)data.slot] = data;
                initialized = true;
            }
            SelectPart(part);
        }
        /// <summary>选择脑机部位。</summary>
        private void ShowBrain() => SelectPart(CyberneticSlot.Brain);
        /// <summary>选择躯干部位。</summary>
        private void ShowTorso() => SelectPart(CyberneticSlot.Torso);
        /// <summary>选择手部部位。</summary>
        private void ShowHands() => SelectPart(CyberneticSlot.Hands);
        /// <summary>选择腿部部位。</summary>
        private void ShowLegs() => SelectPart(CyberneticSlot.Legs);
        /// <summary>按部位过滤可选义体，保留其他三个部位的选择。</summary>
        public void SelectPart(CyberneticSlot slot)
        {
            if (!Enum.IsDefined(typeof(CyberneticSlot), slot) || !setup) return;
            part = slot; candidate = selected[(int)slot]; BuildOptions(); Refresh();
        }
        /// <summary>预览候选卡面，只有点击更换才写入出发选择。</summary>
        public void PreviewCybernetic(CyberneticData data)
        {
            if (!data || data.slot != part || !(setup.availableCybernetics ?? Array.Empty<CyberneticData>()).Contains(data)) return;
            candidate = data; Refresh();
        }
        /// <summary>确认当前部位候选，不创建运行时义体牌或消耗耐久。</summary>
        public void ConfirmSelection()
        {
            if (!candidate || candidate.Validate().Count > 0) return;
            selected[(int)part] = candidate; MarkChanged(); Refresh();
        }
        /// <summary>卸下当前部位；空部位也允许出发。</summary>
        public void UnequipPart() { selected[(int)part] = null; candidate = null; MarkChanged(); Refresh(); }
        /// <summary>全部清空为无义体牌组，不影响普通牌配置。</summary>
        public void ClearSelection() { Array.Clear(selected, 0, selected.Length); candidate = null; MarkChanged(); Refresh(); }
        /// <summary>修改配置后允许重新准备；已有探索不会随预览或修改被偷偷更改。</summary>
        private void MarkChanged()
        { if (status) status.text = "牌组已修改"; if (exploreButton) exploreButton.interactable = true; }
        /// <summary>准备新探索并发布事件，不加载场景；按钮防止重复点击重置探索状态。</summary>
        public void PrepareExploration()
        {
            if (exploreButton && !exploreButton.interactable) return;
            var session = AdventureSession.GetOrCreate();
            if (!session.TryBegin(setup, Selection)) { if (status) status.text = session.LastError; return; }
            if (status) status.text = "探索已准备";
            if (exploreButton) exploreButton.interactable = false;
            ExplorationRequested?.Invoke(session.Run);
        }
        /// <summary>设置功能留待后续接入，点击给出明确反馈。</summary>
        private void SettingsPlaceholder() { if (status) status.text = "设置功能待接入"; }
        /// <summary>故事书功能留待后续接入，点击给出明确反馈。</summary>
        private void StoryPlaceholder() { if (status) status.text = "故事书待接入"; }
        /// <summary>按配置生成候选按钮，名称、张数及同种共享耐久上限直接读取资产。</summary>
        private void BuildOptions()
        {
            if (!optionsRoot || !partButtons?.FirstOrDefault()) return;
            ClearChildren(optionsRoot); options.Clear();
            foreach (var data in (setup.availableCybernetics ?? Array.Empty<CyberneticData>()).Where(value => value && value.slot == part).Distinct())
            {
                var button = Instantiate(partButtons[0], optionsRoot); button.name = data.name + "Option";
                button.onClick = new Button.ButtonClickedEvent();
                var captured = data; button.onClick.AddListener(() => PreviewCybernetic(captured));
                var text = button.GetComponentInChildren<Text>();
                if (text) { text.fontSize = 16; text.text = data.cards[0].cardName + "\n" + data.cards.Length + " 张\n共享耐久 " + data.maxDurability; }
                options.Add(button);
            }
        }
        /// <summary>更新牌组摘要、共享耐久提示和预计 buff，不改变实际探索状态。</summary>
        private void Refresh()
        {
            var names = new[] { "脑机", "躯干", "手部", "腿部" };
            for (int i = 0; partButtons != null && i < partButtons.Length && i < 4; i++)
            {
                var label = partButtons[i].GetComponentInChildren<Text>();
                if (label) label.text = names[i] + "\n" + (selected[i] ? selected[i].cards.Length + " 张" : "未装备");
                partButtons[i].GetComponent<Image>().color = i == (int)part ? new Color(.46f, .52f, .52f) : new Color(.46f, .46f, .46f);
            }
            if (summary) summary.text = string.Join("\n", Enumerable.Range(0, 4).Select(i =>
                names[i] + "：" + (selected[i] ? selected[i].cards[0].cardName + " ×" + selected[i].cards.Length : "未装备"))) +
                "\n义体牌合计：" + Selection.Sum(data => data.cards.Length) + " 张";
            if (buffs)
            {
                var preview = Selection.SelectMany(data => data.cards.Select(card => new CyberneticInstance(data, card))).ToArray();
                var available = (setup.buffs ?? Array.Empty<RunBuffData>()).Where(buff => buff && buff.Validate().Count == 0 && buff.Matches(preview)).ToArray();
                buffs.text = available.Length == 0 ? "可触发 buff：无完整组合" : "可触发 buff（同回合打出四部位）\n" +
                    string.Join("\n", available.Select(buff => buff.buffName + "\n" + buff.description));
            }
            if (detailTitle) detailTitle.text = candidate ? candidate.cards[0].cardName : "未选择义体";
            if (detailInfo) detailInfo.text = candidate ? names[(int)candidate.slot] + " · " + candidate.cards.Length +
                " 张\n共享耐久 " + candidate.maxDurability : "该部位可留空出发";
            if (replaceButton) replaceButton.interactable = candidate;
            RefreshCardPreview();
        }
        /// <summary>预览使用现有卡牌预制体，关闭卡面的出牌交互。</summary>
        private void RefreshCardPreview()
        {
            if (!cardPreviewRoot) return;
            ClearChildren(cardPreviewRoot);
            if (!candidate || !catalog) return;
            var prefab = catalog.Find(candidate.cards[0]); if (!prefab) return;
            var card = Instantiate(prefab, cardPreviewRoot); previewCard = card; card.SetInteractable(false);
            foreach (var graphic in card.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            var rect = card.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.anchoredPosition = Vector2.zero;
            ScalePreview();
        }
        /// <summary>窗口比例变化时重新适配卡面，不重新生成候选或丢失当前选择。</summary>
        private void ScalePreview()
        {
            if (!previewCard || !cardPreviewRoot) return;
            previewSize = cardPreviewRoot.rect.size;
            var rect = previewCard.GetComponent<RectTransform>();
            if (rect.rect.width <= 0 || rect.rect.height <= 0) return;
            rect.localScale = Vector3.one * Mathf.Min(previewSize.x / rect.rect.width, previewSize.y / rect.rect.height);
        }
        /// <summary>替换展示时立即停用旧对象，避免延迟销毁的对象继续拦截鼠标。</summary>
        private static void ClearChildren(Transform root)
        {
            foreach (Transform child in root.Cast<Transform>().ToArray())
            {
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
            }
        }
    }
}
