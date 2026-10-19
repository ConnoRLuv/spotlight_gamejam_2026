# 卡牌拖拽排序

`ReorderableDragList` 与 `ReorderableDragItem` 是独立的 uGUI 拖拽组件，不读取 CardData、战斗状态或费用。可复用于网格、横向或纵向排列的 UI 项。

```csharp
var list = content.GetComponent<ReorderableDragList>();
if (!list) list = content.gameObject.AddComponent<ReorderableDragList>();
list.Configure(scrollRect, dragLayer, true);
list.Reordered += OnReordered; // 使用方负责成对解绑；列表与使用方同寿命时也可在销毁时释放。
foreach (RectTransform item in content) list.Attach(item);
// OnReordered(int from, int to)：从原索引移出并插入目标索引，更新自己的数据列表。
```

`content` 的直接子节点应全部为可排序项，由现有 GridLayoutGroup 或其他 LayoutGroup 排列。`dragLayer` 应位于同一根画布下、在滚动裁剪之外，且不拦截射线。组件在拖动期间留下占位，把原物体移到浮层；松开后回到内容区，发布一次索引变化。靠近视口上下边缘时自动滚动，使用真实时间。短按或长按预览不会改动顺序，开始拖动会关闭卡面的长按预览。

切页、隐藏或重绑时调用 `CancelDrag()`；未完成拖动恢复原位置，不发布排序事件。`Configure(scrollRect, null, false)` 退出排序模式；只读牌面应停用对应 ReorderableDragItem，让 ScrollRect 恢复拖动滚动。

手牌使用同一组件的固定占位模式 `ReorderEnabled = false`，通过 `CanBeginDrag`、`DragStarting`、`DragFinished` 提供目标判定和松开后的操作。详见 [手牌拖拽](HandCardDragUsage.md)。默认排序行为保持不变。

## 新世界脑机

HUD 用当前两套完整顺序提交出牌请求，成功打开现有 DrawPilePanel 时结算一次理智、共享耐久、部位次数与成功出牌记录。随后调用 `drawPanel.OpenReorder(context, finished)`，面板不会再次收费。

- 普通牌、义体牌使用独立草稿，左上第一张为堆顶；拖动、切页不改变真实牌堆。
- 点击关闭或 Esc 时校验两套草稿完整且没有重复实例，再应用顺序。
- 外部 Hide、新战斗、战斗结束或停用时只清理未提交草稿；已支付费用不退回。
- 排序时面板临时居中，黑幕覆盖根画布，背景 CanvasGroup 同时锁定鼠标与键盘导航；结束后恢复原位置和输入设置。
- 默认 `ShowDrawPile()` 仍为只读浏览，不开放免费排序。旧 NewWorldBrainPanel 类与资源保留兼容，不再由脑机入口打开。

验证使用 `DrawPileReorderTests`，覆盖真实指针拖动、两套顺序、黑幕射线、一次费用、切页和战斗重绑清理、边缘滚动；现有长按与只读面板测试继续使用。
