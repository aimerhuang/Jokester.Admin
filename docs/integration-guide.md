# API 集成指南

本文面向前端和下游服务，说明当前后端主要接口的调用方式。

## 基础约定

- 本地默认地址：`http://localhost:5049`
- 统一前缀：`/api`
- 统一响应：

```json
{
  "code": 0,
  "message": "success",
  "requestId": "0HN...",
  "data": {}
}
```

失败响应不复用数值业务码，统一返回机器可读字符串：

```json
{
  "code": "VALIDATION_ERROR",
  "message": "The request is invalid.",
  "requestId": "0HN...",
  "details": null
}
```

- 所有移动端时间字段使用 ISO 8601 UTC，序列化结果带 `Z`。
- 分页 `data` 统一包含 `pageIndex`、`pageSize`、`items`、`total`、`hasMore`。
- 限流返回 HTTP 429、`Retry-After` 响应头、`RATE_LIMITED` 和 `details.retryAfterSeconds`。
- Swagger 为移动端操作声明强类型成功响应以及 `400/401/403/409/412/422/429/500/503` 错误模型。

受保护接口需要请求头：

```http
Authorization: Bearer <accessToken>
```

## 认证与注册

注册邮箱验证码流程：

```http
GET /api/blog/comments/captcha

POST /api/auth/register/email-code
Content-Type: application/json

{
  "email": "<email>",
  "captchaId": "<captchaId>",
  "captchaAnswer": "<图片中的 6 位字符>"
}
```

- 图片验证码由 `GET /api/blog/comments/captcha` 返回，包含 `captchaId`、`imageBase64`、`mimeType` 和 `expiresInSeconds`；5 分钟过期并在校验时一次性消费。
- 邮件验证码为 6 位数字，按规范化邮箱写入 Redis，10 分钟过期。发送成功响应为 `data.retryAfterSeconds=60`；触发邮箱或 IP 限流时返回 HTTP 429、`Retry-After` 和 `details.retryAfterSeconds`，客户端以受限响应为准。
- 注册前按真实客户端调用 `GET /api/legal/documents/current?platform=<ios|android|web>&locale=zh-CN` 获取当前法律版本。隐私政策或服务条款缺失时服务端返回 503，客户端应暂停注册而不能自行填充版本。AI 处理告知可以独立停用；此时 `aiProcessingNotice` 为 `null`，客户端不显示 AI 授权提示，也不能因此阻断注册。
- 收到邮件后调用 `POST /api/auth/register`，除账号字段外，还要传同一个 `email`、`emailCode`，以及已接受的隐私政策/服务条款布尔值与版本号；版本不是当前生效版本时后端拒绝注册。
- 注册 `userName` 必须为 6-20 位 ASCII 字母和数字组合，且至少各含一个；空格、下划线及其他特殊字符不允许。
- 登录连续失败 3 次后，再次调用 `POST /api/auth/login` 需要附带新的 `captchaId` 与 `captchaAnswer`；缺少或错误时返回 `CAPTCHA_REQUIRED`。累计失败 5 次后，账号与 IP 组合锁定 15 分钟并返回 `LOGIN_LOCKED`。

登录和刷新成功都返回 `sessionId`、`accessToken`、`refreshToken`、`accessTokenExpiresAt` 和 `refreshTokenExpiresAt`。Refresh Token 只按 SHA-256 哈希存储并单次轮换；同一个旧 Token 在 10 秒并发宽限内重试会拿到同一轮换结果，超窗重放会撤销该 session family。客户端按以下 401 错误码处理：

- `ACCESS_TOKEN_EXPIRED`：尝试刷新。
- `REFRESH_TOKEN_EXPIRED`：清理本地会话并重新登录。
- `SESSION_REVOKED`：清理本地会话并重新登录。

### 法律文档、AI 授权和账户删除

```http
GET /api/legal/documents/current?platform=ios&locale=zh-CN
GET /api/users/me/consents
PUT /api/users/me/consents/ai-processing
POST /api/auth/account-deletion/requests
GET /api/auth/account-deletion/requests/current
DELETE /api/auth/account-deletion/requests/{requestId}
```

