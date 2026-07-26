# Pigeon Hunter 重构与排行榜准备方案

## 1. 文档目标

本方案的目标不是立即实现排行榜，而是先清理和重构 Pigeon Hunter，使游戏具备稳定、统一、可验证的成绩数据源和整局生命周期。

排行榜的最终规划为：

- Mode A：本地、每周、全部
- Mode B：本地、每周、全部
- Mode C：本地、每周、全部

即 3 个游戏模式乘以 3 个榜单范围，共 9 个逻辑榜单。

本阶段只为这 9 个榜单准备公共数据和接口，不实现排行榜面板、PlayerData 持久化、外部 API 上传或榜单读取。

## 2. 当前代码的主要问题

### 2.1 分数由 UI 持有

当前权威分数保存在 `UIController.scoreCurrent` 中，Mode A/B、Mode C、同步快照和 Top Score 都直接读取或修改这个字段。

这造成以下问题：

- UI 同时是数据模型和显示层。
- 隐藏、替换或拆分 UI 可能影响游戏结果。
- 将来排行榜只能依赖具体 UI 组件读取最终成绩。
- 分数修改入口分散，难以保证没有重复加分。
- Perfect 奖励由 UI 动画结束时直接加分，业务结果依赖动画是否正常播放。

结论：分数必须迁移到独立的整局状态控制器，UI 只能显示分数。

### 2.2 Mode A/B 和 Mode C 有两套整局状态

Mode A/B 的整局状态主要位于 `ActionController`：

- `currentRoundIndex`
- `gameLocked`
- `roundActive`
- `roundEndPending`
- `carryScorePending`
- `carryScoreValue`

Mode C 的整局状态主要位于 `GameManager`：

- `shootingRangeSessionActive`
- `shootingRangeRoundsCompleted`
- `shootingRangeGameOver`
- `shootingRangeRoundEndPending`
- `shootingRangeRoundPassed`
- `shootingRangeHitsThisRound`

因此外部系统无法通过一个统一入口回答以下问题：

- 当前是否正在进行一局游戏？
- 当前模式是什么？
- 当前分数是多少？
- 当前到达第几回合？
- 当前成绩是否有效？
- 整局是否已经结算？

### 2.3 `GameManager` 职责过多

当前 `GameManager` 同时负责：

- 模式选择和开始
- 重开和返回标题
- Mode C 的全部玩法流程
- Mode A/B 的网络事件转发
- Mode C 的网络事件转发
- 所有权转移
- 枪械重生和闪光
- 场景对象显隐
- 晚加入快照
- 强制结算调试入口

这使 `GameManager` 成为修改任何功能时都必须触碰的中心文件，也让排行榜接入容易继续扩大该文件。

### 2.4 `UIController` 职责过多

当前 `UIController` 同时负责：

- 分数数据
- 房间 Top Score 数据与同步
- 模式选择
- 数字材质显示
- 子弹显示
- 命中状态显示
- Round、Good、Perfect、Game Over 显示
- Mode C 开场流程
- 多种逐帧 UI 动画
- Perfect 奖励结算

其中“数据、业务结算、输入选择、纯显示和动画”互相混在一起。

### 2.5 重置语义不明确

当前存在多层重置：

- `ResetAndRestartGame`
- `ReturnToTitleScreen`
- `ActionController.Initialize`
- `ResetRoundState`
- `ResetRoundRuntimeState`
- `ResetShootingRangeSessionState`
- 各种 Clear/Reset UI 方法

部分方法会清理状态后立即开始新回合，部分会保留 Game Over，部分会回标题画面。调用者很难仅从名字判断清理范围。

### 2.6 多人所有权与成绩归属没有独立规则

当前拾取枪械会转移 gameplay ownership。游戏可以在所有权变化后继续，但排行榜需要明确：

- 成绩属于谁？
- 中途换人是否仍是个人成绩？
- 远端客户端是否可以把同步得到的分数保存到自己的 PlayerData？

如果不先解决这个问题，将来可能出现房间内所有玩家保存同一成绩，或者后来拾枪的玩家继承前一个玩家分数的情况。

## 3. 目标架构

建议采用组合式结构，不引入复杂继承、反射、委托或通用接口层。UdonSharp 下应优先使用明确的组件引用、普通公开方法和必要的自定义事件。

