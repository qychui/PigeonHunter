# Pigeon Hunter 排行榜实施方案

## 1. 文档定位

本文档描述 Pigeon Hunter 排行榜正式实施前需要建立的功能、数据规则和开发顺序。

排行榜目标为三种游戏模式分别拥有三个榜单范围：

```text
Mode A：本地榜 / 每周榜 / 全部榜
Mode B：本地榜 / 每周榜 / 全部榜
Mode C：本地榜 / 每周榜 / 全部榜
```

本文档与 `PIGEON_HUNTER_REFACTOR_PLAN.md` 相互独立。原文档仍只约束零行为变更的代码清理和 Region 配置；排行榜通过新的独立脚本接入，不以重构现有游戏结构为前提。

当前实施状态：第一阶段的成绩生命周期控制器、三模式接入、owner 校验、总命中累计、强制结算失效和 Prefab 挂载已经完成。PlayerData、本地榜、每周榜、全部榜和排行榜 UI 尚未实现。

## 2. 当前状态与主要问题

当前成绩主要保存在 `UIController.scoreCurrent` 中。三种模式的最高分也由 `UIController` 中的三个 `[UdonSynced]` 字段维护。

现有实现可以显示当前房间最高分，但不能直接作为正式排行榜的数据层，原因包括：

- 分数状态与 UI 显示职责耦合。
- 只有最高分数，没有成绩所属玩家和完整记录。
- 数据不能跨实例持久化。
- 无法区分本地、每周和全部范围。
- 没有一局成绩的唯一标识、规则版本和重复提交保护。
- 没有明确处理 owner 转移、晚加入和调试结算产生的无效成绩。

排行榜不应从现有房间 Top Score 功能直接扩展。应先建立独立的正式成绩记录，再让本地持久化和外部排行榜消费该记录。

## 3. 排行榜定义

### 3.1 模式标识

三种模式必须使用稳定且互不混用的模式标识：

```text
Mode A：单鸽模式
Mode B：双鸽模式
Mode C：射击场模式
```

本地持久化键和外部服务的 `leaderboardId` 都必须包含模式标识。即使三种模式暂时使用相同计分单位，也不能放入同一排行榜。

### 3.2 榜单范围

```text
本地榜：当前实例内玩家各自持久化的历史最佳成绩
每周榜：外部服务按服务器时间统计的本周成绩
全部榜：外部服务保存的全时最佳成绩
```

本地榜不是当前客户端的单人历史列表，也不是场景对象上的房间最高分。它与 Mario 排行榜一样，只能枚举当前实例玩家，并读取这些玩家已恢复的 PlayerData。

每周榜和全部榜需要外部聚合服务。VRChat PlayerData 无法枚举当前实例之外的离线玩家，因此不能独立提供真正的世界级周榜和全部榜。

## 4. 正式成绩记录

### 4.1 成绩字段

一局正式成绩至少包含以下字段：

```text
modeId           游戏模式
finalScore       最终分数
reachedRound     到达回合
totalHits        本局累计命中数
rulesetVersion   排行榜与计分规则版本
buildVersion     游戏构建版本
runId            本局唯一标识
eligible         是否具有排行榜资格
invalidReason    资格失效原因
```

外部上传时还需要由 PlayerData 或排行榜上传层提供玩家标识、显示名和提交来源，但这些信息不应混入现有游戏 UI 的分数状态。

### 4.2 排名规则

建议三种模式暂时使用同一套稳定排序规则：

```text
1. finalScore 降序
2. reachedRound 降序
3. finalScore 和 reachedRound 都相同时并列
```

个人最佳只在新成绩的 `finalScore` 更高，或同分但 `reachedRound` 更高时替换。两项完全相同时不覆盖旧记录。

正式开发前必须冻结以下规则：

- `finalScore` 是否使用 UI 上经过 `maxScore` 限制后的值。
- `reachedRound` 表示失败回合还是已完成回合。
- 计分规则修改时如何提升 `rulesetVersion`。

推荐将失败时正在进行的回合作为 `reachedRound`。

## 5. 成绩归属与资格

Pigeon Hunter 是 owner 驱动的共享游戏。正式成绩应属于开始游戏时的 gameplay owner。

建议采用以下规则：