- 法律文档与移动端配置接口公开，其余接口需要 Bearer Token。Web 使用同一路由并传 `platform=web`。
- AI 授权请求传 `accepted`、当前 `documentVersion`、所选模型实际使用的 `providerCodes` 和真实 `clientPlatform`，不能固定传 `ios`。授权写入后，后续生图按该用户最新 AI 授权记录的平台解析对应平台或 `all` 告知。AI 处理告知未启用时，客户端不显示对应授权提示，所有第三方 AI 生图在 Provider 调用、Redis 准入和积分预留前返回 HTTP 503、`SERVICE_UNAVAILABLE`；告知已启用但缺少当前 Provider 授权时返回 HTTP 412、`AI_CONSENT_REQUIRED`，并在 `details` 返回文档版本和 URL。
- 删除申请传 `currentPassword`、`confirmation="DELETE"`、UUID `clientRequestId` 和可选 `reason`。创建后立即撤销 Refresh Token 会话；重复同 key/同 payload 返回原申请，同 key/不同 payload 返回 409。
- 删除任务按计划在后台执行并可重试；私有 AI 文件和用户数据删除，必须保留的 Apple/积分财务记录匿名化，完成邮件发送失败也进入重试。

公开移动端配置：

```http
GET /api/mobile/config?platform=ios&appVersion=1.3.0&locale=zh-CN
```

响应包含最低/最新版本、维护模式、运行时可用功能开关和当前法律文档版本。该接口只下发数据，不下发或改变原生可执行代码。

## 公开站点列表

```http
GET /api/sites/site_code
```

该接口不需要登录授权，返回所有未逻辑删除站点，用于前端获取站点编码和启用状态。

响应字段：

- `id`
- `siteName`
- `siteCode`
- `domain`
- `status`：`1` 启用，`0` 禁用
- `description`
- `sort`

示例：

```bash
curl http://localhost:5049/api/sites/site_code
```

## 博客聚合接口

```http
GET /api/blog/summary
GET /api/blog/titles/latest?n=10
GET /api/blog/comments/latest?n=10
GET /api/blog/site/info
```

- `GET /api/blog/summary` 返回文章总数、评论总数、浏览量。
- `GET /api/blog/titles/latest?n=10` 返回最新 `n` 条已发布文章标题，默认 `10`，最大 `50`。
- `GET /api/blog/comments/latest?n=10` 返回最新 `n` 条已通过评论，默认 `10`，最大 `50`。
- `GET /api/blog/site/info` 返回建站时间、运行天数、评论总数、文章总数和浏览量；建站时间来自 `blog_site_config`。

## 博客分类

```http
GET /api/blog/categories
POST /api/blog/categories
PUT /api/blog/categories/{id}
DELETE /api/blog/categories/{id}
```

- 分类列表公开读取。
- 新增、编辑、删除需要登录并拥有对应的 `Blog.Category.*` 权限。
- 删除为软删除，文章历史引用的分类记录会保留。
- 初始化默认分类为：`技术教程`、`日常笔记`、`好物分享`。

## 博客文章

```http
GET /api/blog/articles
GET /api/blog/articles/{id}
POST /api/blog/articles
PUT /api/blog/articles/{id}
DELETE /api/blog/articles/{id}
POST /api/blog/articles/{id}/publish
```

当前博客文章接口固定绑定 `siteCode=blog`，调用方不需要传 `siteId`。

文章列表和详情返回：

- `coverUrl`：文章封面图地址，来自 `blog_article.cover_url`
- `thumbnailUrl`：列表缩略图地址，优先级为：
  1. `coverUrl`
  2. `blog_article_media` 关联到的第一张 `blog_media.url`
  3. 正文 HTML 中第一张 `<img src="...">`

创建或更新文章时，后端会解析正文 HTML 中的 `<img src="...">`，将命中 `blog_media.url` 的图片同步写入 `blog_article_media`。

前端显示正文图片时，应将文章 `content` 作为 HTML 渲染，并确保内容来源可信或经过清洗，避免 XSS。

## 博客媒体

```http
POST /api/blog/media/upload
GET /api/blog/media
DELETE /api/blog/media/{id}
```

上传返回的 `url` 可插入文章正文 `<img src="...">` 中。只有正文中的图片 URL 能匹配 `blog_media.url` 时，才会建立 `blog_article_media` 关联。

## 博客评论公开接口

```http
GET /api/blog/comments/captcha
POST /api/blog/comments/public
GET /api/blog/comments/public
```

- 提交评论前先获取验证码。
- 验证码接口返回 `captchaId`、`imageBase64`、`mimeType`、`expiresInSeconds`。
- 前端可用 `data:${mimeType};base64,${imageBase64}` 展示验证码图片。
- 提交评论时传 `captchaId` 和用户输入的 `captchaAnswer`。
- 公开评论列表只返回 `status=1` 已通过评论。

## 积分接口

```http
GET /api/points/balance
GET /api/points/details?pageIndex=1&pageSize=20
POST /api/points/sign-in
Authorization: Bearer <accessToken>
```

