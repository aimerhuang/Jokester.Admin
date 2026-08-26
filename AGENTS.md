# AGENTS

## Repo Notes

- 项目根目录：`D:\Project\jokester.admin`
- 实际 Web API 项目目录：`D:\Project\jokester.admin\jokester.admin`
- 根目录设计文档：`后台管理系统设计书.md`
- 项目 docs：`docs/integration-guide.md`、`docs/architecture.md`、`docs/runbook.md`、`docs/point-recharge.md`、`docs/ai-prompt-filter.md`、`docs/ai-image-size-mode-prd.md`
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
  - `POST /api/auth/register`：请求体只传 `email`、`emailCode`、`password`
- 后端使用规范化邮箱 `@` 前的账号部分生成 `userName` 和 `nickName`；用户名冲突由后端自动消歧，不要求客户端处理
- 登录接口同时支持用户名和邮箱，注册成功后可直接使用邮箱登录
- 图片验证码为 6 位大写字母/数字，写入 Redis 后 5 分钟过期并在校验时一次性消费；仅用于评论提交和登录失败后的二次验证，不参与注册邮件发送
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

- 当前聚合积分余额保存在 `sys_user.point_balance`，流水保存在 `sys_user_point_detail`；套餐/签到积分批次和扣款分摊分别保存在 `sys_user_point_bucket`、`sys_user_point_bucket_usage`，`expires_at=NULL` 表示永久套餐批次。
- 用户注册成功自动赠送 50 积分，来源 `register`。
- `POST /api/points/sign-in` 每日签到赠送 25 积分，来源 `sign_in`；同一自然日只能签到一次。
- 签到积分当天有效；次日由积分账本在余额查询、登录/刷新/资料、签到或生图扣分路径写入 `source=sign_in_expire` 的过期扣减流水。
- 充值套餐由 `GET /api/points/recharge/packages` 动态提供；Web/Android 固定只允许 `monthly`、`trial`、`basic`、`value` 四档且缺档时失败。`monthly` 必须到账 5000 积分，自核销或 Apple 履约到账起 30 天有效，其余三档永久有效。
- 月卡 VIP 以 `sys_user_membership_entitlement` 独立来源账本为准，不能从积分余额或积分批次推断；有效期内积分耗尽仍是 VIP，多笔有效权益取最晚到期时间，Apple 退款只撤销对应交易权益。
- 生图扣分顺序固定为限时套餐积分、签到积分、永久积分；同优先级按最早到期批次。失败退款必须回原扣款批次并保留原到期时间。
- legacy 生图创建任务前按 `ai_image_point_price` 查价：GPT Image2 使用 `modelCode + resolutionCode + qualityCode`，Nano Banana2 只使用 `modelCode + resolutionCode`；扣除 `points * imageCount`。
- `size-mode-v1` 必须在创建事务内锁定客户端提交的 current `modelCode + catalogVersion` 对应 release 价格；explicit 按 `sizeMode + resolutionCode + qualityCode`，auto 按 `sizeMode + qualityCode` 且 `resolutionCode=NULL`。不得回退到 legacy 价格或按最终输出尺寸改价。
- 任务失败或超时时，`AiImageTaskProcessor` 会调用积分服务写入 `source=image_refund` 的返还流水。
- Apple 退款遇到尚未结算的月卡生图预留时，必须在 `sys_user_point_bucket_usage` 标记延后追扣：失败不恢复已撤销额度，成功才追扣余额/记 debt；存在未结算延后追扣时拒绝新生图。
- 不要把扣积分逻辑散落到 Controller；统一走 `IPointService`。
- 四个 AI 生成/创建请求都必须传 `idempotencyKey`；后端只保存 key 的 SHA-256。同用户同 key 同 payload 返回原任务，不重复扣分；同 key 不同 payload 返回 409。
- `ai_image_task.status`：`0` 待处理、`3` 处理中、`1` 成功、`2` 失败；`billing_status`：`0` 预留、`1` 确认、`2` 部分退款、`3` 全额退款。
- 任务插入、余额扣减和 `image:{taskId}:reserve` 流水必须保持同一事务；失败状态、余额返还和唯一 `image:{taskId}:refund` 流水也必须保持同一事务。
- Redis Lua 负责幂等占位、用户每日图片/积分额度、用户活动任务数、全局积压和 Provider 熔断；Redis 不可用时 AI 创建必须 fail-closed。
- Provider 调用必须通过 `IAiImageProviderGate` 的全局 Redis 租约，不要在具体 Provider Service 中另建进程内并发计数。
- GPT 多图请求按 `imageCount` 拆成同等数量的单图任务，每条任务固定 `image_count=1`；整批任务、余额扣减和逐任务预留流水在同一事务提交，`AiImageTaskRecoveryWorker` 负责重启后的待处理任务恢复和超时处理中任务结算。
- Worker 保存生成图时必须显式使用任务 `user_id` 作为 `private-media/ai/{userId}` 所有者目录，不能依赖后台线程中不存在的 `ICurrentUser`。
- Apple `REFUND` / `REVOKE` 内部处理失败必须持续保留可重试路径；不得用固定 `retry_count` 上限把真实退款永久丢弃。