目标职责如下：

```text
GameManager
  负责总流程编排、模式选择、组件装配和兼容入口

PigeonSessionController
  负责整局状态、分数、进度、计时、成绩有效性和最终结果

ActionController（后续可改名 PigeonRoundController）
  只负责 Mode A/B 的回合、波次、鸽子生成和命中结算

ShootingRangeController
  从 GameManager 抽出，只负责 Mode C 的回合、波次和飞盘逻辑

SyncController
  只负责网络传输、去重、快照和所有权事件，不直接决定 UI 或排行榜结果

UIController
  迁移期作为 UI 门面，最终只协调多个显示组件

InstanceTopScoreController
  负责当前房间的电视 Top Score；它和未来的本地排行榜是两种概念
```

## 4. 新增统一整局控制器

建议新增 `PigeonSessionController`，作为未来排行榜唯一可信的数据源。

### 4.1 状态定义

建议使用整数常量而不是依赖复杂枚举序列化：

```text
Idle       尚未准备
Prepared   已选择模式，等待实际开始
Running    正在游戏
Settling   最终结算中，禁止继续修改成绩
Finished   已生成一次最终结果
Invalid    游戏可以继续，但成绩不可进入排行榜
```

`RoundActive`、`roundEndPending`、波次状态仍由具体模式控制器维护；Session 状态只表示“整局”，不要把回合和波次状态也塞入 Session。

### 4.2 权威字段

建议至少包含：

```text
sessionState
gameMode
score
roundReached
roundsCleared
elapsedTimeMs
startServerTimeMs
eligibleForLeaderboard
invalidReason
resultRevision
hasFinalResult
starterPlayerId
starterDisplayName
```

说明：

- `gameMode` 使用统一的 1、2、3，不再让部分代码使用 0、1、2 后长期传播。
- `roundReached` 表示玩家实际进入的最高回合。
- `roundsCleared` 表示成功通过的回合数。
- `resultRevision` 用于保证一次整局只结算一次。
- 玩家名称只用于展示，不能作为唯一身份或防作弊凭据。

### 4.3 公共方法

建议提供以下明确入口：

```text
PrepareSession(int mode)
BeginSession()
AddScore(int amount)
SetScoreFromNetwork(int value)
EnterRound(int roundNumber)
CompleteRound(int roundNumber)
BeginFinalSettlement()
FinishSession()
InvalidateSession(int reason)
ResetSession()
```

并提供只读查询方法：

```text
GetState()
GetMode()
GetScore()
GetRoundReached()
GetRoundsCleared()
GetElapsedTimeMs()
IsRunning()
IsFinished()
IsLeaderboardEligible()
HasFinalResult()
GetResultRevision()
```

未来排行榜只读取这些方法，不读取 `UIController`、`ActionController` 或 Mode C 私有字段。

### 4.4 最终结果冻结

`FinishSession()` 必须具备幂等性：

- 第一次调用冻结结果并增加 `resultRevision`。
- 后续重复调用不重复生成结果。
- `Finished` 或 `Invalid` 后不再允许 `AddScore`。
- 结果冻结后，UI 动画可以继续播放，但不能再修改成绩。

这能避免 Game Over 音频、动画、同步回调或重复网络事件导致重复提交。

## 5. 分数系统重构

### 5.1 分数写入统一化

所有业务代码改为调用：

```text
sessionController.AddScore(amount)
```

写入成功后，由 Session 通知 UI 刷新，或者由调用方在迁移期显式执行：

```text
uiController.SetScoreDisplay(sessionController.GetScore())
```

最终应删除或停止业务代码使用：

```text
UIController.scoreCurrent
UIController.AddScore
UIController.AddScoreForHit
UIController.SetScoreValue
```

迁移期间可以暂时保留这些方法作为兼容转发，但它们只能转发到 Session，不能再保存另一份分数。

### 5.2 Perfect 奖励迁移

当前 Perfect 奖励由 `UIController.EndPerfectDisplay()` 在动画结束时加分。这应改为：

1. 模式控制器确定本回合 Perfect。
2. 模式控制器计算奖励。
3. 立即通过 Session 增加奖励并形成确定的业务结果。
4. UI 只接收“显示多少奖励”和“播放多久”，不得修改分数。

