# 义体选择组件

4 张无文字/图标的透明外部底图，统一画布 224×246 px：
- PartTab-Normal.png / PartTab-Selected.png：顶部部位页签，带底部计数分区。
- DeckOption-Normal.png / DeckOption-Selected.png：卡组选项，无内部文字分区。
- Selected 底部带指示标记，常态在相同位置预留透明区域，切换时保持 RectTransform 大小不变。

6 张 Icon-*.png 直接复制项目原始义体图标，未重绘。

Unity 导入：Texture Type = Sprite (2D and UI)，Sprite Mode = Single，Alpha Is Transparency = 开，Mip Maps = 关，Compression = None，Filter Mode = Bilinear。
底框用 Image / Simple；保持原比例，不建议把含选中指示标记的整张底图设成 Sliced。
部位页签建议显示 112×123；卡组选项建议显示 134×148，按现有布局调整。
图标作为独立子 Image，开启 Preserve Aspect；文字用独立 Text/TMP。
常态与选中态切换 Sprite，正文、图标和计数不烘焙在底框内。
未修改场景或脚本。

底框使用 imagegen 对效果图清除内容后裁切导出，提示要求保留边框、倒角、选中指示和现有配色，仅去除八个选项内部的图标和文字。图标直接来自 Assets/Art/义体。