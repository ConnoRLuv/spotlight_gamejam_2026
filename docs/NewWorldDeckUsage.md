# Type-00：新世界

新世界四部位已接入正式资产、卡面目录和 StartScene。旧默认原制式迁移为新世界，各部位三张、同型号共享耐久三点；原制式和普罗米修斯仍保留为可选卡组，成员自定义混搭默认不会被构建器覆盖。

| 部位 | 理智消耗 | 效果 | 完整卡面 |
| --- | ---: | --- | --- |
| 脑机 | 4 | 查看普通和义体抽牌堆，各自调整抽牌顺序；下一张在前 | 义体牌2-1 |
| 躯干 | 8 | 当前玩家回合解除两类手牌上限；下一个玩家回合恢复上限 | 义体牌2-2 |
| 手部 | 3 | 当前回合过载获得的 AP 计入现有永久 AP 池，最多 12 点；未消费部分跨回合保留 | 义体牌2-3 |
| 腿部 | 10 | 结束行动时直接开始一个额外玩家回合，跳过敌方行动，正常刷新 AP、抽牌和部位使用记录 | 义体牌2-4 |

每个部位每回合/每场战斗仍受统一次数限制；三张副本不能绕过部位限制。使用任意副本扣减同型号共享耐久，跨战斗不恢复。维修使用 NewWorld 系列价格，每支付 150 塞勒币恢复共享耐久 1 点；未提供购买价，因此不擅自开放购买。

## 套装

沿用全局组合规则：同一玩家回合成功打出新世界四部位后激活，持续至本场战斗结束。携带、抽牌、弃牌、取消或失败都不触发；额外回合重新累计尚未完成的四部位组合，已经激活的套装保持生效。

激活后每次成功出牌，从普通弃牌堆回收最近弃置的一张基本牌。手牌已达当前有效上限或没有基本牌时不回收。躯干解除上限期间允许继续回收。回收沿用真实卡牌实例，不新建副本，不触发一次额外出牌；刚打出的基本牌已进入弃牌堆时也可能被回收。

## 资源与接入

- 卡牌：`Assets/GameData/Cards/NewWorld{Brain,Torso,Hands,Legs}Card.asset`
- 义体：`Assets/GameData/Cybernetics/NewWorld{Brain,Torso,Hands,Legs}Card.asset`
- 预制体：`Assets/Prefabs/Cards/NewWorld{Brain,Torso,Hands,Legs}Card.prefab`
- 牌组：`Assets/GameData/CyberneticDecks/NewWorld.asset`
- 套装：`Assets/GameData/SetBonuses/NewWorld.asset`
- 维修：`Assets/GameData/ShopPrices/NewWorld.asset`

完整卡面使用 `Assets/Art/Card/义体牌2-1.png` 至 `义体牌2-4.png`，维修使用 `Assets/Art/义体/` 下四个“新世界”图标。图片中印刷的 Type-02 是原美术内容；逻辑型号仍为用户指定的 Type-00，本次不修改原图。

StartScene 同时预告 RunBuffData 和新世界套装，不因预览而激活。新世界加入普罗米修斯改造候选池，原有候选和成员配置保留。脑机成功使用时结算一次费用、耐久和部位次数，直接打开现有抽牌堆面板，以普通牌／义体牌两个页签拖拽排序；黑幕阻止其他区域操作，关闭或 Esc 提交顺序。外部终止丢弃尚未提交的草稿，不会再次扣费。拖拽组件的复用方式见 [卡牌拖拽排序](CardDragReorderUsage.md)。

菜单 `Spotlight/Cards/Create NewWorld Assets` 可重复补齐资产。已有数值和自定义默认选择不会被重置，不打开或保存 BattleScene，不改变构建场景顺序。

## 验证

NewWorldAssetTests 核验费用、张数、耐久、卡面、图标、维修和出发/改造入口。NewWorldRulesTests 使用正式卡组覆盖四部位联动、额外回合、共享耐久、满手回收限制及战斗结束清理。ExplorationSceneFlowTests 验证十二张新世界牌带入真实战斗，并通过 HUD 打开抽牌堆、一次支付及关闭完成。DrawPileReorderTests 验证拖动、两套草稿、遮罩和生命周期。
