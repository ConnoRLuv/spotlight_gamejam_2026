# 商人对话与商店

## 主分支商店美术

`ShopPanel.prefab` 已接入 `Shop_Popup.prefab` 使用的商店背景、治疗按钮及关闭按钮，继续沿用原预制体的 `ShopPanel`、`BasePanel` 和交易引用。`Shop_Popup` 本身保留为美术资源模板，不负责扣费，也没有重复添加一套商店逻辑。

左侧四个白框依次为脑机、躯干、手部、腿部。显示当前探索真实型号与共享耐久；有卡图时显示卡图，暂无卡图时显示部位文字。点击整个义体框修复一点耐久，框下显示费用；同部位多个型号仍可点“切换”。未持有的部位不显示旧名称、图像或耐久。

右侧六个商品位置保留原有卡面、价格、购买状态和结算；商人立绘位于右侧，商品避开立绘区域。治疗原图中的“治疗”文字保留，右侧独立显示恢复量、费用和生命。窗口按背景原始比例等比适配，顶部 toolbar 继续露出；打开流程时将整个流程父节点提升到战斗上方，退出后恢复战斗操作。

如需再次同步样式，可执行 **Spotlight → Shop → Apply Main Artwork To Existing Shop**。该工具增量修改现有控件，不改原始 PNG、`Shop_Popup` 或构建场景顺序；保留源 `ShopPanel` 的编辑预览激活状态与布局锚点。实际场景入口仍负责初始隐藏。

## BattleScene 测试

打开 `Assets/Scenes/BattleScene.unity` 并进入 Play Mode，点击画面上方的 **测试商店**。

1. 自动显示第一句商人对白，点击“继续”显示第二句。
2. 点击“进入商店”显示商店界面。
3. 点击右上角“退出”或按 Esc 返回战斗。

对话期间点击“离开”或按 Esc 会取消整个流程，不进入商店。窗口显示与淡出期间阻止底层战斗按钮的鼠标和键盘操作，不改变 `Time.timeScale`。正在选择卡牌时无法打开测试入口；打开时关闭牌堆预览。

对话、商店及父遮罩始终位于顶部 toolbar 下方，保留生命、塞勒币和地图进度等原有工具栏展示；窗口打开期间仍保持原有战斗输入锁。边界读取 `BattleLayout_16x9/TopBar` 的实际位置，窗口尺寸变化后自动调整。

## 当前范围

- 两个预制体位于 `Assets/Prefabs/UI/ShopDialoguePanel.prefab` 和 `ShopPanel.prefab`，使用 `BasePanel` 的秒数显隐接口，默认过渡 0.15 秒；`DialoguePanel.prefab` 保留主分支的对话面板资源。
- 左侧按脑机、躯干、手部、腿部显示当前探索持有的义体型号与共享耐久。同种多张牌只显示一份，耐久为零仍保留显示。
- 同部位有多个型号时显示“切换”按钮，默认仍为原带入型号；切换只选择修复对象，不是装备切换，也不参与 buff 判定。购买或改造获得的新型号同样可以修复，原型号不会被替换。
- 没有持有义体的部位保留部位标识，名称、耐久和维修按钮留空。
- 右侧商品通过 ShopPanel 的 `productDefinitions` 配置，默认普罗米修斯四部位与原制式脑机、躯干。原制式尚未提供购买价，保持不可购买。
- 购买一件仅获得 **1 张**对应义体持久牌，不按初始配置展开多张，不加入当前手牌或抽牌堆；下一场战斗沿用探索牌组生成时加入。已持有同型号也允许购买，新牌沿用当前共享耐久（包括报废状态），不自动修复。
- 每个商店节点的同种商品只能购买一次，购买后显示“已售出”并置灰。限购记录保存在 `RunState.Shop` 中，关闭面板、重新绑定、重建交易服务或返回同节点均不会补货。
- `AdventureMapBridge` 在商店节点的打开回调前调用 `run.Shop.Enter("map:种子:node:节点ID")`；地图流程接入其他入口时也应在打开前传入稳定的节点身份。新商店使用独立库存；BattleScene 测试入口反复打开默认视为同一商店，不自动重置。
- 每次修复仅恢复该型号 **1 点共享耐久**。满耐久、余额不足或价格缺失时按钮禁用，服务层也会拒绝扣费。
- 回血每次花费 **100 塞勒币**，恢复 **最多 10 点生命**，不超过上限。缺少不足 10 点生命时仍收取 100；满血、余额不足或已死亡时拒绝扣费，不能复活，不恢复理智或改变护盾。
- 生命与塞勒币读取绑定的 `RunState`，交易后商店和 TopBar 立即同步生命与余额；关闭商店后同步战斗卡面的耐久。浏览和型号切换不改变资源、牌区或使用次数。
- 默认使用项目现有玩家立绘和义体医生立绘。更换或清空对话配置中的 Sprite 即可替换为其他人物或颜色占位。

## 对话复用

通过 **Create → Spotlight → UI → 顺序对话** 创建 `DialogueSequenceData`。配置左右人物名称、立绘、每句正文、说话人位置以及末句按钮文案。示例商人配置位于 `Assets/GameData/UI/MerchantDialogue.asset`。

