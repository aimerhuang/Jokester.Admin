# 用户管理 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v0.2 |
| 日期 | 2026-11-23 |
| 需求状态 | 第 9 节（现有接口收紧与权限回收）已在当前工作树实现并通过单元测试，未提交、未部署，迁移未在任何数据库执行；其余为设计稿，第 13 节待决策项按建议值书写 |
| 适用端 | 用户 Web（`Jokester-Ai-Image`）、Jokester Admin API；Android / iOS 不接入 |
| 后端基线 | `sys_user`、`sys_user_point_detail`、`sys_user_point_bucket`、`sys_user_membership_entitlement`、`UsersController`、`IUserService`、`IPointService` |
| 关联契约 | [integration-guide.md](./integration-guide.md)、[point-recharge.md](./point-recharge.md)、[security-hardening-prd.md](./security-hardening-prd.md)、[ai-model-config-status-prd.md](./ai-model-config-status-prd.md) |

## 2. 推荐结论

1. **新建超级管理员专用接口 `/api/admin/users`，不复用 `GET /api/users`。** 现有 `/api/users` 按菜单权限码鉴权，而按初始化脚本，注册用户默认角色持有全部 `System.User.*` 权限码（见第 3 节）。新接口按超级管理员身份判断，做法与 `/api/ai/model-routes`、`POST /api/points/recharge/admin/codes` 一致。
2. **上线本功能前必须先收紧现有 `/api/users/*`（第 9 节，P0）。** 否则“只有超级管理员能管理用户”在后端不成立：普通登录用户今天就能读他人积分流水、改他人用户名。
3. **禁用是可逆的账号冻结，不动积分。** 禁用后不能登录、不能刷新令牌、不能新建生图任务；积分余额、流水、会员权益原样保留，启用后恢复。禁用必须填写原因并留审计。
4. **积分使用情况按 `source` 聚合，不按 `change_type`。** `change_type=refund` 同时包含生图失败返还（正数，`image_refund`）和 Apple 退款追扣（负数，`apple_refund`），按类型求和会把两者抵消。
5. **详情页的余额与用户本人看到的一致。** 查询走 `IPointService`，先执行与用户本人查余额相同的过期结算，再返回余额和批次汇总；不在 `UserService` 里另写一套积分计算。
6. **列表不含已注销用户。** 账户删除会把用户名、邮箱匿名化并软删除，展示没有意义。

## 3. 背景与现状

运营目前只能靠查库了解用户情况：某个用户反馈“积分不对”时，需要有数据库权限的人手工查 `sys_user_point_detail`；发现滥用账号时也只能改库禁用。

后端已有的能力和问题：

| 现有接口 | 鉴权 | 现状 |
| --- | --- | --- |
| `GET /api/users` | `System.User.View` | 只支持分页，无搜索、筛选、排序；不返回注册时间、最近登录、会员状态 |
| `PUT /api/users/{id}/status` | `System.User.UpdateStatus` | 能改状态并撤销会话，但不校验取值、不防自禁、不留原因和审计 |
| `GET /api/users/{id}/point-details` | **仅登录** | 无权限码、无本人校验：任意登录用户可读任意用户的积分流水 |
| `PUT /api/users/{id}/nickname` | **仅登录** | 无权限码、无本人校验：任意登录用户可改任意用户的登录用户名与昵称，并撤销对方会话 |
| `PUT /api/users/{id}/password`、`/avatar`、`/signature` | `System.User.Update` | 无本人校验；前端个人资料页用它们修改本人信息，说明普通用户持有 `System.User.Update` |
| `PUT /api/users/{id}` | `System.User.Update` | 请求体含 `isSuperAdmin`、`status`、`roleIds` |

权限码为什么挡不住普通用户：`jokester.admin.sql` 给注册用户默认角色 `ai_operator` 授权时没有按站点过滤（`JOIN sys_menu m WHERE r.role_code = 'ai_operator' AND m.menu_code <> 'ai_prompt_sensitive_word_manage'`），等于授予了所有站点的全部菜单，包括 `System.User.View / Create / Update / UpdateStatus / Delete / Authorize`。对照同一段脚本里 `blog_admin` 是按 `m.site_id = @blog_site_id` 过滤的。

