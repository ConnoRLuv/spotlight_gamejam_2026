# BattleScene 战斗演示

打开 `Assets/Scenes/BattleScene.unity` 即可查看参考图布局。场景使用 uGUI、1920×1080 参考画布和 16:9 内容区；4:3 或超宽窗口保留完整内容，外围补白。

## 层级与编辑入口

- `BattleCanvas/BattleLayout_16x9/TopBar`：生命值、钱、地图进度、设置。
- `PlayerArea`：玩家肖像、血条、理智值和四部位装备栏。未使用的部位为空；义体牌成功使用后显示缩略卡面、名称和当前耐久，悬停可查看大图和具体描述。
- `EnemyArea`：敌人肖像、血条、名字、说明。
- `HandAndTurnArea`：行动点、手牌、结束回合、抽牌堆、弃牌堆。`HandArea` 的普通牌/义体牌标签切换对应手牌，超出宽度时可横向拖动。
- `BattleCanvas` 上的 `BattleSceneReferences` 保存布局引用，`BattleHUD` 订阅战斗事件并提交出牌指令。
- `BattleGame/GameBootstrap`：战斗入口，已绑定规则、幻痛、12 张普通牌（攻击/防御/回复各 4 张）和四个义体（每个配置关联 3 张牌）。

进入 Play Mode，敌人说明默认隐藏；鼠标进入敌人肖像时显示，离开时隐藏。编辑模式下说明可见，方便调整位置。

进入 Play Mode 自动开始战斗，敌人生命 30、每次攻击 6。点击攻击牌后点击敌人确认，Esc 可取消；防御、回复及无需选择的义体牌直接结算。脑机打开最多三个候选的选择面板，确认时才支付费用，取消不改变资源或牌堆。启用手部义体后，行动点下方显示 `1 AP → 攻击` 按钮。结束回合执行敌人行动并刷新玩家回合，腿部义体可跳过一次敌人行动。

生命、理智、护盾、AP、耐久和牌堆计数由战斗状态更新。悬停弃牌堆会显示当前普通与义体弃牌，鼠标移入面板可滚动浏览，离开后淡出；详细用法见 `docs/PanelsUsage.md`。地图进度、设置和抽牌堆浏览仍为占位入口。

左侧装备栏只显示本场成功使用的义体，保留现有卡牌效果及理智/耐久支付。脑机等待选牌或取消时不填入，失败出牌不填入；同场跨回合保留，开启新战斗重新清空。详见 `docs/CyberneticEquipmentUsage.md`。

## 卡牌资产

七张预制体位于 `Assets/Prefabs/Cards`，共用 `CardView`，使用低饱和度色块和中文文字。`Assets/GameData/Cards` 保存卡牌，`Effects` 保存效果，`Cybernetics` 保存来源义体；`CardPrefabCatalog` 将配置映射到预制体。费用和效果在配置中修改，运行时实例费用与耐久由 `CardView.Bind` 展示。

幻痛复用目录的默认预制体，显示中性灰色；手部生成的攻击复用攻击预制体，展示 AP 0 和本回合限定提示。不要直接修改共享 `CardData.cost` 来实现免费牌。

## 编辑器工具

`Spotlight > Battle Scene` 菜单提供：

- `Create From Wireframe`：首次创建场景，已有 `BattleScene` 时拒绝覆盖。
- `Validate Current Scene`：检查 UI 引用、中文字体、相机、事件系统和丢失脚本。
- `Export Preview`：输出 1920×1080、1280×720、1024×768、2560×1080 的预览到 `Logs/BattleScene`。
- `Validate Play Mode Interaction`：在 Play Mode 检查按钮与肖像的 UI 射线命中和悬停说明切换，不执行按钮业务。

中文字体为 Noto Sans CJK SC，字体与 SIL OFL 许可证已保存在 `Assets/Fonts`。

`Spotlight > Cards` 提供资产创建与场景接入、资源校验、七张卡牌预览导出和 Play Mode 业务验证。创建工具保留已有配置与预制体；有未保存场景修改时先保存。Play Mode 业务验证会改变当前演示战斗，建议从刚启动的战斗运行。
