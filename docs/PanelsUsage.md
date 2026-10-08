# 面板显隐与弃牌预览

`Assets/Scripts/UI/BasePanel.cs` 是可继承的 uGUI 面板基础类，自动要求 `CanvasGroup`。

```csharp
panel.Show();        // 立即显示
panel.Hide();        // 立即隐藏
panel.Show(0.2f);    // 0.2 秒淡入
panel.Hide(0.15f);   // 0.15 秒淡出，完成后停用对象
```

时间单位为秒，允许 0；负数、NaN 和无穷大会抛出参数异常。淡入淡出使用 DOTween 和非缩放时间，`Time.timeScale = 0` 时仍可显示、关闭面板。新的相反指令会取消旧过渡；传入 0 会立即完成显隐。开始隐藏时立刻停止鼠标交互，避免透明面板遮挡其他控件。

请通过 `Show/Hide` 控制面板，初始隐藏可在场景或预制体中停用对象。`IsVisible` 表示目标可见状态，开始淡出时已经为 false。子类覆盖 `Awake`、`OnEnable`、`OnDisable` 或 `OnDestroy` 时需要调用基类方法，以保留动画清理。

## BattleScene 弃牌面板

- 预制体：`Assets/Prefabs/UI/DiscardPanel.prefab`。
- `DiscardPanel` 继承 `BasePanel`，由 `BattleHUD` 注入当前 `BattleContext`。
- 弃牌堆按钮挂载 `DiscardPileHover`，默认淡入/淡出各 0.15 秒、移出宽限 0.1 秒，可在 Inspector 修改。
- 悬停弃牌堆显示面板，鼠标移入面板后保持显示以便滚动；离开两个区域后关闭。
- 普通弃牌在前、义体弃牌在后；标题显示两类数量，来源通过卡牌底部文字区分。每张配置相同的牌也保留独立实例。
- 没有弃牌时显示“暂无弃牌”；卡牌仅供查看，不接受出牌操作。
- 只显示当前弃牌区。洗回抽牌堆的牌会消失，手部生成后直接移除的临时攻击不会出现在弃牌区。
- 显示期间通过战斗事件刷新；隐藏时解除订阅，重新显示时读取最新内容。新战斗开始或 HUD 禁用时关闭并解除旧战斗绑定。

## DOTween 依赖

采用官方免费版 DOTween 1.3.030，保留原包文件和 `readme.txt`，安装在 `Assets/Plugins/Demigiant/DOTween`。下载源：<https://dotween.demigiant.com/download.php>；许可：<https://dotween.demigiant.com/license.php>。

`Assets/Resources/DOTweenSettings.asset` 保存模块及安全模式设置，启用 UI 模块与官方程序集生成。`SpotlightGameJam.UI` 引用 `DOTween.Modules`。官方初始化在 Project Settings 添加 `DOTWEEN` 编译符号。未安装的 EPOOutline 集成关闭；不修改 DOTween 供应商代码。

## 编辑器验证入口

`Spotlight > Panels > Create Discard Panel And Wire Scene` 创建缺失预制体并绑定 BattleScene，保留已有预制体，不重建其他 UI；当前场景有未保存修改时拒绝执行。

`Spotlight > Panels > Validate Play Mode` 从 BattleScene 编辑模式启动，验证真实悬停事件、动画、滚轮、弃牌刷新和新战斗绑定。验证会临时改变演示战斗，结束后退出 Play Mode，不保存运行时修改。报告与 1920×1080、1024×768 截图写入 `Logs/Panels`。

新增 `PanelTests` 检查立即显隐、输入拦截、非法秒数、当前弃牌与空状态；完整测试通过 Unity Test Runner 运行。

## 本次验证结果（2026-10-08）

- 原项目 Unity 2022.3.62f3：EditMode 61 项全部通过，无失败或跳过。报告：`Logs/editmode-59640d5fb2b9461689cd5e2ce4d67783.json`。
- BattleScene 运行验证通过：零秒显隐、暂停时动画、过渡反转、悬停移入与移出、实时弃牌刷新、洗回抽牌堆、滚轮、新战斗重新绑定、快速进出与禁用清理。报告：`Logs/Panels/play-mode-validation.txt`。
- 已检查 1920×1080 和 1024×768 截图，卡牌网格、中文文字和滚动条均正常；验证结束自动退出 Play Mode 并恢复时间倍率。
- 本次验证覆盖编辑器编译、资产和运行交互，未执行独立 Player 构建。
