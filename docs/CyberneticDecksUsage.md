# 义体牌组配置入口

当前实现原天穹军团制式四部位、Type-01：普罗米修斯、Type-00：新世界和 Type-02：复仇女神。新世界各部位三张、共享耐久三点，维修 150 塞勒币恢复一点，详见 [新世界卡组说明](NewWorldDeckUsage.md)。复仇女神张数与共享耐久为 3/3/3/2，购买 600、单点修复 200 塞勒币，详见 [复仇女神说明](NemesisDeckUsage.md)。

CyberneticDeckData 保存一套可复用的初始配置，用于资源组织和直接打开 BattleScene 测试。探索时以 StartScene 的每部位选择为准，允许混搭和留空。各 CyberneticData.cards 数组长度决定该部位入堆数量；每个条目创建独立身份的持久牌，同种义体全部条目共享一份耐久，不按张数倍增耐久。

新增义体需要创建 CardData、CardEffectData、CyberneticData 和卡面预制体，将卡面加入 CardPrefabCatalog，将义体加入 Assets/GameData/AdventureSetup.asset 的 availableCybernetics。默认选择修改 defaultCybernetics。原制式各 3 张；普罗米修斯为 3/1/3/1。

全局 buff 独立配置在 RunBuffData 中，不使用 CyberneticDeckData.reservedSetBonusDescription 的旧设计备注。每个组合覆盖四部位，同部位可允许多种义体 ID，便于配置混搭。实际判定记录同一玩家回合成功打出的义体种类，四部位齐全后才触发；改造牌也可参与。普罗米修斯持续至本场战斗结束，不能跨回合补齐，下一场重新触发；携带组合本身不激活。

详细行为、探索事件和跨场景接入见 [探索说明](ExplorationUsage.md)。
