# Pigeon Hunter 零行为变更代码清理与 Region 配置方案

## 1. 文档定位

本方案替代此前的 Pigeon Hunter 逻辑重构方案。

此前方案包含统一计分规则、调整数据权威、重写结算状态、合并方法和重新分配职责等内容。这类修改会改变可执行代码，可能引入玩法、动画时序或网络同步回归，因此不再属于本轮代码优化范围。

本轮只做两件事：

- 清理不影响运行结果的代码噪音。
- 使用 `#region` 整理现有脚本的阅读结构。

本轮不实现排行榜，也不为排行榜改造现有游戏逻辑。排行榜应在现有游戏验证稳定后，通过独立方案和独立脚本接入。

## 2. 核心原则

### 2.1 零行为变更

清理前后必须保持以下内容完全一致：

- 三种模式的开始、回合、波次、命中、失败和重开流程。
- 分数、Perfect 奖励、难度和通关规则。
- 动画、音频、延迟和 UI 显示时序。
- owner、远端和晚加入玩家的网络同步行为。
- 随机种子、生成顺序和随机调用次数。
- Unity Inspector、Prefab、场景和 Udon Program Asset 引用。
- 所有 public、UnityEvent、Interact、NetworkCallable 和字符串事件入口。

发现逻辑问题时只记录，不在本轮顺手修复。修复必须使用单独任务、单独分析和单独验证矩阵。

### 2.2 保持资产和脚本结构

- 不新增、删除、移动或重命名运行时脚本。
- 不修改类名、命名空间和 `.meta` GUID。
- 不替换 Prefab 上的组件。
- 不手动修改 UdonSharp Program Asset 或 `SerializedUdonPrograms`。
- 不修改 Prefab、场景、材质、AnimatorController 和音频资产。
- 不将现有职责拆到新脚本。
- 不建立新的基类、接口、配置对象或规则对象。

### 2.3 Region 只用于导航

`#region` 不代表代码架构发生变化，也不授权移动职责或改写逻辑。

配置 Region 时：

- 只在现有成员边界前后插入 `#region` 和 `#endregion`。
- 不为了让方法进入某个 Region 而移动方法。
- 不改变方法顺序。
- 不在方法内部添加 Region。
- 不嵌套 Region。
- Region 名称使用英文职责名，避免 `Misc`、`Other`、`Temp` 等模糊名称。
- 一个 Region 应包含连续、相近的现有代码；不能跨越互不相关的方法。

## 3. 允许修改的白名单

本轮代码改动只能属于以下类型：

### 3.1 Region

```csharp
#region Mode Selection And Start Flow

// 原有代码，内容和顺序不变

#endregion
```

允许增加、删除或更正 `#region/#endregion`，但不得借此调整可执行代码。

### 3.2 空白和格式

- 删除多余空行、行尾空格和重复缩进。
- 统一大括号和换行格式。
- 统一文件末尾换行。
- 不执行会大面积改写整个文件的自动格式化。

### 3.3 注释

- 删除整块已注释掉的旧代码。
- 删除与当前代码明显不符的过期注释。
- 修正拼写错误和错误的模式名称。
- 为复杂现有流程增加简短的职责说明。
- 不通过注释掩盖未解决的逻辑问题。

### 3.4 Using

- 删除编译器能够确认未使用的 `using`。
- 不增加与本轮清理无关的依赖。
- 删除后必须重新编译，防止扩展方法或类型解析发生变化。

## 4. 明确禁止的修改

以下内容即使看起来更简洁，也不属于本轮清理：

- 修改任何条件判断、循环、返回值或调用顺序。
- 提取、合并、拆分或内联方法。
- 修改方法参数、访问级别或名称。
- 新增业务辅助方法。
- 删除未确认的 private、public 或序列化成员。
- 将多个布尔值改成枚举或阶段状态机。
- 统一 Mode A/B 与 Mode C 的规则实现。
- 修改目标得分、命中特效等级或回合来源。
- 修改 UI 与 GameManager 之间的数据流。
- 修改 Reset、Start、Update、Interact 或 OnDeserialization 流程。
- 修改 requestId、revision、NetworkCallable 或 RequestSerialization。
- 调整即时网络事件与手动序列化的组合。
- 修改随机公式、salt、seed、随机调用次数或生成顺序。
- 修改常量、默认值、Tooltip、Range 或 Inspector 配置。
- 修改字段序列化形式或使用 `[FormerlySerializedAs]` 迁移字段。
- 删除空脚本、备份文件或 Program Asset。