这样即使 UI 对象缺失、动画被关闭或玩家晚加入，最终成绩仍一致。

### 5.3 分数上限

当前分数会受到数字材质位数和 `UIController.maxScore` 限制。重构后需要区分：

- 数据上限：由游戏规则决定。
- 显示上限：由六位数字材质决定。

建议 Session 使用明确的 `maxSessionScore`。UI 只对显示值做格式化或溢出显示，不得反向截断 Session 的权威分数。

## 6. Mode A/B/C 生命周期统一

### 6.1 统一开始流程

当前 `StartModeA` 和 `StartModeB` 高度重复，应合并为：

```text
StartPigeonMode(int mode)
```

Mode C 保留独立玩法入口，但走相同的 Session 前置流程：

```text
StartConfirmedMode(mode)
  -> ResetForNewSession()
  -> sessionController.PrepareSession(mode)
  -> 配置模式场景和模式控制器
  -> 在实际允许玩家射击时 BeginSession()
```

不要在收到模式选择同步时让每个客户端都创建自己的有效成绩。只有符合成绩所有权规则的客户端可以建立 eligible Session，其他客户端只应用网络镜像状态。

### 6.2 统一回合进度

Mode A/B：

- `BeginRound()` 后调用 `EnterRound(currentRound)`。
- 成功结算后调用 `CompleteRound(currentRound)`。
- 失败结算不增加 `roundsCleared`。

Mode C：

- 开始当前射击场回合时调用 `EnterRound(shootingRangeRoundsCompleted + 1)`。
- 成功后调用 `CompleteRound(currentRound)`。
- 失败时保持 `roundReached = roundsCleared + 1`。

这样未来三个模式都能提供一致的 `roundReached` 和 `roundsCleared`。

### 6.3 统一整局结束

Mode A/B 只有失败回合才结束整局，不能在每次 `EndRound()` 时结束 Session。

推荐接入点：

- Mode A/B：失败结果已经确定、最终分数已经计算完成，但 `FinalizeEndRound()` 尚未清理状态时。
- Mode C：`FinalizeShootingRangeRoundEnd()` 判断失败后、设置 Game Over 并返回标题前。

两个模式控制器最终都只调用：

```text
sessionController.BeginFinalSettlement()
sessionController.FinishSession()
```

Game Over UI、音频和返回标题不再负责生成成绩。

## 7. GameManager 清理方案

### 7.1 保留职责

`GameManager` 最终只保留：

- 引用装配与启动初始化
- 模式选择请求
- 开始、重开、返回菜单的总流程编排
- 枪、模式控制器、Session、Sync 和 UI 之间的少量路由
- 对旧 Prefab/UdonEvent 暂时提供兼容入口

### 7.2 抽出 Mode C

建议新增 `ShootingRangeController`，迁移以下代码：

- Mode C Session 内的回合与波次字段
- 飞盘生成、池索引和轨迹参数
- Mode C 命中/射击次数
- Mode C 回合通过判断
- Mode C 难度与生命周期计算
- Mode C Round End 流程
- Mode C 波次种子应用

`GameManager` 不再直接维护 `shootingRangeGameOver` 等玩法字段，而是通过统一的 Session 状态和 `ShootingRangeController` 查询回合状态。

### 7.3 合并重复初始化

将 `StartModeA`、`StartModeB`、`StartModeC` 中重复的 UI 清理、目标回收和状态初始化收敛为：

```text
ResetCommonPresentation()
ResetAllTargetPools()
ConfigureModeScene(int mode)
StartModeController(int mode)
```

`StartPigeonMode(mode)` 和 `StartShootingRangeMode()` 只保留模式差异。

### 7.4 明确重置层级

建议统一为四种语义：

```text
ResetWaveState()       仅清理当前波次
ResetRoundState()      清理当前回合，保留整局分数
ResetSessionState()    清理整局成绩和所有模式运行状态
ReturnToMenu()         清理运行状态并切换标题画面
```

“重开当前模式”应显式调用：

```text
ResetSessionState()
StartConfirmedMode(currentMode)
```

不要再通过局部保存 `shootingRangeGameOver` 后调用一个会重置它的方法来实现返回标题。

## 8. ActionController 清理方案

### 8.1 限定为 Mode A/B 控制器

`ActionController` 的名字过于宽泛。完成迁移后建议改名为 `PigeonRoundController`，职责限定为：