- 注册成功后后端自动赠送 50 积分，并写入 `sys_user_point_detail`，来源为 `register`。
- 每日签到接口 `POST /api/points/sign-in` 每个自然日只能成功一次，成功后领取 25 积分。
- 签到积分当天有效；第二天调用积分查询、签到或生图扣分时，后端会把上一日未使用签到积分写为 `source=sign_in_expire` 的过期扣减流水。

查询余额响应 `data`：

```json
{
  "availablePoints": 75,
  "hasSignedInToday": true,
  "todaySignInPoints": 25
}
```

签到成功响应 `data`：

```json
{
  "points": 25,
  "expireAt": "2026-06-11T15:59:59.9999999Z",
  "availablePoints": 75
}
```

### 积分充值与兑换

```http
GET /api/points/recharge/packages
GET /api/points/recharge/packages?platform=ios
POST /api/points/recharge/orders
POST /api/points/recharge/redeem
POST /api/points/recharge/apple/transactions
POST /api/points/recharge/admin/codes
Authorization: Bearer <accessToken>
```

- 套餐接口按当前用户返回首充资格和实际可得积分。
- `platform=ios` 只返回已映射的 StoreKit 商品，`purchaseMethod=apple_iap`，包含 `appleProductId`/`appleProductType`，不返回外部购买 URL。响应 `data` 仍是数组，以兼容现有 Web/Android 客户端。
- 下单接口只创建待支付订单；`purchaseUrl` 来自套餐表配置，未配置时返回 `null`。
- iOS 不调用外部订单接口。购买后向 Apple 交易接口提交 `transactionId`、`productId`、`appAccountToken`，并在 `Idempotency-Key` 请求头传 UUID；服务端从 App Store Server API 获取并验证交易，履约成功后客户端才调用 StoreKit `finish()`。
- 兑换成功后原子增加余额并写入 `source=recharge` 积分流水；兑换码区分大小写且只能使用一次。
- 兑换按用户和 IP 共享限流。成功、无效码、冲突及 `429 RATE_LIMITED` 都写入最小化操作审计结果（HTTP 状态、成功标记和稳定错误码），不会记录明文兑换码。
- 管理员签发接口仅限超级管理员，可在 `packageCode` 套餐模式和 `points` 自定义积分模式中二选一；明文兑换码只在签发响应中返回一次。
- 签发时服务端会在事务内重新锁定并核验当前用户仍是启用的超级管理员；Redis 同时按用户和 IP 限流。
- Apple 退款/撤销由公开的 App Store Server Notifications V2 入口验签接收。余额不足以扣回时记录 `apple_iap_debt`，未结清债务会阻止继续生图。

请求、响应、订单状态和 `purchase_url` 配置详见
[point-recharge.md](./point-recharge.md)。

## AI 生图

统一路由：

```http
GET /api/ai/images
GET /api/ai/images/models
GET /api/ai/images/parameters
GET /api/ai/images/pricing-options
POST /api/ai/images/parameters/resolve
GET /api/ai/images/{id}
POST /api/ai/images/generate
POST /api/ai/images
POST /api/ai/images/{id}/favorite
DELETE /api/ai/images/{id}
POST /api/ai/images/nanoBananaImage/generate
POST /api/ai/images/nanoBananaImage
POST /api/ai/images/upload
GET /api/assets/{assetId}/content
GET /api/assets/{assetId}/thumbnail
DELETE /api/assets/{assetId}
Authorization: Bearer <accessToken>
```

以上接口需要登录；生成和创建任务需要 `AiImage.Generate` 权限，列表查询需要 `AiImage.Page` 权限，详情查询需要 `AiImage.Record.View` 权限，删除需要 `AiImage.Record.Delete`。

上传返回的 `assetId` 由服务端绑定当前用户。内容、缩略图、引用和删除都会校验所有权；跨用户操作与不存在统一返回 404。Asset 删除会立即软删除数据库记录并清理原图和缩略图，重复删除对原所有者幂等。`DELETE /api/ai/images/{id}` 只软删除任务记录，不级联删除上传 Asset 或生成文件；账户删除流程会清理该用户全部私有媒体。

移动端统一使用 `POST /api/ai/images` 创建任务，并始终传 `modelCode`。后端从 `ai_image_model_config.provider` 判断 OpenAI Images 或 Gemini Images 协议；模型编码无需包含 `nano` 等命名约定。`/nanoBananaImage` 仅在兼容期保留并已在 Swagger 标记 Deprecated。`GET /api/ai/images/models` 返回 `providerCode`、参考图/画质能力、最大参考图数量、支持的图片数量及参数选项。

生成图片会按 `ai_image_point_price` 的 `model_code + resolution_code + quality_code` 查询积分价格：

