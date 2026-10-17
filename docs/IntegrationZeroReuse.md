# integration/zero 框架接入

## 套装规则

普罗米修斯与新世界的成功出牌记录、组合判断和生命周期统一由 `RunBuffState` 管理。

- 同一玩家回合成功使用符合配置的脑机、躯干、手部、腿部后激活；携带、抽牌、弃牌、左侧展示均不激活。
- 每个部位可以配置多个候选，部位内为“或”，四部位之间为“且”。
- 失败、取消不计入记录；额外行动回合是新回合，未完成的组合不跨回合累计。
- 新世界与默认普罗米修斯效果持续至本场战斗结束，耗尽共享耐久不撤销已经触发的效果。
- 第四张牌先正常扣费、消耗耐久及部位次数并执行牌效，再激活组合。新世界的成功出牌被动随后可回收一张基本牌。
- 新世界被动在激活后每次成功出牌回收一张基本牌；普通手牌满时跳过，随机／最近弃置策略保留。
- 新世界资产及 `setBonuses` 字段保留，`NewWorldSetBonusState` 仅负责兼容查询和被动效果，不再维护第二份触发状态。

## 义体效果复用

`Goddess of Vengeance` 目录原先包含一套命令行战斗演示。它的默认数据实际标注为旧制式，不是已确认的复仇女神正式配置。
原始文件完整归档在 `docs/reference/ImplantPrototype/*.cs.txt`，不再参与 Unity 运行时编译。

| 原型效果 | 正式框架入口 |
| --- | --- |
| 查看义体堆顶并选择一张 | `CyberneticPeekEffectData`，经 `CardPlayRequest.ChoiceId` 确认 |
| 下一次承伤转理智 | `DamageToSanityEffectData`，复用 `DamageResolver` |
| 本回合攻击牌免 AP | `FreeAttackCardsEffectData`，复用 `BattleContext.GetEffectiveApCost` |
| 额外玩家回合 | `ExtraTurnEffectData`，复用 `TurnController` |

攻击减费仅作用于 `Basic + CardType.Attack`，不免理智费用，不免防御牌费用；回合结束清除。
新世界腿部保留脚本身份并继承 `ExtraTurnEffectData`，不再重复实现效果。
新义体只需配置 `CyberneticData`、`CardData` 和相应效果资产；成功出牌统一经过 `CardPlayService`，共享耐久和部位次数继续由原框架管理。
本次没有用演示中的旧制式数值生成复仇女神正式卡组。

## 重排面板

`NewWorldBrainPanel` 继承 `BasePanel`，提供 `Show/Hide(float fadeSeconds)`，默认 0.15 秒。
打开只复制展示顺序，确认经原子出牌入口结算；取消、Esc、外部停用不改变资源和真实牌区。
`Hide` 开始立即释放鼠标交互，完成后清理卡面；结算通知提前关闭面板时，完成回调仍只执行一次。

增量菜单：`Spotlight → Panels → Create NewWorld Brain Panel`。重复执行复用同一面板并补齐 CanvasGroup、四个裁剪滚动区和 HUD 引用，不重建战斗界面。

## 地图会话与节点接口

`AdventureSession.Map` 保存同一个 `MapRunState`；生命、理智、塞勒币、牌组继续保存在原 `RunState`。
`TryBindMap` 只允许首次绑定或绑定同一对象；新探索清空旧地图，正常跨场景往返不清空。
地图的确定性随机算法和生成规则保持原有实现。

`AdventureMapBridge` 已接到 MapScene：

- 首次生成后把地图交给会话保存；返回场景用 `MapGeneratorBehaviour.BindRun` 恢复原状态。
- 独立运行 MapScene 而没有准备探索时，保留地图演示，不自动生成玩家冒险。
- 真实进入节点后发布 `NodeRequested(MapNode)`，并分发 Inspector 的 `combatRequested/shopRequested/eventRequested`。
- 战斗监听方可加载 BattleScene，由 `GameBootstrap.StartSessionEncounter` 读取同一探索状态。
- 桥自身不跳场景、不启动第二场战斗，也不创建商店或对话。

增量菜单：`Spotlight → Exploration → Wire Map Session Bridge`。不修改 Build Settings 或启动顺序。
本分支尚未集成 `feat/shop`；商店／事件入口目前是明确的回调接口，待集成后可绑定通用 DialoguePanel 和 ShopPanel，并传入 `bridge.Run`。

## 验证

EditMode 覆盖携带不触发、跨回合不累计、成功四部位、失败第四部位、耐久归零、战斗结束清理与攻击减费。
PlayMode 覆盖重排实际按钮、取消、反复打开、淡出与回调，以及 MapScene → BattleScene → MapScene 的进度和资源保留。
测试按路径加载场景，不为验证改变构建场景列表。
