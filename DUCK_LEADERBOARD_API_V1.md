# Duck Leaderboard API v1

## 1. 文档定位

本文档是 Pigeon Hunter Unity 客户端与外部排行榜服务之间的固定协议。

`Pigeon` 继续作为游戏和 Unity 脚本命名；`Duck` 专门用于外部 API、服务端模块、数据库表和外部榜单标识。两者的边界如下：

```text
Unity / 游戏：PigeonRunRecordController、PigeonGlobalLeaderboardBridge
外部 API：/api/v1/duck/*
外部榜单：duck_single_v1、duck_pair_v1、duck_range_v1
服务端模块：duck_protocol、duck_ranking
数据库表：duck_run_records、duck_leaderboard_bests
```

协议版本为 `1`。字段名称、字段顺序、编码方式、签名输入或排名语义发生不兼容变化时，必须发布新的协议版本，不能静默修改 v1。

当前状态：Duck API v1 已部署到 `https://api.nhui.top`。三个榜单的查询路由、独立签名配置、数据库迁移、契约测试和数据库集成测试均已完成。Unity 端 `PigeonGlobalLeaderboardBridge`、`PigeonGlobalLeaderboardReader` 和 `PigeonGlobalLeaderboardView` 已接入排行榜 Prefab，生产 Duck 签名值已与服务端配置一致，Weekly 与 AllTime 已启用。六个查询组合已经过线上空榜和非空响应验证。2026-07-27 已为每种模式写入 12 条 `Bot` live 记录和 1 条 `Bot` saved_best 记录，Weekly/AllTime 过滤、并列名次及超过单页容量的数据查询已验证；Unity 现场显示和分页按钮仍需单独验证。

## 2. 传输约束

- 提交使用玩家辅助的 HTTPS GET 流程，并通过 `VRCStringDownloader` 获取结果。
- 查询使用 HTTPS GET，返回 UTF-8 JSON。
- 生产环境必须校验 HMAC-SHA256 签名。
- HTTP 请求不能依赖重定向到 HTTPS。
- 上传和查询失败不能影响游戏结算、本地 PlayerData 或返回菜单。
- Unity 客户端包含签名材料，因此 HMAC 只能用于防止普通参数篡改，不能作为权威反作弊证明。

## 3. 榜单标识

三种游戏模式使用完全独立的榜单：

| Unity 模式 | 外部榜单标识 |
| --- | --- |
| Mode A / 单鸽 | `duck_single_v1` |
| Mode B / 双鸽 | `duck_pair_v1` |
| Mode C / 射击场 | `duck_range_v1` |

提交参数不再额外包含 `modeId`。服务端以 `leaderboardId` 识别模式，避免两个字段互相矛盾。

## 4. Endpoints

```text
GET /api/v1/health
GET /api/v1/duck/runs/submit?...&sig={signature}
GET /api/v1/duck/leaderboards/{leaderboardId}?period={all|week}&limit={count}
```

共享的 `/api/v1/health` 继续检查进程与数据库状态。Mario 的 `/api/v1/runs/submit` 和 `/api/v1/leaderboards/*` 保持不变。

## 5. 提交字段

规范查询字符串必须严格使用以下顺序：

| 顺序 | 字段 | 格式 | v1 校验 |
| ---: | --- | --- | --- |
| 1 | `schemaVersion` | 十进制整数 | 必须等于 `1` |
| 2 | `leaderboardId` | ASCII 标识 | 必须是已启用的 Duck 榜单 |
| 3 | `rulesetVersion` | 十进制整数 | `1` 至 `2147483647`，并由服务端启用 |
| 4 | `buildVersion` | 十进制整数 | `1` 至 `2147483647`，并由服务端启用 |
| 5 | `runnerId` | 小写十六进制 | 固定 32 个字符 |
| 6 | `displayName` | UTF-8 字符串 | 编码后 1 至 128 字节，不允许控制字符 |
| 7 | `submissionSource` | ASCII 枚举 | `live` 或 `saved_best` |
| 8 | `score` | 十进制整数 | `0` 至 `2147483647` |
| 9 | `reachedRound` | 十进制整数 | `1` 至 `2147483647` |
| 10 | `totalHits` | 十进制整数 | `0` 至 `2147483647` |
| 11 | `runNonce` | 小写十六进制 | 固定 64 个字符 |
| 12 | `eligibilityFlags` | 十进制整数 | 公开榜必须等于 `0` |