下列此前建议明确取消，不在本轮实施：

```text
统一 Perfect、难度、通关和命中档位规则
为目标新增 GetScoreForRound 等业务接口
将结算布尔组合改写为阶段状态机
重新分配 GameManager、ActionController、UIController 职责
删除兼容 public 方法或序列化字段
拆分 Mode C、Session、UI 或网络控制器
```

## 5. Region 配置规范

Region 只按当前代码的实际连续顺序配置。如果文件中的职责交叉，允许出现两个名称带前缀的相关 Region，例如：

```text
Mode C Network Events
Mode C Runtime And Settlement
```

不要移动方法来强行合成一个 Region。

### 5.1 GameManager.cs

建议按现有顺序选择适用区域：

```text
Configuration And Runtime State
Ownership
Session State And Public Result Access
Shared Existing Rule Queries
Gun Network Events
Initialization And Receiver Binding
Main Loop And Public Round Routing
Mode Selection And Scheduled Start
Common Mode Start Presentation
Mode A And B Routing
Mode A And B Network Apply
Mode C Network Events
Mode C Target Events
Pigeon Exit Presentation
Mode C Runtime
Mode C Settlement
Gun Runtime Utilities
Scene Presentation And Menu Toggles
```

约束：

- 不合并 Mode A/B 和 Mode C 的实现。
- 不移动 Mode C 方法来重新排序。
- 不调整 Session 或网络同步逻辑。

### 5.2 ActionController.cs

```text
Runtime State And Public Properties
Initialization And Main Tick
Round Flow
Synced Round Plan
Synced Round Result
Round Settlement
Hit And Shot Flow
Single Mode Resolution
Pair Mode Resolution
Spawn And Wave Flow
Target Pool
Deterministic Random Plan
Reset And UI Helpers
```

约束：

- 不改写 `Tick()`。
- 不调整回合结算阶段或动画等待顺序。
- 不修改随机计划和同步等待条件。

### 5.3 SyncController.cs

```text
Receiver Configuration
Synced Payload Fields
Handled Request State
Ownership
Visual Object Sync
Menu Selection And Start Sync
Mode A And B Sync
Session Snapshot
Mode C Sync
Deserialization And Replay
Flow Events
Receiver Helpers
```

约束：

- 网络发送方法、NetworkCallable 和 Apply 方法保持原顺序。
- `OnDeserialization()` 的 Apply 顺序完全不变。
- `ReplayPendingReceiverEvents()` 的顺序完全不变。
- 不尝试用通用 payload 或数组抽象不同网络事件。

### 5.4 UIController.cs

```text
Serialized References And Display State
Initialization And Main Tick
Mode Selection
Score Round And Difficulty Display
Bullet Display
Pigeon Hit Indicators And Masks
Clay Hit Indicators And Masks
Settlement Animation Public API
Settlement Animation Ticks
Overlay And Shooting Range Intro
Room Top Score
Digit Material Rendering
Buffers And Raw Object Helpers
```

约束：

- 不合并鸽子与飞盘动画状态机。
- 不删除兼容显示或计分入口。
- 不修改 UdonSynced Top Score 字段。

### 5.5 PigeonTarget.cs

```text
Configuration Runtime State And Properties
Lifecycle And Setup
Flight Initialization
Exit And Escape Requests
Hit Resolution And Feedback
Scoring Queries
Animator State
Movement And Lifetime
Boundary Reflection
Coordinate Space And Bounds
Audio
Deterministic Random
```

约束：

- 不修改确定性随机数算法。
- 不调整碰墙反射、逃离或回收条件。
- 不与 `ClayTarget` 建立公共基类。

### 5.6 ClayTarget.cs

```text
Configuration Runtime State And Properties
Lifecycle And Flight
Hit Resolution And Recycling
Visual Timeline
Audio
Scoring Queries And Feedback
```

约束：

- 不修改抛物线、视觉阶段和回收判断。
- 不与 `PigeonTarget` 合并生命周期。

