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
        public Text mapProgressText;
        public Button settingsButton;

        [Header("角色展示区")]
        public Image playerPortrait;
        public Text playerHealthText;
        public Text sanityText;
        public RectTransform cyberneticDurabilityArea;
        public Image enemyPortrait;
        public Text enemyHealthText;
        public Text enemyNameText;
        public GameObject enemyTooltip;

        [Header("手牌与回合操作")]
        public Text actionPointsText;
        public RectTransform handArea;
        public Button endTurnButton;
        public Button drawPileButton;
        public Button discardPileButton;
    }
}
