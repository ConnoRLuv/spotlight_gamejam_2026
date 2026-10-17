using System;
using System.Collections.Generic;

namespace ImplantSystem
{
    /// <summary>命令行演示：验证资源、回合、基本牌、义体耐久与四种效果的联动。</summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var state = new CombatState
            {
                Hp = 30, MaxHp = 30,
                Sanity = 100, MaxSanity = 100,
                MaxActionPoints = 2, ActionPoints = 2   // AP 初始 2
            };
            state.OnLog += msg => Console.WriteLine("  " + msg);

            foreach (ImplantDefinition def in DefaultImplants.Create())
                state.ImplantDeck.AddToTop(new Implant(def));
            state.ImplantDeck.Shuffle(new Random(1));

            var engine = new ImplantEngine(EffectCatalog.CreateDefault());

            Console.WriteLine("== 开局（AP " + state.ActionPoints + "，理智 " + state.Sanity + "）==\n");

            state.BeginTurn();
            Print(state, "第 1 回合开始");

            Console.WriteLine("\n== 打基本牌 ==");
            state.TryPlayCard(new Card(CardKind.Attack));
            state.TryPlayCard(new Card(CardKind.Defend));
            state.TryPlayCard(new Card(CardKind.Overload));

            Console.WriteLine("\n== 打出脑机（耐久 -1，本局次数 +1，看顶 3 选 1）==");
            Implant brain = Grab(state, "brain");
            engine.TryPlay(state, brain);
            if (state.Pending != null)
            {
                IReadOnlyList<Implant> opts = state.Pending.Options;
                for (int i = 0; i < opts.Count; i++) Console.WriteLine("    [" + i + "] " + opts[i]);
                engine.ResolveChoice(state, 0);
            }
            Console.WriteLine("  脑机打出后：" + brain);

            Console.WriteLine("\n== 打出躯干，然后受 6 点伤害 ==");
            Implant torso = Grab(state, "torso");
            engine.TryPlay(state, torso);
            state.TakeDamage(6);
            Console.WriteLine("  理智 = " + state.Sanity + "，护盾 = " + state.Shield);

            Console.WriteLine("\n== 打出腿部 ==");
            Implant leg = Grab(state, "leg");
            engine.TryPlay(state, leg);

            Console.WriteLine("\n== 回合结束（护盾清空、本回合标记清空、额外回合保留）==");
            state.EndTurn();
            Console.WriteLine("  护盾 = " + state.Shield +
                              "，attacks_free = " + state.TurnFlags.Contains("attacks_free") +
                              "，待用额外回合 = " + state.PendingExtraTurns);

            Console.WriteLine("\n== 耐久与使用次数单元验证（耐久 5 / 每部位最多 3 次）==");
            var testDef = new ImplantDefinition
            {
                Key = "t", DisplayName = "测试义体",
                Durability = 5, SanityCost = 0, MaxUsesPerRun = 3
            };
            var imp = new Implant(testDef);
            for (int i = 1; i <= 4; i++)
            {
                bool can = imp.CanBePlayed;
                if (can) { imp.ReduceDurability(1); imp.RecordUse(); }
                Console.WriteLine("  第 " + i + " 次：可打出=" + can + " -> " + imp);
            }
            imp.Repair(10);
            Console.WriteLine("  修复后：" + imp + "（耐久回满，但本局使用次数不会恢复）");
        }

        static void Print(CombatState state, string title)
        {
            Console.WriteLine("[" + title + "] AP=" + state.ActionPoints + "/" + state.MaxActionPoints +
                              " 理智=" + state.Sanity + " 护盾=" + state.Shield);
            Console.Write("  正常手牌：");
            for (int i = 0; i < state.NormalHand.Count; i++) Console.Write(" " + state.NormalHand[i]);
            Console.Write("\n  义体手牌：");
            for (int i = 0; i < state.ImplantHand.Count; i++) Console.Write(" " + state.ImplantHand[i]);
            Console.WriteLine();
        }

        static Implant Grab(CombatState state, string key)
        {
            for (int i = 0; i < state.ImplantHand.Count; i++)
                if (state.ImplantHand[i].Definition.Key == key) return state.ImplantHand[i];

            while (state.ImplantDeck.Count > 0)
            {
                Implant card = state.ImplantDeck.Draw();
                state.ImplantHand.Add(card);
                if (card.Definition.Key == key) return card;
            }
            throw new InvalidOperationException("牌堆和手牌里都没有 " + key);
        }
    }
}
