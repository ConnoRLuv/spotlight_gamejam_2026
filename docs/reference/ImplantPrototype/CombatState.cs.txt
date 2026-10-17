using System;
using System.Collections.Generic;

namespace ImplantSystem
{
    public enum CardKind
    {
        Attack,    // 攻击：6 伤害，1 AP
        Defend,    // 防御：3 护盾，1 AP
        Heal,      // 回复：3 血量，1 AP
        Overload   // 过载：5 理智 → +1 AP（不消耗 AP）
    }

    /// <summary>基本牌 / 功能牌。数值先写死，牌堆构成属于"其他的"，以后再接。</summary>
    public sealed class Card
    {
        public readonly CardKind Kind;
        public readonly int ActionPointCost;
        public readonly int SanityCost;

        public Card(CardKind kind)
        {
            Kind = kind;
            if (kind == CardKind.Overload)
            {
                ActionPointCost = 0;
                SanityCost = 5;
            }
            else
            {
                ActionPointCost = 1;
                SanityCost = 0;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case CardKind.Attack: return "攻击（6 伤害，1 AP）";
                case CardKind.Defend: return "防御（3 护盾，1 AP）";
                case CardKind.Heal: return "回复（3 血量，1 AP）";
                default: return "过载（5 理智 → +1 AP）";
            }
        }
    }

    /// <summary>
    /// 一场战斗里的玩家完整状态。义体逻辑、基本牌逻辑都只依赖这个类，不碰 UI。
    /// 接 Unity 时表现层监听 OnLog 刷新界面即可。
    /// </summary>
    public sealed class CombatState
    {
        public const int ApCap = 12;            // AP 上限
        public const int NormalHandSize = 2;    // 基本牌+功能牌每回合补到 2 张
        public const int ImplantHandLimit = 5;  // 义体一回合持有上限

        // 血量
        public int Hp;
        public int MaxHp;

        // 理智值
        public int Sanity;
        public int MaxSanity;

        // 行动点：初始 2，每回合上限 +1（上限 12），回合开始回满
        public int MaxActionPoints;
        public int ActionPoints;

        // 护盾（不保留到下回合）
        public int Shield;

        // 基本牌 + 功能牌（正常手牌）
        public readonly List<Card> NormalHand = new List<Card>();

        // 义体牌：独立于正常手牌，三堆（抽牌堆 / 手牌 / 弃牌堆）
        public readonly ImplantDeck ImplantDeck = new ImplantDeck();
        public readonly List<Implant> ImplantHand = new List<Implant>();
        public readonly List<Implant> ImplantDiscard = new List<Implant>();

        /// <summary>本回合生效、回合结束清空的状态标记（躯干、手部这类"本回合内"效果）。</summary>
        public readonly HashSet<string> TurnFlags = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>腿部：已获得但还没用的额外行动回合（跨回合保留）。</summary>
        public int PendingExtraTurns;

        /// <summary>待玩家选择项（脑机"三选一"）。</summary>
        public PendingChoice Pending;

        public event Action<string> OnLog;

        int _basicCursor;

        public void Log(string message)
        {
            if (OnLog != null) OnLog(message);
        }

        // ------------------------------------------------------------------
        // 回合
        // ------------------------------------------------------------------

        /// <summary>回合开始：AP 回满再让上限 +1，抽基本牌补到 2 张，自动抽 1 张义体。</summary>
        public void BeginTurn()
        {
            ActionPoints = MaxActionPoints;                                  // 回满
            MaxActionPoints = Math.Min(MaxActionPoints + 1, ApCap);          // 为下一回合增长

            while (NormalHand.Count < NormalHandSize)                        // 基本牌+功能牌补到 2
                NormalHand.Add(DrawBasicCard());

            DrawImplant(1);                                                  // 义体自动抽 1 张
        }

        public void EndTurn()
        {
            TurnFlags.Clear();     // "本回合内"的效果失效
            Shield = 0;            // 护盾不保留
            DiscardExcessImplants();
        }

        // ------------------------------------------------------------------
        // 抽牌
        // ------------------------------------------------------------------

