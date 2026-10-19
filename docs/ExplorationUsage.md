# StartScene 与探索牌组

在 Unity 中打开 Assets/Scenes/StartScene.unity。点击部位、预览候选并点击“更换”；“卸下此部位”和“清空四部位”允许空牌组出发。默认选择原制式四部位，可选原制式与普罗米修斯。角色立绘为灰色占位。

当前 StartScene 已启用临时测试入口：运行开始场景，逐个选择部位、预览义体并点击“更换”，再点击“出发探索”，即可异步进入 BattleScene。原制式、普罗米修斯及混搭都按所选配置张数带入，部分或全空选择同样允许。战斗读取同一个 AdventureSession.Run，保留牌组、共享耐久、塞勒币、血量与理智，不用场景预设牌组覆盖。

StartSceneController 仍只负责创建会话和发布出发事件，跳转由同一对象上的 `ExplorationBattleTestEntry` 组件监听；停用或移除该组件即可恢复只准备探索的行为。未来地图流程可替换此组件来选择节点场景。修改选择不改变已经创建的探索，再次出发才创建新探索。启动顺序与构建列表保持原样。

## 配置入口

- Assets/GameData/AdventureSetup.asset：规则、普通牌、幻痛、可选义体、默认选择、buff 列表、随机种子。
- 当前直接运行 BattleScene 使用复仇女神测试牌组；`GameBootstrap.initialCyberneticDeck` 指向 Nemesis，`runBuffs` 包含复仇女神组合。已有探索会话仍使用会话牌组及 buff，不被场景测试配置覆盖。
- Assets/GameData/Cybernetics：每部位选择一种配置；cards 每个条目代表一张牌，重复引用表示多份。
- Assets/GameData/Buffs/Prometheus.asset：四部位普罗米修斯组合。RunBuffData.requiredCybernetics 同部位候选为“或”，四部位为“且”，可配置混搭。
- 新建 buff 使用 Create > Spotlight > 探索 > 全局 Buff；当前已实现效果为过载生命支付，其他效果通过 RunBuffEffect 和统一费用入口扩展。`duration` 配置持续范围，普罗米修斯为 `Battle`（本场战斗）；也预留 `PlayerTurn` 和 `Adventure`。
- 场景生成菜单：Spotlight > Start Scene > Create Layout And Exploration Setup。已有场景不会被重建。
- 当前 StartScene 的临时跳转可用 `Spotlight > Start Scene > Enable Battle Test Entry` 接入；无需重建场景。

## 持久状态与耐久

每张义体牌各有 CyberneticInstance，保存唯一 Id、Card 和义体配置；同种义体的多张牌共享耐久池。例如三张普罗米修斯脑机共用 4 点耐久，使用任何一张都会扣减公共耐久。不同种类与不同探索的耐久池互不影响。每场战斗重建 CardInstance，但 Source 复用持久牌及其公共耐久；公共耐久归零时该种义体全部不能使用，下一战不生成这组战斗牌。维修任意一张恢复整组公共耐久，不恢复部位使用次数。

CyberneticLoadout 名称为历史兼容，实际是持牌集合。AddInitial 只在出发时按张数展开；AcquireCard 只增加一张改造牌。旧 Equip/Get/Equipped 入口仅用于初始配置及调试，不表示当前装备。战斗不调用换装操作；左侧栏仅显示该部位最近成功使用的牌。

出发只配置可触发的 buff，不自动激活。StartScene 显示当前牌组能够触发的组合预告。战斗中，同一玩家回合成功打出符合组合的四部位后才激活；第四张正常支付并结算完毕后生效，不改变其已经支付的费用。成功使用最后一点耐久也计入记录，报废不会撤销已触发的 buff。抽牌、弃牌、维修、预览、取消和失败都不计入；额外玩家回合也是新回合，不能跨回合补齐。改造新牌只要符合配置并成功打出，也可以参与。普罗米修斯 buff 触发后跨回合保留至本场战斗结束，胜负或退出战斗时清除；下一场重新累计。重复触发同一组合不叠加。

## 费用与场景接入

普罗米修斯 buff 令过载由支付 5 理智改为支付 5 生命，绕过护盾，不回复理智，允许零理智使用，支付后必须留至少 1 生命。血量 <= 5 时拒绝，费用不足不消耗牌或 AP。buff 失效后恢复通常理智支付规则。

BattleContext.UsesHealthFor / CanPayCardCost 同时供 UI 和出牌服务使用；共享 CardData.sanityCost 保持 5。普罗米修斯腿部仍只将义体费用改为生命并恢复等量理智，与过载 buff 分别结算。

进入 BattleScene 后 GameBootstrap 自动读取已有 AdventureSession；没有探索会话时仍用场景内配置独立测试。会话保留生命、理智、塞勒币、幻痛、永久改造和同种共享耐久；战斗牌区、AP、回合效果及部位次数每场重建。当前仅包含开始场景直达战斗的临时测试跳转，不包含存档、地图选择、对话或商店。

## 规则验证

`ExplorationRulesTests` 覆盖同种共享耐久及维修、不同种类与探索隔离、第四部位成功后触发、失败出牌、跨回合和额外回合清理累计、改造牌及混搭组合、最后耐久耗尽、战斗结束与重复释放、过载生命支付边界。真实普罗米修斯资源用例同时验证改造选牌、共享耐久、过载卡面“生命 5”及下一回合 buff 保留。

PlayMode 的 `ExplorationSceneFlowTests` 通过真实开始场景、更换按钮、出发按钮和异步加载，验证原制式十二张、普罗米修斯八张、混搭十二张、单脑机三张、空牌组五种情况，并核对会话身份、三种属性及重复点击。测试使用编辑器加载开始场景，不调整 Build Settings 的场景顺序。
