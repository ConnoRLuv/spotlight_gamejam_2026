# 基础战斗框架使用说明

## 项目结构

- Assets/Scripts/Config：战斗规则配置。
- Assets/Scripts/Battle：角色状态、AP、战斗上下文、出牌/回合入口和伤害结算。
- Assets/Scripts/Cards：卡牌配置、实例、牌区、牌堆和效果模板。
- Assets/Scripts/Cybernetics：四部位装备、跨战斗耐久与单场使用计数。
- Assets/Scripts/Run：冒险中的玩家状态、金币和初始手牌奖励。
- Assets/Scripts/Core：GameBootstrap 场景组装入口。
- Assets/Scripts/Utilities：可复现随机选择与洗牌。
- Assets/Scripts/UI：CardView、预制体目录、BattleHUD 和场景引用。
- Assets/Tests/EditMode：规则测试；一个运行时程序集和一个测试程序集。

CardData 沿用原脚本和 GUID，保留全局类型及 cost/cardType 等字段以保持兼容。
新代码采用 SpotlightGameJam 命名空间。CardType 是遗留展示分类，
玩法逻辑使用新增 category；cost 明确表示 AP，sanityCost 表示理智费用。

## 在 Inspector 创建配置

建议将资产保存在 Assets/GameData。

1. Create → Spotlight → 战斗规则，默认值为生命 100、理智 50、AP 2/+1/12、抽牌 3/1、容量 13/5、每部位每战斗 3 次。
2. Create → Spotlight → 卡牌效果，创建伤害、护盾、治疗、抽牌或临时行动点资产。
3. Create → Cards → Card Data，填写唯一 cardId、category、cost、sanityCost、targetType、effects。
4. 创建幻痛：category=Special、locksInHand=true、cost=0、sanityCost=0、targetType=None；无需 effects。
5. 若使用义体，Create → Spotlight → 义体配置，填写 cyberneticId、slot、maxDurability 和 cards。

基础配置示例：

| 牌 | category | cost | sanityCost | targetType | effects |
| --- | --- | --- | --- | --- | --- |
| 攻击 | Basic | 1 | 0 | SingleEnemy | DamageEffectData，amount=6 |
| 防御 | Basic | 1 | 0 | Self | ShieldEffectData，amount=3 |
| 回复 | Basic | 1 | 0 | Self | HealEffectData，amount=3 |
| 过载 | Function | 0 | 5 | None | ActionPointEffectData，amount=1 |
| 义体测试牌 | Cybernetic | 0 | 策划填写正数 | Self | ShieldEffectData，amount=3 |

功能牌和义体牌的理智费用必须为正数，AP 费用必须为 0。过载的理智费用已确认为 5。
义体 cards 数组中的每个条目代表一张牌，可以重复引用同一 CardData 配置；每场战斗生成独立实例。
`Assets/GameData` 已提供三张基本牌和四个军团义体的正式演示配置。

## 接入场景

在入口 GameObject 上添加 GameBootstrap，并填入：

- Rules：战斗规则资产。
- Ordinary Deck：普通初始牌堆，基本牌与功能牌的比例由数组组成决定。
- Phantom Pain：幻痛配置。
- Initial Cybernetics：初始装备，四个部位每部位最多一个；允许为空数组。
- Enemy Health / Enemy Attack Damage：基础敌人的生命与每次攻击伤害。
- Random Seed：本次冒险的随机种子；下一场战斗使用递增种子。
- Start On Play：开启后在 Start 中创建新冒险与第一场战斗。

框架没有自动编辑 SampleScene。配置缺失时入口记录 LastError 并输出明确错误。
开始新冒险调用 StartNewRun；击败敌人后继续同一冒险调用 StartEncounter。
StartEncounter 保留玩家生命/理智和装备耐久，新建牌堆、AP、回合及部位使用计数。
玩家死亡后必须开始新冒险。

纯 C# 调用入口：