按该脚本初始化的库中，任一注册用户可以调用 `PUT /api/users/{他人id}/password` 接管账号，或 `PUT /api/users/{自己id}` 带 `isSuperAdmin=true` 提权。**生产库的 `sys_role_menu` 本文未核对**，用下面的查询确认；有返回行即受影响：

```sql
SELECT m.permission_code
FROM sys_role r
JOIN sys_role_menu rm ON rm.role_id = r.id
JOIN sys_menu m ON m.id = rm.menu_id
WHERE r.role_code = 'ai_operator' AND m.permission_code LIKE 'System.%';
```

## 4. 目标与非目标

### 4.1 目标

1. 超级管理员在 Web 后台看到全部用户，可搜索、筛选、排序、分页。
2. 超级管理员可以禁用、启用账号，操作有原因、有审计、立即撤销对方会话。
3. 双击列表行进入用户详情页，看到该用户的基础信息、积分余额构成和积分使用情况（汇总 + 流水）。
4. 普通用户无法通过任何接口读到他人的资料或积分数据。

### 4.2 非目标（首期）

- 手工增减用户积分、补发 / 撤销会员（涉及积分批次与流水的事务一致性，单独立项）。
- 在本页新建用户、重置密码、修改角色 / 站点 / 超级管理员标记、删除用户。
- 查看用户的生图内容、提示词、画布、私有媒体。
- 登录历史列表（`sys_login_log` 没有 `user_id` 索引）。
- 导出、批量禁用。
- 把用户管理授权给非超级管理员角色。

## 5. 用户与权限

| 角色 | 用户管理 |
| --- | --- |
| 未登录 | 401 |
| 普通登录用户 | 导航不出现入口；接口返回 403 |
| 超级管理员 | 可见，可读，可禁用 / 启用 |

- 不新增菜单权限码，按超级管理员身份判断。
- 读接口校验 Access Token 中的超级管理员声明；写接口必须在事务内重新查询并锁定当前用户，确认其仍为启用、未删除的超级管理员（已签发的 Access Token 在降权后最长 15 分钟内仍有效）。
- 前端用 `auth.user.isSuperAdmin` 决定入口是否显示，路由加 `requiresSuperAdmin`；前端隐藏只是体验，鉴权以后端为准。

## 6. 功能需求：用户列表（`/admin/users`）

### 6.1 入口

侧边导航在“模型配置”下方增加“用户管理”，`superAdminOnly`。

### 6.2 列表

| 列 | 内容 |
| --- | --- |
| 用户 | 头像 + 昵称，第二行用户名 |
| ID | 用户 ID |
| 邮箱 | 完整显示，可复制；为空显示“—” |
| 积分余额 | `sys_user.point_balance`，右对齐 |
| 会员 | 有效月卡显示“VIP”与到期日；无则留空 |
| 标记 | 超级管理员显示“超管”徽标 |
| 状态 | 启用（绿）/ 禁用（灰） |
| 注册时间 | `created_at` |
| 最近登录 | `last_login_time`；从未登录显示“—” |
| 操作 | “详情”、“禁用” / “启用” |

- 默认按注册时间倒序，每页 20 条，可选 20 / 50 / 100。
- 搜索：一个关键字输入框，匹配用户名、昵称、邮箱（包含匹配）；关键字为纯数字时同时精确匹配用户 ID。输入停止 300ms 后请求。
- 筛选：状态（全部 / 启用 / 禁用）、类型（全部 / 普通用户 / 超级管理员）。
- 排序：注册时间、最近登录、积分余额，可升可降。
- 页头显示符合当前筛选条件的用户总数。
- 列表里的积分余额是库中存量值，签到积分过期但用户次日尚未访问时可能比实际多（最多一天的签到积分）；详情页显示结算后的准确值。
- 搜索、筛选、页码写入 URL 查询参数，从详情页返回时恢复原位置。

### 6.3 进入详情

- 双击行进入 `/admin/users/{id}`。
- 双击在触屏设备上不可用，且不易被发现，所以同时提供：行内“详情”按钮；行获得焦点时按 Enter。
- 双击发生在操作列按钮、邮箱复制按钮上时不触发跳转。

### 6.4 禁用 / 启用

**禁用**：点击后弹确认框，显示用户名和邮箱，要求填写原因（必填，1–200 字符），文案说明后果：“该用户将立即被登出且无法登录；已签发的访问令牌最长 15 分钟内失效；积分和会员权益保留。”