最终的 `sig` 参数不属于规范字段列表。

服务端必须拒绝缺失、重复、未知或空参数。十进制整数不能带符号或前导零，数值零只能写成 `0`。

以下内容不由客户端提交：

```text
submittedAt
submittedDate
rank
period
invalidReason
```

`durationMs`、`completed`、`world` 和 `area` 属于 Mario 协议，Duck API 不接受这些字段。

## 6. 字符串编码

`displayName` 按 UTF-8 字节执行 RFC 3986 百分号编码：

- `A-Z`、`a-z`、`0-9`、`-`、`.`、`_`、`~` 保持不变。
- 其他字节编码为大写 `%HH`。
- 空格必须编码为 `%20`，不能使用 `+`。
- 不对 VRChat 提供的显示名进行 Unicode 归一化。
- 服务端严格解码 UTF-8，并重新生成规范查询字符串后再验证签名。

示例：

```text
Player Name -> Player%20Name
A&B=100%   -> A%26B%3D100%25
玩家一号    -> %E7%8E%A9%E5%AE%B6%E4%B8%80%E5%8F%B7
```

## 7. 规范查询与签名

规范查询字符串为：

```text
schemaVersion={schemaVersion}&leaderboardId={leaderboardId}&rulesetVersion={rulesetVersion}&buildVersion={buildVersion}&runnerId={runnerId}&displayName={encodedDisplayName}&submissionSource={submissionSource}&score={score}&reachedRound={reachedRound}&totalHits={totalHits}&runNonce={runNonce}&eligibilityFlags={eligibilityFlags}
```

签名消息必须逐字节等于：

```text
GET\n/api/v1/duck/runs/submit\n{canonicalQuery}
```

末尾没有换行。签名算法为：

```text
signatureBytes = HMAC-SHA256(UTF8(sharedKey), UTF8(message))
sig = signatureBytes 的小写十六进制
```

生产环境使用独立的 Duck 签名密钥，不与 Mario 共用。服务端部署变量建议命名为 `DUCK_SUBMISSION_HMAC_KEY`。

## 8. Nonce 与重复提交

数据库唯一键为：

```text
(leaderboard_id, run_nonce)
```

要求：

- 相同生成 URL 的重试必须复用同一个 `runNonce`。
- 重复有效提交返回 HTTP 200 和 `duplicate=true`，不能插入第二行。
- `live` nonce 由 runner、榜单、规则/构建版本、当前 `runId` 和完整成绩派生。
- `saved_best` nonce 由 runner、榜单、规则/构建版本、最佳记录日期和完整成绩派生。
- Unity 中的原始 `runId` 不要求为 64 位；Bridge 将 nonce 载荷稳定派生为 64 位小写十六进制。
- 个人最佳变好后必须产生不同的 nonce。

## 9. 提交来源

`live` 表示当前 `PigeonRunRecordController` 仍持有的正式结算成绩。

`saved_best` 表示从 PlayerData 恢复的个人最佳，用于玩家首次接入外部榜或现场成绩未成功上传后的 AllTime 补交。

规则：

- AllTime 接受 `live` 与 `saved_best`。
- Weekly 只统计 `live`。
- `saved_best` 使用服务器接收时间保存，但不能因此进入 Weekly。
- 资格失效的局不生成上传链接；公开提交仍要求 `eligibilityFlags=0`。

## 10. 排名规则

每个 period 先为每个 `runnerId` 选出一条最佳记录，再对玩家最佳记录排序：

```text
1. score 降序
2. score 相同时 reachedRound 降序
3. 两项相同时并列
```

`totalHits` 仅展示，不参与个人最佳替换或排名。

同分同回合的记录内部使用 `submitted_at` 升序、数据库记录 ID 升序保证返回顺序稳定，但这些字段不改变显示名次。名次使用竞赛排名：

```text
1, 1, 3, 4, 4, 6
```

个人最佳只有在新记录分数更高，或同分但回合更高时才替换。两项完全相同时不替换旧记录。

