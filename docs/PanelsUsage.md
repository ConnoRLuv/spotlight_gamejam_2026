# 面板显隐与只读牌堆浏览

## 其他成员的接入接口

BattleScene 已绑定抽牌和弃牌两个面板。只有显式调用才打开，悬停或点击牌堆按钮不会查看卡牌；按钮仍显示两套牌堆数量。

BattleScene 顶部的临时 `PilePanelTestControls` 已删除，正式牌堆图标与计数保留。需要面板交互时使用下列 HUD 接口；仍保持两个面板互斥。

```csharp
// 在对应义体效果的交互层中，先按玩法完成校验及扣费，再请求显示。
// hud 为当前 BattleScene 的 BattleHUD 引用。
hud.ShowDrawPile();       // 默认 0.15 秒淡入
hud.ShowDiscardPile();    // 自动立即关闭另一个面板
hud.ClosePilePanels();    // 默认 0.15 秒淡出
hud.ShowDrawPile(0f);     // 立即显示
hud.ClosePilePanels(0f);  // 立即关闭
```

HUD 未启用、未绑定战斗、战斗已结束或正在进行选牌／排序操作时，显示请求不打开面板。效果代码应在适当的交互阶段调用。面板层不扣费用、耐久或次数；默认浏览不修改牌区。弃牌回收可显式进入选择模式；新世界脑机可显式进入抽牌堆排序模式，复用关闭按钮提交两套草稿，详见 [卡牌拖拽排序](CardDragReorderUsage.md)。

## 展示行为

- 抽牌堆预制体：`Assets/Prefabs/UI/DrawPilePanel.prefab`。
- 弃牌堆预制体：`Assets/Prefabs/UI/DiscardPanel.prefab`，保留原资产 GUID。
- BattleScene 抽牌面板位于左下角抽牌堆按钮右侧，弃牌面板位于右下方；两者参考尺寸为 1000×640，卡面网格为 240×280。
- 两者继承 `PilePreviewPanel`，共享 `Bind(BattleContext)`、`Show(float)`、`Hide(float)`、页签及滚动逻辑。
- 每次从隐藏状态打开默认显示普通牌页。页签显示普通牌和义体牌各自数量，内容不会混合。
- 抽牌堆第一张预览为下一张抽取的堆顶；弃牌堆第一张为最近弃置的牌。只对展示快照反向排序，不改变真实顺序、不触发洗牌。
- 空页显示“该牌堆暂无卡牌”。卡面默认只读，不出牌；现支持共用的圆环长按放大，拖拽与滚轮仍由列表处理。效果可显式进入选牌模式，长按不会确认选择，详见 [统一长按预览](CardHoldPreviewUsage.md)。
- 关闭按钮和 Esc 均淡出关闭。开始淡出即停止鼠标拦截，结束后停用对象。
- 网格根据视口宽度调整为最多三列；切页后回到列表顶部，相同内容的资源刷新保留滚动位置。
- 新战斗绑定时隐藏并清除旧内容；战斗结束或 HUD 停用时关闭。隐藏时解除战斗订阅，再次打开读取当前真实牌堆。
- 原 `DiscardPileHover` 及其场景挂载已移除，不再使用鼠标移入/移出控制显隐。

## BasePanel 与 DOTween

`BasePanel` 自动要求 `CanvasGroup`，继续提供共用显隐接口：

```csharp
panel.Show();       // BasePanel 的默认值仍为 0，立即显示
panel.Hide();       // 立即隐藏
panel.Show(0.2f);   // 0.2 秒淡入
panel.Hide(0.15f);  // 0.15 秒淡出
```

`PilePreviewPanel.Show()` 和 HUD 的牌堆入口默认使用 0.15 秒。时间单位为秒；负数、NaN、无穷大均拒绝。动画使用 DOTween 非缩放时间，暂停游戏仍能完成。新的显隐指令中断旧动画，避免旧淡出回调关闭重新打开的面板。

沿用现有官方免费 DOTween 1.3.030、UI 模块与安全模式，不修改依赖或供应商源码。

## 构建与验证

- `Spotlight > Panels > Create Pile Panels And Wire Scene` 增量升级两个预制体和 BattleScene 引用，保留已有节点及 GUID；旧菜单 `Create Discard Panel And Wire Scene` 转发到同一工具。
- 构建不会向牌堆按钮添加开窗事件，不修改任何义体效果。BattleScene 有未保存修改时停止，避免覆盖成员编辑。
- `Spotlight > Panels > Validate Play Mode` 验证初始隐藏、悬停/点击无效、显式打开、页签、只读卡面、暂停时动画、关闭、互斥、滚动、新战斗绑定及 HUD 生命周期。
- 验证临时操作演示战斗，结束后退出 Play Mode 并恢复时间倍率，不保存运行时状态。
- 本轮报告位于 `Logs/PilePanels`；截图覆盖 1920×1080、1280×720、1024×768、2560×1080。
- `PanelTests`、`PilePanelTests` 覆盖真实预制体、展示顺序、空状态、费用与牌区不变及绑定清理，完整测试通过 Unity Test Runner 运行。