**启用**：弹确认框，原因选填。

后端规则：

| 规则 | 处理 |
| --- | --- |
| 不能禁用自己 | 409 `USER_STATUS_PROTECTED` |
| 不能禁用其他超级管理员 | 409 `USER_STATUS_PROTECTED`；超级管理员账号通过 `--seed-admin` 或数据库维护，避免误操作或被盗号后互相锁死 |
| 目标用户已注销 / 不存在 | 404 |
| 目标用户有进行中的账户删除请求时启用 | 409 `USER_DELETION_IN_PROGRESS`；账户删除流程会把用户置为禁用，后台不得把它改回来（具体状态集合实现时对照 `AccountDeletionService` 状态机） |
| 目标状态与当前状态相同 | 返回成功，不写审计、不撤销会话 |
| `status` 不是 `0` / `1` | 400 |

禁用生效范围（均为现有行为，本需求不改）：

- 登录、刷新令牌被拒绝（登录提示文案见第 13 节第 4 项）；状态变更后立即撤销该用户全部 Refresh Token 会话并清理权限缓存。
- 已签发 Access Token 最长 15 分钟后失效；这期间积分查询、签到、生图创建等路径各自校验 `status=1`，会被拒绝。
- `size-mode-v1` 排队中的任务在 Worker 预检时以 `ACCOUNT_UNAVAILABLE` 失败并退款。legacy 排队任务与已在 Provider 侧执行的任务的处理，实现时需要核对并写进验收用例（见 14.1 第 8 条）。
- 不冻结、不清零积分；不撤销会员权益；会员有效期在禁用期间照常流逝。

### 6.5 状态变更记录

每次禁用 / 启用写一条审计：目标用户、动作、原因、操作人、IP、时间。在详情页展示（见 7.4）。

## 7. 功能需求：用户详情（`/admin/users/{id}`）

页面自上而下四块。顶部有“返回列表”。

### 7.1 基础信息

头像、昵称、用户名、用户 ID、邮箱、手机号、状态徽标、超管徽标、会员状态与到期时间、注册时间、最近登录时间与 IP、备注。右上角“禁用 / 启用”按钮，规则同 6.4。

不展示：密码哈希、盐、角色与站点明细。

### 7.2 积分概览

一行卡片，数据为结算后的实时值：

| 卡片 | 取值 |
| --- | --- |
| 可用积分 | 结算后的 `point_balance` |
| 永久积分 | 批次汇总 `PermanentPoints` |
| 限时积分 | 批次汇总 `ExpiringPoints`（含当日签到积分、月卡积分） |
| 最近到期 | `NextExpiringPoints` 与 `NextExpireAt`；没有则显示“—” |

与用户本人在积分页看到的 `GET /api/points/balance` 字段一一对应，客服核对时口径一致。

### 7.3 积分使用情况

**时间范围**：近 7 天 / 近 30 天（默认）/ 全部 / 自定义起止日期。汇总和流水共用同一个范围。

**汇总卡片**：

| 指标 | 算法（范围内 `sys_user_point_detail`） |
| --- | --- |
| 获得 | `source ∈ {register, sign_in, recharge, apple_iap}` 的 `change_points` 之和 |
| 生图消耗 | `−(Σ image_generate + Σ image_refund)`，即预留扣减减去失败返还后的净消耗 |
| 过期 | `−Σ(sign_in_expire, recharge_expire, apple_iap_expire, point_expire)` |
| 退款追扣 | `−Σ apple_refund` |
| 生图次数 | `source=image_generate` 的流水条数（一条对应一张图的任务） |

**按来源明细表**：每个 `source` 一行，列为来源名称、笔数、积分合计（带正负号），按合计绝对值倒序。不在已知清单内的 `source` 原样显示编码，归入“其他”，不丢弃。来源中文名复用积分页已有的文案键（`credits.source*`）。

**积分流水表**：

| 列 | 内容 |
| --- | --- |
| 时间 | `created_at` |
| 来源 | `source` 对应的中文名 |
| 变动 | `change_points`，正数绿色带 `+`，负数红色 |
| 变动后余额 | `balance_after` |
| 备注 | `remark`（含任务 ID、模型、分辨率等，由后端写入） |

