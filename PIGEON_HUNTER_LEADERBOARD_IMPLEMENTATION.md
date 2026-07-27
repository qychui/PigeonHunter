# Pigeon Hunter 排行榜实施方案

## 1. 文档定位

本文档描述 Pigeon Hunter 排行榜正式实施前需要建立的功能、数据规则和开发顺序。

排行榜目标为三种游戏模式分别拥有三个榜单范围：

```text
Mode A：本地榜 / 每周榜 / 全部榜
Mode B：本地榜 / 每周榜 / 全部榜
Mode C：本地榜 / 每周榜 / 全部榜
```

本文档与 `PIGEON_HUNTER_REFACTOR_PLAN.md` 相互独立。原文档仍只约束零行为变更的代码清理和 Region 配置；排行榜通过新的独立脚本接入，不以重构现有游戏结构为前提。外部服务的固定字段、签名和返回格式以 `DUCK_LEADERBOARD_API_V1.md` 为准。

当前实施状态：第一阶段的成绩生命周期控制器、三模式接入、owner 校验、总命中累计、强制结算失效和 Prefab 挂载已经完成。第二阶段的三模式 PlayerData 个人最佳持久化、恢复前成绩缓存、玩家记录查询接口和当前实例本地榜已经完成。复制的 Mario 排行榜 Prefab 已替换为 Pigeon 本地榜控制器；Mode A、Mode B、Mode C 与 Local、Weekly、AllTime 是两组独立选择状态。Duck API v1 契约、独立服务端路由、数据表、迁移、签名配置和线上部署已经完成。Unity 端 `PigeonGlobalLeaderboardBridge`、`PigeonGlobalLeaderboardReader` 和 `PigeonGlobalLeaderboardView` 均已实现并替换 Prefab 中的旧 Mario 组件；生产 Duck 签名值已与服务端配置核对一致，六个模式/范围查询 URL 已配置，Weekly 与 AllTime 已启用。`RetroTV DuckHunt` Prefab 和当前场景中的已解包副本也已接入同一套成绩记录、PlayerData 持久化和上传 Bridge。

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

### 7.1 当前实例本地榜实现

当前本地榜沿用 Mario Prefab 中的通用 `Leaderboard` 和 `LeaderboardSlot` 组件创建当前实例玩家槽位，新增的 `PigeonLocalLeaderboardController` 负责：

- 根据当前选择的 Mode A、Mode B、Mode C 切换 PlayerData 键前缀。
- 区分有效记录、已恢复但无记录、尚在加载三种状态。
- 按 `finalScore` 降序、`reachedRound` 降序排序。
- 对分数和回合都相同的成绩显示并列名次。
- 显示玩家名、分数、回合、总命中数和记录日期。
- 处理玩家加入、离开、PlayerData 恢复和更新后的延迟刷新。
- 复用原 Prefab 的上一页、下一页和滚动位置控制。

复制 Prefab 中的 Mario 本地持久化展示脚本已替换为 `PigeonLocalLeaderboardController`。旧 Mario 提交、外部读取和外部视图组件均在原组件位置替换为 Pigeon 实现，从而保留原有 GameObject、文件 ID、分页按钮和上传按钮事件，同时不再访问 Mario 外部接口。

Mode A、Mode B、Mode C 三个按钮只承担游戏模式选择；Local、Weekly、AllTime 三个按钮只承担榜单范围选择。三个范围均已可用。两组按钮共同由 `PigeonLeaderboardModeController` 管理，切换模式不会改变范围，切换范围也不会改变模式；外部范围下切换模式或范围会请求对应 Duck 数据集。

## 8. 第三阶段：每周榜和全部榜

### 8.1 命名约定

用户界面和范围常量统一使用 `AllTime`，因为它与 `Weekly` 描述的是同一维度的时间范围，也能避免玩家将 `Global` 误解成跨区域服务器或全体在线玩家。

代码中的 `Global` 保留为外部排行榜模块的统称，例如 `PigeonGlobalLeaderboardReader`、`PigeonGlobalLeaderboardView` 和 `GlobalLeaderboardPanel`。该模块同时承载 `Weekly` 与 `AllTime`，因此这里的 `Global` 表示“由外部服务聚合”，不是一个可选择的榜单范围。不要同时存在 `ScopeGlobal` 和 `ScopeAllTime` 两套含义相同的范围常量。

### 8.2 外部榜单标识

外部 API、服务端模块、数据表和榜单标识统一使用 `Duck` 命名；Unity 游戏脚本继续使用 `Pigeon` 命名。三种模式使用以下独立外部标识：

