# AGENTS

## Repo Notes

- 项目根目录：`D:\Project\jokester.admin`
- 实际 Web API 项目目录：`D:\Project\jokester.admin\jokester.admin`
- 根目录设计文档：`后台管理系统设计书.md`
- 项目 docs：`docs/integration-guide.md`、`docs/architecture.md`、`docs/runbook.md`、`docs/point-recharge.md`、`docs/ai-prompt-filter.md`
- 根目录数据库脚本：`jokester.admin.sql`

## Current Stack

- `.NET 10`
- `SqlSugar`
- `Mapster`
- `StackExchange.Redis`
- `Swashbuckle.AspNetCore`

不要再把数据访问写回 `Dapper` / `MySqlConnector` 手工 SQL 风格，当前代码已经切到 `SqlSugar + Mapster`。

## Auth And Cache

- JWT 已接入
- Access Token 固定校验 `iss` / `aud` / `HS256`，有效期必须为 10–15 分钟，并包含 `jti` 与 `sid`
- 刷新令牌默认使用 Redis，服务端只用 SHA-256 哈希定位 Token，不保存明文
- Refresh Token 使用 session/token family；Redis Lua 原子消费，重放时撤销整个 family
- 登出、禁用、改密、用户名或角色/站点/超级管理员上下文变化时会撤销 Refresh Token 会话；已签发 Access Token 最长在 15 分钟后失效
- 权限列表默认使用 Redis 缓存
- JWT / MySQL / Redis 当前不再允许代码内硬编码兜底，缺配置时应直接失败
- 权限变更后应同步清理缓存，优先走 `IPermissionCacheInvalidator`
- 当前 Redis 连接通过 `AbortOnConnectFail=false` 初始化，避免首次不可达时阻塞应用启动
- 当前实现包含开发期降级：
  - Redis 不可用时，权限读取退回数据库查询
  - `EnableInMemoryRefreshTokenFallback=true` 时，刷新令牌退回当前进程内存存储
- 进程内存刷新令牌仅适合本地开发调试，不能当作正式多实例方案

## Admin Bootstrap

已支持命令行初始化管理员：

```powershell
dotnet run --no-build -- --seed-admin <admin-user-name> <admin-password>
```

开发环境接口：

```http
POST /api/dev/bootstrap/super-admin
```

当前超级管理员账号密码应从本地配置或人工输入提供，不要在仓库内保存明文默认值。
开发环境引导接口当前依赖：

- `BootstrapAdmin.UserName`
- `BootstrapAdmin.Password`
- `BootstrapAdmin.Secret`（必填）：调用方须在请求头 `X-Bootstrap-Secret` 中提供与此值完全匹配的字符串，否则返回 401。

## User API Contract

`SaveUserRequest` 使用明文 `password` 字段：

- 新增必填
- 编辑留空表示不修改密码

不要再要求前端传 `passwordHash` / `salt`。

## Registration Email Contract

- 注册邮箱验证码分两步：
  - `POST /api/auth/register/email-code`：请求体只传 `email`
  - `POST /api/auth/register`：请求体传 `userName`、`nickName`、`password`、`email`、`emailCode`
- `emailCode` 由后端生成后通过 SMTP 发到用户邮箱，并按规范化后的 `email` 写入 Redis，10 分钟过期
- 注册时前端必须传同一个 `email` 和用户输入的 `emailCode`；验证通过后后端删除验证码键
- `EmailValidation.EnableApiValidation=false` 时只做本地基础验证：邮箱格式、长度和 `BlacklistDomains`
- `EmailValidation.EnableApiValidation=true` 时才调用第三方邮箱验证 API
- 当前 163 SMTP 推荐配置：`Mail.Host=smtp.163.com`、`Mail.Port=587`、`Mail.UseSsl=false`、`Mail.SecureSocketOptions=StartTls`
- 如果改用 465 端口，应设置 `Mail.SecureSocketOptions=SslOnConnect`（或兼容设置 `Mail.UseSsl=true`）
- `Mail.Password` 应使用 163 邮箱 SMTP 授权码，不要使用邮箱登录密码，也不要写入仓库

## Blog API Contract

