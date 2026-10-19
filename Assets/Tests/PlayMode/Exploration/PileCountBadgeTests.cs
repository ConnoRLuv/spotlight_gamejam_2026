using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpotlightGameJam.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SpotlightGameJam.Tests
{
    /// <summary>验证图标圆圈按普通与义体合计显示，随出牌、抽牌与重洗实时刷新。</summary>
    public sealed class PileCountBadgeTests
    {
        /// <summary>通过真实 BattleScene 和战斗入口核验数量，不修改或保存场景状态。</summary>
        [UnityTest]
        public IEnumerator CountsCombineBothDecksAndRefreshAfterPlayDrawAndReshuffle()
        {
            if (AdventureSession.Instance) Object.Destroy(AdventureSession.Instance.gameObject);
            yield return null;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var hud = Object.FindObjectOfType<BattleHUD>(); var context = hud.Battle.Context;
            var refs = hud.GetComponent<BattleSceneReferences>();
            var draw = refs.drawPileButton.transform.Find("Count").GetComponent<Text>();
            var discard = refs.discardPileButton.transform.Find("Count").GetComponent<Text>();
            AssertCounts(context, draw, discard); Assert.That(discard.text, Is.EqualTo("0"));
            Assert.That(refs.drawPileButton.transform.Find("Label").GetComponent<Text>().text, Is.Empty);
            Assert.That(refs.discardPileButton.transform.Find("Label").GetComponent<Text>().text, Is.Empty);
            Assert.That(draw.raycastTarget || discard.raycastTarget, Is.False);
            var basic = context.Ordinary.Hand.Cards.First(card => card.Data.category == CardCategory.Basic);
            Assert.That(hud.Battle.TryPlay(basic, basic.Data.targetType == CardTargetType.SingleEnemy ? context.Enemies[0] : null).Success, Is.True);
            Assert.That(discard.text, Is.EqualTo("1"));
            // 躯干无需选牌，数量验证不依赖当前测试卡组脑机是否需要交互确认。
            var torso = context.Cybernetic.Hand.Cards.Concat(context.Cybernetic.DrawPile.Cards).First(card => card.Source.Data.slot == CyberneticSlot.Torso);
            if (!context.Cybernetic.Hand.Contains(torso)) Assert.That(context.Cybernetic.TakeFromDrawPile(torso), Is.True);
            Assert.That(hud.Battle.TryPlay(torso).Success, Is.True);
            Assert.That(discard.text, Is.EqualTo("2")); AssertCounts(context, draw, discard);
            hud.ShowCybernetic(); AssertCounts(context, draw, discard); hud.ShowOrdinary(); AssertCounts(context, draw, discard);
            Assert.That(hud.Battle.EndTurn(), Is.True); AssertCounts(context, draw, discard);
            foreach (var deck in new[] { context.Ordinary, context.Cybernetic })
                foreach (var card in deck.DrawPile.Cards.ToArray())
                { deck.DrawPile.TryRemove(card); deck.DiscardPile.TryAdd(card); }
            hud.RefreshDisplay(); Assert.That(draw.text, Is.EqualTo("0"));
            Assert.That(hud.Battle.EndTurn(), Is.True); AssertCounts(context, draw, discard);
            Assert.That(int.Parse(draw.text), Is.GreaterThan(0), "抽空后回收弃牌重洗，图标应同步显示新抽牌堆数量。");
        }

        /// <summary>两张数量文本各自合计对应牌区，不包含手牌，不按当前页签过滤。</summary>
        private static void AssertCounts(BattleContext context, Text draw, Text discard)
        {
            Assert.That(draw.text, Is.EqualTo((context.Ordinary.DrawPile.Count + context.Cybernetic.DrawPile.Count).ToString()));
            Assert.That(discard.text, Is.EqualTo((context.Ordinary.DiscardPile.Count + context.Cybernetic.DiscardPile.Count).ToString()));
        }
    }
}