```text
duck_single_v1
duck_pair_v1
duck_range_v1
```

固定接口为：

```text
GET /api/v1/duck/runs/submit
GET /api/v1/duck/leaderboards/{leaderboardId}?period=week|all
```

提交协议不包含 `durationMs`、`completed`、`world`、`area` 或额外的 `modeId`。`leaderboardId` 已经唯一表示模式，完整字段顺序和 HMAC 输入以 `DUCK_LEADERBOARD_API_V1.md` 为准。

每个标识分别支持以下 UI 范围与 API period 映射：

```text
Weekly -> period=week
AllTime -> period=all
```

### 8.3 服务端职责

外部服务至少需要：

- 接收并校验成绩字段。
- 使用服务器接收时间决定成绩所属周。
- 根据 `runId` 或 nonce 拒绝重复提交。
- 校验模式、规则版本、构建版本和数值范围。
- 每个玩家、每个模式、每个范围只返回最佳记录。
- 分别提供 weekly 和 alltime 查询。
- 使用与本地榜一致的排序规则。

周榜不能使用客户端提供的日期作为权威时间。

### 8.4 VRChat 上传与读取

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

### 8.5 Unity 提交 Bridge 当前实现

`PigeonGlobalLeaderboardBridge` 当前负责：

- 根据 `PigeonLeaderboardModeController` 当前模式选择 `duck_single_v1`、`duck_pair_v1` 或 `duck_range_v1`。
- 当前模式存在刚正式结算且资格有效的成绩时生成 `live` 提交；否则使用该模式 PlayerData 个人最佳生成 `saved_best` 提交。
- 严格按 Duck v1 字段顺序构造规范查询字符串，不提交 Mario 专用字段或额外 `modeId`。
- 将显示名按 UTF-8 和 RFC 3986 编码，构造稳定的 64 位小写十六进制 nonce，并使用 HMAC-SHA256 签名。
- 复用原 Prefab 的 URL 生成、粘贴、提交和状态控件，解析成功、重复提交、排名和下载错误状态。
- 优先使用 Inspector 显式引用；跨 Prefab 成绩引用为空时，依次从 `PigeonHunter` 和 `RetroTV DuckHunt` 根对象中查找成绩组件作为启动回退。
- 每个 `PigeonRunRecordController` 会在初始化、准备新局和正式结算时向 Bridge 注册自己；场景中同时存在多台游戏时，最近开始或结算的游戏实例成为当前 `live` 上传源。

Prefab 内已经配置 Duck endpoint、三种榜单标识、模式控制器、`UdonHashLib` 和独立 Duck 签名值，并清除了旧 Mario endpoint、榜单标识和签名值。`signingKey` 为空时 Bridge 仍会显示 `SIGNATURE NOT CONFIGURED`，不会生成无效链接。

### 8.6 Unity Reader / View 当前实现

`PigeonGlobalLeaderboardReader` 为三个模式分别保存 Weekly 与 AllTime 的固定 `VRCUrl`，共六个数据集。每次切换外部范围或模式时：

- 有缓存时先显示缓存并在后台刷新，没有缓存时显示加载状态。
- 同一时间只发起一个 `VRCStringDownloader` 请求；请求期间继续切换时，当前请求完成后自动加载最后选择的数据集。
- 严格校验 `schemaVersion=1`、`leaderboardId`、`period` 和 `entries`。
- 解析服务端 `rank`、`runnerId`、`displayName`、`score`、`reachedRound`、`totalHits` 和 `submittedDate`。
- 下载失败或响应无效时优先保留已缓存榜单；没有缓存时显示错误状态。

`PigeonGlobalLeaderboardView` 复用原有 10 个外部榜槽位和共享分页按钮。每行显示：

```text
名次 | 玩家名 / HITS | 分数 / ROUND / 日期
```

名次直接使用服务端返回的竞赛排名，因此同分同回合可以显示 `1, 1, 3`。View 每页显示 10 条，最多展示 API 返回的 50 条；没有独立状态文本时，空榜和无缓存错误使用第一行显示，加载、刷新结果与缓存刷新失败状态附加在分页文本后。

### 8.7 RetroTV DuckHunt 接入

`RetroTV DuckHunt` 不内嵌另一份排行榜 UI，而是复用场景中的 `Leaderboard GameObject PigeonHunt`。其 `GameManager` 与标准 Pigeon Hunter 一样挂载：

```text
PigeonRunRecordController
PigeonLeaderboardPersistence
对应的两个 UdonBehaviour backing
```