## Point Recharge Contract

- `GET /api/points/recharge/packages`、`POST /api/points/recharge/orders`、`POST /api/points/recharge/redeem` 均要求登录。
- Web 套餐编码固定为 `monthly`、`trial`、`basic`、`value`；管理端签发套餐码时应从套餐查询接口动态读取，不另行硬编码。
- `POST /api/points/recharge/admin/codes` 只允许超级管理员签发兑换码；支持 `packageCode` 套餐模式或 `points` 自定义积分模式（二选一），自定义积分范围为 1–1,000,000，`count` 范围为 1–100。明文码只在签发响应中返回一次，服务端仅保存 SHA-256 哈希和掩码。
- 签发兑换码时必须在事务内重新查询并锁定当前用户，验证其仍为启用、未删除的超级管理员；接口通过 Redis 按用户和 IP 双重限流。
- 购买接口只创建 24 小时有效的待支付订单，不直接增加积分；不能由前端伪造支付完成。
- 套餐购买地址由 `point_recharge_package.purchase_url` 配置，支持 `{orderNo}`、`{packageCode}`、`{userId}` 占位符；只接受绝对 HTTP(S) URL。
- 兑换码区分大小写且只能使用一次；核销、余额增加和 `source=recharge` 积分流水必须保持同一事务。
- 首充体验包首次兑换 200 积分，同一用户后续兑换该套餐为 100 积分。
- 现有数据库依次使用 `docs/migrations/20260809-add-point-recharge.sql`、`docs/migrations/20260819-add-expiring-point-buckets.sql`、`docs/migrations/20260820-add-user-membership-entitlements.sql` 升级；完整接口说明见 `docs/point-recharge.md`。

## Mobile And iOS API Contract

- 移动端响应中的时间统一为带 `Z`/UTC offset 的 ISO 8601；历史积分、充值表仍按现有本地时间写库，只在 API DTO 边界转换为 UTC，新 Apple/法律/授权/账号删除/Asset 表直接存 UTC。
- Apple IAP Product ID 只能来自 `apple_iap_product` 的服务端映射，客户端不得提交积分或价格；Apple Bundle ID、Issuer ID、Key ID 和私钥必须由部署配置/Secret Manager 注入，仓库中不得保存真实值，多行 PEM 不通过 `.env` 文件注入。
- iOS 套餐查询不返回外部购买链接；交易履约、积分入账和流水必须同事务，App Store Server Notification 必须验签并按 notification UUID 幂等处理。
- `GET /api/legal/documents/current` 必须解析当前隐私政策和服务条款，任一缺失都返回 503；该法律文档系统与注册解耦，注册不提交或校验法律版本。AI processing 告知可暂不启用，此时响应 `aiProcessingNotice=null`。
- 所有第三方 AI 生成入口必须校验 AI processing 告知和 consent：未配置已审批告知时 fail-closed 返回 `503 SERVICE_UNAVAILABLE`；已有告知但用户缺少当前 Provider 授权时返回 `412 AI_CONSENT_REQUIRED`。不得因为 TestFlight 或小范围使用绕过。
- 部署专属法律版本和 URL 不进迁移或仓库配置；拿到审批值后用 `--configure-legal-documents` 幂等维护命令配置。暂不启用 AI 告知时显式设置 `AiProcessing.Enabled=false`；启用时 `providerCodes` 必须覆盖当前启用模型路由映射出的 `openai` / `google`。
- 移动端新调用统一走 `POST /api/ai/images`，按服务端模型能力路由；旧 GPT/Nano 专用入口只用于 Web/Android 兼容，不作为新客户端契约。
- 新移动端图片输入使用当前用户拥有的 `assetIds`；上传、读取、引用和删除均校验 `media_asset.owner_user_id`，不得重新允许任意远程 URL。旧 `referenceImageUrls`/`imageUrls` 仅保留后端同源私有媒体兼容路径。
- `DELETE /api/assets/{assetId}` 只允许 Asset 所有者调用，跨用户与不存在统一返回 404；删除任务只软删除任务记录，不级联删除 Asset 或生成文件，账户删除流程负责清理用户全部私有媒体。
- iOS 升级涉及 `docs/migrations/20260812-ios-api-upgrade.sql`，表结构变化时必须同时同步 `jokester.admin.sql`。