## 11. Weekly 与 AllTime

`period` 只接受：

```text
week
all
```

- `week` 表示 UTC 周一 `00:00:00`（包含）至下一个周一 `00:00:00`（不包含）。
- `week` 只查询 `submissionSource=live` 且 `eligibilityFlags=0` 的记录。
- `all` 查询所有合格的 `live` 与 `saved_best` 记录。
- UI 将 `period=all` 显示为 `AllTime`；API 不使用 `global` 作为 period 值。
- `limit` 默认 50，服务端限制为 1 至 100。

## 12. 提交响应

首次接受：

```json
{
  "ok": true,
  "accepted": true,
  "duplicate": false,
  "personalBest": true,
  "rank": 42
}
```

重复 nonce：

```json
{
  "ok": true,
  "accepted": false,
  "duplicate": true,
  "personalBest": true,
  "rank": 42
}
```

错误状态码：

| HTTP | 含义 |
| ---: | --- |
| 400 | 参数缺失、重复、未知、格式错误或超出范围 |
| 401 | HMAC 签名无效 |
| 404 | 路由或榜单未配置 |
| 405 | HTTP 方法不支持 |
| 429 | 提交频率超限 |
| 500 | 服务端未预期异常 |
| 503 | 数据库或签名服务未配置 |

所有提交响应使用 UTF-8 JSON 和 `Cache-Control: no-store`。

## 13. 榜单查询响应

示例请求：

```text
GET /api/v1/duck/leaderboards/duck_single_v1?period=all&limit=50
GET /api/v1/duck/leaderboards/duck_single_v1?period=week&limit=50
```

示例响应：

```json
{
  "schemaVersion": 1,
  "leaderboardId": "duck_single_v1",
  "period": "all",
  "generatedAt": "2026-07-27T12:00:00Z",
  "entries": [
    {
      "rank": 1,
      "runnerId": "0123456789abcdef0123456789abcdef",
      "displayName": "Player Name",
      "score": 12345,
      "reachedRound": 7,
      "totalHits": 42,
      "submittedDate": "2026-07-27"
    }
  ]
}
```

Reader 必须拒绝不支持的 `schemaVersion`，不能尝试部分解析。查询响应使用 UTF-8 JSON，并可使用短时间公共缓存，例如 `Cache-Control: public, max-age=15`。

## 14. v1 测试向量

以下密钥只用于自动化测试：

```text
duck-contract-test-key-v1
```

规范查询：

```text
schemaVersion=1&leaderboardId=duck_single_v1&rulesetVersion=1&buildVersion=1&runnerId=0123456789abcdef0123456789abcdef&displayName=Player%20Name&submissionSource=live&score=12345&reachedRound=7&totalHits=42&runNonce=1111111111111111111111111111111111111111111111111111111111111111&eligibilityFlags=0
```

预期签名：

```text
284ad1861c9a453172605e81592ae64c186a855f29629f9128db82440393dbc9
```

Unity 与服务端实现必须逐字节复现该签名。

## 15. 服务端隔离要求

Duck API 不修改 Mario v1 的字段、模型、路由或排名规则。建议新增：

```text
app/duck_api.py
app/duck_protocol.py
app/duck_ranking.py
tests/test_duck_contract.py
alembic/versions/0002_duck_leaderboards.py
```

数据库新增：

```text
duck_run_records
duck_leaderboard_bests
```

通用的数据库连接、限流器和 `leaderboard_configs` 可以复用。不能用虚假的 Mario `world`、`area` 或 `duration_ms` 保存 Duck 成绩。

## 16. 完成标准

- 固定字段顺序和测试向量在 Python 与 Udon 中一致。
- 缺失、重复、未知和非规范编码参数均被拒绝。
- 三个 Duck 榜单按规则/构建版本独立启用。
- 重复 nonce 返回成功但不产生第二条记录。
- AllTime 与 Weekly 都按分数、回合和竞赛排名返回正确结果。
- Weekly 不包含 `saved_best`。
- Mario 的接口契约测试和线上路径保持不变。
- 部署后的 HTTPS 接口可以通过 VRChat `VRCStringDownloader` 访问。
