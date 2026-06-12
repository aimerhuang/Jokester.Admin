# jokester.admin

基于 `.NET 10 Web API` 的后台管理系统后端，当前已接入以下能力：

- JWT 登录、刷新、登出、当前用户信息
- 用户注册赠送积分、每日签到领积分、AI 生图扣积分与失败返还
- 用户、角色、站点、菜单、日志审计接口
- 权限码校验中间件
- `SqlSugar` 数据访问
- `Mapster` DTO 映射
- `Redis` 刷新令牌与权限缓存
- Swagger UI

- [docs/integration-guide.md](./docs/integration-guide.md)：接口集成说明
- [docs/architecture.md](./docs/architecture.md)：架构和数据流说明
- [docs/runbook.md](./docs/runbook.md)：本地运行、冒烟检查和排障

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
- `OpenAI.ApiKey`
- `OpenAI.BaseUrl`
- `OpenAI.ImageModel`

当前实现不会再从代码内回退任何 JWT / MySQL / Redis 默认值。如缺少上述配置，应用会在启动时直接失败。

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

辅助脚本见 [scripts/reset-super-admin.ps1](./scripts/reset-super-admin.ps1)。

## 鉴权接口

- `POST /api/auth/login`
- `POST /api/auth/register/email-code`
- `POST /api/auth/register`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/profile`

注册邮箱验证码流程：
- 前端先调用 `POST /api/auth/register/email-code`，请求体只传 `{ "email": "user@163.com" }`
- 后端生成 6 位验证码，写入 Redis，键前缀为 `register_email_code:` 加上配置的 `Redis.InstanceName`，有效期 10 分钟，并通过 SMTP 发信
- 用户收到验证码后，前端再调用 `POST /api/auth/register`
- 注册请求体需要传 `userName`、`nickName`、`password`、`email`、`emailCode`
- 注册时后端会用同一个 `email` 去 Redis 查验证码；验证成功后创建用户并删除验证码键

登录成功返回：

- `accessToken`
- `refreshToken`
- `accessTokenExpiresAt`
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
- `POST /api/ai/images`：创建 GPT Image2 后台生图任务
- `POST /api/ai/images/nanoBananaImage/generate`：直接生成 Nano Banana2 图片
- `POST /api/ai/images/nanoBananaImage`：创建 Nano Banana2 后台生图任务
- `GET /api/ai/images`：查询 AI 生图任务记录，支持 `isFavorite`、`prompt`、`startDate`、`endDate` 筛选；列表会隐藏已写入错误信息且没有结果图片的任务
- `GET /api/ai/images/{id}`：查询 AI 生图任务详情
- `GET /api/ai/images/models`：查询启用的 AI 图片模型
- `GET /api/ai/images/parameters`：查询 GPT Image2 参数选项和积分价格表
- `GET /api/ai/images/pricing-options`：查询 AI 图片积分定价列表，每项包含模型、分辨率、画质和消耗积分
- `POST /api/ai/images/parameters/resolve`：解析 GPT Image2 参数为实际宽高和供应商画质
- `POST /api/ai/images/upload`：上传 AI 图片引用文件
- `POST /api/ai/images/{id}/favorite`：收藏或取消收藏任务中的单张结果图片
- `DELETE /api/ai/images/{id}`：删除 AI 生图任务记录

## Nano Banana2 生图接口约定

- `POST /api/ai/images/nanoBananaImage/generate`：直接生成 Nano Banana2 图片
- `POST /api/ai/images/nanoBananaImage`：创建 Nano Banana2 后台生图任务
- 请求体不传 `imageUrls` 或传空数组时执行文生图
- 请求体传 `imageUrls` 时执行图生图，图片 URL 应为后端内部静态图片 URL
- 请求体可传 `size`，也可传 `resolutionCode` + `aspectRatioCode`；`aspectRatioCode=auto` 时后端不计算具体画幅比例，直接把上游 `size` 参数设为 `auto`；积分价格仍按业务分辨率档位匹配，不依赖画幅比例
- 配置项通过 `.env` / 环境变量注入：`NanoBanana2__BaseUrl`、`NanoBanana2__ApiKey`、`NanoBanana2__ImageModel`、`NanoBanana2__TextToImagePath`、`NanoBanana2__ImageToImagePath`

说明：

- 生成接口需要 `AiImage.Generate` 权限，列表查询需要 `AiImage.Page` 权限，详情/删除分别需要 `AiImage.Record.View`、`AiImage.Record.Delete`，收藏/取消收藏需要 `AiImage.Favorite`
- 列表查询支持 `prompt` 对提示词做模糊查询，`startDate`/`endDate` 按创建时间筛选；只传日期的 `endDate` 会包含当天
- 列表查询支持 `isFavorite=true` 只返回包含当前用户收藏图片的任务，`isFavorite=false` 返回不包含当前用户收藏图片的任务
- 请求体记录 `prompt`、`resolutionCode`、`qualityCode`、`aspectRatioCode`；后台任务还记录 `imageCount`
- GPT Image2 分辨率档位按长边计算：`1k=1024`、`2k=2048`、`4k=3840`；最终尺寸会压到 `16px` 倍数且总像素不超过 `8,294,400`，例如 `4k + 1:1` 为 `2880x2880`
- 直接生成和后台任务请求体都支持 `referenceImageUrls`，前端传已上传到后端的图片 URL JSON 数组，最多 6 张
- 直接生成响应包含 `taskId` 和 `url`；用户关闭网页不会取消已入队任务，完成后仍可在历史记录中通过 `taskId` / `resultUrls` 找回图片
- 请求体传入的 `modelCode` 是业务模型编码，后端按 `ai_image_model_config.model_code` + `resolutionCode` 命中配置行，并把数据库 `provider_model` 原样作为上游 `model` 参数；图生图 `/images/edits` 也必须遵循该约束
- 生成图片保存到服务器静态目录 `wwwroot/ai-images/{yyyyMM}/`，响应返回可访问的 `url`
- 后台任务完成后，`GET /api/ai/images` 和 `GET /api/ai/images/{id}` 返回 `resultUrls` 图片 URL 数组、`favoriteUrls` 当前用户已收藏的结果图 URL 数组，以及 `isFavorite` 是否存在收藏；列表接口会隐藏已写入错误信息且 `resultUrls` 为空的任务，详情接口仍可按 id 查询；`ai_image_task.result_urls` 保存图片 URL 的 JSON 数组，`ai_image_task.reference_image_urls` 保存参考图 URL 的 JSON 数组
- 收藏接口请求体示例：`{ "imageUrl": "/ai-images/202606/xxx.png", "isFavorite": true }`；取消收藏传 `isFavorite=false`，`imageUrl` 必须属于该任务的 `resultUrls`


- 启动时可能出现 `DataProtection` 的 DPAPI / 文件权限告警
  - 当前不影响 API 启动与本地联调
  - 若要彻底消除，需要单独调整密钥持久化目录或开发环境 DataProtection 策略
- 本机 `localhost:6379` 当前只确认到端口可连接，尚未完成稳定的 Redis `PING/SET/GET` 实写验证
  - 现阶段只能确认“项目已接入本地 Redis 配置”，不能确认“本地 Redis 实例已经可正常读写”
  - 如果要做完整验收，需要先把本机 Redis 实例自身校验清楚，再确认登录后 refresh token / 权限缓存键写入
- 设计书存在明显编码异常，实际实现应以当前代码和接口为准

## 积分与 AI 生图接口约定

- `GET /api/points/balance`：查询当前用户积分余额和今日签到状态。
- `POST /api/points/sign-in`：每日签到领取 25 积分；同一自然日只能领取一次。
- 注册成功自动赠送 50 积分。
- 签到积分当天有效；第二天调用积分查询、签到或生图扣分时会清理上一日未使用签到积分。
- AI 生图按 `ai_image_point_price` 的 `model_code + resolution_code + quality_code` 扣积分，扣分数量为 `points * imageCount`。
- `GET /api/ai/images/pricing-options`：返回可直接用于前端展示的积分定价列表，每项包含 `modelCode`、`modelName`、`resolutionCode`、`resolutionName`、`qualityCode`、`qualityName` 和 `points`。
- GPT Image2 价格匹配使用 `modelCode + resolutionCode + qualityCode`；Nano Banana2 官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality` 与画幅比例都不参与积分价格匹配。
- 生图任务失败或超时时，会写入 `source=image_refund` 的积分返还流水。

## 当前完成范围

- 已完成：认证、权限链路、用户/角色/站点/菜单管理、日志审计、管理员初始化、Redis 缓存、Swagger、博客文章、媒体、评论与仪表盘统计接口、积分余额/签到接口、GPT Image2 与 Nano Banana2 生图任务及积分扣减
- 未完成：更完整的博客内容模型和真实前台发布链路
