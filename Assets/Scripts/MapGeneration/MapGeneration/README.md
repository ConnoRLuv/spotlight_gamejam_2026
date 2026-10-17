# Roguelike 分层地图生成系统

杀戮尖塔风格的关卡地图生成器。分层有向无环图（Layered DAG）+ 随机游走 + 可插拔约束规则，
按"扩展时尽量不改老代码"的目标做了分层。

把整个 `MapGeneration` 文件夹拖进 Unity 工程的 `Assets/` 下即可。

---

## 一、目录结构

```
MapGeneration/
├── RoguelikeMap.Core.asmdef          核心程序集（noEngineReferences = true）
├── Core/Randomness.cs                确定性随机源 + 洗牌/权重抽样工具
├── Model/MapModel.cs                 节点、图、网格坐标（纯数据结构）
├── Model/NodeTypeCatalog.cs          房间类型注册表
├── Config/MapGenerationConfig.cs     全部生成参数（可直接被 ScriptableObject 序列化）
├── Generation/
│   ├── MapGenerationContext.cs       管线上下文 + IMapGenerationStep 接口
│   ├── MapGenerator.cs               生成器（Step 管线 + 校验 + 重试）
│   ├── MapGraphDebug.cs              ASCII 打印，脱离 Unity 也能看结果
│   └── Steps/                        五个内置生成步骤
├── Rules/MapRuleSet.cs               约束规则接口 + 七条内置规则
├── Validation/MapValidators.cs       五个内置校验器
├── Runtime/MapRunState.cs            玩家在地图上的进度
├── Serialization/MapSerializer.cs    DTO 与存档快照（不依赖 UnityEngine）
├── Unity/                            Unity 接入层
│   ├── RoguelikeMap.Unity.asmdef
│   ├── MapGenerationConfigAsset.cs   配置资产（数值 + 美术 + 背景底纹）
│   ├── MapGeneratorBehaviour.cs      MonoBehaviour 封装
│   ├── MapGraphView.cs               UGUI 渲染 + 滚动 + 点击走图
│   ├── MapNodeView.cs                单个节点的表现（图标 / 文字 / 高亮 / 点击）
│   ├── MapEdgeView.cs                单条路线的表现（按方向选图，两端自动对上节点）
│   ├── MapSpriteFactory.cs           没有美术时的兜底图形（圆底 / 高亮环）
│   └── MapJsonUtility.cs             JsonUtility 读写
└── Editor/                           编辑器工具（只在编辑器里编译）
    ├── RoguelikeMap.Editor.asmdef
    └── MapArtPrefabBuilder.cs        把 地图/地图 里的图片做成节点与路线预制体
└── Tests/ConsoleTest/                纯控制台验证工程，不装 Unity 也能调
```

---

## 二、30 秒接入

```csharp
// 完全不碰 Unity 的用法（可以在单元测试、服务器、工具链里跑）
var config = MapGenerationConfig.CreateDefault();
var catalog = NodeTypeCatalog.CreateDefault();

MapGenerationResult result = MapGenerator.CreateDefault().Generate(config, catalog, seed: 12345u);

Debug.Log(MapGraphDebug.ToAscii(result.Graph));   // 直接看 ASCII 图
```

Unity 里的用法：

1. 右键 `Create → Roguelike Map → Generation Config`，生成一个配置资产
2. 场景里放一个空物体，挂 `MapGeneratorBehaviour`，把资产拖进去
3. 同物体上再挂一个 `MapGraphView`（挂在 Canvas 下的拉伸 RectTransform 上）
4. 想让地图用 `地图/地图` 里的美术，点菜单 **Tools → 地图 → 生成节点与路线预制体**，它会把图片
   做成节点 / 路线预制体，并把图标、预制体、背景插画、底纹一起挂到配置资产上（见第十一节）
5. 在 `Start` 之后的任意时机调 `view.Render(behaviour.Graph, behaviour.Catalog, behaviour.RunState)`

预制体、图标、背景这些都存在**配置资产**里，所以换一套皮不用动场景：
场景里的 `MapGraphView` 只要引用同一个资产，节点和路线就会自动换成新的美术。
一个预制体都不配也能跑 —— 会退化成代码生成的圆底节点加直线段。