```csharp
// 调用方决定对话完成后的行为；DialoguePanel 不依赖商店或地图。
dialoguePanel.Play(sequence,
    onCompleted: () => Debug.Log("对话正常完成"),
    onCancelled: () => Debug.Log("玩家取消对话"));
```

点击继续调用 `Advance()`，取消调用 `Cancel()`。正常完成与取消互斥且每轮最多回调一次；直接停用或销毁面板会清除回调，不触发完成。仅支持顺序对话，分支、奖励、存档和事件条件未实现。后续地图使用时由地图流程管理外围输入遮罩。

## 商店与测试入口

`ShopPanel.Bind(run)` 绑定已有探索状态，再调用 `Show()`。`CloseRequested` 通知调用方释放外围遮罩。ShopPanel 自身不创建探索会话或加载场景；交易交给 `ShopService` 的 `CanPurchase/TryPurchase`、`CanRepair/TryRepair` 和 `CanHeal/TryHeal`，统一校验并返回失败原因。回血价格与数量由服务的 `HealingPrice/HealingAmount` 常量统一提供给界面。

## 价格配置

`CyberneticData.shopPricing` 引用系列级 `CyberneticShopPriceData` 资产，位于 `Assets/GameData/ShopPrices`：

| 系列资产 | 购买一件 | 每次修复 +1 |
| --- | --- | --- |
| Prometheus | 1000 塞勒币 | 300 塞勒币 |
| Standard（原制式） | 未定，禁止购买 | 150 塞勒币 |
| NewWorld（新世界） | 未定，禁止购买 | 150 塞勒币 |
| Nemesis（复仇女神） | 600 塞勒币 | 200 塞勒币 |

新世界与复仇女神保留对应系列价格入口。实际销售内容由 `productDefinitions` 决定，义体配置引用相应 `shopPricing` 后即可沿用交易服务。缺失价格不会变成免费交易，`canPurchase` 关闭表示购买价未开放。

正式商店由 MapScene 的节点流程打开，BattleScene 不再保留商店测试入口，也不会赠送测试币。需要临时测试币时可使用菜单 **Spotlight → Shop → Grant 3000 Test Coins (Play Mode)**。短按左侧义体图标按型号收费并恢复 1 点共享耐久，满耐久不扣费，长按只预览。携带完整普罗米修斯时同型号商品仍可购买，每次商店每种商品限购一张。

BattleScene 中的 `BattleCanvas/ShopPrototypeTest` 整组已删除，包括商店面板、商人对话面板、父遮罩及测试组件。共享预制体和台词资产仍供地图流程复用。商店回归测试从共享预制体临时创建完整测试对象，只在运行期间存在，不写回场景；测试入口初始化一次 9999 塞勒币，重复打开不会补币。

菜单 **Spotlight → Shop → Create Prototype And Wire Battle Scene** 用于显式恢复测试入口并补齐缺失资产与引用。重复执行不重建已有预制体或覆盖成员布局，不改变 Build Settings。BattleScene 有未保存修改时会拒绝执行。

## 验证入口

项目使用完整 Play Mode 域与场景重载。不要同时关闭 Domain Reload 和 Scene Reload：本项目在该组合下曾出现 uGUI `Selectable` 静态计数变为 -1，导致按钮启用时 `Selectable.cs:518` 数组越界。若编辑器已处于此错误状态，退出运行并重新加载脚本域（或重启编辑器）后再测试。

- EditMode：`ShopArtIntegrationTests` 验证主分支美术引用与图标清理；`ShopPrototypeTests` 验证显示与复用；`ShopTransactionTests` 验证余额边界、仅购买一张、原型号保留、各系列修复价格、拒绝无效交易及下一场战斗持久化。
- PlayMode：`ShopPrototypeFlowTests` 验证真实购买、修复和回血点击、切换修复对象、TopBar 生命与余额同步、共享耐久刷新、重复开关与输入锁，同时检查流程父节点显示顺序和淡入后的维修框鼠标射线命中。
- 分辨率用例导出两个面板在 1920×1080、1280×720、1024×768 的截图到忽略目录 `Logs/ShopPrototype`，同时检查文本边界和实际文字生成。
### 对话美术

修复框只展示义体图标、耐久与价格，部位和名称不再显示。短按执行修复；按住鼠标左键约 0.45 秒显示只读大卡面，松开或移出即收起，长按不扣费。满耐久或余额不足时仍可长按预览；切换型号、关闭商店时会收起旧预览。

右侧六个商品卡面同样支持长按圆环进度与大图预览，不触发购买；购买需点击下方购买按钮。没有完整美术的卡牌显示名称与效果描述。

`ShopDialoguePanel.prefab` 沿用可复用的 `DialoguePanel.Play` 接口，使用现有 `DialogBox.png`、`NameBox.png`、`CancelButton.png` 及地下城背景；人物继续从对话数据读取，不固定为商人。说话人显示在姓名框中，继续按钮在末句显示配置的完成文案。

通过 `Spotlight/Shop/Apply Existing Dialogue Artwork` 可增量应用美术，不会重建场景或替换对话脚本与配置引用。

