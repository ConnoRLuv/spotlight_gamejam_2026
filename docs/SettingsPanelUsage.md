# 战斗设置面板

BattleScene 的 SettingsButton 打开 SettingsModal，使用现有 SettingPanel 美术实例和 70% 黑色遮罩。设置面板保持原美术尺寸并居中，遮罩铺满画布。

“继续游戏”按钮或 Esc 立即关闭。显示时关闭只读牌堆、取消待确认攻击，锁住底层战斗输入；正在选牌、调整牌序或拖拽时不会打断当前操作。关闭或停用组件会恢复原输入状态。打开和关闭不改时间倍率、生命、理智、塞勒币、牌组或耐久。

音量栏右侧的滑条实时调节总音量，最左端静音、最右端为最大音量。使用 AudioListener.volume 控制全部 AudioSource，通过 PlayerPrefs 的 SpotlightGameJam.Audio.MasterVolume 保存。关闭设置、切场景、应用暂停或退出时写入磁盘；下一次启动在首个场景加载前应用保存值。首次启动默认最大音量。

“返回标题”结束当前局，清理 AdventureSession 中的资源、牌组、地图进度和战斗上下文，异步进入 StartupScene。加载期间保留黑幕并禁止重复点击；音量偏好继续保留。标题场景已追加到构建列表末尾，原有场景顺序保持不变。

共享 SettingPanel.prefab 保持原样，仅战斗实例移除 PopupTween 的测试控制，由 BasePanel 管理显隐，并增加音量滑条。

菜单 Spotlight → UI → Wire Battle Settings Panel 可增量补齐接线，重复执行不会创建重复对象，并确保 StartupScene 已启用且不会重排现有构建场景。