- 单鸽/双鸽回合流程
- 波次和子弹窗口
- 鸽子生成、回收与结算动画触发
- 回合命中数与通过判断
- Mode A/B 确定性随机计划的应用

### 8.2 删除整局分数所有权

应删除：

- `carryScorePending`
- `carryScoreValue`
- 从 `UIController.scoreCurrent` 读取/写回分数的代码

通过回合后，Session 分数天然保留，不再需要先从 UI 抄到 `carryScoreValue`，再在下一回合写回 UI。

### 8.3 删除重复 Game Over 状态

`gameLocked` 可以暂时保留为 Mode A/B 内部输入锁，但“整局是否结束”的权威状态应来自 Session。

完成迁移后：

- 输入锁是回合控制细节。
- Game Over 是 Session 状态。
- 标题画面是否显示是 UI 状态。

三者不再由同一个布尔值间接推断。

### 8.4 保留不应强行抽象的内容

以下逻辑不建议与 Mode C 合并：

- 单鸽/双鸽生成策略
- 飞行方向和边界偏转
- 配对鸽结算动画
- 鸽子自然逃离
- Mode A/B 的确定性 Round Plan

这些是具体玩法，不是公共整局模型。强行做通用基类会增加 UdonSharp 复杂度，却不能减少真正的维护成本。

## 9. UIController 拆分方案

建议分两步拆分，避免一次性破坏 Prefab 上大量引用。

### 9.1 第一阶段：保留门面

保留 `UIController` 的公开方法，让现有调用继续工作，但内部逐步委托给独立组件：

```text
ModeSelectionView
ScoreDisplay
RoundStatusDisplay
HitIndicatorView
WeaponStatusView
ResultAnimationView
SceneViewController
```

### 9.2 第二阶段：删除业务状态

UI 最终不得持有：

- 权威分数
- 整局是否结束
- 回合是否通过
- Perfect 奖励值的业务结算权
- 排行榜成绩有效性

UI 可以持有：

- 当前显示到第几帧或第几步
- 动画计时器
- 当前显隐状态
- 材质和 GameObject 引用

### 9.3 房间 Top Score 独立

当前三个 `[UdonSynced]` Top Score 字段应迁移到 `InstanceTopScoreController`。

它表示当前房间电视上展示的最高分，与未来榜单不同：

- Instance Top Score：当前实例内的即时共享记录。
- Local Leaderboard：当前实例玩家各自持久化的历史最佳记录。
- Weekly/All：外部服务提供的跨实例排名。

不要让这三种数据共用同一个字段或同步方式。

## 10. 网络同步重构

### 10.1 权威顺序

所有同步结果应遵守：

```text
网络数据到达
  -> 验证 revision/requestId
  -> 更新玩法控制器和 Session
  -> 根据 Session 刷新 UI
```

禁止网络回调先修改 UI，然后再从 UI 反推业务状态。

### 10.2 统一 Session 快照

当前 Mode A/B 同步 Round Result，Mode C 同步 Round Snapshot。建议在不替代具体玩法事件的前提下，增加统一 Session 快照：

```text
sessionRevision
sessionState
gameMode
score
roundReached
roundsCleared
eligibleForLeaderboard
```

用途：

- 晚加入玩家恢复公共显示。
- 所有客户端看到一致的分数和回合。
- 未来榜单逻辑能区分“本地有效结果”和“远端镜像结果”。

命中、飞盘生成、鸽子生成等高频玩法事件仍保留模式专用 payload，不必强行合并。

### 10.3 所有权变化策略

建议采用以下竞争成绩规则：

- Session 开始时记录 gameplay owner。
- 只有该玩家的本地客户端拥有可保存的成绩。
- 其他客户端只维护镜像 Session，永远不能保存该成绩。
- Session 运行中发生 gameplay ownership 转移时，将本局标记为 `InvalidOwnerTransfer`。
- 游戏本身可以继续，但最终结果不进入后续 9 个榜单。

如果以后希望支持多人轮流射击，应另外设计 Team/Shared 排行榜，不能把共享成绩当成个人成绩。

## 11. 面向 9 个榜单的数据准备

### 11.1 两个选择维度

排行榜 UI 不应真的复制九套完整组件，而应使用两个正交选择维度：