- 按时间倒序，同一时间按 ID 倒序，每页 20 条。
- 筛选：来源（多选）、方向（全部 / 增加 / 减少）。
- 不返回 `business_key`（内部幂等键）。

### 7.4 状态变更记录

按时间倒序列出该用户的禁用 / 启用记录：时间、动作、原因、操作人。没有记录时显示“暂无记录”。

### 7.5 异常与空状态

- 用户不存在或已注销：整页显示“用户不存在”，提供返回列表的链接。
- 某一块加载失败只影响该块，显示重试按钮，不清空其他块。
- 范围内没有流水：汇总显示 0，表格显示“该时间范围内没有积分变动”。

## 8. 后端设计

### 8.1 接口

前缀 `/api/admin/users`，全部要求超级管理员。

| 方法与路径 | 说明 |
| --- | --- |
| `GET /` | 用户列表 |
| `GET /{id}` | 用户详情（基础信息 + 积分概览） |
| `GET /{id}/point-summary` | 积分使用汇总 |
| `GET /{id}/point-details` | 积分流水 |
| `PUT /{id}/status` | 禁用 / 启用 |
| `GET /{id}/status-audits` | 状态变更记录 |

**列表** `GET /api/admin/users`

查询参数：`pageIndex`、`pageSize`（1–100）、`keyword`（≤100 字符）、`status`（`0`/`1`）、`isSuperAdmin`（`true`/`false`）、`sortBy`（`createdAt` 默认 / `lastLoginTime` / `pointBalance`）、`sortOrder`（`desc` 默认 / `asc`）。排序字段相同时按 `id` 倒序，保证翻页稳定。

```json
{
  "total": 1284,
  "pageIndex": 1,
  "pageSize": 20,
  "items": [
    {
      "id": 1024,
      "userName": "alice",
      "nickName": "Alice",
      "email": "alice@example.com",
      "avatarUrl": "/avatar/202611/xxxx.png",
      "pointBalance": 1250,
      "status": 1,
      "isSuperAdmin": false,
      "membership": { "tierCode": "monthly_vip", "expiresAt": "2026-12-10T08:00:00Z" },
      "createdAt": "2026-08-01T03:12:00Z",
      "lastLoginTime": "2026-11-22T14:05:31Z"
    }
  ]
}
```

- 只查 `is_deleted=0`。
- `membership` 为当前有效权益中到期最晚的一条，没有则为 `null`；按当前页的用户 ID 批量查询，不逐行查。
- 列表不返回 `lastLoginIp`、手机号、备注，这些只在详情返回。

**详情** `GET /api/admin/users/{id}`

```json
{
  "id": 1024,
  "userName": "alice",
  "nickName": "Alice",
  "email": "alice@example.com",
  "phone": null,
  "avatarUrl": "/avatar/202611/xxxx.png",
  "signature": null,
  "remark": null,
  "status": 1,
  "isSuperAdmin": false,
  "membership": { "tierCode": "monthly_vip", "expiresAt": "2026-12-10T08:00:00Z" },
  "createdAt": "2026-08-01T03:12:00Z",
  "lastLoginTime": "2026-11-22T14:05:31Z",
  "lastLoginIp": "203.0.113.7",
  "points": {
    "availablePoints": 1250,
    "permanentPoints": 800,
    "expiringPoints": 450,
    "nextExpiringPoints": 25,
    "nextExpireAt": "2026-11-23T16:00:00Z"
  }
}
```

**积分汇总** `GET /api/admin/users/{id}/point-summary?from=&to=`

`from` / `to` 为带时区的 ISO 8601，均可省略（省略表示不限）；`from` 含、`to` 不含。

```json
{
  "from": "2026-10-24T16:00:00Z",
  "to": null,
  "earnedPoints": 5275,
  "imageSpentPoints": 4020,
  "expiredPoints": 125,
  "clawedBackPoints": 0,
  "imageTaskCount": 201,
  "bySource": [
    { "source": "recharge", "count": 1, "totalPoints": 5000 },
    { "source": "image_generate", "count": 201, "totalPoints": -4200 },
    { "source": "image_refund", "count": 9, "totalPoints": 180 },
    { "source": "sign_in", "count": 11, "totalPoints": 275 },
    { "source": "sign_in_expire", "count": 5, "totalPoints": -125 }
  ]
}
```