---

## 三、架构：为什么这样分层

整条生成链路是 **管线 + 规则 + 校验** 三段式，三个部分互相不知道对方的实现：

```
MapGenerator
  ├─ Step 管线 ──► 铺路径 → 消交叉 → 清理 → 分类型 → 挂 Boss
  │                 （每一步只依赖 MapGenerationContext）
  ├─ MapRuleSet ──► 分类型时逐节点查询，任一条 Deny 就换候选
  └─ Validator ───► 出图后体检，不过就换派生种子重生成
```

三个刻意的设计决定：

**1. 核心层零引擎依赖。**
`RoguelikeMap.Core.asmdef` 设了 `noEngineReferences: true`，
所以你在核心层里写 `using UnityEngine;` 会直接编译失败。
这让生成器可以在控制台里跑、可以写单元测试、可以搬到服务器上算。

**2. 节点之间用 int Id 互相引用。**
不用对象引用，序列化、存档、网络同步都只是 int 数组，不用处理引用环。

**3. 用重试代替局部修复。**
约束冲突时不做复杂的图修复，而是换一个派生种子重来。分层 DAG 生成极快——
实测 2000 张图平均 0.135 ms/张，且首次尝试就全部通过。

---

## 四、扩展指南（你最关心的部分）

| 想做的事 | 改哪里 | 改几处 | 要写代码吗 |
|---|---|---|---|
| 调权重 / 层数 / 列数 / 路径数 | ScriptableObject 资产 | 0 | 否 |
| 新增一种房间类型 | 资产里加 TypeKey + 权重表加一条 | 0 | 否 |
| 新增一条类型约束（如"商店不能连着精英"） | 实现 `IMapRule`，`Add` 进 `MapRuleSet` | 1 个新文件 | 是 |
| 新增一个生成阶段（生物群系、地图修饰器） | 实现 `IMapGenerationStep`，`InsertStep` 插进管线 | 1 个新文件 | 是 |
| 新增一条出图体检项 | 实现 `IMapValidator` | 1 个新文件 | 是 |
| 换掉路径铺法（六边形 / 环形 / 手写布局） | 换掉 `CarvePathsStep` | 1 个新文件 | 是 |
| 换随机算法（服务器随机流 / 可回放随机） | 实现 `IRandomSource` | 1 个新文件 | 是 |
| 接入美术（颜色 / 图标 / 预制体） | 资产的 `_visuals` 列表 | 0 | 否 |
| 加地图主题（第一章全是战斗之类的） | 写一个 Step，读完图后按层刷 TypeKey | 1 个新文件 | 是 |

没有任何一种扩展需要修改 `MapGenerator`、`MapGraph` 或其它既有文件。

几个具体的写法：

```csharp
// 新增规则：Boss 前两层不允许出现商店
public sealed class NoShopNearBossRule : IMapRule
{
    public string Name => "Boss 前不出商店";

    public RuleVerdict Evaluate(MapNode node, string candidateTypeKey, MapGenerationContext ctx)
    {
        if (candidateTypeKey != NodeTypeKeys.Shop) return RuleVerdict.Allow;
        if (node.Row >= ctx.Graph.Rows - 3) return RuleVerdict.Deny("离 Boss 太近");
        return RuleVerdict.Allow;
    }
}

// 注册
var generator = new MapGenerator(MapRuleSet.CreateDefault().Add(new NoShopNearBossRule()));
```

```csharp
// 新增阶段：所有精英层强制换成战斗（做"第一章"关卡）
public sealed class NoEliteInActOneStep : IMapGenerationStep
{
    public string Name => "第一章禁用精英";

    public void Execute(MapGenerationContext ctx)
    {
        foreach (MapNode node in ctx.Graph.Nodes)
            if (node.TypeKey == NodeTypeKeys.Elite) node.TypeKey = NodeTypeKeys.Monster;
    }
}

generator.InsertStep(generator.Steps.Count - 1, new NoEliteInActOneStep());
```

> 注意：上面这个 Step 会在分类型之后改类型，所以要插在 `AttachBossStep` 之前，
> 且如果你希望它被校验，记得让它跑在 `RuleConsistencyValidator` 之前（校验器永远在所有 Step 之后跑）。