```text
Mode:   A / B / C
Period: Local / Weekly / All
```

组合后得到 9 个逻辑榜单，共享同一套 Slot、分页和展示视图。

### 11.2 每个模式独立规则

未来建议使用独立标识：

```text
pigeon_mode_a_v1
pigeon_mode_b_v1
pigeon_mode_c_v1
```

每个模式有各自的：

- PlayerData Key 前缀
- Weekly API 数据
- All API 数据
- rulesetVersion
- 最佳成绩

本地榜按模式读取 PlayerData；Weekly 和 All 按模式请求外部 API。

### 11.3 公共结果结构

Session 最终应能提供以下结构所需的全部值：

```text
mode
score
roundReached
roundsCleared
durationMs
finishedDate
buildVersion
rulesetVersion
eligible
invalidReason
resultRevision/runId
```

当前推荐排名顺序为：

```text
score 降序
roundReached 降序
durationMs 升序
```

最终排序规则可以在实现榜单前再次确认，但重构阶段必须完整保留这些原始字段，不能只保留一个格式化字符串。

## 12. 文件调整建议

建议最终形成：

```text
PigeonHunter/UScripts/Core/
  GameManager.cs
  PigeonSessionController.cs
  PigeonGameMode.cs

PigeonHunter/UScripts/Modes/Pigeon/
  PigeonRoundController.cs
  PigeonTarget.cs

PigeonHunter/UScripts/Modes/ShootingRange/
  ShootingRangeController.cs
  ClayTarget.cs

PigeonHunter/UScripts/Networking/
  SyncController.cs
  SessionSnapshotCodec.cs（只有确实需要编码时再添加）

PigeonHunter/UScripts/UI/
  UIController.cs
  ModeSelectionView.cs
  ScoreDisplay.cs
  RoundStatusDisplay.cs
  HitIndicatorView.cs
  WeaponStatusView.cs
  ResultAnimationView.cs
  SceneViewController.cs
  InstanceTopScoreController.cs

PigeonHunter/UScripts/Weapons/
  GunController.cs

PigeonHunter/UScripts/Audio/
  SoundManager.cs
```

不要在第一步直接移动所有文件。Unity `.meta`、Prefab 引用和 UdonSharp Program Asset 对大规模路径/类型调整较敏感，应在每个阶段保证场景可打开、UdonSharp 可编译后再继续。

## 13. 分阶段实施顺序

### 阶段 0：建立行为基线

- 记录三个模式正常开始、通过、失败、重开和返回标题的行为。
- 记录每个回合的计分、Perfect 奖励和分数上限。
- 记录单人、双客户端、晚加入和中途换枪 owner 的结果。
- 暂不重命名文件或移动目录。

验收：获得一份可重复执行的手动测试矩阵。

### 阶段 1：引入 Session，但保持原行为

- 新增 `PigeonSessionController`。
- 在 GameManager 中建立统一引用。
- 接入模式准备、开始、进入回合、通过回合和失败整局事件。
- Session 先镜像现有分数，不立即删除旧字段。
- 增加开发期一致性检查：Session 分数必须等于旧 UI 分数。

验收：三个模式的 Session 生命周期正确，现有画面和玩法不变。

### 阶段 2：迁移权威分数

- 所有命中得分改写 Session。
- Perfect 奖励从 UI 迁移到模式结算。
- 同步结果先更新 Session，再刷新 UI。
- `UIController` 的旧分数方法改为兼容转发。
- 删除 `carryScorePending/carryScoreValue`。

验收：关闭部分 UI 动画或显示对象后，最终分数仍正确；不存在双重加分。

### 阶段 3：统一开始、失败和重置流程

- 合并 Mode A/B 开始方法。
- 引入明确的 Wave/Round/Session/Menu 重置入口。
- 统一整局 Finish 调用。
- 移除通过 `gameLocked` 和 `shootingRangeGameOver` 推断公共 Session 状态的代码。

验收：所有模式每局只产生一次最终结果，重开后 revision 和成绩从新局开始。

### 阶段 4：抽出 ShootingRangeController

- 将 Mode C 玩法字段和方法从 GameManager 迁出。
- GameManager 只负责调用 Mode C 控制器。
- 保持现有 Mode C 同步 payload，先不同时重写网络协议。