### 5.7 MainAreaAnimationController.cs

```text
Configuration And Runtime State
Lifecycle
Public Animation Commands
Animator State
Generic Movement State
Round Start Movement
Round Next Movement
Hit Miss And End Round Movement
Audio And Rendering Helpers
```

约束：

- 不修改任何持续时间、阶段常量或物体显隐时机。
- 不合并相似但时序不同的运动流程。

### 5.8 其他脚本

`GunController.cs`：

```text
Configuration And Runtime State
Pickup And Ownership
Input And Fire Control
Raycast And Hit Routing
Effects And Audio
Reset And Helpers
```

`SoundManager.cs`：

```text
Audio References And Sequence State
Single Sound Playback
Round Sequences
Sequence Tick And Stop
Helpers
```

`QychuiUtilities.cs`：

```text
Audio And Particle Helpers
Vector And Transform Helpers
RectTransform Bounds
Collider Helpers
```

文件较短时可以不使用 Region。不要为了形式统一给只有几个方法的文件增加大量 Region。

## 6. 清理流程

### 阶段 0：建立基线

- 保存清理前的编译结果。
- 记录三个模式的单机、双客户端和晚加入测试结果。
- 记录当前警告，区分项目已有警告和新增警告。
- 搜索 public、NetworkCallable、UnityEvent 和字符串事件入口。

### 阶段 1：只配置 Region

- 一次只处理一个脚本。
- 只插入 Region，不移动方法。
- 每处理一个文件，检查 Region 数量是否成对。
- 编译确认无预处理器或括号错误。

### 阶段 2：非行为清理

- 删除未使用 using。
- 删除明确的注释代码和过期注释。
- 清理空白、缩进和文件末尾换行。
- 不删除成员、不改方法体。

### 阶段 3：Diff 审计

逐文件检查差异。允许出现的新增或删除内容只能是：

```text
#region
#endregion
using
注释
空白
```

如果 diff 中出现以下内容，本轮改动不合格：

```text
if / else / switch / for / while
return
方法调用
赋值语句
字段或常量
方法签名
Attribute
网络事件名称
```

### 阶段 4：验证

- `Assembly-CSharp` 编译为 0 错误。
- Unity 中触发 UdonSharp 编译。
- 打开相关场景和 Prefab，确认没有 Missing Script。
- 验证三种模式正常开始、通过、失败和重开。
- 验证 owner、远端、Perfect 分数、鸽子生成和晚加入同步。

## 7. 验收标准

- 没有新增、删除、移动或重命名脚本。
- 没有修改 Prefab、场景、Program Asset 或 `.meta`。
- 所有 Region 成对且没有方法内 Region。
- 可执行语句与清理前完全一致。
- public、UnityEvent、Interact、NetworkCallable 和字符串事件入口完全一致。
- 序列化字段、默认值和 Inspector 配置完全一致。
- 网络字段、事件顺序和随机调用顺序完全一致。
- C# 与 UdonSharp 编译通过。
- 三种模式及双客户端行为与基线一致。

## 8. 问题记录规则

清理过程中发现以下问题时，只记录到单独的问题列表，不直接处理：

- 分数或 Perfect 奖励不同步。
- 新玩家加入状态不一致。
- 远端游戏开始但目标不生成。
- UI 字段参与业务计算。
- 重复规则或重复状态机。
- 可疑死代码、空脚本或备份文件。
- 可能不再使用的 public 方法和序列化字段。

每个问题后续单独处理时必须包含：

```text
复现步骤
根因
允许修改的脚本和行为范围
网络与晚加入影响
回归测试矩阵
```

## 9. 排行榜边界

本轮不实现或准备排行榜代码，不修改现有游戏来适配排行榜。

后续排行榜仍规划为三个模式各自拥有：

```text
Local
Weekly
All
```

共 9 个逻辑榜单。排行榜设计必须单独评审，并优先通过新增排行榜专用脚本读取稳定结果；不能借代码清理任务修改现有玩法、计分、UI 动画或网络同步。

## 10. 最终结论

本方案中的“优化”只表示提高代码可读性和可导航性，不表示调整架构或业务实现。

执行时以零行为变更为最高约束。任何需要修改可执行代码的事项，无论改动多小，都必须退出本方案并建立独立任务。