四个合计由后端按 7.3 的口径算好返回，前端不重复实现归类规则。

**积分流水** `GET /api/admin/users/{id}/point-details`

查询参数：`pageIndex`、`pageSize`、`from`、`to`、`source`（可重复）、`direction`（`in` / `out`）。响应为 `PagedResult`，项字段：`id`、`changePoints`、`balanceAfter`、`changeType`、`source`、`remark`、`createdAt`。

**状态** `PUT /api/admin/users/{id}/status`

```json
{ "status": 0, "reason": "批量注册刷签到积分" }
```

成功返回更新后的 `{ id, status }`。

**状态记录** `GET /api/admin/users/{id}/status-audits`：分页，项字段 `id`、`action`（`disable` / `enable`）、`reason`、`operatorUserId`、`operatorUserName`、`createdAt`。

### 8.2 数据表变更

新增审计表 `sys_user_status_audit`（时间存 UTC，与其他新表一致）：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `id` | `BIGINT` 主键自增 | |
| `user_id` | `BIGINT NOT NULL` | 目标用户，外键 `sys_user.id` |
| `action` | `VARCHAR(16) NOT NULL` | `disable` / `enable` |
| `reason` | `VARCHAR(200) NULL` | 禁用必填 |
| `operator_user_id` | `BIGINT NOT NULL` | |
| `operator_ip` | `VARCHAR(50) NULL` | |
| `created_at` | `DATETIME NOT NULL` | UTC |

索引 `idx_sys_user_status_audit_user (user_id, id)`。

`sys_user` 增加索引 `idx_sys_user_created_at (created_at)`，支撑默认排序。`last_login_time`、`point_balance` 排序和关键字包含匹配首期走全表扫描，用户量到 10 万级再评估索引或前缀匹配。

现有表不加列。`sys_user_point_detail` 已有 `(user_id, created_at)` 索引，够用。

迁移 `docs/migrations/20261123-add-user-admin.sql`，同步 `jokester.admin.sql`；API 读取新表，先迁移再部署。9.3 的权限回收是另一个独立迁移，发布顺序相反（先部署 API）。

账户删除流程需要同步清理 `sys_user_status_audit` 中该用户的 `reason`（原因是自由文本，可能含个人信息）。

### 8.3 服务与代码落点

- 新增 `AdminUsersController`（`/api/admin/users`）和 `IUserAdminService`；Controller 只做参数绑定。
- 超级管理员校验、事务、审计都在 Service 内，写操作用 `TranLock(DbLockType.Wait)` 锁定并复核操作人，与 `AiImageModelRouteAdminService` 写法一致。
- 状态变更在同一事务内：复核操作人 → 锁定目标用户 → 校验 6.4 的规则 → 更新 `status` → 写审计；审计失败整体回滚。提交后调用 `IPermissionCacheInvalidator.RemoveUserAsync` 和 `IRefreshTokenStore.RevokeUserSessionsAsync`。
- 积分概览：`IPointService` 新增按用户 ID 查询余额概览的方法，内部复用 `GetBalanceAsync` 的结算步骤（锁用户行 → `ExpireAsync` → `ExpireLegacyPreviousSignInPointsAsync` → `GetSummaryAsync`），但不要求目标用户 `status=1`，这样禁用用户也能查看。结算只写本来就该写的过期流水，是幂等的记账。
- 积分汇总与流水是纯读查询，时间参数用 `ApiDateTime.ToLocalStorage` 转成库内本地时间后过滤，返回时用 `ApiDateTime.FromLocalStorage` 转 UTC。
- 汇总的来源归类集中定义在一处（来源常量目前分散在 `PointService`、`PointRechargeService`、`PointBucketLedger`、`AppleIapService`），新增来源时只改这一处。
- 数据访问使用 `SqlSugar + Mapster`。
- `SecurityRateLimitMiddleware` 增加规则：`/api/admin/users` 写操作按用户和 IP 各每分钟 20 次。读接口沿用现有 `api-read-ip-1m`。

### 8.4 时间

所有响应时间为带 `Z` 的 ISO 8601。`sys_user`、`sys_user_point_detail`、`sys_user_membership_entitlement` 是按本地时间写库的历史表，在 DTO 边界用 `ApiDateTime.FromLocalStorage` 转换；`sys_user_status_audit` 直接存 UTC。

