# AGENTS

## Repo Notes

- 项目根目录：`D:\Project\jokester.admin`
- 实际 Web API 项目目录：`D:\Project\jokester.admin\jokester.admin`
- 根目录设计文档：`后台管理系统设计书.md`
- 项目 docs：`docs/integration-guide.md`、`docs/architecture.md`、`docs/runbook.md`
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
- 刷新令牌默认使用 Redis
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

## GPT Image2 Image API Contract

- `GET /api/ai/images/parameters` returns enabled image parameter options for resolution (`1k/2k/4k`), quality (`low/med/high`), aspect ratio (`1:1`, `16:9`, `9:16`, `4:3`, `3:4`, `3:2`, `2:3`, `21:9`), and enabled `pointPrices`.
- `POST /api/ai/images/parameters/resolve` resolves `resolutionCode` + `qualityCode` + `aspectRatioCode` into `width`, `height`, `size`, and provider quality.
- Resolution tiers use long-side pixels: `1k=1024`, `2k=2048`, `4k=3840`; provider dimensions are rounded to `16px` multiples and capped at `8,294,400` total pixels. For example `4k + med + 1:1` resolves to `2880x2880`, `4k + med + 16:9` resolves to `3840x2160`, and provider quality is `medium`.
- `POST /api/ai/images/generate` directly generates one GPT Image2 image and returns `url`, `base64`, `dataUrl`, `resolutionCode`, `qualityCode`, `aspectRatioCode`, computed `width`/`height`, `size`, `quality`, and prompt metadata.
- `POST /api/ai/images/generate` and `POST /api/ai/images` accept `referenceImageUrls` as a JSON array of backend-hosted image URLs, with a maximum of 6 reference images.
- When `referenceImageUrls` is non-empty, the service calls OpenAI `/images/edits`, resolves each internal URL to a file under `wwwroot`, and sends multipart `image[]` file fields; without references it calls `/images/generations`.
- `POST /api/ai/images` creates a queued task and records `prompt`, selected parameter codes, computed `width`/`height`, `size`, provider `quality`, `imageCount`, and `referenceImageUrls` in `ai_image_task`.
- Generated PNG files are saved under `wwwroot/ai-images/{yyyyMM}/` and exposed through `app.UseStaticFiles()`.
- `GET /api/ai/images` and `GET /api/ai/images/{id}` return `resultUrls`, `errorMessage`, `createdAt`, and `updatedAt`; non-super-admin users only see or delete their own tasks.
- Existing databases need `ai_image_parameter` plus the `ai_image_task` parameter snapshot columns; keep [jokester.admin.sql](jokester.admin.sql) aligned when schema changes.

## Nano Banana2 Image API Contract

- `POST /api/ai/images/nanoBananaImage/generate` directly generates one Nano Banana2 image.
- `POST /api/ai/images/nanoBananaImage` creates a queued Nano Banana2 image task.
- The request accepts `prompt`, optional `size`, optional `quality`, and optional `imageUrls`.
- The request also accepts `resolutionCode` + `aspectRatioCode`; when `aspectRatioCode=auto`, the backend does not read reference image dimensions or calculate a concrete ratio, and sends upstream `size=auto` instead.
- Nano Banana2 billing is independent of aspect ratio: price lookup still uses the business resolution tier (`resolutionCode`) as the price table `resolution_code`, not the upstream `size=auto` value.
- Empty/missing `imageUrls` means text-to-image; non-empty `imageUrls` means image-to-image.
- `imageUrls` must be backend-hosted internal URLs, with a maximum of 6 input images.
- Configuration comes from `NanoBanana2` (`BaseUrl`, `ApiKey`, `ImageModel`, `TextToImagePath`, `ImageToImagePath`) and should be supplied via `.env` / environment variables for secrets.

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
- GPT Image2 生图接口已支持直接生成、后台任务、服务器静态图片落盘、参数编码解析、参考图 URL JSON 入参、按价格表扣积分和失败返还
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
- 本机 `localhost:6379` 目前只确认到端口可连接，尚未完成稳定的 Redis 实例读写验收。后续如果用户要求“确认 Redis 可用”，要先验证 Redis 实例本身能正常 `PING/SET/GET`，再验证登录链路中的 refresh token 和权限缓存键写入。