- 模式正式开始时锁存本局玩家。
- 只有该玩家的本地客户端可以保存和上传成绩。
- 远端客户端只负责显示同步结果，不能将共享游戏成绩保存为自己的成绩。
- 晚加入玩家不能获得已经开始的本局成绩。
- 一局过程中发生 gameplay owner 转移时，本局排行榜资格失效。
- 使用调试开始、强制通过、强制失败或强制结算时，本局资格失效。
- 非正常回合或非标准入口开始时，本局资格失效。
- 同一 `runId` 只能结算和提交一次。

建议定义以下资格失效原因：

```text
InvalidNone
InvalidDebugCommand
InvalidForcedSettlement
InvalidOwnerTransfer
InvalidWrongStart
InvalidLateJoinState
InvalidInterrupted
```

排行榜资格失效不能中断现有游戏。游戏仍应正常结算、播放动画并返回菜单，只跳过排行榜保存和上传。

## 6. 第一阶段：成绩生命周期控制器

### 6.1 新脚本

首先新增独立的 `PigeonRunRecordController`，暂时不实现排行榜 UI 和外部上传。

建议职责：

- 准备一局新的成绩记录。
- 管理本局状态和排行榜资格。
- 锁存开始时的模式和 owner。
- 在正式 Game Over 后生成最终成绩。
- 防止同一局重复结算。
- 向后续 Persistence 和 Global Bridge 暴露只读结果。

建议状态：

```text
RunIdle
RunPrepared
RunRunning
RunFinalized
RunInvalid
```

建议公共接入方法：

```text
PrepareRun(modeId)
BeginRun()
RecordHit()
FinalizeRun(finalScore, reachedRound)
InvalidateRun(reason)
ResetRun()
```

脚本之间继续使用 UdonSharp 兼容的基本类型和 getter，不新增自定义 class 或 struct 作为成绩载体。

### 6.2 现有游戏接入点

原有脚本只增加必要通知，不重写现有开始、回合或结算流程：

```text
模式确认完成        -> PrepareRun(modeId)
第一回合正式开始    -> BeginRun()
Mode A/B 正式失败   -> FinalizeRun(...)
Mode C 正式失败     -> FinalizeRun(...)
重开或返回菜单      -> ResetRun()
owner 或调试状态变化 -> InvalidateRun(reason)
```

排行榜记录是整局 Session 成绩，不应在每个 Round 结束时提交。

Mode A/B 必须接在原有正式 Game Over 流程中。Mode C 必须接在 `shootingRangeGameOver` 正式确定的流程中。两个入口最终都只调用同一个成绩控制器，但不要求合并原有游戏结算实现。

### 6.3 最终分数时机

最终成绩必须在所有分数变化完成后锁存，特别需要覆盖：

- 普通命中分数。
- 不同目标产生的动态分数。
- Perfect 奖励。
- owner 回合结果同步后的最终分数。
- Mode C 独立结算流程产生的分数。

不能在 `EndRound()` 刚触发时提交整局成绩。若仍有 Perfect 动画或延迟奖励等待结算，应在正式 Game Over 确认且分数稳定后调用 `FinalizeRun()`。

## 7. 第二阶段：PlayerData 持久化和本地榜

成绩控制器验证稳定后，再新增 `PigeonLeaderboardPersistence`。

建议职责：

- 等待本地玩家的 `OnPlayerRestored`。
- PlayerData 尚未恢复时缓存待保存成绩。
- 为 Mode A、Mode B、Mode C 分别保存个人最佳。
- 按正式排名规则判断是否替换旧记录。
- 玩家加入、离开或 PlayerData 更新时刷新当前实例榜。
- 提供当前玩家最佳成绩 getter，供 UI 和外部上传使用。

建议每个模式分别保存：

```text
hasRecord
bestScore
bestRound
bestTotalHits
bestDateYmd
rulesetVersion
buildVersion
runnerId
```

PlayerData 键必须带项目、版本和模式前缀，例如：

```text
ph.lb.v1.modeA.bestScore
ph.lb.v1.modeB.bestScore
ph.lb.v1.modeC.bestScore
```

现有三个 `[UdonSynced]` Top Score 字段在开发期间继续保留，避免影响原有标题画面和网络行为。新本地榜稳定后，再单独决定将旧显示保留、删除或改为当前玩家个人最佳。