- 当前博客后台接口固定归属 `siteCode=blog`
- 公开站点接口 `GET /api/sites/site_code` 不需要授权，返回所有未删除站点及 `status`：`1` 启用、`0` 禁用
- 文章新增/编辑、文章列表、媒体上传、媒体列表不再接收调用方传入的 `siteId`
- `blog_article`、`blog_media` 表仍保留 `site_id`
- 这些 `site_id` 由后端内部解析 `blog` 站点后自动写入和过滤，不要把它重新暴露为前端必填参数
- 文章列表和详情返回 `coverUrl` 与 `thumbnailUrl`
- `thumbnailUrl` 优先使用 `coverUrl`，其次取 `blog_article_media` 关联到的第一张 `blog_media.url`，最后从正文第一张 `<img src="...">` 兜底提取
- 创建/更新文章时会解析正文中的 `<img src="...">`，用匹配到的 `blog_media.url` 同步维护 `blog_article_media`

## Points And AI Image Billing

- 当前积分余额保存在 `sys_user.point_balance`，流水保存在 `sys_user_point_detail`。
- 用户注册成功自动赠送 50 积分，来源 `register`。
- `POST /api/points/sign-in` 每日签到赠送 25 积分，来源 `sign_in`；同一自然日只能签到一次。
- 签到积分当天有效；次日由 `PointService` 在余额查询、签到或生图扣分路径写入 `source=sign_in_expire` 的过期扣减流水。
- 生图创建任务前必须按 `ai_image_point_price.model_code + resolution_code + quality_code` 查价格，并扣除 `points * imageCount`。
- GPT Image2 价格匹配使用 `modelCode + resolutionCode + qualityCode`；Nano Banana2 官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality_code` 列与画幅比例都不参与匹配（库中存 `''` 或 `NULL` 都不影响）。`PointService.GetImageGenerateCostAsync` 仅在调用方显式传入 `quality` 时才把 `quality_code` 加入查询条件。
- 任务失败或超时时，`AiImageTaskProcessor` 会调用积分服务写入 `source=image_refund` 的返还流水。
- 不要把扣积分逻辑散落到 Controller；统一走 `IPointService`。
- 四个 AI 生成/创建请求都必须传 `idempotencyKey`；后端只保存 key 的 SHA-256。同用户同 key 同 payload 返回原任务，不重复扣分；同 key 不同 payload 返回 409。
- `ai_image_task.status`：`0` 待处理、`3` 处理中、`1` 成功、`2` 失败；`billing_status`：`0` 预留、`1` 确认、`2` 部分退款、`3` 全额退款。
- 任务插入、余额扣减和 `image:{taskId}:reserve` 流水必须保持同一事务；失败状态、余额返还和唯一 `image:{taskId}:refund` 流水也必须保持同一事务。
- Redis Lua 负责幂等占位、用户每日图片/积分额度、用户活动任务数、全局积压和 Provider 熔断；Redis 不可用时 AI 创建必须 fail-closed。
- Provider 调用必须通过 `IAiImageProviderGate` 的全局 Redis 租约，不要在具体 Provider Service 中另建进程内并发计数。
- GPT 多图请求按 `imageCount` 拆成同等数量的单图任务，每条任务固定 `image_count=1`；整批任务、余额扣减和逐任务预留流水在同一事务提交，`AiImageTaskRecoveryWorker` 负责重启后的待处理任务恢复和超时处理中任务结算。
- Worker 保存生成图时必须显式使用任务 `user_id` 作为 `private-media/ai/{userId}` 所有者目录，不能依赖后台线程中不存在的 `ICurrentUser`。

## Point Recharge Contract

- `GET /api/points/recharge/packages`、`POST /api/points/recharge/orders`、`POST /api/points/recharge/redeem` 均要求登录。
- `POST /api/points/recharge/admin/codes` 只允许超级管理员签发兑换码；明文码只在签发响应中返回一次，服务端仅保存 SHA-256 哈希和掩码。
- 购买接口只创建 24 小时有效的待支付订单，不直接增加积分；不能由前端伪造支付完成。
- 套餐购买地址由 `point_recharge_package.purchase_url` 配置，支持 `{orderNo}`、`{packageCode}`、`{userId}` 占位符；只接受绝对 HTTP(S) URL。
- 兑换码区分大小写且只能使用一次；核销、余额增加和 `source=recharge` 积分流水必须保持同一事务。
- 首充体验包首次兑换 200 积分，同一用户后续兑换该套餐为 100 积分。
- 现有数据库使用 `docs/migrations/20260809-add-point-recharge.sql` 升级；完整接口说明见 `docs/point-recharge.md`。

## GPT Image2 Image API Contract

- `GET /api/ai/images/parameters` returns enabled image parameter options for resolution (`1k/2k/4k`), quality (`low/med/high`), aspect ratio (`1:1`, `16:9`, `9:16`, `4:3`, `3:4`, `3:2`, `2:3`, `21:9`), and enabled `pointPrices`.
- `POST /api/ai/images/parameters/resolve` resolves `resolutionCode` + `qualityCode` + `aspectRatioCode` into `width`, `height`, `size`, and provider quality.
- Resolution tiers use long-side pixels: `1k=1024`, `2k=2048`, `4k=3840`; provider dimensions are rounded to `16px` multiples and capped at `8,294,400` total pixels. For example `4k + med + 1:1` resolves to `2880x2880`, `4k + med + 16:9` resolves to `3840x2160`, and provider quality is `medium`.
- `POST /api/ai/images/generate` directly generates one GPT Image2 image and returns `url`, `base64`, `dataUrl`, `resolutionCode`, `qualityCode`, `aspectRatioCode`, computed `width`/`height`, `size`, `quality`, and prompt metadata.
- `POST /api/ai/images/generate` and `POST /api/ai/images` accept `referenceImageUrls` as a JSON array of backend-hosted image URLs, with a maximum of 6 reference images.
- When `referenceImageUrls` is non-empty, the service calls OpenAI `/images/edits`, resolves each authenticated private-media URL to a file under `private-media/ai`, and sends multipart `image[]` file fields; without references it calls `/images/generations`.
- `POST /api/ai/images` creates one queued task per requested image. Every row records `image_count=1` plus the shared prompt/parameter/reference snapshots, and the response returns all task ids in `ids`.
- GPT primary and fallback routes are both stored in `ai_image_model_config` and distinguished by `route_role=primary/fallback`. Each route owns its provider model, URL, key, and paths; enabled primary failures fall back to the enabled fallback row for the same model and resolution. `OpenAI.PrimaryTimeoutSeconds` remains the primary-attempt timeout.
- AI reference and generated images are saved outside `wwwroot` under `private-media/ai`. Clients receive `/api/media/ai/...` URLs and must send the JWT when downloading; anonymous or cross-user access returns `401/404`.
- Blog media and avatars remain in separate public `/blog` and `/avatar` static prefixes.
- `GET /api/ai/images` and `GET /api/ai/images/{id}` return `resultUrls`, `errorMessage`, `createdAt`, and `updatedAt`; non-super-admin users only see or delete their own tasks.
- Existing databases also need `docs/migrations/20260811-add-ai-image-route-role.sql`; keep [jokester.admin.sql](jokester.admin.sql) aligned when schema changes.

## Nano Banana2 Image API Contract

- `POST /api/ai/images/nanoBananaImage/generate` directly generates one Nano Banana2 image.
- `POST /api/ai/images/nanoBananaImage` creates a queued Nano Banana2 image task.
- The request accepts `prompt`, optional `size`, optional `quality`, and optional `imageUrls`.
- The request also accepts `resolutionCode` + `aspectRatioCode`; when `aspectRatioCode=auto`, the backend does not read reference image dimensions or calculate a concrete ratio, and sends upstream `size=auto` instead.
- Nano Banana2 billing is independent of aspect ratio: price lookup still uses the business resolution tier (`resolutionCode`) as the price table `resolution_code`, not the upstream `size=auto` value.
- Empty/missing `imageUrls` means text-to-image; non-empty `imageUrls` means image-to-image.
- `imageUrls` must be authenticated `/api/media/ai/...` URLs owned by the current user (super admin excepted), with a maximum of 6 input images.
- Nano Banana2 has no separate `NanoBanana2` configuration section and does not use the GPT primary/fallback route. It always resolves the currently enabled channel from `ai_image_model_config`; provider secrets must be maintained in that database configuration and never committed to the repository.

## AI Prompt Filter Contract

- GPT Image2 与 Nano Banana2 的直接生成、队列生成都必须走 `IAiPromptFilter`。
- 新任务在 Redis admission、任务插入和积分预留前过滤；同 key 同 payload 的已有幂等任务仍优先返回。
- `AiImageTaskProcessor` 在取得 Provider 租约前按当前词库复检；命中后失败结算并退款，不能计入 Provider 熔断失败。
- GPT `negativePrompt` 虽当前不发送给 Provider，但参与幂等和持久化，因此同样过滤。
- MySQL 是权威词库，进程内不可变快照负责匹配，Redis 只发布版本通知；无有效快照或快照过期时生图必须 fail-closed。
- 命中返回 HTTP 400、业务码 `2001`，不得向普通调用方返回命中词或完整敏感提示词。
- 敏感词过滤只采用 MySQL 关键词词库与进程内快照，不部署或调用本地/远程审核模型。
- 关键词匹配模式包括 `contains`、`word`、`compact`；简繁体、拼音、谐音和上下文隐喻需要维护独立词条与回归样例。
- 项目安全分类固定为 `sexual_minors`、`non_consensual_nudity`、`graphic_violence`、`self_harm`、`hate_extremism`、`weapons_drugs`、`deepfake_privacy`；普通成人色情仍可保留 `sexual_content`，不得强塞进非自愿类别。
- houbb 原生标签 `0..4` 不是项目七类 taxonomy。第三方开源词库只能作为离线候选源，必须保留来源、commit、原标签、许可证和原始/审核文件哈希，经人工重分类后仍以 `action=audit,status=0` 导入，禁止整库直接启用。
- houbb 无覆盖或覆盖不足的项目补充词必须使用独立 `source_code=project-curated`，不能冒充第三方来源；不要启用 `幼女`、`少女`、`学生妹`、`枪`、`换脸` 等单独宽泛词。
- 现有数据库按顺序使用 `docs/migrations/20260811-add-ai-prompt-sensitive-words.sql`、`docs/migrations/20260811-expand-ai-prompt-sensitive-words-houbb.sql` 升级；完整说明见 `docs/ai-prompt-filter.md`，并保持 `jokester.admin.sql` 同步。

## Runtime Notes

- 本地默认地址：`http://localhost:5049`
- Swagger UI：`/swagger`
- `appsettings.json` / `appsettings.Development.json` 当前仅保留空占位，实际值应由本地配置文件、环境变量或 Secret Manager 注入
- MySQL 连接串当前需要：
  - `SslMode=None`
  - `AllowPublicKeyRetrieval=True`
