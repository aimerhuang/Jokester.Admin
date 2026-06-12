# API 集成指南

本文面向前端和下游服务，说明当前后端主要接口的调用方式。

## 基础约定

- 本地默认地址：`http://localhost:5049`
- 统一前缀：`/api`
- 统一响应：

```json
{
  "code": 200,
  "message": "success",
  "data": {}
}
```

受保护接口需要请求头：

```http
Authorization: Bearer <accessToken>
```

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
  "expireAt": "2026-06-11T23:59:59.9999999",
  "availablePoints": 75
}
```

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
Authorization: Bearer <accessToken>
```

以上接口需要登录；生成和创建任务需要 `AiImage.Generate` 权限，列表查询需要 `AiImage.Page` 权限，详情查询需要 `AiImage.Record.View` 权限，删除需要 `AiImage.Record.Delete`。

生成图片会按 `ai_image_point_price` 的 `model_code + resolution_code + quality_code` 查询积分价格：

- GPT Image2 使用 `modelCode + resolutionCode + qualityCode`。
- Nano Banana2 官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality` 与画幅比例都不参与积分价格匹配。
- 扣分数量为价格表 `points * imageCount`。
- `imageCount` 为单次生成的图片数量，按各供应商官方限制校验：GPT Image2 支持 `1-10`，Nano Banana2 支持 `1-4`；超出范围后端拒绝请求。
- 积分不足或价格组合未配置时，后端拒绝创建任务，不调用上游生图服务。
- 任务生成失败或超时后，后端写入 `source=image_refund` 的返还流水。

参数选项与解析：

```http
GET /api/ai/images/parameters
GET /api/ai/images/pricing-options
POST /api/ai/images/parameters/resolve
Authorization: Bearer <accessToken>
```

`GET /api/ai/images/parameters` 会同时返回启用的分辨率、画质、画幅比例选项和 `pointPrices`。`GET /api/ai/images/pricing-options` 返回可直接用于前端展示的积分定价列表，每项包含 `modelCode`、`modelName`、`resolutionCode`、`resolutionName`、`qualityCode`、`qualityName`、`points`、`priceAmount`、`currency` 和 `sort`，其中 `points` 表示该选项单张图片消耗的积分。`resolutionCode` 支持 `1k`、`2k`、`4k`，按长边计算；其中 `4k` 的长边上限为 `3840`，同时会按供应商限制把宽高压到 `16px` 倍数且总像素不超过 `8,294,400`。例如 `4k + 1:1` 会解析为 `2880x2880`，`4k + 16:9` 会解析为 `3840x2160`。`qualityCode` 支持 `low`、`med`、`high`；`aspectRatioCode` 支持 `1:1`、`16:9`、`9:16`、`4:3`、`3:4`、`3:2`、`2:3`、`21:9`。

`resolution` 可作为 `resolutionCode` 的兼容别名；两者同时传入时优先使用 `resolution`。`modelCode` 是业务模型编码，后端会按 `ai_image_model_config.model_code` + `resolutionCode` 读取数据库配置，并把命中的 `provider_model` 原样传给上游 `model` 字段。不要假设供应商模型参数等于 `modelCode`。

GPT Image2 直接生成请求体：

```json
{
  "prompt": "一张写实风格博客封面图",
  "modelCode": "gpt-image-2",
  "imageCount": 1,
  "resolutionCode": "1k",
  "qualityCode": "med",
  "aspectRatioCode": "1:1",
  "referenceImageUrls": [
    "/ai-images/uploads/202606/reference-1.png",
    "/ai-images/uploads/202606/reference-2.png"
  ]
}
```

GPT Image2 创建后台任务请求体：

```json
{
  "siteId": 0,
  "prompt": "一张写实风格博客封面图",
  "negativePrompt": null,
  "modelCode": "gpt-image-2",
  "imageCount": 1,
  "resolutionCode": "1k",
  "qualityCode": "med",
  "aspectRatioCode": "1:1",
  "referenceImageUrls": [
    "/ai-images/uploads/202606/reference-1.png"
  ]
}
```

`referenceImageUrls` 是可选 JSON 数组，最多 6 张；前端应先调用 `POST /api/ai/images/upload` 上传参考图，拿到内部 URL 后再点击生图按钮。传参考图时后端调用上游 `/images/edits`，把内部 URL 解析为 `wwwroot` 下的本地文件，并以 multipart `image[]` 文件字段提交；不传参考图时调用 `/images/generations`。

直接生成响应 `data` 返回 `taskId`、`modelName`、`prompt`、`resolutionCode`、`qualityCode`、`aspectRatioCode`、`width`、`height`、`size`、`quality`、`mimeType`、`url`、`urls`。`imageCount` 默认 `1`，可传 `1-10` 生成多张；`url` 是首张图片地址，`urls` 是本次生成的全部图片地址数组，前端应优先使用 `urls` 展示结果。图片会保存到服务器静态目录，均为可直接访问的静态图片地址。

直接生成接口会先创建 `ai_image_task` 历史记录，再交给后台 worker 生图，并在接口侧最多等待 5 分钟返回完成结果。用户关闭网页不会取消后台任务，完成后的图片仍可通过历史记录接口按 `taskId` 找回。

列表接口支持以下筛选参数：

- `prompt`：按提示词模糊查询。
- `isFavorite`：`true` 只返回包含当前用户收藏图片的任务，`false` 只返回不包含当前用户收藏图片的任务。
- `startDate` / `endDate`：按任务创建时间筛选；只传日期的 `endDate` 会包含当天。

收藏或取消收藏单张结果图：

```json
{
  "imageUrl": "/ai-images/202606/xxx.png",
  "isFavorite": true
}
```

`imageUrl` 必须来自该任务的 `resultUrls`；取消收藏时传 `isFavorite=false`。收藏状态按当前登录用户隔离。

后台任务成功后，`GET /api/ai/images` 和 `GET /api/ai/images/{id}` 的任务数据返回 `resultUrls` 图片 URL 数组、`favoriteUrls` 当前用户已收藏的结果图 URL 数组，以及 `isFavorite` 是否存在收藏；列表接口会隐藏已写入错误信息且没有结果图片的任务，详情接口仍可按 id 查询；记录同时保存本次任务的参数编码、计算后的 `size`、供应商 `quality` 和 `reference_image_urls`。普通用户只返回/删除自己的任务，超级管理员可查看全部任务。

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
  "prompt": "一张写实风格博客封面图",
  "modelCode": "nano-banana-2",
  "resolutionCode": "1k",
  "aspectRatioCode": "auto",
  "imageCount": 1,
  "imageUrls": [
    "/ai-images/uploads/202606/reference-1.png"
  ]
}
```

`imageUrls` 是可选 JSON 数组，最多 6 张；图片 URL 必须是后端内部静态图片 URL。`imageCount` 默认 `1`，可传 `1-4` 生成多张。Nano Banana2 支持直接传 `size`，也支持传 `resolutionCode` + `aspectRatioCode`。`aspectRatioCode` 传 `auto` 时，后端不会读取参考图尺寸或推导具体画幅比例，而是把上游 `size` 参数直接设为 `auto`，由上游服务自行决定画幅；积分扣减仍按 `resolutionCode` 对应的价格档位匹配，和画幅比例无关。直接生成响应 `data` 返回 `modelName`、`prompt`、`size`、`quality`、`mimeType`、`url`、`urls`、`base64`、`dataUrl`、`isImageToImage`、`imageUrls`、`revisedPrompt`，其中 `url`/`base64`/`dataUrl` 对应首张图片，`urls` 是本次生成的全部图片地址数组。
