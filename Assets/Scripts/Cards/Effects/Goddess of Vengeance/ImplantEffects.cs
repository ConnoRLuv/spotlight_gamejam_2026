using System;
using System.Collections.Generic;

namespace ImplantSystem
{
    /// <summary>
    /// 义体效果接口。一个效果 = 一个新类 + 注册进 EffectCatalog，引擎不用改。
    /// Activate 可能产生 PendingChoice，Resolve 在玩家选完后结算。
    /// </summary>
    public interface IImplantEffect
    {
        string Key { get; }
        string Description { get; }
        bool CanActivate(CombatState state, Implant implant);
        void Activate(CombatState state, Implant implant);
        void Resolve(CombatState state, int chosenIndex);
    }

    public abstract class ImplantEffectBase : IImplantEffect
    {
        public abstract string Key { get; }
        public abstract string Description { get; }
        public abstract bool CanActivate(CombatState state, Implant implant);
        public abstract void Activate(CombatState state, Implant implant);
        public virtual void Resolve(CombatState state, int chosenIndex) { }
    }

    /// <summary>脑机：看义体牌堆顶 N 张，选 1 张加入义体手牌。</summary>
    public sealed class ScryTakeEffect : ImplantEffectBase
    {
        public override string Key => "scry_take";
        public override string Description => "观看义体牌堆顶若干张，选一张加入手牌";

        public override bool CanActivate(CombatState state, Implant implant)
        {
            return state.ImplantDeck.Count > 0;
        }

        public override void Activate(CombatState state, Implant implant)
        {
            int n = implant.Definition.EffectParam > 0 ? implant.Definition.EffectParam : 3;
            List<Implant> top = state.ImplantDeck.PeekTop(n);
            state.Pending = new PendingChoice { Owner = this, Prompt = "选择一张加入义体手牌", Options = top };
            state.Log("军用战术协调矩阵：观看牌堆顶 " + top.Count + " 张。");
        }

        public override void Resolve(CombatState state, int chosenIndex)
        {
            IReadOnlyList<Implant> options = state.Pending != null ? state.Pending.Options : null;
            if (options == null || chosenIndex < 0 || chosenIndex >= options.Count) return;

            Implant picked = options[chosenIndex];
            state.ImplantDeck.RemoveCard(picked);
            state.ImplantHand.Add(picked);
            state.DiscardExcessImplants();
            state.Log("把 " + picked + " 加入义体手牌。");
        }
    }

    /// <summary>躯干：本回合内，下一次受到的伤害转为消耗等量理智。</summary>
    public sealed class DamageRedirectEffect : ImplantEffectBase
    {
        public override string Key => "damage_redirect";
        public override string Description => "本回合内，下一次受到的伤害改为消耗等量理智值";

        public override bool CanActivate(CombatState state, Implant implant)
        {
            return !state.TurnFlags.Contains("redirect_next_damage");
        }

        public override void Activate(CombatState state, Implant implant)
        {
            state.TurnFlags.Add("redirect_next_damage");
            state.Log("军用生命维持器已启动：本回合下一次伤害将转为扣理智。");
        }
    }

    /// <summary>手部：本回合内，攻击牌无需消耗行动点。</summary>
    public sealed class FreeAttacksEffect : ImplantEffectBase
    {
        public override string Key => "free_attacks";
        public override string Description => "本回合内，攻击牌无需消耗行动点";

        public override bool CanActivate(CombatState state, Implant implant)
        {
            return !state.TurnFlags.Contains("attacks_free");
        }

        public override void Activate(CombatState state, Implant implant)
        {
            state.TurnFlags.Add("attacks_free");
            state.Log("军用脉冲发射器已启动：本回合攻击牌免行动点。");
        }
    }

    /// <summary>腿部：多获得一个行动回合。</summary>
    public sealed class ExtraTurnEffect : ImplantEffectBase
    {
        public override string Key => "extra_turn";
        public override string Description => "多获得一个行动回合";

        public override bool CanActivate(CombatState state, Implant implant)
        {
            return true;
        }