## 9. 现有 `/api/users` 接口收紧（P0，先于本功能上线）

实现状态：9.1 与 9.3 已实现（`UserService`、`UsersController`、`docs/migrations/20261123-restrict-ai-operator-menus.sql`、`jokester.admin.sql`，测试 `UserServiceAuthorizationTests`）；9.2 随用户管理功能一起做。

### 9.1 规则

| 接口 | 新规则 |
| --- | --- |
| `GET /api/users/{id}/point-details` | 本人或超级管理员；否则 404 |
| `PUT /api/users/{id}/nickname` | 本人或超级管理员；否则 404 |
| `PUT /api/users/{id}/password`、`POST /{id}/avatar`、`PUT /{id}/signature` | 本人或超级管理员；否则 404 |
| `GET /api/users`、`POST /api/users`、`PUT /api/users/{id}`、`PUT /{id}/status`、`DELETE /{id}`、`GET /{id}/menus/tree`、`PUT /{id}/menus` | 仅超级管理员；否则 403 |

- 校验放在 `UserService` 内（服务层再次按当前主体和所有者校验，对应 `security-hardening-prd.md` P0-04），不只靠 Controller 上的权限码。
- 超级管理员除 Access Token 声明外还要复核数据库中仍是启用、未删除的超级管理员（非事务内锁定；事务内复核留给 8.3 的新写接口）。
- 管理类接口保留原有 `[Permission]` 特性，服务层再叠加超级管理员校验。影响：通过角色持有 `System.User.*` 但没有超级管理员标记的账号不再能管理用户。
- 跨用户访问统一返回 404，不暴露用户 ID 是否存在。
- Web、iOS、Android 客户端都只用本人 ID 调用 `point-details`、`nickname`、`password`、`avatar`，收紧后不受影响。
- `PUT /api/users/{id}/nickname` 当前同时修改登录用户名 `userName`；本人修改保持现有行为，本需求不改。

### 9.2 `PUT /api/users/{id}/status`

保留给既有调用方，但内部改为调用 8.3 的同一个状态变更方法（原因记为空），避免出现一条绕过自禁保护和审计的旁路。

### 9.3 权限回收

迁移 `docs/migrations/20261123-restrict-ai-operator-menus.sql` 删除 `ai_operator` 与所有非 `ai_image` 站点菜单之间的 `sys_role_menu` 关联，`jokester.admin.sql` 的初始化授权改为按 AI 站点过滤。

回收范围比用户接口大：误授的不只是 `System.User.*`，还包括 `System.Role.*`、`System.Menu.*`、`System.Site.*`、`System.Log.*` 和全部 `Blog.*`（文章、媒体、评论管理）。这些接口本次没有加服务层校验，只能靠回收权限码关闭，所以迁移是必须执行的一步，不是可选项。

- 脚本自带两条预检查询：将被回收的权限码；绑定了非 AI 站点、可能有意依赖这些权限的 `ai_operator` 账号。后者要先分配专用角色。
- 发布顺序：先部署 API，再执行迁移。9.1 中本人可用的接口已去掉 `[Permission("System.User.Update")]`，新版 API 不依赖该权限码；反过来先回收权限，旧版 API 上用户会改不了自己的密码。
- 执行后清理权限缓存（Redis `<InstanceName>perm:*`），否则旧权限最长保留 2 小时。

未处理、需要另行决策：`ai_operator` 同时是“生图管理员”和所有注册用户的默认角色，它在 AI 站点内仍持有 `PromptLibrary.Sync.Run / Switch`（触发提示词库同步、切换快照）和 `AiImage.SensitiveWord.View / Test`（查看敏感词库）。这些是早期迁移显式授予的，是否应对普通注册用户开放，需要产品判断；合理的做法是为注册用户新建一个只含生图权限的默认角色。

## 10. 错误契约

| HTTP | 机器码 | 场景 |
| --- | --- | --- |
| 401 | `UNAUTHORIZED` | 未登录 |
| 403 | `RESOURCE_FORBIDDEN` | 非超级管理员；事务内复核发现已不是启用的超级管理员 |
| 404 | `RESOURCE_NOT_FOUND` | 用户不存在或已注销 |
| 400 | `VALIDATION_ERROR` | 参数不合法；禁用未填原因；`from` 晚于 `to` |
| 409 | `USER_STATUS_PROTECTED`（新增） | 禁用自己或其他超级管理员 |
| 409 | `USER_DELETION_IN_PROGRESS`（新增） | 启用正在注销的用户 |
| 429 | `RATE_LIMITED` | 限流 |