- Redis 默认连接串当前为：
  - `localhost:6379,abortConnect=false`
- 163 SMTP 默认主机当前为：
  - `smtp.163.com`
- 本地没有第三方邮箱验证服务时：
  - 设置 `EmailValidation.EnableApiValidation=false`
  - 这会保留本地基础验证，并跳过 `EmailValidation.ApiEndpoint`

## Current API Additions

- 角色接口已支持分页筛选、状态更新、删除
- 站点接口已支持分页筛选、状态更新、删除和公开 `site_code` 列表
- 菜单接口已支持树查询、分页筛选、状态更新、删除
- 用户接口已支持按用户查询授权菜单树和通过用户专属授权角色保存菜单权限，权限码为 `System.User.Authorize`
- 博客评论接口已支持公开图片验证码、公开提交、公开已审核列表、后台分页、审核、删除
- 博客仪表盘接口已支持文章/评论/媒体统计和最新待审核评论
- 日志接口已支持登录日志/操作日志查询与批量删除
- GPT Image2 生图接口已支持直接生成、后台任务、服务器私有图片落盘和鉴权下载、参数编码解析、参考图 URL JSON 入参、按价格表扣积分和失败返还
- Nano Banana2 生图接口已支持直接生成、后台任务、文生图/图生图、按价格表扣积分和失败返还
- 积分接口已支持余额查询和每日签到
- 博客评论和仪表盘权限码包括：
  - `Blog.Comment.View`
  - `Blog.Comment.Review`
  - `Blog.Comment.Delete`
  - `Blog.Dashboard.View`
- 日志接口当前权限码包括：
  - `System.Log.Login.View`
  - `System.Log.Login.Delete`
  - `System.Log.Operation.View`
  - `System.Log.Operation.Delete`

## Known Environment Issue

- 当前环境启动时可能出现 `DataProtection` DPAPI 和本地 key 文件权限告警。服务一般仍可启动，但如果要做彻底治理，应单独处理 DataProtection 持久化目录。
- 2026-08-08 已使用本地配置完成一次 Redis `PING/SET/GET` 和 Refresh Token Lua 原子消费/重放测试；临时键已清理。仍需在完整登录链路中验收 refresh token、权限缓存键和持续稳定性。
