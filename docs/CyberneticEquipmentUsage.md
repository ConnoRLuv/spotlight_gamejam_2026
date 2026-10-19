# 左侧义体栏与长按大图

## 行为

义体栏按脑机、躯干、手部、腿部从上到下排列，四个槽位使用深色半透明底并保留间隔。每场战斗开始时图标与文字留空；成功支付费用、执行效果并扣除耐久后，左侧对应位置只显示 `CyberneticData.icon` 中的部件图标和当前耐久。部位标题、名称对象已删除，耐久使用 24 号常规字体，框体按精简内容调整尺寸并预留内边距。这里的义体栏仅展示本场已经使用的义体，不新增装备、buff 判定或费用结算流程。

脑机需要选牌确认后才填入；取消或失败不填入。同场跨回合保留，HUD 禁用后重新启用也能从本场状态恢复；新战斗清空展示，耐久仍跨战斗保留。

已填入的部位统一长按显示圆环并放大真实卡牌，不再悬停即开窗。空槽不预览，松开、移出、HUD 停用或切换战斗后关闭。大图只读且不拦截射线，保持原比例。耐久为0时仍允许查看；完整名称及说明通过大图查看。详见 [统一长按说明](CardHoldPreviewUsage.md)。

## 类与场景

- `BattleContext.GetUsedCyberneticCard`：查询本场对应部位最后成功使用的卡牌。记录与战斗上下文一起重建，不从初始装备直接推断展示。
- `CardPlayService`：校验成功并支付费用后记录义体牌；普通牌、功能牌、失败出牌和等待选择不会更新这份记录。
- `CyberneticEquipmentSlot`：复用一个 Image 展示对应部件图标及耐久；由 HUD 在战斗事件发生时刷新，长按仍查看完整卡面。未配置 icon 时只显示耐久，不用卡面代替图标。
- `CardHoldPreview` 与 `CardPreviewOverlay`：统一长按和只读放大。`CyberneticEquipmentTooltip` 及其预制体保留旧序列化接口，但部位悬停不再自动调用，避免两套窗口重叠。
- `BattleHUD.equipmentSlots`：绑定四个装备槽；HUD 解绑时清空槽与悬停说明，选择弹窗打开时关闭悬停说明。
- `Assets/Prefabs/UI/CyberneticEquipmentTooltip.prefab`：共用悬停详情预制体。

场景中 `CyberneticDurabilityArea/EquipmentSlot_0..3` 对应脑机、躯干、手部、腿部。旧色块和旧耐久文字保留引用并停用，避免破坏其他序列化引用。

## 编辑器入口

`Spotlight > Equipment > Create Slots And Wire BattleScene` 增量创建缺失的义体槽及详情预制体，并保存场景引用；有未保存的场景修改时拒绝运行。已有预制体保留，槽位统一应用紧凑的图标布局及 24 号字体。

`Spotlight > Equipment > Validate Play Mode` 已复用新的 CardHoldPreviewFlowTests，验证长按、点击抑制、牌堆只读、滚动取消、开始界面、义体栏和新战斗清理。汇总保存在 Logs/Equipment/hold-preview-result.txt，不再运行旧的独立悬停模拟流程。

## 验证范围

新增四项 EditMode 测试，覆盖使用前为空、成功使用后记录、失败/等待选牌不记录、新战斗清空且耐久保留。

初次装备栏验证时，八项 `FunctionCardTests` 因过载理智费用尚未确认、`OverloadCard.asset` 尚未生成而失败。2026-10-09 已按用户确认的 5 点理智费用接入过载，全量测试更新为 73/73 通过；过载实际界面验证亦通过。本次装备栏验证不依赖该资产。未运行独立 Player 构建。

本次实际验证：4 项新增测试通过；全量 73 项中 65 项通过，8 项失败均属于上述待接入的过载功能。完整失败名称及断言见 `Logs/Equipment/editmode-c6a21bb581de4f718342bca85388d634.json`。装备栏 Play Mode 验证通过，并已检查 1920×1080 与 1024×768 下初始空栏和悬停大图，场景四个装备槽引用完整，所有新增资产具备 meta。
