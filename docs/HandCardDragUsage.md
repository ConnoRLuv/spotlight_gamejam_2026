# 手牌拖拽

BattleHUD 在运行时将手牌挂到现有 `ReorderableDragItem` / `ReorderableDragList`，不需要重建 BattleScene 或手牌预制体。

- 单敌人和全敌人目标牌拖到敌人立绘上有效；其他目标模式拖到玩家立绘上有效。依据当前实例配置判断，兼容复仇女神脑机临时转换出的攻击牌。
- 达到原有拖动阈值才开始。拖动时取消长按进度和放大卡面；普通单击和未拖动的长按继续保留。
- 黑幕上方仅显示 HandArea、有效目标的原立绘，以及对应生命条、理智条和护盾。利用临时 Canvas 排序保留立绘的透明轮廓，透明区域后的场景仍暗，不再显示方形亮区。黑幕拦截指针，临时关闭 EventSystem 导航，保留按钮正常颜色。拖动期间 `BattleHUD.IsChoosing` 返回 true。
- 松开时先恢复原卡牌位置和输入状态，再提交出牌。成功的卡牌按原规则进入弃牌堆；无效位置、取消、失焦、切页、战斗重绑或停用时不支付任何费用。
- 新世界脑机、弃牌回收等交互牌继续使用 HUD 现有排序或选牌流程，拖放组件不结算费用、耐久或使用次数。

`HandCardDragController` 只负责显示与输入。目标由 `Configure` 的委托提供，附加高亮对象通过 `highlightedObjects` 提供，放置完成后回调业务。`HandDragBackdrop` 是完整黑幕，不复制、拉伸或重新挂接人物与 HandArea。临时 Canvas 的排序与射线器状态在取消、松开和停用时恢复，运行时创建的组件缓存复用，销毁控制器时清理。

拖拽前调用 `BattleController.CanStartPlay`，与真实出牌共用 `CardPlayService` 的只读请求校验。阶段、牌区、行动点、理智/生命支付、义体来源/耐久/次数、封锁、候选和效果前提均使用同一规则；不扣费、不移动牌、不通知出牌事件。理智仍沿用已有的正值允许透支规则，不在 UI 中另设支付门槛。真实松开提交时再次校验，防止拖动期间状态改变。

独立拖拽组件新增固定占位模式 `ReorderEnabled = false`，配合 `CanBeginDrag`、`DragStarting` 和 `DragFinished`。默认仍启用排序，抽牌堆保持原有行为；回调执行前卡牌已归位，可安全刷新或销毁卡牌展示。

验证入口为 `HandCardDragTests`。截图测试 `CaptureAttackAndSelfDragAtThreeResolutions` 会在 `Temp/HandCardDrag` 输出真实运行画面；三种分辨率均使用原相机及画布，结束后恢复渲染与输入状态。