        public override void Activate(CombatState state, Implant implant)
        {
            state.PendingExtraTurns++;
            state.Log("军用战术推进器：获得一个额外行动回合（待用 " + state.PendingExtraTurns + "）。");
        }
    }

    /// <summary>效果目录：字符串键 → 效果。新效果 Add 一行即可。</summary>
    public sealed class EffectCatalog
    {
        readonly Dictionary<string, IImplantEffect> _effects =
            new Dictionary<string, IImplantEffect>(StringComparer.Ordinal);

        public EffectCatalog Add(IImplantEffect effect)
        {
            if (effect != null) _effects[effect.Key] = effect;
            return this;
        }

        public IImplantEffect Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            IImplantEffect effect;
            return _effects.TryGetValue(key, out effect) ? effect : null;
        }

        public static EffectCatalog CreateDefault()
        {
            return new EffectCatalog()
                .Add(new ScryTakeEffect())
                .Add(new DamageRedirectEffect())
                .Add(new FreeAttacksEffect())
                .Add(new ExtraTurnEffect());
        }
    }

    /// <summary>默认义体：旧天穹军团制式四件套。每部位 1 张，每部位每局最多用 3 次。</summary>
    public static class DefaultImplants
    {
        public static List<ImplantDefinition> Create()
        {
            return new List<ImplantDefinition>
            {
                new ImplantDefinition
                {
                    Key = "brain", DisplayName = "脑机·军用战术协调矩阵",
                    Durability = 5, SanityCost = 8, MaxUsesPerRun = 3,
                    EffectType = "scry_take", EffectParam = 3
                },
                new ImplantDefinition
                {
                    Key = "torso", DisplayName = "躯干·军用生命维持器系统",
                    Durability = 5, SanityCost = 4, MaxUsesPerRun = 3,
                    EffectType = "damage_redirect", EffectParam = 0
                },
                new ImplantDefinition
                {
                    Key = "hand", DisplayName = "手部·军用脉冲发射器",
                    Durability = 3, SanityCost = 2, MaxUsesPerRun = 3,
                    EffectType = "free_attacks", EffectParam = 0
                },
                new ImplantDefinition
                {
                    Key = "leg", DisplayName = "腿部·军用战术推进器",
                    Durability = 3, SanityCost = 8, MaxUsesPerRun = 3,
                    EffectType = "extra_turn", EffectParam = 0
                }
            };
        }
    }

    /// <summary>
    /// 义体打出引擎：把"义体、资源、耐久、使用次数、效果、待选择项"串起来。
    /// UI 只需调 TryPlay（打出一张义体卡）和 ResolveChoice（玩家做完选择）。
    /// </summary>
    public sealed class ImplantEngine
    {
        readonly EffectCatalog _catalog;

        public ImplantEngine(EffectCatalog catalog)
        {
            _catalog = catalog ?? EffectCatalog.CreateDefault();
        }

        public bool TryPlay(CombatState state, Implant implant)
        {
            if (implant == null) return false;
            if (implant.IsDestroyed) { state.Log("义体已报废，无法打出。"); return false; }
            if (implant.UsesExhausted) { state.Log("该部位本局使用次数已用完。"); return false; }

            IImplantEffect effect = _catalog.Get(implant.Definition.EffectType);
            if (effect == null) { state.Log("未注册的效果：" + implant.Definition.EffectType); return false; }

            if (state.Sanity < implant.Definition.SanityCost) { state.Log("理智不足（需要 " + implant.Definition.SanityCost + "）。"); return false; }
            if (!effect.CanActivate(state, implant)) { state.Log("当前无法激活：" + implant.Definition.DisplayName + "。"); return false; }

            state.Sanity -= implant.Definition.SanityCost;
            implant.ReduceDurability(1);   // 打出时耐久 -1
            implant.RecordUse();            // 每部位使用次数 +1
            effect.Activate(state, implant);
            return true;
        }

        public void ResolveChoice(CombatState state, int chosenIndex)
        {
            if (state.Pending == null) return;
            PendingChoice choice = state.Pending;
            choice.IsResolved = true;
            choice.Owner.Resolve(state, chosenIndex);
            state.Pending = null;
        }
    }
}
