using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 集中保存 BattleScene 的展示控件，供后续战斗 UI 绑定使用。
    /// 场景当前展示策划线框图；此组件不创建战斗，也不修改战斗状态。
    /// </summary>
    public sealed class BattleSceneReferences : MonoBehaviour
    {
        [Header("顶部状态栏")]
        public Text healthText;
        public Text goldText;
        // 战斗顶栏已移除地图进度，保留旧字段供既有场景序列化兼容，允许为空。
        public Text mapProgressText;
        public Button settingsButton;
        public SettingsPanelController settingsPanel;

        [Header("角色展示区")]
        public Image playerPortrait;
        public Text playerHealthText;
        public Text sanityText;
        public RectTransform cyberneticDurabilityArea;
        public Image enemyPortrait;
        public Text enemyHealthText;
        public Text enemyNameText;
        public GameObject enemyTooltip;

        [Header("角色资源条")]
        public Image playerHealthFill;
        public Image playerSanityFill;
        public Image enemyHealthFill;
        public Text playerShieldText;
        public Text enemyShieldText;

        [Header("手牌与回合操作")]
        public Text actionPointsText;
        public RectTransform handArea;
        public Button endTurnButton;
        public Button drawPileButton;
        public Button discardPileButton;
    }
}