---

## 五、关键参数速查

| 参数 | 默认 | 说明 |
|---|---|---|
| `Rows` | 15 | 可玩层数（不含 Boss 层） |
| `Columns` | 7 | 横向格子数 |
| `PathCount` | 6 | 铺几条路径，越大分叉越多 |
| `HasStartNode` | true | 是否生成唯一起点节点（第 0 层一个点，向上分叉） |
| `StartTypeKey` | start | 起点节点的类型键 |
| `MaxOutDegree` | 3 | 单节点最大出度（随机游走天然满足） |
| `MaxGenerationAttempts` | 64 | 校验失败时的最大重试次数 |
| `MinPathCount` / `MaxPathCount` | 8 / 4096 | 有效路径数的合格区间 |
| `FixedRows` | `{1:战斗, 14:商人}` | 固定层 |
| `Weights` | 见下 | 权重 = `BaseWeight + WeightPerRow × 层号` |
| `FallbackOrder` | 战斗→事件→商人 | 规则全拒绝时的降级顺序 |

节点只保留五种内容类型：**战斗 / 精英 / 事件 / 商人 / 首领**，外加一个"起点"标记。
（这个类型的键一直是 `event`；早期版本的显示名写的是"对话"，现在改成"事件"。键没变，旧存档不受影响。）

默认权重（实测出的分布：战斗 50%、事件 28%、商人 10%、精英 9%、起点 2%、首领 2%）：

```
monster   Base 45.0  PerRow -0.60   MinRow 2
event     Base 22.0  PerRow +0.30   MinRow 2
elite     Base  0.0  PerRow +1.50   MinRow 5
shop      Base  4.0  PerRow +0.25   MinRow 5
```

权重用的是 `BaseWeight + WeightPerRow × row` 线性模型，而不是 `AnimationCurve`，
目的就是让配置类不依赖 UnityEngine。想要曲线的话，在 Unity 层把曲线烘焙成若干段线性区间塞进权重表即可。

---

## 六、默认规则与杀戮尖塔的对应

| 规则 | 内置实现 |
|---|---|
| 第 0 层是唯一起点 | `HasStartNode`（铺路径步骤） |
| 第 1 层必是战斗（起点后的第一场） | `FixedRows` |
| Boss 前一层必是商人 | `FixedRows` |
| 精英 / 商人不能连续出现 | `NoConsecutiveSameTypeRule` |
| 前 5 层不出精英、商人 | `MinRowRule` + 权重表的 `MinRow` |
| 商人 / 精英总量上限 | `MaxTypeCountRule` |

规则是**数据 + 可插拔对象**，不是硬编码进生成器的条件分支。
想要一套完全不同的规则，`MapRuleSet.CreateEmpty()` 然后自己拼。

---

## 七、存档的两种方案

**方案 A（推荐）：只存种子。**

```csharp
MapRunSnapshot snap = behaviour.CaptureSnapshot();   // 就一个 uint + 一个 int 列表
string json = MapJsonUtility.ToJson(snap);

// 读档
behaviour.TryRestore(MapJsonUtility.SnapshotFromJson(json));
```

因为生成是确定性的，同种子必然重建出完全相同的图。
快照里带了 `ConfigHash`，配置被改过时会给出警告，而不是静默生成一张不一样的地图。

**方案 B：存完整图。** `MapSerializer.ToDto / FromDto`，
适合"地图允许被运行时修改、不能靠种子重建"的情况。

---

## 八、验证情况

核心层已用 Roslyn 按 C# 7.3（Unity 兼容档位）编译通过，零错误零警告，并跑过以下测试：

| 测试 | 结果 |
|---|---|
| 批量生成 2000 张 | 2000/2000 成功，平均 0.125 ms/张，最大重试次数 0 |
| 确定性 | 同种子两次生成的节点、边、类型完全一致 |
| 序列化往返 | DTO 转出再转回，图完全一致 |
| 走图 | 从起点到 Boss 走满 15 层，读档后进度一致 |
| 扩展 | 只加数据（不改生成器代码）新增房间类型即生效 |