RetroTV 的模式确认、正式开始、命中记录和 Game Over 继续走原有 `GameManager` 接入点，因此三种模式生成的字段和资格规则与标准机台一致。两台游戏共享相同的 PlayerData 键和 Duck API 榜单，不创建 RetroTV 专属榜单；同一玩家在任一机台获得更好的成绩都会更新对应模式的个人最佳。

场景中必须存在且启用一个名为 `Leaderboard GameObject PigeonHunt` 的排行榜对象，才能生成和提交上传 URL。排行榜对象缺失时，RetroTV 仍会正常记录并保存本地个人最佳，但不会提供外部上传入口。

当前 `VRCHuntGame` 场景中的 `RetroTV DuckHunt` 是已解包对象，Prefab 修改不会自动传播，因此该场景副本也单独同步了上述四个组件和 `GameManager.runRecordController` 引用。后续若重新从 Prefab 替换场景对象，不需要再次手动补组件。

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
8. 按 Duck API v1 契约实现外部提交协议、服务端与 Unity Bridge（已完成）
9. 实现每周榜和全部榜 Reader / View（已完成）
10. 启用 Weekly / AllTime 并完成三模式与三范围的排行榜 UI（已完成）
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

## 12. 当前实例本地榜验收标准

- 当前实例中的玩家各自拥有一个通用排行榜槽位。
- Mode A、Mode B、Mode C 读取互不混用的 PlayerData 键。
- 新玩家数据恢复前显示 `LOADING`，恢复后无成绩显示 `NO RECORD`。
- 分数降序、同分时回合降序，两项相同时并列。
- 玩家加入、离开或 PlayerData 更新后榜单会刷新。
- 三个模式按钮和分页按钮均调用 Pigeon 控制器，不再调用 Mario 控制器。
- Local、Weekly、AllTime 与三个模式按钮保持独立状态。
- Pigeon Prefab 不再使用 Mario 外部读取、视图或提交脚本。
- 不改变原游戏的成绩结算、网络同步或标题 Top Score 行为。
- C# 与 UdonSharp Program Asset 编译无错误。

## 13. 外部提交 Bridge 验收标准

- Mode A、Mode B、Mode C 分别生成 `duck_single_v1`、`duck_pair_v1`、`duck_range_v1` URL。
- 只有当前选择模式的正式有效结算可以作为 `live`；其他情况只能回退到该模式的 `saved_best`。
- URL 参数名称、顺序、UTF-8 百分号编码和 HMAC 输入与 `DUCK_LEADERBOARD_API_V1.md` 完全一致。
- 不包含 `durationMs`、`completed`、`world`、`area` 或 `modeId`。
- 同一成绩重复生成 URL 时 nonce 稳定；新成绩或更好的本地最佳会产生不同 nonce。
- 提交成功、重复提交、排名、服务端拒绝和下载失败均有明确状态。
- Duck 与 Mario 使用不同 endpoint、榜单标识和签名配置，Pigeon Prefab 不再访问 Mario 提交接口。
- Bridge 或网络不可用不会影响原游戏结算和本地 PlayerData。
- Unity C# 与 UdonSharp 编译无错误，生产 `signingKey` 已在发布前配置。

## 14. 外部 Reader / View 验收标准

- 三种模式与 Weekly / AllTime 正确映射到六个 Duck 查询 URL。
- Reader 拒绝错误 schema、榜单 ID、period、缺失字段或非法成绩值。
- 快速切换模式和范围时只显示最后选择的数据集，不会把旧请求结果画到新范围。
- 服务端返回的并列名次保持不变，不由客户端按数组下标重新编号。
- 每行显示玩家名、分数、回合、总命中数和提交日期，不显示 Mario 时间、世界或区域字段。
- 空榜、加载、无缓存错误和有缓存刷新失败均有可识别状态。
- Local 与外部榜共用分页按钮时，按钮事件按当前范围路由到正确 View。
- Weekly 与 AllTime 按钮可用，切换范围不会改变当前游戏模式。
- 六个生产查询接口返回 `schemaVersion=1`、正确榜单 ID、正确 period 和数组类型 entries。
- 六个生产榜已写入 `Bot Bill`、`Bot Puck` 等 Bot 测试数据：每种模式 12 条本周 live 和 1 条仅 AllTime 的 saved_best；API 非空响应、周期过滤、并列名次和第二页数据已验证。
- Unity 非空行显示和分页按钮仍需在 ClientSim 或 VRChat 客户端现场验证。