```csharp
// bootstrap 是当前场景中的 GameBootstrap 引用。
BattleController battle = bootstrap.Battle;
CardInstance card = battle.Context.Ordinary.Hand.Cards[0];
CardPlayResult result = battle.TryPlay(card, battle.Context.Enemies[0]);
if (!result.Success)
    UnityEngine.Debug.Log(result.Message);
// 不需要目标的牌使用 battle.TryPlay(card)。
battle.EndTurn();
```

## UI 和效果扩展

UI 可通过 GameBootstrap.BattleCreated 获取新的战斗，通过 BattleContext.Changed、
BattleController.StateChanged 和 CardPlayed 订阅变化。在 OnDisable 中解除订阅，
替换战斗时先解除旧 Context 的订阅。现有 CardView/BattleHUD 已在 BattleScene 中接入。
UI 不直接扣血、移牌或修改 AP，只提交 TryPlay/TryPlayRequest/TryConvertApToAttack/EndTurn 指令。

新效果继承 CardEffectData：

- Validate 校验配置数值与引用。
- RequiresTarget 为 true 的效果不能搭配 None 目标模式；开战前会报告该配置错误。
- CanExecute 校验出牌前置条件，必须无副作用，不抽取随机数。
- Execute 完成效果；所有伤害调用 DamageResolver.Apply。
- 需要玩家选择的效果使用 RequiresChoice、CanExecuteRequest 和 ExecuteRequest；UI 先获得候选，再以 CardPlayRequest.choiceId 提交。预校验必须在支付前完成，不能在 Execute 中等待界面选择。
- 所有数量默认非负；治疗不复活死亡角色。

已实现义体选牌、伤害转理智、AP 转换和额外行动回合；商店、地图、存档及正式美术留给后续功能。

## 规则说明

- 普通 AP 每回合重置为当回合额度，临时 AP 优先消耗并在结束回合时清零。
- 剩余手牌保留；溢出的新抽牌进对应弃牌堆。抽牌堆不足时重洗本次抽牌前已有的弃牌。
- 护盾抵挡本回合敌人行动，下个玩家回合开始清除。
- 义体每回合同部位一次、每战斗同部位三次；成功出牌才扣理智、耐久和次数。
- 理智归零即时加入唯一幻痛；满手时替换一张非锁定牌。幻痛使玩家每次伤害在所有存活角色间随机选择，包含玩家自身。
- 战斗结果即时判断，死亡后停止后续效果和敌人行动；玩家死亡优先判失败。
- 脑机仅查看当前义体抽牌堆顶，按实例 ID 取出候选，其余牌顺序保持；取消或候选失效不扣费。
- 生命维持器先保留护盾吸收，剩余伤害扣理智，不足部分扣生命；完全被护盾吸收时保护仍保留，下个玩家回合开始失效。
- 手部转换优先支付临时 AP，生成实例以 EffectiveApCost=0 出牌；生成牌用后及玩家行动结束时清理，均不进入弃牌堆。
- 腿部排队一个完整新玩家回合：AP 额度增长、抽牌及部位回合标记正常刷新，单场使用次数和耐久保留。
- RunState 提供金币及初始手牌奖励 API，本阶段不自动发放未配置的战斗奖励。

## 验证

打开 Window → General → Test Runner，选择 EditMode，运行 SpotlightGameJam.EditModeTests。
测试覆盖资源支付、牌区容量/归属、洗牌、基础效果、幻痛、义体限制、回合状态和跨战斗保留。
Tools/unity_mcp_test.py 是测试辅助脚本，通过已有 localhost:8080 MCP 服务调用同一运行器，
需要 fastmcp Python 环境；完整结果写入 Logs/editmode-<job-id>.json。

2026-10-08 基础框架的原 35 项测试已通过；本次卡牌实现扩充至 55 项，包含义体规则和真实预制体配置验证。本地测试报告路径：`Logs/card-all.xml`。