## 8. 第三阶段：每周榜和全部榜

### 8.1 外部榜单标识

建议为三种模式分配独立的外部标识：

```text
pigeon_single_v1
pigeon_pair_v1
pigeon_range_v1
```

每个标识分别支持：

```text
weekly
alltime
```

### 8.2 服务端职责

外部服务至少需要：

- 接收并校验成绩字段。
- 使用服务器接收时间决定成绩所属周。
- 根据 `runId` 或 nonce 拒绝重复提交。
- 校验模式、规则版本、构建版本和数值范围。
- 每个玩家、每个模式、每个范围只返回最佳记录。
- 分别提供 weekly 和 alltime 查询。
- 使用与本地榜一致的排序规则。

周榜不能使用客户端提供的日期作为权威时间。

### 8.3 VRChat 上传与读取

可以沿用 Mario 排行榜的整体架构：

```text
PigeonRunRecordController
        |
        +-> PigeonLeaderboardPersistence
        |
        +-> PigeonGlobalLeaderboardBridge
                    |
                    +-> 外部提交服务

PigeonGlobalLeaderboardReader
        |
        +-> weekly / alltime 查询
        |
        +-> PigeonGlobalLeaderboardView
```

受 VRChat 动态 HTTP 能力限制，上传可以继续采用 Mario 的玩家辅助流程：

```text
生成签名 URL
-> 玩家复制到 VRCUrlInputField
-> 玩家点击提交
-> VRCStringDownloader 发起 GET
-> 显示上传结果
```

Pigeon Hunter 应使用自己的提交协议和字段，不要将 `reachedRound` 等数据伪装成 Mario 的 `world/area/completed` 字段。

上传或下载失败不能影响游戏结算、本地成绩保存或返回菜单。

## 9. 第四阶段：排行榜 UI

UI 最后实现。界面应维护两个独立选择状态：

```text
游戏模式：Mode A / Mode B / Mode C
榜单范围：本地 / 每周 / 全部
```

它们共同决定当前显示的数据集，形成九种组合，但不建议复制九套脚本或九套完整面板。

建议结构：

```text
PigeonLeaderboardModeController
    currentGameMode
    currentBoardScope

LocalLeaderboardPanel
    当前实例 PlayerData 排行

GlobalLeaderboardPanel
    Weekly / AllTime 外部排行
```

本地榜和外部榜可以使用不同的数据读取器，但应共享尽可能一致的行显示格式。

每个榜单至少需要以下显示状态：

- 正常记录列表。
- 当前玩家记录或排名。
- 无记录。
- PlayerData 加载中。
- 外部榜加载中。
- 下载失败。
- 上传未配置或未完成。
- 分页和刷新冷却。

## 10. 实施顺序

严格按以下顺序开发和验证：

```text
1. 冻结成绩字段和排名规则
2. 实现 PigeonRunRecordController
3. 接入 Mode A/B/C 的开始和正式 Game Over
4. 实现 owner、晚加入、调试和重复结算资格控制
5. 验证三种模式最终成绩准确
6. 实现三模式 PlayerData 个人最佳
7. 实现当前实例本地榜
8. 定义并实现外部提交协议和服务端
9. 实现每周榜和全部榜读取
10. 实现三模式与三范围的排行榜 UI
```

不要先制作榜单面板，也不要先接外部服务器。正式成绩记录如果不稳定，后续九个榜单都会保存或显示错误数据。

## 11. 第一阶段验收标准

在开始 PlayerData 和排行榜 UI 前，成绩生命周期必须满足：

- 三种模式均能准确识别一局开始和结束。
- 只有开始时的 owner 能产生有效成绩。
- 远端和晚加入玩家不会保存当前局成绩。
- owner 中途转移会使成绩失效。
- 调试和强制结算不会产生有效成绩。
- 同一局不会重复提交。
- Mode A/B 的 Perfect 奖励包含在最终分数中。
- Mode C 使用自己的正式结算结果。
- 最终模式、分数、回合和命中数均可从成绩控制器读取。
- 排行榜控制器异常或缺失不会影响原游戏流程。
- C# 和 UdonSharp 编译通过。

完成以上验收后，才能开始实现 PlayerData 本地榜。