- GPT Image2 使用 `modelCode + resolutionCode + qualityCode`。
- Nano Banana2 官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality` 与画幅比例都不参与积分价格匹配。
- 扣分数量为价格表 `points * imageCount`。
- `imageCount` 为单次生成的图片数量，按各供应商官方限制校验：GPT Image2 支持 `1-10`，Nano Banana2 支持 `1-4`；超出范围后端拒绝请求。
- 积分不足或价格组合未配置时，后端拒绝创建任务，不调用上游生图服务。
- 四个生成/创建接口都必须传 `idempotencyKey`（8-128 个非控制字符）。同一用户使用相同 key 和相同请求时返回原任务（多图 GPT 请求返回原任务集合）且不重复扣分；同 key 不同请求返回 HTTP 409。
- 任务与积分预留在同一数据库事务提交；任务成功确认预留，失败或超时只对未完成图片写入一次 `source=image_refund` 流水。
- Redis 原子限制用户每日图片/积分额度、单用户活动任务数和全局任务积压；Redis 或成本控制状态不可用时拒绝新任务，不调用 Provider。

参数选项与解析：

```http
GET /api/ai/images/parameters
GET /api/ai/images/pricing-options
POST /api/ai/images/parameters/resolve
Authorization: Bearer <accessToken>
```

`GET /api/ai/images/parameters` 会同时返回启用的分辨率、画质、画幅比例选项和 `pointPrices`。`GET /api/ai/images/pricing-options` 返回可直接用于前端展示的积分定价列表，每项包含 `modelCode`、`modelName`、`resolutionCode`、`resolutionName`、`qualityCode`、`qualityName`、`points`、`priceAmount`、`currency` 和 `sort`，其中 `points` 表示该选项单张图片消耗的积分。`resolutionCode` 支持 `1k`、`2k`、`4k`，按长边计算；其中 `4k` 的长边上限为 `3840`，同时会按供应商限制把宽高压到 `16px` 倍数且总像素不超过 `8,294,400`。例如 `4k + 1:1` 会解析为 `2880x2880`，`4k + 16:9` 会解析为 `3840x2160`。`qualityCode` 支持 `low`、`med`、`high`；`aspectRatioCode` 支持 `1:1`、`16:9`、`9:16`、`4:3`、`3:4`、`3:2`、`2:3`、`21:9`。

`resolution` 可作为 `resolutionCode` 的兼容别名；两者同时传入时优先使用 `resolution`。`modelCode` 是业务模型编码，后端会按 `model_code + resolution_code + route_role` 读取数据库路由，并把当前路由的 `provider_model` 原样传给上游 `model` 字段。不要假设主、备供应商模型参数彼此相同或等于 `modelCode`。

GPT Image2 直接生成请求体：

```json
{
  "idempotencyKey": "img-20260808-000001",
  "prompt": "一张写实风格博客封面图",
  "modelCode": "gpt-image-2",
  "imageCount": 1,
  "resolutionCode": "1k",
  "qualityCode": "med",
  "aspectRatioCode": "1:1",
  "referenceAssetIds": [
    "AST20260812153000A1B2C3D4E5F6A7B8C"
  ]
}
```

GPT Image2 创建后台任务请求体：

```json
{
  "idempotencyKey": "img-20260808-000002",
  "siteId": 0,
  "prompt": "一张写实风格博客封面图",
  "negativePrompt": null,
  "modelCode": "gpt-image-2",
  "imageCount": 1,
  "resolutionCode": "1k",
  "qualityCode": "med",
  "aspectRatioCode": "1:1",
  "referenceAssetIds": [
    "AST20260812153000A1B2C3D4E5F6A7B8C"
  ]
}
```

新版客户端使用 `referenceAssetIds`（最多 6 个）和可选 `maskAssetId`。先调用 `POST /api/ai/images/upload`，响应会返回不可枚举的 `assetId`、同源 `url`/`thumbnailUrl`、真实 MIME、宽高、字节数、`metadataStripped=true` 和 UTC 创建时间。服务端检查 magic bytes、解码尺寸/像素/帧数并重新编码以剥离元数据：HEIC/HEIF 主图规范化为 PNG，JPEG、PNG、WebP 保持各自格式；512px 缩略图统一为 WebP。

兼容期仍接受当前用户私有 `/api/media/ai/...` 的 `referenceImageUrls` / `maskImageUrl`，但不接受任意远程 URL。Asset 不存在和跨用户 Asset 对普通调用方都表现为 404，避免泄露资源是否存在。

直接生成响应 `data` 返回 `taskId`、`taskIds`、`modelName`、`prompt`、`resolutionCode`、`qualityCode`、`aspectRatioCode`、`width`、`height`、`size`、`quality`、`mimeType`、`url`、`urls`。`imageCount` 默认 `1`，可传 `1-10` 生成多张；`taskId` 是首个任务 id，`taskIds` 是全部单图任务 id，`url` 是首张图片地址，`urls` 是本次生成的全部图片地址数组。URL 是鉴权下载地址，Web 前端应携带 Bearer Token 请求 Blob 后展示，不能把它当作匿名静态 `<img src>`。

直接生成接口会先创建 `ai_image_task` 历史记录，再交给后台 worker 生图，并在接口侧最多等待 5 分钟返回完成结果。用户关闭网页不会取消后台任务，完成后的图片仍可通过历史记录接口按 `taskId` / `taskIds` 找回。

GPT 多图按请求 `imageCount` 拆成同等数量的任务，每条 `ai_image_task.image_count` 固定为 `1`，并由 worker 在全局 Provider 并发限制内同时处理。创建接口返回 `id` 和 `ids`：`id` 是首个任务 id，`ids` 包含本批全部任务 id。整批任务插入、总积分扣减和每个任务的 `image:{taskId}:reserve` 流水在同一数据库事务提交；同一幂等键重试返回原 `ids`。

GPT 主备上游都来自 `ai_image_model_config`。后端按 `model_code + resolution_code` 先读取启用的 `route_role=primary`，失败后切换到同一槽位启用的 `route_role=fallback`；两条记录分别提供自己的 `provider_model`、地址、Key 和请求路径。禁用主记录即可强制直连备用记录，切换不需要重启服务。

列表接口支持以下筛选参数：

- `prompt`：按提示词模糊查询。
- `isFavorite`：`true` 只返回包含当前用户收藏图片的任务，`false` 只返回不包含当前用户收藏图片的任务。
- `startDate` / `endDate`：按任务创建时间筛选；只传日期的 `endDate` 会包含当天。

收藏或取消收藏单张结果图：

```json
{
  "imageUrl": "/api/media/ai/42/202608/xxx.png",
  "isFavorite": true
}
```

`imageUrl` 必须来自该任务的 `resultUrls`；取消收藏时传 `isFavorite=false`。收藏状态按当前登录用户隔离。

后台任务成功后，`GET /api/ai/images` 和 `GET /api/ai/images/{id}` 的任务数据返回 `resultUrls`、`favoriteUrls`、`isFavorite`、`completedImageCount`、`pointCost` 与 `billingStatus`。`status` 是 `queued/processing/succeeded/failed/cancelled`，兼容字段 `statusCode` 仍为 `0/3/1/2`；同时返回 `progress`、`pollAfterSeconds`、`expiresAt` 和 UTC 时间。失败任务也会出现在列表中。普通用户只返回/删除自己的任务，活动任务不能删除；超级管理员可查看全部任务。

Nano Banana2 直接生成或创建后台任务：

```http
POST /api/ai/images/nanoBananaImage/generate
POST /api/ai/images/nanoBananaImage
Authorization: Bearer <accessToken>
```

不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图。

请求体：

```json
{
  "idempotencyKey": "nano-20260808-000001",
  "prompt": "一张写实风格博客封面图",
  "modelCode": "nano-banana-2",
  "resolutionCode": "1k",
  "aspectRatioCode": "auto",
  "imageCount": 1,
  "imageUrls": [
    "/api/media/ai/42/202608/reference-1.png"
  ]
}
```

`imageUrls` 是可选 JSON 数组，最多 6 张；图片 URL 必须是当前用户可访问的 `/api/media/ai/...` 私有 URL。`imageCount` 默认 `1`，可传 `1-4` 生成多张。Nano Banana2 支持直接传 `size`，也支持传 `resolutionCode` + `aspectRatioCode`。`aspectRatioCode` 传 `auto` 时，后端不会读取参考图尺寸或推导具体画幅比例，而是把上游 `size` 参数直接设为 `auto`，由上游服务自行决定画幅；积分扣减仍按 `resolutionCode` 对应的价格档位匹配，和画幅比例无关。直接生成也先创建同一个持久化后台任务并等待结果，响应 `data` 返回 `taskId`、`modelName`、`prompt`、`size`、`quality`、`mimeType`、`url`、`urls`、`base64`、`dataUrl`、`isImageToImage`、`imageUrls`、`revisedPrompt`。

Nano Banana2 没有单独的配置段，也不参与 GPT 主备切换。文生图和图生图都直接使用 `ai_image_model_config` 中当前启用的 `route_role=primary` 渠道。
