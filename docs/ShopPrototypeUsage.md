# 商人对话与商店

## BattleScene 测试

打开 `Assets/Scenes/BattleScene.unity` 并进入 Play Mode，点击画面上方的 **测试商店**。

1. 自动显示第一句商人对白，点击“继续”显示第二句。
2. 点击“进入商店”显示商店界面。
3. 点击右上角“退出”或按 Esc 返回战斗。

对话期间点击“离开”或按 Esc 会取消整个流程，不进入商店。窗口显示与淡出期间阻止底层战斗按钮的鼠标和键盘操作，不改变 `Time.timeScale`。正在选择卡牌时无法打开测试入口；打开时关闭牌堆预览。

对话、商店及父遮罩始终位于顶部 toolbar 下方，保留生命、塞勒币和地图进度等原有工具栏展示；窗口打开期间仍保持原有战斗输入锁。边界读取 `BattleLayout_16x9/TopBar` 的实际位置，窗口尺寸变化后自动调整。

## 当前范围

- 两个预制体位于 `Assets/Prefabs/UI/DialoguePanel.prefab` 和 `ShopPanel.prefab`，使用 `BasePanel` 的秒数显隐接口，默认过渡 0.15 秒。
- 左侧按脑机、躯干、手部、腿部显示当前探索持有的义体型号与共享耐久。同种多张牌只显示一份，耐久为零仍保留显示。
- 同部位有多个型号时显示“切换”按钮，默认仍为原带入型号；切换只选择修复对象，不是装备切换，也不参与 buff 判定。购买或改造获得的新型号同样可以修复，原型号不会被替换。
- 没有持有义体的部位保留部位标识，名称、耐久和维修按钮留空。
- 右侧商品通过 ShopPanel 的 `productDefinitions` 配置，默认普罗米修斯四部位与原制式脑机、躯干。原制式尚未提供购买价，保持不可购买。
- 购买一件仅获得 **1 张**对应义体持久牌，不按初始配置展开多张，不加入当前手牌或抽牌堆；下一场战斗沿用探索牌组生成时加入。已拥有同型号（包括报废牌）不能再次购买。
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

新世界与复仇女神目前仅预留价格资产，未创建新卡牌或效果。后续义体配置引用对应价格，再加入商店的 `productDefinitions` 即可接入。缺失价格不会变成免费交易，`canPurchase` 关闭表示购买价未开放。

BattleScene 仍沿用原初始余额，不自动发测试币。需要操作有费用的按钮时，进入 Play Mode，使用菜单 **Spotlight → Shop → Grant 3000 Test Coins (Play Mode)** 显式补充测试塞勒币。测试购买需要当前牌组未持有商品型号，可调整出发选择或独立战斗的初始牌组；已携带完整普罗米修斯时同型号商品会显示“已拥有”。

BattleScene 的 `BattleCanvas/ShopPrototypeTest` 负责示例流程，`ModalRoot` 是淡出期间仍然阻止输入的父遮罩。`ShopPrototypeTestEntry` 临时关闭 `BattleLayout_16x9` 上 CanvasGroup 的交互，退出或停用时恢复原值。

“测试商店”按钮位于 `BattleLayout_16x9/PilePanelTestControls` 中“关闭面板”的右侧，间距 12，按钮高度一致。

移除测试入口时，退出 Play Mode，删除或停用 **ShopPrototypeTest** 对象以及 **PilePanelTestControls/TestShopButton**；保留两个预制体与台词资产供地图复用。单独停用入口组件也会清理已打开的窗口。

菜单 **Spotlight → Shop → Create Prototype And Wire Battle Scene** 用于补齐缺失资产与引用。重复执行不重建已有预制体或覆盖成员布局，不改变 Build Settings。BattleScene 有未保存修改时会拒绝执行。

## 验证入口

- EditMode：`ShopPrototypeTests` 验证显示与复用；`ShopTransactionTests` 验证余额边界、仅购买一张、原型号保留、各系列修复价格、拒绝无效交易及下一场战斗持久化。
- PlayMode：`ShopPrototypeFlowTests` 验证真实购买、修复和回血点击、切换修复对象、TopBar 生命与余额同步、共享耐久刷新、重复开关与输入锁。
- 分辨率用例导出两个面板在 1920×1080、1280×720、1024×768 的截图到忽略目录 `Logs/ShopPrototype`，同时检查文本边界和实际文字生成。