## GPT Image2 Image API Contract

- `GET /api/ai/images/parameters` returns enabled image parameter options for resolution (`1k/2k/4k`), quality (`low/med/high`), aspect ratio (`1:1`, `16:9`, `9:16`, `4:3`, `3:4`, `3:2`, `2:3`, `21:9`), and enabled `pointPrices`.
- `POST /api/ai/images/parameters/resolve` resolves `resolutionCode` + `qualityCode` + `aspectRatioCode` into `width`, `height`, `size`, and provider quality.
- Resolution tiers use long-side pixels: `1k=1024`, `2k=2048`, `4k=3840`. GPT `size` dimensions must both be `16px` multiples and at most `3840`, the long-to-short-side ratio must not exceed `3:1`, and total pixels must be between `655,360` and `8,294,400`. Typical results include `1k + 1:1 = 1024x1024`, `2k + 16:9 = 2048x1152`, `4k + 1:1 = 2880x2880`, and `4k + 16:9 = 3840x2160`.
- Legacy GPT Image2 does not accept the project-level `aspectRatioCode=auto`; callers must provide an explicit supported ratio. `size-mode-v1` uses `sizeMode=auto` instead, while Nano Banana2 keeps its existing legacy `auto` behavior.
- `POST /api/ai/images/generate` directly generates one GPT Image2 image and returns `url`, `base64`, `dataUrl`, `resolutionCode`, `qualityCode`, `aspectRatioCode`, computed `width`/`height`, `size`, `quality`, and prompt metadata.
- `POST /api/ai/images/generate` and `POST /api/ai/images` prefer `referenceAssetIds` plus optional `maskAssetId`, with a maximum of 6 reference images. Legacy `referenceImageUrls` / `maskImageUrl` remain only for current-user same-origin private-media compatibility; arbitrary remote URLs are rejected.
- When resolved references are non-empty, the service calls OpenAI `/images/edits` and sends multipart `image[]` file fields; without references it calls `/images/generations`. Asset ownership and file existence must be checked before admission and Provider calls.
- `POST /api/ai/images` creates one queued task per requested image. Every row records `image_count=1` plus the shared prompt/parameter/reference snapshots, and the response returns all task ids in `ids`.
- GPT primary and fallback routes are both stored in `ai_image_model_config` and distinguished by `route_role=primary/fallback`. Each route owns its provider model, URL, key, and paths; enabled primary failures fall back to the enabled fallback row for the same model and resolution. `OpenAI.PrimaryTimeoutSeconds` is the primary-attempt timeout and defaults to `180` seconds.
- AI reference and generated images are saved outside `wwwroot` under `private-media/ai`. Clients receive `/api/media/ai/...` URLs and must send the JWT when downloading; anonymous or cross-user access returns `401/404`.
- Blog media and avatars remain in separate public `/blog` and `/avatar` static prefixes.
- `GET /api/ai/images` and `GET /api/ai/images/{id}` return `resultUrls`, `errorMessage`, `createdAt`, and `updatedAt`; non-super-admin users only see or delete their own tasks.
- Existing databases also need `docs/migrations/20260811-add-ai-image-route-role.sql`; keep [jokester.admin.sql](jokester.admin.sql) aligned when schema changes.

## AI Image Size Mode V1 Contract

