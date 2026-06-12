# 架构说明

## 技术栈

- `.NET 10 Web API`
- `SqlSugar`
- `Mapster`
- `StackExchange.Redis`
- `MySQL 8`
- `Swagger`

## 分层

- `Controllers`：HTTP 路由与鉴权声明
- `Application`：业务服务、DTO、当前用户、权限缓存抽象
- `Domain`：SqlSugar 实体
- `Infrastructure`：数据库、Redis、JWT、审计日志等基础设施

## 认证与权限

系统使用 JWT + RBAC + 站点维度：

1. 登录返回 `accessToken`、`refreshToken`、用户信息、站点列表和权限码。
2. 受保护接口通过 `[Authorize]` 校验登录。
3. 需要具体权限的接口通过 `[Permission("...")]` 声明权限码。
4. 超级管理员绕过权限检查。
5. 普通用户权限优先从 Redis 读取，失败或未命中时回退数据库。

## 站点模型

`sys_site.site_code` 是站点业务编码。后台仍保留多站点模型，但博客接口当前固定绑定 `siteCode=blog`：

- 文章和媒体请求不要求调用方传 `siteId`
- 服务层解析 `blog` 站点 ID 后写入和过滤业务数据
- `blog_article.site_id`、`blog_media.site_id` 保留，供数据归属和后续多站点扩展使用

公开站点接口 `GET /api/sites/site_code` 不需要登录，用于前端获取所有站点和 `status` 状态。

## 博客文章缩略图数据流

相关表：

- `blog_article`：文章主体，`cover_url` 保存显式封面图
- `blog_media`：上传媒体资源，`url` 是可访问地址
- `blog_article_media`：文章与媒体的引用关系

创建或更新文章时：

1. 后端保存文章内容。
2. 解析正文 HTML 中的 `<img src="...">`。
3. 用解析出的 URL 匹配 `blog_media.url`。
4. 删除该文章旧的 `blog_article_media` 记录。
5. 将匹配到的媒体按正文出现顺序写入 `blog_article_media`。

查询文章列表或详情时，`thumbnailUrl` 的计算优先级：

1. `blog_article.cover_url`
2. `blog_article_media` 关联到的第一张 `blog_media.url`
3. 正文 HTML 中第一张 `<img src="...">`

因此：

- 列表缩略图不要求前端解析正文。
- 文章正文图片仍由前端按 HTML 渲染 `content` 展示。
- 如果正文图片不是来自已上传媒体，接口仍可通过 HTML 兜底返回第一张图片作为缩略图，但不会写入 `blog_article_media`。

## AI 生图与积分数据流

相关表：

- `ai_image_task`：后台生图任务，保存 `prompt`、参数编码、`size`、`quality`、`image_count`、`reference_image_urls`、`result_urls`、`status`
- `ai_image_point_price`：按 `model_code + resolution_code + quality_code` 定义出图积分价格
- `sys_user.point_balance`：用户当前可用积分余额
- `sys_user_point_detail`：积分流水，记录注册赠送、签到赠送、出图扣减、过期清理和失败返还

积分规则：

1. 用户注册成功后获得 50 积分，写入 `source=register` 的赠送流水。
2. 登录用户可调用 `POST /api/points/sign-in` 每日签到一次，领取 25 积分。
3. 签到积分当天有效；第二天在查询余额、再次签到或出图扣分时，会把上一日未使用部分写为 `source=sign_in_expire` 的过期扣减流水。
4. 创建生图任务前，服务按价格表组合计算 `points * imageCount`，余额不足或价格缺失时拒绝创建任务。
5. 任务创建后立即扣减积分并写入 `source=image_generate` 流水；后台生成失败或超时时，写入 `source=image_refund` 流水返还积分。

GPT Image2 流程：

1. 后端校验 `prompt`、分辨率编码、质量编码、画幅比例编码和最多 6 个 `referenceImageUrls`。
2. 解析 `modelCode + resolutionCode` 到 `ai_image_model_config`，并通过 `ai_image_point_price` 查询扣分价格。
3. 创建 `ai_image_task` 历史记录，扣除积分，然后把任务 id 写入后台队列。
4. `AiImageTaskWorker` 在独立后台任务中调用外部生图服务，单个任务超时上限为 5 分钟。
5. 接口侧最多等待 5 分钟轮询任务结果；完成后返回 `taskId` 和静态图片 `url`。
6. 如果用户关闭网页，后台任务仍继续执行，完成后把静态图片 URL 写入 `result_urls`，历史记录接口仍可查询。

Nano Banana2 流程：

1. `POST /api/ai/images/nanoBananaImage/generate` 直接同步生成一张图；`POST /api/ai/images/nanoBananaImage` 创建后台任务。
2. 价格表匹配使用 `model_code = modelCode`、业务分辨率档位作为 `resolution_code`；Nano Banana2 官方无 `quality` 参数，`quality_code` 不参与匹配（库中存 `''` 或 `NULL` 都不影响），画幅比例同样不参与积分价格匹配。
3. 不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图。
4. 当请求 `aspectRatioCode=auto` 时，后端不读取参考图尺寸，也不计算具体画幅比例；上游请求的 `size` 直接传 `auto`，由上游服务自行决定画幅。

后台任务流程：

1. 创建 `ai_image_task`，记录 `prompt`、参数编码、计算后的宽高、`size`、供应商 `quality`、`image_count`、`reference_image_urls`，状态为待处理。
2. 后台队列按任务数量逐张调用外部生图服务，并复用任务中的参考图 URL。
3. 每张图落盘后把静态图片 URL 写入 `result_urls` JSON 数组，并通过任务列表和详情接口返回 `resultUrls`。
4. 任务成功后状态改为成功；失败时写入 `error_message`、返还本次任务积分。任务列表会隐藏已写入错误信息且没有结果图片的记录，详情接口仍可按 id 返回 `errorMessage`。
5. 普通用户只能查询或删除自己的任务；超级管理员可查看全部任务。


## 主要路由

### 公开接口

- `POST /api/auth/login`
- `POST /api/auth/register/email-code`
- `POST /api/auth/register`
- `POST /api/auth/refresh`
- `GET /api/sites/site_code`
- `GET /api/blog/articles`
- `GET /api/blog/articles/{id}`
- `GET /api/blog/comments/captcha`
- `POST /api/blog/comments/public`
- `GET /api/blog/comments/public`

`GET /api/blog/comments/captcha` 返回 SVG 图片验证码的 Base64 数据，答案存入 Redis，校验后一次性失效。

### 后台接口

- `GET/POST/PUT/DELETE /api/users`
- `GET /api/users/{id}/menus/tree`
- `PUT /api/users/{id}/menus`
- `GET/POST/PUT/DELETE /api/roles`
- `GET/POST/PUT/DELETE /api/sites`
- `GET/POST/PUT/DELETE /api/menus`
- `GET/POST/PUT/DELETE /api/blog/articles`
- `GET/POST/DELETE /api/blog/media`
- `GET/PUT/DELETE /api/blog/comments`
- `GET /api/blog/dashboard/stats`
- `GET/POST/DELETE /api/ai/images`
- `GET /api/ai/images/models`
- `GET /api/ai/images/parameters`
- `POST /api/ai/images/parameters/resolve`
- `POST /api/ai/images/generate`
- `POST /api/ai/images/nanoBananaImage/generate`
- `POST /api/ai/images/nanoBananaImage`
- `POST /api/ai/images/upload`
- `GET /api/points/balance`
- `POST /api/points/sign-in`
- `GET/DELETE /api/logs/login`
- `GET/DELETE /api/logs/operation`