验收：GameManager 不再包含飞盘生成、轨迹、波次和 Mode C Round End 细节。

### 阶段 5：拆分 UIController

- 先保留 UIController 门面。
- 逐块迁移 Mode Selection、Score、Round、Hit、Weapon、Result 和 Scene View。
- 抽出 Instance Top Score。
- 删除 UI 中所有业务数据写入。

验收：UI 组件只消费状态；替换 ScoreDisplay 不影响分数结果。

### 阶段 6：整理网络边界

- 增加统一 Session Snapshot。
- 所有网络回调先更新状态再更新显示。
- 集中 requestId/revision 去重规则。
- 接入所有权变化导致的成绩失效。
- 验证晚加入不会创建可保存的本地成绩。

验收：双客户端分数、模式、回合和 Session 状态一致；只有正确 owner 的本地结果 eligible。

### 阶段 7：删除兼容层和死代码

- 删除 UI 权威分数字段和旧转发方法。
- 删除重复 Game Over 状态。
- 删除不再使用的重置分支和重复 UI 清理。
- 删除临时一致性检查。
- 最后再进行安全的文件改名和目录移动。

验收：全项目搜索不到业务代码直接读写 UI 分数，GameManager 和 UIController 的职责显著缩小。

### 阶段 8：排行榜接口冻结

- 冻结 Session 最终结果字段和读取方法。
- 确认三个模式的排名规则与 rulesetVersion。
- 确认 Local/Weekly/All 的数据源和失败策略。
- 此阶段结束后再开始实现排行榜。

验收：排行榜实现不需要修改鸽子、飞盘、枪械或 UI 动画的内部逻辑。

## 14. 测试矩阵

每个阶段至少覆盖：

| 场景 | Mode A | Mode B | Mode C |
|---|---:|---:|---:|
| 正常开始第一局 | 必测 | 必测 | 必测 |
| 普通命中计分 | 必测 | 必测 | 必测 |
| Perfect 奖励 | 必测 | 必测 | 必测 |
| 通过一回合并保留分数 | 必测 | 必测 | 必测 |
| 失败并冻结最终结果 | 必测 | 必测 | 必测 |
| Game Over 后重开 | 必测 | 必测 | 必测 |
| 返回菜单后切换模式 | 必测 | 必测 | 必测 |
| 双客户端同步 | 必测 | 必测 | 必测 |
| 晚加入快照 | 必测 | 必测 | 必测 |
| 中途转移 owner | 必测 | 必测 | 必测 |
| 重复网络事件 | 必测 | 必测 | 必测 |
| 缺失部分 UI 引用 | 必测 | 必测 | 必测 |

重点断言：

- 一次命中只加一次分。
- 一次整局只产生一次最终结果。
- UI 动画不能改变权威成绩。
- 通过回合不会结束 Session。
- 失败回合会记录正确的 `roundReached`。
- 重开不会继承上一局分数或 eligibility。
- 远端镜像结果不会被当作本地个人成绩。
- owner 转移后成绩按规则失效，但玩法仍能继续。

## 15. 完成标准

在开始排行榜开发前，Pigeon Hunter 应满足：

- 存在唯一的权威 Session 分数。
- 存在统一的 Mode A/B/C 整局状态。
- 三个模式通过同一接口报告分数、回合和最终结果。
- UI 不再持有或修改业务成绩。
- GameManager 不再包含完整的 Mode C 玩法实现。
- Mode A/B 不再通过 UI 临时保存跨回合分数。
- 所有权转移有明确的成绩有效性规则。
- 晚加入只恢复镜像状态，不产生本地有效成绩。
- 最终结果具有 revision/runId，能够防止本地重复处理。
- 9 个逻辑榜单可以只依赖 Session 最终结果实现，不需要再次侵入玩法代码。

## 16. 明确不在本阶段实施的内容

- 不制作排行榜面板。
- 不创建九套榜单 Slot。
- 不写 PlayerData 排名逻辑。
- 不请求 Weekly/All API。
- 不复制或修改 Mario 排行榜 Prefab。
- 不生成上传 URL 或签名。
- 不实现反作弊服务。
- 不为了未来排行榜改变现有计分数值和难度规则。

先完成数据权威、生命周期、所有权和职责拆分，再实现排行榜，可以显著降低后续返工风险。
