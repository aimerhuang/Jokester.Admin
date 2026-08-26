# jokester.admin

基于 `.NET 10 Web API` 的后台管理系统后端，当前已接入以下能力：

- JWT 登录、刷新、登出、当前用户信息
- iOS StoreKit 交易履约、App Store Server Notifications V2 和退款负债处理
- 法律文档版本、第三方 AI 授权、账户删除和公开移动端配置
- 用户注册赠送积分、每日签到、积分明细、充值兑换、AI 生图扣积分与失败返还
- 私有 AI Asset ID、缩略图、资源归属校验和统一模型任务路由
- YouMind 官方中文提示词库同步、搜索、行为统计和历史快照切换
- 用户、角色、站点、菜单、日志审计接口
- 权限码校验中间件
- `SqlSugar` 数据访问
- `Mapster` DTO 映射
- `Redis` 刷新令牌与权限缓存
- Swagger UI

- [docs/integration-guide.md](./docs/integration-guide.md)：接口集成说明
- [docs/architecture.md](./docs/architecture.md)：架构和数据流说明
- [docs/runbook.md](./docs/runbook.md)：本地运行、冒烟检查和排障
- [docs/point-recharge.md](./docs/point-recharge.md)：充值套餐、订单、兑换码与购买地址配置
- [docs/point-package-frontend-prd.md](./docs/point-package-frontend-prd.md)：积分套餐、兑换、StoreKit 与生图积分的前端改造 PRD
- [docs/architecture.md](./docs/architecture.md#提示词库数据流)：提示词库数据源、同步、图片与快照边界
- [docs/ai-prompt-filter.md](./docs/ai-prompt-filter.md)：基于中英文关键词词库的生图过滤、热更新和管理接口
- [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md)：第三方数据来源、固定版本与许可证说明
- [docs/security-hardening-prd.md](./docs/security-hardening-prd.md)：公网 App API 安全加固计划

## 当前技术栈

- `.NET 10`
- `SqlSugar`
- `Mapster`
- `StackExchange.Redis`
- `Swashbuckle.AspNetCore`
- `MySQL 8`

## 本地运行

1. 确保本地服务可用
   - MySQL: `localhost:3306`
   - Redis: `localhost:6379`
2. 初始化数据库
   - 执行根目录 [jokester.admin.sql](./jokester.admin.sql)
3. 启动项目

```powershell
cd .\jokester.admin
dotnet run --launch-profile http
```

默认地址：

- `http://localhost:5049`
- Swagger UI: `http://localhost:5049/swagger`

## 配置

当前默认配置位于 [appsettings.json](./jokester.admin/appsettings.json)，仓库内默认留空，运行时必须通过本地配置文件、环境变量或 Secret Manager 注入，主要字段包括：

- `Jwt.Issuer`
- `Jwt.Audience`
- `Jwt.SecretKey`
- `Database.ConnectionString`
- `Redis.ConnectionString`
- `Redis.InstanceName`
- `Redis.EnableInMemoryRefreshTokenFallback`
- `Security.AllowedOrigins`
- `Security.KnownProxies`
- `Mail.Host`
- `Mail.Port`
- `Mail.UseSsl`
- `Mail.SecureSocketOptions`
- `Mail.UserName`
- `Mail.Password`
- `Mail.FromAddress`
- `Mail.FromName`
- `EmailValidation.EnableApiValidation`
- `EmailValidation.ApiEndpoint`
- `EmailValidation.ApiKey`
- `EmailValidation.ApiKeyHeaderName`
- `EmailValidation.BlacklistDomains`
- `BootstrapAdmin.UserName`
- `BootstrapAdmin.Password`
- `BootstrapAdmin.Secret`
- `OpenAI.PrimaryTimeoutSeconds`（默认 180 秒，主路由超时后切换回退路由）
- `AppleAppStore.*`（仅启用 Apple IAP 时必填；私钥必须由 Secret Manager 或部署密钥注入）
- `Mobile.MinimumSupportedVersion`
- `Mobile.LatestVersion`
- `Mobile.MaintenanceMode`
- `Mobile.Features.*`

核心生产环境变量模板见 [.env.example](./.env.example)。可选、部署专属或多行密钥配置以 [appsettings.json](./jokester.admin/appsettings.json) 和 [runbook](./docs/runbook.md) 为准。GPT Image2 与 Nano Banana2 的 Provider 地址、模型、Key 和请求路径以数据库 `ai_image_model_config` 为准，不从 `.env` 或 `appsettings.json` 读取。

JWT、MySQL 和 Redis 的核心配置没有代码内兜底，缺失时应用会在启动阶段失败。生产环境还必须配置至少一个精确的 `Security.AllowedOrigins`。Mail、第三方邮箱验证、开发管理员引导和提示词库等配置只在对应功能启用或调用时要求提供。

MySQL 连接串当前需要包含：

```text
SslMode=None;AllowPublicKeyRetrieval=True;
```

Redis 连接串示例格式：

```text
localhost:6379,abortConnect=false
```

Redis 相关运行说明：

- 应用启动时会以 `AbortOnConnectFail=false` 创建 `ConnectionMultiplexer`
- Redis 首次不可达时，不会因为连接初始化失败直接阻塞服务启动
- `EnableInMemoryRefreshTokenFallback=true` 时，Redis 不可用会退回当前进程内存保存刷新令牌，仅适合本地开发调试
- Refresh Token 仅按哈希存储，并使用 session/token family 原子轮换；并发刷新只有一个成功，重放会撤销同一会话族
- 权限缓存读取失败时会退回数据库直查

邮件发送与注册邮箱验证说明：
- 注册验证码邮件通过 `Mail` 配置的 SMTP 服务发送
- 163 邮箱示例：`Host=smtp.163.com`、`Port=587`、`UseSsl=false`、`SecureSocketOptions=StartTls`
- 如果改用 465 端口，应设置 `SecureSocketOptions=SslOnConnect`（或兼容设置 `UseSsl=true`）
- `Mail.UserName` 和 `Mail.FromAddress` 建议保持为同一个 163 邮箱地址
- `Mail.Password` 应填写 163 邮箱 SMTP 授权码，不是邮箱登录密码，不要提交到仓库
- `EmailValidation.EnableApiValidation=false` 时只做本地基础校验：邮箱格式、长度和 `BlacklistDomains`
- `EmailValidation.EnableApiValidation=true` 时才会调用 `ApiEndpoint`；接口响应需包含 `valid`、`isValid`、`deliverable`、`isDeliverable` 之一且值为 `true`，或在 `result` / `data` 中提供等价结果

## 管理员初始化

当前支持两种方式初始化或重置超级管理员。

命令行方式：

```powershell
cd .\jokester.admin
dotnet run --no-build -- --seed-admin <admin-user-name> <admin-password>
```

开发环境接口：

```http
POST /api/dev/bootstrap/super-admin
```

该接口当前要求开发环境显式提供：

- `BootstrapAdmin.UserName`
- `BootstrapAdmin.Password`
- `BootstrapAdmin.Secret`

并在请求头中传入：

```http
X-Bootstrap-Secret: <BootstrapAdmin.Secret>
```

辅助脚本见 [scripts/reset-super-admin.ps1](./scripts/reset-super-admin.ps1)，调用时必须通过 `-Secret <BootstrapAdmin.Secret>` 提供引导密钥。

## 鉴权接口

- `POST /api/auth/login`
- `POST /api/auth/register/email-code`
- `POST /api/auth/register`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `POST /api/auth/logout-all`
- `GET /api/auth/profile`
- `POST /api/auth/account-deletion/requests`
- `GET /api/auth/account-deletion/requests/current`
- `DELETE /api/auth/account-deletion/requests/{requestId}`

注册邮箱验证码流程：
- 前端直接调用 `POST /api/auth/register/email-code`，请求体只传 `email`
- 后端生成 6 位验证码，写入 Redis，键前缀为 `register_email_code:` 加上配置的 `Redis.InstanceName`，有效期 10 分钟，并通过 SMTP 发信；成功响应的 `data.retryAfterSeconds=60` 用于客户端发送按钮倒计时
- 用户收到验证码后，前端再调用 `POST /api/auth/register`
- 注册请求体只传 `email`、`emailCode`、`password`，不传图片验证码、用户名、昵称或法律文档版本
- 后端按规范化邮箱 `@` 前的账号部分生成 `userName` 和 `nickName`，用户名冲突时自动消歧
- 注册时后端会用同一个 `email` 去 Redis 查验证码；验证成功后创建用户并删除验证码键
- 注册成功后可以直接使用邮箱和密码登录
- 隐私政策、服务条款和 AI 数据处理授权继续由独立法律文档与授权流程维护；其中当前 AI processing 授权仍是第三方 AI 生图的前置条件

登录成功返回：

- `accessToken`
- `refreshToken`
- `sessionId`
- `accessTokenExpiresAt`
- `refreshTokenExpiresAt`
- `user`
- `sites`
- `permissions`

受保护接口使用：

```http
Authorization: Bearer <accessToken>
```

登出时可附带：

```http
X-Refresh-Token: <refreshToken>
```

Refresh Token 单次轮换并只按 SHA-256 哈希定位。旧 Token 在 10 秒并发宽限内返回同一轮换结果；超出宽限后的重放会撤销整个 session family。错误响应通过 `ACCESS_TOKEN_EXPIRED`、`REFRESH_TOKEN_EXPIRED` 和 `SESSION_REVOKED` 区分处理方式。

公开移动端元数据接口：

- `GET /api/legal/documents/current`
- `GET /api/mobile/config`

登录用户可通过 `GET /api/users/me/consents` 和 `PUT /api/users/me/consents/ai-processing` 管理第三方 AI 数据处理授权。缺少当前模型 Provider 所需授权时，生图入口返回 HTTP 412 和 `AI_CONSENT_REQUIRED`。

## 用户管理接口

- `GET /api/users`
- `POST /api/users`
- `PUT /api/users/{id}`
- `PUT /api/users/{id}/status`
- `DELETE /api/users/{id}`

`POST /api/users` 和 `PUT /api/users/{id}` 请求体使用 `SaveUserRequest`，关键字段包括：

- `userName`
- `nickName`
- `password`
- `email`
- `phone`
- `status`
- `avatarUrl`
- `remark`
- `isSuperAdmin`
- `roleIds`
- `siteIds`

说明：

- 新增用户时 `password` 必填
- 编辑用户时 `password` 留空表示不修改密码

## 角色/站点/菜单/日志接口

- `GET /api/roles`
- `POST /api/roles`
- `PUT /api/roles/{id}`
- `PUT /api/roles/{id}/menus`
- `PUT /api/roles/{id}/status`
- `DELETE /api/roles/{id}`
- `GET /api/sites`
- `GET /api/sites/site_code`
- `POST /api/sites`
- `PUT /api/sites/{id}`
- `PUT /api/sites/{id}/status`
- `DELETE /api/sites/{id}`
- `GET /api/menus/tree`
- `GET /api/menus`
- `POST /api/menus`
- `PUT /api/menus/{id}`
- `PUT /api/menus/{id}/status`
- `DELETE /api/menus/{id}`
- `GET /api/logs/login`
- `DELETE /api/logs/login`
- `GET /api/logs/operation`
- `DELETE /api/logs/operation`

说明：

- 站点公开接口 `GET /api/sites/site_code` 不需要授权，返回所有未删除站点及 `status`：`1` 启用、`0` 禁用
- 角色、站点、菜单列表接口当前均支持分页筛选
- 日志接口当前已接入显式权限码校验
- 日志删除当前按请求体 `ids` 批量删除
- 非 `GET` 请求会自动写入操作日志

## 博客接口约定

- `GET /api/blog/articles`
- `GET /api/blog/articles/{id}`
- `POST /api/blog/articles`
- `PUT /api/blog/articles/{id}`
- `DELETE /api/blog/articles/{id}`
- `POST /api/blog/articles/{id}/publish`
- `POST /api/blog/media/upload`
- `GET /api/blog/media`
- `DELETE /api/blog/media/{id}`
- `GET /api/blog/comments/captcha`
- `POST /api/blog/comments/public`
- `GET /api/blog/comments/public`
- `GET /api/blog/comments`
- `PUT /api/blog/comments/{id}/review`
- `DELETE /api/blog/comments/{id}`
- `GET /api/blog/dashboard/stats`

文章列表和详情返回 `coverUrl` 与 `thumbnailUrl`。`thumbnailUrl` 优先使用 `coverUrl`，其次使用 `blog_article_media` 关联到的第一张媒体图，最后从正文第一张 `<img src="...">` 兜底提取。

说明：

- 当前仓库里的博客后台只面向固定博客站点，以上接口不再接收 `siteId`
- 后端会固定解析 `siteCode=blog` 对应的站点 ID，再完成文章和媒体的创建、查询、更新、删除
- 数据表中的 `site_id` 仍然保留，用于数据归属和后续扩展，不需要前端传值
- 创建/更新文章时，后端会解析正文中的 `<img src="...">` 并同步维护 `blog_article_media` 关联表
- 文章新增/编辑请求体支持 `status`：`0` 草稿、`1` 发布、`2` 隐藏；不传时按草稿处理
- 评论公开提交前先调用 `GET /api/blog/comments/captcha` 获取 `captchaId`、`imageBase64`、`mimeType`；前端按图片展示验证码，提交 `POST /api/blog/comments/public` 时传 `captchaId`、`captchaAnswer`，新评论默认 `status=0` 待审核
- 公开评论列表 `GET /api/blog/comments/public` 只返回 `status=1` 已通过评论；后台评论分页/审核/删除需要 `Blog.Comment.View`、`Blog.Comment.Review`、`Blog.Comment.Delete`
- 仪表盘统计 `GET /api/blog/dashboard/stats` 需要 `Blog.Dashboard.View`，返回文章、评论、媒体统计和最新 10 条待审核评论

## AI 生图接口约定

- `POST /api/ai/images/generate`：直接生成 GPT Image2 图片；后端会先创建历史任务、扣除积分，再由后台 worker 执行并最多等待 5 分钟返回结果
- `POST /api/ai/images`：按 `modelCode` 解析 Provider 协议并创建后台任务，是移动端统一创建入口
- `POST /api/ai/images/nanoBananaImage/generate`：直接生成 Nano Banana2 图片
- `POST /api/ai/images/nanoBananaImage`：创建 Nano Banana2 后台生图任务
- `GET /api/ai/images`：查询 AI 生图任务记录，支持 `isFavorite`、`prompt`、`startDate`、`endDate` 筛选；失败任务保留在列表中并返回 `errorMessage`
- `GET /api/ai/images/{id}`：查询 AI 生图任务详情
- `GET /api/ai/images/models`：查询启用的 AI 图片模型
- `GET /api/ai/images/parameters`：查询 GPT Image2 参数选项和积分价格表
- `GET /api/ai/images/pricing-options`：legacy 客户端返回扁平定价列表；`size-mode-v1` 客户端按 `modelCode + catalogVersion` 返回版本化定价 envelope
- `POST /api/ai/images/parameters/resolve`：解析 GPT Image2 参数为实际宽高和供应商画质
- `POST /api/ai/images/upload`：上传 AI 图片引用文件
- `GET /api/assets/{assetId}/content`、`GET /api/assets/{assetId}/thumbnail`：鉴权读取当前用户的 Asset 原图和缩略图
- `DELETE /api/assets/{assetId}`：由 Asset 所有者软删除记录并清理原图和缩略图；跨用户与不存在统一返回 404
- `POST /api/ai/images/{id}/favorite`：收藏或取消收藏任务中的单张结果图片
- `DELETE /api/ai/images/{id}`：软删除 AI 生图任务记录，不级联删除上传 Asset 或生成文件
- `size-mode-v1` 客户端通过 capability/platform/version/build Header 获取模型级 `catalogVersion` 与 `sizeModes`；v2 pricing、resolve、create 和 generate 使用同一不可变 catalog
- 新协议支持 `sizeMode=explicit|auto`、durable 幂等批次、逐图结果、请求/输出尺寸分离和稳定失败字段；auto 只有在路由验证、独立价格、服务端 cohort 与配置全部就绪时才开放
- schema 迁移不会发布模型目录；审批完成后由 `dotnet run --project .\jokester.admin -- --configure-ai-image-catalog` 在单事务中补齐受控参数/路由/价格、发布 immutable release 并切换 current pointer，完整配置和回滚顺序见 [runbook](./docs/runbook.md#ai-尺寸模式升级与回滚)

## 提示词库接口约定

- `GET /api/prompts`、`GET /api/prompts/{id}`：查询当前激活的 126 条中文提示词快照
- `POST /api/prompts/{id}/events`：记录详情、复制和使用行为，不影响当前排序
- `GET /api/admin/prompt-sync/status`：查看同步、内容哈希、封面和当前快照状态
- `POST /api/admin/prompt-sync/run`：提交官方 `/youmarketing-api/prompts` 同步任务
- `POST /api/admin/prompt-sync/snapshots/{snapshotId}/activate`：切换到封面完整的历史成功快照
- 同步只接收标题、描述和正文都包含中文的条目；不足 126 条或任一封面失败时保留当前快照

## Nano Banana2 生图接口约定

- `POST /api/ai/images/nanoBananaImage/generate`：直接生成 Nano Banana2 图片
- `POST /api/ai/images/nanoBananaImage`：创建 Nano Banana2 后台生图任务
- 请求体不传 `imageUrls` 或传空数组时执行文生图
- 请求体传 `imageUrls` 时执行图生图，图片 URL 必须来自当前用户的 `/api/media/ai/...` 私有媒体
- 请求体可传 `size`，也可传 `resolutionCode` + `aspectRatioCode`；`aspectRatioCode=auto` 时后端不计算具体画幅比例，直接把上游 `size` 参数设为 `auto`；积分价格仍按业务分辨率档位匹配，不依赖画幅比例
- Nano Banana2 不使用单独的配置段，也不参与 GPT 主备切换；路由、Provider 模型和 Key 直接读取 `ai_image_model_config` 中当前启用的渠道

说明：

- 生成接口需要 `AiImage.Generate` 权限，列表查询需要 `AiImage.Page` 权限，详情/删除分别需要 `AiImage.Record.View`、`AiImage.Record.Delete`，收藏/取消收藏需要 `AiImage.Favorite`
- 列表查询支持 `prompt` 对提示词做模糊查询，`startDate`/`endDate` 按创建时间筛选；只传日期的 `endDate` 会包含当天
- 列表查询支持 `isFavorite=true` 只返回包含当前用户收藏图片的任务，`isFavorite=false` 返回不包含当前用户收藏图片的任务
- 请求体记录 `prompt`、`resolutionCode`、`qualityCode`、`aspectRatioCode`；GPT 多图请求按 `imageCount` 创建同等数量的单图任务，每条任务固定 `imageCount=1`
- GPT Image2 分辨率档位按长边计算：`1k=1024`、`2k=2048`、`4k=3840`。上游 `size` 的宽高都必须为 `16px` 倍数且不超过 `3840`，长短边比例不超过 `3:1`，总像素必须在 `655,360` 到 `8,294,400` 之间；典型结果包括 `1k + 1:1 = 1024x1024`、`2k + 16:9 = 2048x1152`、`4k + 1:1 = 2880x2880`、`4k + 16:9 = 3840x2160`
- legacy GPT Image2 不接受项目层 `aspectRatioCode=auto`；`size-mode-v1` 的 GPT auto 使用独立 `sizeMode=auto` 契约且仓库默认关闭，Nano Banana2 的 legacy auto 行为保持不变
- 上传响应返回 `assetId`、同源内容/缩略图地址、真实 MIME、尺寸、字节数和 `metadataStripped`；HEIC/HEIF 主图解码并规范化为 PNG，JPEG、PNG、WebP 清除元数据后保持原格式，512px 缩略图统一保存为 WebP；新版 iOS 通过 `referenceAssetIds` / `maskAssetId` 引用资源
- 兼容期仍接受当前用户私有 `/api/media/ai/...` 的 `referenceImageUrls` / `maskImageUrl`，不接受任意远程 URL；后端在入队前校验 Asset 所有权和文件存在性
- 直接生成响应包含 `taskId`、`taskIds` 和 `url`；创建接口返回首个 `id` 和完整 `ids`。用户关闭网页不会取消已入队任务，完成后仍可在历史记录中找回图片
- 请求体传入的 `modelCode` 是业务模型编码，后端按 `model_code` + `resolution_code` 分别解析 `route_role=primary/fallback`；每条路由自己的 `provider_model` 会原样作为上游 `model` 参数
- GPT 主备地址、Key、Provider 模型和请求路径统一保存在 `ai_image_model_config`。主备均启用时先主后备；禁用主路由后会直接使用备用路由
- AI 上传图和生成图保存在 `private-media/ai`（不在 `wwwroot`）；响应 URL 使用 `/api/media/ai/...`，下载时必须携带 Access Token
- 后台任务列表和详情返回字符串 `status`（`queued/processing/succeeded/failed/cancelled`）、兼容数字 `statusCode`、`progress`、`pollAfterSeconds`、UTC 时间和结果 Asset；失败任务保留并返回 `errorMessage`
- 收藏接口请求体示例：`{ "imageUrl": "/api/media/ai/tasks/202608/xxx.png", "isFavorite": true }`；取消收藏传 `isFavorite=false`，`imageUrl` 必须属于该任务的 `resultUrls`

## 已知环境问题

- 启动时可能出现 `DataProtection` 的 DPAPI / 文件权限告警
  - 当前不影响 API 启动与本地联调
  - 若要彻底消除，需要单独调整密钥持久化目录或开发环境 DataProtection 策略
- 2026-08-08 已完成一次本地 Redis `PING/SET/GET` 和 Refresh Token Lua 集成测试；仍需在完整登录链路中继续验收权限缓存键与长期稳定性
  - 如果要做完整验收，需要先把本机 Redis 实例自身校验清楚，再确认登录后 refresh token / 权限缓存键写入

## 积分与 AI 生图接口约定

- `GET /api/points/balance`：查询当前用户积分余额和今日签到状态。
- `GET /api/points/details`：分页查询当前用户积分流水。
- `POST /api/points/sign-in`：每日签到领取 25 积分；同一自然日只能领取一次。
- 注册成功自动赠送 50 积分。
- Web/Android 充值套餐固定为 `monthly`、`trial`、`basic`、`value`；`monthly` 到账 5000 积分并在 30 天后到期，其余三档积分永久有效。
- 到期批次会在积分查询、登录/刷新/资料、签到或生图扣分事务内先结算；生图依次消费限时套餐积分、签到积分和永久积分。
- legacy 生图按 `ai_image_point_price` 扣分：GPT Image2 使用 `modelCode + resolutionCode + qualityCode`，Nano Banana2 只使用 `modelCode + resolutionCode`；总额为 `points * imageCount`。
- `size-mode-v1` 在创建事务内锁定 `modelCode + catalogVersion` 的不可变 release 价格：explicit 包含分辨率，auto 的 `resolutionCode` 为 `null`，两者都不会按最终输出尺寸补扣或退款。
- `GET /api/ai/images/pricing-options` 对 legacy 返回扁平列表；声明新能力的客户端必须带 `modelCode + catalogVersion`，返回 `{ modelCode, catalogVersion, items }`。
- 生图任务失败或超时时，会写入 `source=image_refund` 流水并按原扣款批次、原到期时间返还积分。

## 当前完成范围

- 已实现（当前工作树）：API-01 至 API-14 以及 AI `size-mode-v1` 的服务端代码、数据库脚本、Swagger 强类型契约和定向自动化测试；Web/Android 旧充值、legacy 尺寸和参考图 URL 仍保留兼容期。此状态不等同于已合并、已部署或已获公网发布批准。
- 待部署配置：已有数据库按 [runbook](./docs/runbook.md#积分充值上线检查) 核对并只应用尚未执行的 `20260809-add-point-recharge.sql`、`20260812-ios-api-upgrade.sql`、`20260819-add-expiring-point-buckets.sql`、`20260820-add-user-membership-entitlements.sql`；再配置已审批法律文档、真实 StoreKit Product 映射和 Apple 秘密，并完成 Sandbox/TestFlight 验收。
- 其他未完成：更完整的博客内容模型和真实前台发布链路；公网发布仍需关闭 [安全加固 PRD](./docs/security-hardening-prd.md#42-当前未关闭的发布阻断) 中的生产环境阻断项