Unity 接入层（`Unity/` 目录）没有 Unity 环境可以编译验证，请以导入后的编译器提示为准。

---

## 九、已知取舍

- **Boss 前一层固定商人 → 倒数第二层不能是商人。** 因为 `NoConsecutiveSameTypeRule` 是双向检查的。
  如果你想让商人连出两个，从默认规则集里去掉这一条即可。
- **`MaxTypeCountRule` 在最终校验时会排除节点自身。** 否则一个已经就位的精英会把自己算进数量、
  导致假阳性——这个坑在测试里踩过一次，代码里留了注释。
- **DTO 的 `Seed` 是 `uint`。** Unity 的 `JsonUtility` 能处理，如果你的版本报不支持，
  把 DTO 里的字段改成 `int` 即可，核心层不用动。
- **地图没有做对称性 / 美观度优化。** 连线只保证不交叉，不保证视觉均衡。
  如果想让节点更匀称，可以再加一个 Step 做位置微调。

---

## 十、脱离 Unity 调参的工作流

`Tests/ConsoleTest` 是一个独立的 .NET 控制台工程，直接引用核心层的源码：

```bash
cd Tests/ConsoleTest
dotnet run
```

它会打印一张 ASCII 地图、跑 2000 次批量生成统计成功率和类型分布、
验证确定性与序列化，最后跑一遍"新增房间类型"的扩展测试。

调数值的时候用这个，比反复开 Unity 编辑器快得多。

---

## 十一、美术接入：节点 / 路线 / 背景底纹

`地图/地图` 里的图片，点一次菜单就会变成地图用的东西：

> **Tools → 地图 → 生成节点与路线预制体**
>
> 命令行也行：`Unity.exe -batchmode -quit -projectPath <工程> -executeMethod RoguelikeMap.EditorTools.MapArtPrefabBuilder.BuildAll`

它会做四件事，而且可以重复跑（换了图重跑一次就行）：

1. 每种房间类型做一个节点预制体 → `地图/地图/Prefabs/Nodes/Node_*.prefab`
2. 每个方向做一个路线预制体 → `地图/地图/Prefabs/Routes/Route_*.prefab`
3. 把这些预制体、图标、背景插画、底纹写进 `MapGenerationConfig.asset`
4. 顺手把出厂的间距 / 尺寸调成和美术匹配的值（手调过的值不会被覆盖）

> **打开工程时会自动挂一次。**`MapArtAutoWire` 在编辑器加载之后检查一遍，发现美术没挂就自动跑工具，
> 挂好之后不再介入（一次会话只试一次）。想完全手动控制，把 `MapArtPrefabBuilder.cs` 里的
> `MapArtAutoWire.AutoWireOnLoad` 改成 `false`。
>
> 工具还会先统一校正美术图片的导入设置（必须是**单张图 / Sprite**），并把每张图真正的 guid 与局部 ID 打进 Console ——
> 图"挂了却显示不出来"时，这行日志是最好用的线索。

### 美术引用失效时会怎样

配置资产里的引用如果指向了一张已经不存在的图（美术被删掉、或者被别的编辑器版本重新导入过），
Unity 里这种"空壳引用"**不等于 null**：直接赋给图片组件，会在生成网格的阶段抛
`MissingReferenceException`（一屏节点能刷出上百条）。所以运行时代码做了三件事：

- 挂图之前先用 `MapSpriteFactory.IsUsable` 验一道，失效就当作"没配图"，退回代码生成的圆底 / 直线段；
- 同一条提示只打一次，不会把 Console 淹掉；
- 代码生成的兜底图也会自检，域重载被关掉时不会抱着上一局的死图不放。

修好的办法就一个：运行一次菜单工具（或者重开工程，自动挂载会处理）。

图片和房间类型的对应关系写在 `MapArtPrefabBuilder.NodeArt` 里，换文件或加一行就能改：

| 房间类型 | 图片 | 房间类型 | 图片 |
|---|---|---|---|
| 起点 | `起点.png` | 商人 | `商店.png` |
| 战斗 | `怪物（普通）.png` | 事件 | `事件.png` |
| 精英 | `怪物（精英）.png` | 首领 | `怪物（BOSS）.png` |

