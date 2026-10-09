# 义体牌组配置入口

`CyberneticDeckData` 将一套初始义体组合为可复用资产，方便后续接入其他系列。牌组不新增部位或结算规则：部位、耐久和关联卡由 `CyberneticData` 定义，费用、目标及效果由 `CardData` 定义。

## 新增牌组

1. 在 Project 中使用 **Create → Spotlight → 义体牌组** 创建资产，建议放在 `Assets/GameData/CyberneticDecks`。
2. 填写稳定的 `Deck Id` 和便于识别的 `Deck Name`；不同牌组使用不同 ID。
3. 在 `Cybernetics` 数组引用本套初始义体，每个部位最多一个；允许仅配置部分部位。
4. 单个义体使用 **Create → Spotlight → 义体配置** 创建，填写 ID、部位、最大耐久及 `Cards` 数组。同一张卡重复引用三次即表示三张，每张会生成独立实例。
5. 在 BattleScene 的 `BattleGame / GameBootstrap / Initial Cybernetic Deck` 中选择牌组，下次开始新冒险生效。

当前默认资产为 `Assets/GameData/CyberneticDecks/TianQiongLegion.asset`，包含天穹军团四种义体，各自关联三张牌，共十二张。普通牌组仍独立配置在 `Ordinary Deck`。

## 选择与兼容规则

- 选择 `Initial Cybernetic Deck` 后，它覆盖旧的 `Initial Cybernetics` 数组，两者不会合并。
- 字段留空时继续使用旧数组，兼容已有场景；旧数组也可自由混搭不同系列的义体。
- 牌组只决定新冒险的初始装备，后续战斗从当前 `RunState.Loadout` 生成义体牌，保留耐久与装备变化。修改 Inspector 中的牌组不会替换正在进行的冒险装备。
- 每回合仍从独立义体牌堆抽一张；洗牌、弃牌、部位使用次数及耐久结算沿用原有规则。

## 尚未实现的内容

其他系列可以先创建牌组、义体及卡牌草稿，但不要选择未完成的牌组开战。启用时会校验 ID、名称、空引用、重复部位及关联卡效果，失败不会替换已有冒险。

新增效果继承 `CardEffectData` 并实现校验与结算，再配置到卡牌。新增专属外观可在 `CardPrefabCatalog.entries` 登记配置与预制体；未登记时使用现有备用卡面展示数据。

这里提供编辑器配置和战斗接入，不包含运行时牌组选单、解锁、商店或其他系列的具体效果实现。

## 验证记录

2026-10-09 全量 EditMode 测试 82/82 通过，覆盖牌组选择、旧配置回退、卡牌来源、跨战斗耐久及无效草稿拒绝。默认天穹军团牌组保留十二张义体牌，敌人测试血量仍为 9999。