- 新协议客户端通过 `X-Client-Capabilities: ai-size-mode-v1` 协商 schema，并同时传平台、版本和 build Header；Header 不授予 auto，服务端用户 cohort 仍必须通过。
- `GET /api/ai/images/models` 是模型能力权威源。`sizeContractVersion=size-mode-v1` 时读取模型级 `catalogVersion` 与 `capabilities.sizeModes`；`/parameters` 不能扩大模型能力。
- v2 pricing 必须传 `modelCode + catalogVersion`，返回 envelope；auto 价格 `resolutionCode=null`。legacy pricing 保持扁平 non-null resolution schema。
- v1 resolve/create/generate 新 key 都必须提交 current `catalogVersion`。auto 必须省略或传 null 的所有尺寸字段；explicit 必须传规范 `resolutionCode + aspectRatioCode`。Nano 兼容端点拒绝 `sizeMode/catalogVersion`。
- durable 幂等事实优先于 catalog、Redis、Asset 和价格检查；catalog 不进入客户端意图指纹。创建响应保持 `id/taskId/ids/taskIds` 并返回 `requestState`，软删除不得导致重建或重扣。
- `size-mode-v1` auto 任务的 `resolution_code`、`aspect_ratio_code`、请求宽高必须保存 NULL，`requested_size=auto`；实际输出尺寸只从图片解码结果回填。不要给 nullable 实体属性恢复 legacy 初始化默认值。
- 请求头、ordinal 明细、单图任务、输入快照、Outbox、release price 锁定、积分扣减和逐任务预留必须同一事务。Redis 使用不可猜测 owner token 绑定整批；未提交 owner 才能撤销。
- Worker 必须按任务 release、claim epoch/token 和 Provider owner lease 执行。未知 inflight attempt 不自动再次外发，最晚在 `reconcile_by` 失败退款；部分队列写入由 Outbox 恢复，超过 `AiCostControl.OutboxBindDeadlineMinutes` 的未派发任务统一结算。
- migration 为 `docs/migrations/20260821-add-ai-image-size-mode-v1.sql`，并已同步 `jokester.admin.sql`。它不发布 auto 路由/价格/URL/Secret；仓库默认关闭 `AiImageSizeMode.Enabled/AutoEnabled`。
- release 只能通过 `--configure-ai-image-catalog` 受控发布：`Approved=true`、auto 三条操作路径验证、独立审批价格和 endpoint/result allowlist 缺一不可。已发布 `CatalogVersion` 不可改写；`EnsureGptImage2TwoK=true` 会补齐并固定 `1k/2k/4k` 参数顺序，复制路由前必须确认 Provider 模型不按分辨率使用不同别名。

## Nano Banana2 Image API Contract

- `POST /api/ai/images/nanoBananaImage/generate` directly generates one Nano Banana2 image.
- `POST /api/ai/images/nanoBananaImage` creates a queued Nano Banana2 image task.
- Compatibility requests accept `prompt`, optional `size`, optional `quality`, preferred `imageAssetIds`, and legacy `imageUrls`.
- The request also accepts `resolutionCode` + `aspectRatioCode`; when `aspectRatioCode=auto`, the backend does not read reference image dimensions or calculate a concrete ratio, and sends upstream `size=auto` instead.
- Nano Banana2 billing is independent of aspect ratio: price lookup still uses the business resolution tier (`resolutionCode`) as the price table `resolution_code`, not the upstream `size=auto` value.
- Empty/missing `imageAssetIds` and `imageUrls` means text-to-image; either non-empty input means image-to-image.
- `imageAssetIds` must belong to the current user and not be deleted. Legacy `imageUrls` must be authenticated `/api/media/ai/...` URLs accessible to the current user. The combined input limit is 6 images.
- Nano Banana2 has no separate `NanoBanana2` configuration section and does not use the GPT primary/fallback route. It always resolves the currently enabled channel from `ai_image_model_config`; provider secrets must be maintained in that database configuration and never committed to the repository.

## AI Prompt Filter Contract

- GPT Image2 与 Nano Banana2 的直接生成、队列生成都必须走 `IAiPromptFilter`。
- 新任务在 Redis admission、任务插入和积分预留前过滤；同 key 同 payload 的已有幂等任务仍优先返回。
- `AiImageTaskProcessor` 在取得 Provider 租约前按当前词库复检；命中后失败结算并退款，不能计入 Provider 熔断失败。
- GPT `negativePrompt` 虽当前不发送给 Provider，但参与幂等和持久化，因此同样过滤。
- MySQL 是权威词库，进程内不可变快照负责匹配，Redis 只发布版本通知；无有效快照或快照过期时生图必须 fail-closed。
- 命中返回 HTTP 422、机器码 `PROMPT_BLOCKED`，不得向普通调用方返回命中词或完整敏感提示词。
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

## Known Environment Issue

- 当前环境启动时可能出现 `DataProtection` DPAPI 和本地 key 文件权限告警。服务一般仍可启动，但如果要做彻底治理，应单独处理 DataProtection 持久化目录。
- 2026-08-08 已使用本地配置完成一次 Redis `PING/SET/GET` 和 Refresh Token Lua 原子消费/重放测试；临时键已清理。仍需在完整登录链路中验收 refresh token、权限缓存键和持续稳定性。
