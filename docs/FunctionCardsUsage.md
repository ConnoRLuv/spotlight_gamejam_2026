# 功能牌：过载

功能规则：过载消耗 5 点理智、无需 AP，获得 1 点本回合有效的临时 AP；不采用早期描述中的“多抽一张基本牌”。基本牌与功能牌共同使用普通牌堆、13 张手牌上限和每回合共抽 3 张的额度。

## 配置与解锁

- `CardData.category = Function`，`cost = 0`，`targetType = None`。
- `sanityCost = 5`，采用 2026-10-09 用户确认的理智费用。
- `effects` 引用 `ActionPointEffectData`，`amount = 1`。
- `requiresEquippedCybernetic = true`：装载任意义体后才加入遭遇的普通牌堆，出牌时也检查装备状态。按“已装载”判断，不检查对应部位的耐久和使用次数。
- 功能牌不需要来源义体实例，不消耗义体耐久，不占用任何部位的每回合/每战斗使用次数。

新增 `requiresEquippedCybernetic` 默认 false，保留已有基本牌和义体牌行为。牌组筛选在 `GameBootstrap.CreateEncounter` 完成；其他入口提交出牌时由 `CardPlayService` 校验，拒绝路径不扣费、不移动卡牌。

## 创建和接入

在 Unity 编辑模式调用 `SpotlightGameJam.Editor.FunctionCardAssetsBuilder.Build(5)`，创建并接入以下资产：

- `Assets/GameData/Cards/OverloadCard.asset`。
- `Assets/GameData/Effects/Overload.asset`。
- `Assets/Prefabs/Cards/OverloadCard.prefab`，复用卡牌结构和中文字体，使用低饱和度黄灰色。

构建入口追加卡牌目录映射和 BattleScene 初始牌组中的一张演示用过载；保留原有卡牌、义体和 UI。重复执行不会重复添加或覆盖已有配置。

已有资产可通过 `Spotlight > Cards > Wire Existing Function Card` 重新接入。修改费用只需调整过载的 `sanityCost`，无需修改脚本。

## 出牌与展示

点击普通手牌中的过载即可出牌，无需目标选择。零 AP 时仍可使用；理智不足则保留手牌。理智支付到零会立即添加幻痛，然后继续获得临时 AP。成功使用后进入普通弃牌区，在弃牌悬停面板中使用专属预制体只读展示。

临时 AP 优先用于支付基本牌或手部转换，并在结束玩家回合时清零。普通 AP 的首回合 2、逐回合 +1、上限 12 保持不变；临时 AP 单独计数。

## 验证入口

`FunctionCardTests` 覆盖交付资产、零 AP 出牌、理智不足、理智到零触发幻痛、回合结束清零、装备解锁，以及手牌/弃牌共享目录映射。

接入资产后，从 BattleScene 编辑模式执行 `Spotlight > Cards > Validate Function Card Play Mode`。它通过真实手牌按钮验证费用、耐久、弃牌悬停、临时 AP 支付基本牌与回合结束清除。验证仅修改运行时战斗，结束退出 Play Mode；报告和截图写入 `Logs/Functions`。

2026-10-09 已按 5 点理智费用创建上述资产，并将一张过载加入 BattleScene 演示牌组。全量 EditMode 测试 73/73 通过，结果见 `Logs/editmode-7de798ee46ea4830978f5822eb050084.json`。实际界面验证通过，报告见 `Logs/Functions/play-mode-validation.txt`；Unity Console 无错误或警告。未运行独立 Player 构建。
