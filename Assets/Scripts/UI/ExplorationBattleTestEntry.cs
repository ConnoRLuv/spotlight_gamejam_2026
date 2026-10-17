using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotlightGameJam.UI
{
    /// <summary>临时探索测试入口：出发准备好会话后进入战斗；接入地图流程时停用或移除此组件。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StartSceneController))]
    public sealed class ExplorationBattleTestEntry : MonoBehaviour
    {
        [SerializeField] private StartSceneController source;
        [SerializeField] private string battleScenePath = "Assets/Scenes/BattleScene.unity";
        private bool loading;
        private void OnEnable()
        {
            if (!source) source = GetComponent<StartSceneController>();
            if (source) source.ExplorationRequested += EnterBattle;
        }
        private void OnDisable()
        { if (source) source.ExplorationRequested -= EnterBattle; }
        /// <summary>只消费本次准备成功的探索会话，异步进入战斗，不重新创建牌组或重置玩家属性。</summary>
        private void EnterBattle(RunState run)
        {
            if (loading || run == null) return;
            var session = AdventureSession.Instance;
            if (!session || !ReferenceEquals(session.Run, run))
            { Debug.LogError("探索会话与出发牌组不一致，无法进入测试战斗。",this); return; }
            if (!Application.CanStreamedLevelBeLoaded(battleScenePath))
            { Debug.LogError("测试战斗场景未启用，请检查 Build Settings：" + battleScenePath,this); return; }
            // 会话通过 DontDestroyOnLoad 保留；BattleScene 的 GameBootstrap 自动读取它。
            loading = true;
            SceneManager.LoadSceneAsync(battleScenePath,LoadSceneMode.Single);
        }
    }
}