        public void DrawImplant(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Implant card = ImplantDeck.Draw();
                if (card == null)
                {
                    RecycleImplantDiscard();
                    card = ImplantDeck.Draw();
                    if (card == null) break;
                }
                ImplantHand.Add(card);
            }
            DiscardExcessImplants();
        }

        void RecycleImplantDiscard()
        {
            for (int i = 0; i < ImplantDiscard.Count; i++)
                ImplantDeck.AddToTop(ImplantDiscard[i]);
            ImplantDiscard.Clear();
            ImplantDeck.Shuffle(new Random(Environment.TickCount));
        }

        /// <summary>义体手牌超过持有上限时，把多余的弃入弃牌堆。</summary>
        public void DiscardExcessImplants()
        {
            while (ImplantHand.Count > ImplantHandLimit)
            {
                Implant extra = ImplantHand[ImplantHand.Count - 1];
                ImplantHand.RemoveAt(ImplantHand.Count - 1);
                ImplantDiscard.Add(extra);
            }
        }

        /// <summary>占位：抽一张基本牌/功能牌。真正的牌堆构成属于"其他的"，暂用轮换顺序。</summary>
        public Card DrawBasicCard()
        {
            CardKind[] kinds = { CardKind.Attack, CardKind.Defend, CardKind.Heal, CardKind.Overload };
            return new Card(kinds[_basicCursor++ % kinds.Length]);
        }

        // ------------------------------------------------------------------
        // 打基本牌 / 功能牌
        // ------------------------------------------------------------------

        public bool TryPlayCard(Card card)
        {
            if (card == null) return false;

            if (card.Kind == CardKind.Overload)
            {
                if (Sanity < card.SanityCost) { Log("理智不足，无法打过载。"); return false; }
                Sanity -= card.SanityCost;
                ActionPoints = Math.Min(ActionPoints + 1, ApCap);
                Log("过载：消耗 5 理智，行动点 +1（当前 " + ActionPoints + "）。");
                return true;
            }

            int apCost = card.ActionPointCost;
            // 手部义体：本回合攻击牌免行动点
            if (card.Kind == CardKind.Attack && TurnFlags.Contains("attacks_free"))
                apCost = 0;

            if (ActionPoints < apCost) { Log("行动点不足，无法打 " + card + "。"); return false; }
            ActionPoints -= apCost;

            switch (card.Kind)
            {
                case CardKind.Attack:
                    Log("攻击：对敌人造成 6 点伤害。");
                    break;
                case CardKind.Defend:
                    Shield += 3;
                    Log("防御：获得 3 点护盾（当前 " + Shield + "）。");
                    break;
                case CardKind.Heal:
                    Hp = Math.Min(Hp + 3, MaxHp);
                    Log("回复：血量 +3（当前 " + Hp + "）。");
                    break;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 伤害结算
        // ------------------------------------------------------------------

        /// <summary>
        /// 玩家受到伤害。顺序：躯干义体（下一次伤害转理智）→ 护盾吸收 → 扣血。
        /// 返回最终落到血量上的伤害（被转理智/被护盾吸收则返回 0）。
        /// </summary>
        public int TakeDamage(int amount)
        {
            if (amount <= 0) return 0;

            if (TurnFlags.Remove("redirect_next_damage"))
            {
                int lost = Math.Min(amount, Sanity);
                Sanity -= lost;
                Log("躯干义体：把 " + amount + " 点伤害转为扣 " + lost + " 点理智。");
                return 0;
            }

            int remaining = amount;
            if (Shield > 0)
            {
                int absorbed = Math.Min(Shield, remaining);
                Shield -= absorbed;
                remaining -= absorbed;
                Log("护盾吸收 " + absorbed + " 点伤害。");
                if (remaining <= 0) return 0;
            }

            Hp -= remaining;
            if (Hp < 0) Hp = 0;
            Log("受到 " + remaining + " 点伤害（血量 " + Hp + "）。");
            return remaining;
        }
    }

    /// <summary>一个待选择项。效果 Activate 产生它，引擎在玩家选完后调 Resolve。</summary>
    public sealed class PendingChoice
    {
        public IImplantEffect Owner;
        public string Prompt;
        public IReadOnlyList<Implant> Options;
        public bool IsResolved { get; internal set; }
    }
}