## 11. 安全与隐私

1. 邮箱、最近登录 IP、积分流水是个人信息，只有超级管理员接口返回；响应中不包含密码哈希、盐、令牌、`business_key`。
2. 流水备注含任务 ID，不含提示词或图片地址；本页不提供从任务 ID 跳转到生成内容的入口。
3. 操作日志不记录请求体（`OperationLogMiddleware` 现状），禁用原因只存审计表。
4. 列表接口可被用来批量拉取全部用户邮箱：`pageSize` 上限 100，不提供导出；超级管理员账号被盗的防护依赖登录侧的限流与二次验证，不在本需求范围。
5. 关键字搜索参数化查询，不拼接 SQL；`keyword` 中的 `%`、`_` 按字面量转义。

## 12. 分期

| 阶段 | 内容 |
| --- | --- |
| P0 | 第 9 节接口收紧与权限回收；用户列表（搜索、筛选、排序）；禁用 / 启用及审计；详情页基础信息、积分概览、使用汇总、流水、状态记录 |
| P1 | 按模型的生图消耗汇总（任务数、成功率、净消耗）；积分每日趋势图；充值与兑换记录；登录历史（需给 `sys_login_log` 加 `user_id` 索引）；流水导出 |
| P2 | 手工调整积分（独立来源 `admin_adjust`，走积分批次，必填原因并审计）；批量禁用；把用户管理授权给非超管角色 |

## 13. 待决策项

| # | 问题 | 建议 |
| --- | --- | --- |
| 1 | “所有用户”是否包含已注销用户？ | 不包含。注销后用户名和邮箱已匿名化 |
| 2 | 能否禁用其他超级管理员？ | 首期不能。超管只有少数几个，走 `--seed-admin` 或数据库维护更安全 |
| 3 | 禁用是否必须填原因？ | 必填。以后用户申诉时需要知道当初为什么禁用 |
| 4 | 被禁用的用户登录时看到什么？ | 现状是密码正确也返回“用户名或密码错误”（`AuthService` 对禁用账号抛 `InvalidCredentials`，`ACCOUNT_DISABLED` 已定义但未使用），用户会以为忘了密码。建议改为密码校验通过后返回 `ACCOUNT_DISABLED`“账号已被禁用，请联系客服”；密码错误时仍返回通用错误，不泄露账号状态。首期不发邮件通知 |
| 5 | 邮箱在列表中是否脱敏？ | 不脱敏。客服按邮箱定位用户是主要场景，且只对超管可见 |
| 6 | 管理员查看详情时是否执行过期结算（会写过期流水）？ | 执行。否则余额可能包含已过期的签到积分，与用户看到的不一致 |
| 7 | 详情页是否显示生图任务列表？ | 首期不显示，只看积分维度；按模型汇总放 P1 |
| 8 | 既有 `PUT /api/users/{id}/status` 等老接口是否直接下线？ | 首期保留并收紧为仅超管；确认没有其他后台前端在用后再下线 |

## 14. 验收标准

### 14.1 后端测试