路线是六张按方向画的片段：`分支（上）`、`分支（上右）`、`分支（上左）`、`分支（下）`、`分支（下右）`、`分支（下左）`。
工具会读每张图边缘的透明像素，自动推出"这一段从哪连到哪"，写进预制体的 pivot 和 `MapEdgeView._targetEnd`。
所以摆放时线段的起点正好在父节点、终点正好在子节点上，不用手调坐标。

> 起点原来没有图，只能用代码生成的纯色圆底。现在补了一张 `起点.png`：一个绿色向上的箭头，
> 描边用的是整套图标共用的那支颜色。它是脚本按现有图标的配色画出来的，想换成手绘的，
> 直接替换同名文件、再跑一次工具即可（不用改代码）。

### 当前位置标记（起点的逻辑）

玩家脚下那个节点会一直亮着，并套一个更大的金色高亮圈 —— 开局时它就是起点，所以一进地图就能看到"我在这里"：

- 它不会被"走过"的灰色压暗（普通已走过的房间会），
- 它和"可达节点"的高亮是两套颜色：可达 = 房间类型色，当前位置 = 金色，
- 起点不可点击、也不会被当成可选房间 —— 这是核心层的规则（`MapRunState.Reset` 直接把玩家放在起点并记为已访问，
  `GetOptions()` 返回的是它的后继），视图只是把状态显示出来。

颜色和大小在 `MapView → Style → Current Highlight Color / Current Highlight Scale`；
把 `Show Available Highlight` 关掉会连同可达高亮一起关掉。

### 为什么格子是竖长的

这六张线是**按竖长的格子**画的：斜线的弦大约 1 : 2.3。
所以默认间距取列距 150、行距 350 —— 这个比例下，斜着的那四张图**几乎不用旋转**（实测 ≤3°）就能正好接上两个节点。
把行距改小，斜线会被转得更斜，但两端依然精确落在节点上（`MapRouteLayout.RotateToFit` 保证的）。

### 三层画面：背景插画 / 底纹 / 地图

绘制顺序从下到上是：

```
Background（背景插画）   固定在原地，铺满整个地图区域，不跟着滚
Backdrop（底纹）         跟着地图一起滚的图案，宽度 = 地图宽 + BackdropPadding×2
Edges / Nodes            路线和节点，跟着底纹一起滚
```

- **背景插画**：`MapGenerationConfigAsset.BackgroundSprite`（默认挂 `背景插画.JPG`）。勾了
  `BackgroundPreserveAspect` 就保持原比例，多余的地方留空。
- **底纹**：`BackdropSprite`（默认 `底纹.png`）。靠 UV 平铺画出来，`BackdropTileScale` 控制每一块多大，
  `BackdropPadding` 是它比地图四周大出来的量（同时也是两边留给背景插画的宽度），
  `BackdropAlpha` 压透明就能让插画透出来。
  工具会把 `底纹.png` 的 Wrap Mode 改成 `Repeat` —— 平铺必须 Repeat，否则接缝会被拉成一条线。

### 滚动

长地图（15 层 × 350 间距 ≈ 5300 单位）默认走**滚动**布局：把 `MapGraphView` 上的"自动缩放"关掉即可。
滚轮上下滚的时候，底纹和关卡图标一起动，背景插画不动。

| 参数 | 位置 | 说明 |
|---|---|---|
| `_autoFitToViewport` | MapGraphView | 打开 = 整张图缩到一屏（总览用）；关掉 = 原生大小 + 滚动 |
| `_scrollSpeed` | MapGraphView | 滚轮速度，0 = 不响应滚轮，只留接口给外部控制 |
| `_scrollToCurrentOnEnter` | MapGraphView | 走一步之后自动把镜头滚到当前节点 |

想用代码控制镜头：`ScrollToBottom()`、`ScrollToTop()`、`ScrollToNode(nodeId)`；
读 `ScrollOffset` 就知道现在滚到哪儿了。

> 横向放不下的时候（比如手机竖屏），地图会整体等比缩小到刚好占满宽度，纵向仍然靠滚动，不会挤成一团。