1. 普通用户调用 `/api/admin/users` 下任一接口返回 403；被降权但 Token 未过期的用户调用状态接口返回 403。
2. 用户 A 调用 `GET /api/users/{B}/point-details`、`PUT /api/users/{B}/nickname`、`PUT /api/users/{B}/password`、`POST /api/users/{B}/avatar`、`PUT /api/users/{B}/signature` 均返回 404 且无副作用；A 对自己的 ID 调用成功。
3. 普通用户调用 `GET /api/users`、`PUT /api/users/{id}`（含 `isSuperAdmin=true`）、`PUT /api/users/{id}/status`、`DELETE /api/users/{id}` 返回 403。
4. 迁移后 `ai_operator` 不再关联任何 `System.*`、`Blog.*` 权限码；全新库按 `jokester.admin.sql` 初始化后同样如此（已在 MySQL 8.4 临时容器演练：旧脚本初始化的库中 `ai_operator` 有 28 条 system、13 条 blog、11 条 ai_image 授权，迁移后只剩 11 条 ai_image，其他角色不变，重复执行无变化；生产库未执行）。
5. 列表：关键字分别命中用户名、昵称、邮箱、ID；状态与类型筛选正确；三种排序翻页无重复、无遗漏；已注销用户不出现；关键字 `%` 不匹配全部用户。
6. 禁用：目标用户的刷新令牌立即失效、登录被拒绝（错误码按第 13 节第 4 项的结论断言）；审计表新增一条含原因和操作人的记录；积分余额、批次、会员权益不变。
7. 禁用自己、禁用其他超级管理员返回 409 `USER_STATUS_PROTECTED`；未填原因返回 400；重复禁用返回成功且不新增审计。
8. 禁用后：该用户排队中的 `size-mode-v1` 任务以 `ACCOUNT_UNAVAILABLE` 失败并全额退款，且不计入模型状态样本；legacy 排队任务、处理中任务的结算结果与积分流水符合预期（实现时先确认现有行为，再把结论补进 6.4）。
9. 启用正在注销的用户返回 409 `USER_DELETION_IN_PROGRESS`。
10. 详情的 `points` 与同一用户调用 `GET /api/points/balance` 的对应字段相等；对禁用用户也能返回。
11. 汇总：构造含 `image_generate`、`image_refund`、`apple_refund`、`sign_in_expire` 的流水，`imageSpentPoints`、`clawedBackPoints`、`expiredPoints` 分别符合 7.3 的算法；未知 `source` 出现在 `bySource` 中。
12. 时间范围边界：`from` 含、`to` 不含；跨本地自然日的流水按 UTC 参数正确过滤。
13. 任一响应中搜索不到 `passwordHash`、`salt`、`businessKey`。

### 14.2 前端 E2E

1. 普通用户看不到“用户管理”，直接访问 `/admin/users`、`/admin/users/1` 被拦回。
2. 双击行、点击“详情”、聚焦行后按 Enter 都进入详情；双击“禁用”按钮不跳转。
3. 禁用时原因为空无法提交；提交后行状态变为“禁用”，按钮变为“启用”。
4. 自己和其他超级管理员的行不显示“禁用”按钮。
5. 切换时间范围后汇总与流水同时刷新；从详情返回列表时保留搜索条件和页码。

### 14.3 产品验收

1. 客服拿到用户邮箱后，不接触数据库，在 1 分钟内查到该用户当前余额、近 30 天生图消耗和最近一笔充值流水。
2. 运营禁用一个测试账号后，该账号在已登录的浏览器中 15 分钟内无法继续生图；重新启用后积分余额与禁用前一致。

## 15. 实施影响

### 15.1 后端

- 新增：`AdminUsersController`、`IUserAdminService`、`SysUserStatusAuditEntity`、对应 DTO、两个 `MachineErrorCodes`、限流规则、迁移脚本。
- 修改：`IPointService` 增加按用户查询余额概览的方法；`UserService` 增加本人 / 超管校验；`UsersController` 调整权限特性；`AccountDeletionService` 清理审计原因；`jokester.admin.sql` 修正 `ai_operator` 授权并加新表。
- 不改：积分扣减、退款、过期、会员、生图任务流程；登录与令牌流程。
- 文档：实现后更新 `docs/integration-guide.md`、`docs/architecture.md`，`AGENTS.md` 增加本契约小节。

### 15.2 前端（`Jokester-Ai-Image`）

- 新增路由 `admin/users`、`admin/users/:id`（`requiresSuperAdmin`），视图 `UserAdminView.vue`、`UserAdminDetailView.vue`，服务 `services/adminUsers.ts`。
- `AppLayout.vue` 导航增加一项；来源名称复用 `CreditsView.vue` 的 `SOURCE_LABEL_KEYS`（抽到公共位置）。

### 15.3 部署

分两次发布：

1. 第 9 节收紧（可立即发布）：部署 API → 在生产库运行权限回收迁移里的两条预检查询并处理结果 → 执行回收 → 清理 Redis 权限缓存。步骤见 `docs/runbook.md`“用户接口鉴权收紧上线检查”。
2. 用户管理功能：执行 `20261123-add-user-admin.sql` → 部署 API → 部署前端。
