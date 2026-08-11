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
- `blog_category.site_id` 用于按站点隔离分类配置
- `blog_site_config.build_date` 存放建站时间，网站信息接口据此计算运行天数

公开站点接口 `GET /api/sites/site_code` 不需要登录，用于前端获取所有站点和 `status` 状态。

## 博客首页与分类数据流

相关表：

- `blog_article`：文章主体，`cover_url` 保存显式封面图
- `blog_comment`：评论主体，公开首页只读取已通过评论
- `blog_category`：博客分类，支持软删除和按站点配置
- `blog_site_config`：博客站点配置，保存建站时间
- `blog_media`：上传媒体资源，`url` 是可访问地址
- `blog_article_media`：文章与媒体的引用关系

首页聚合接口：

1. `GET /api/blog/summary` 汇总文章数、评论数和浏览量。
2. `GET /api/blog/titles/latest?n=10` 返回最新 `n` 条已发布文章标题，并带上分类名称。
3. `GET /api/blog/comments/latest?n=10` 返回最新 `n` 条已通过评论，并关联文章标题。
4. `GET /api/blog/site/info` 读取建站时间并汇总文章、评论和浏览量。

分类 CRUD：

1. 分类按站点隔离。
2. 删除仅做逻辑删除，避免历史文章分类丢失。
3. 创建或更新时由服务层校验同站点分类名唯一。

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

## 提示词库数据流

提示词库固定读取 YouMind 官方仓库提交
`589f148fd605574569580665403311c5eb88143e` 的 `README_zh.md`。同步器使用 Markdown AST
解析条目，以详情链接的 `?id=` CMS ID 作为稳定标识，只发布标题、描述和正文均含实质中文内容、
且封面完整的前 126 条记录。

同步使用 staging 和原子快照切换：文本与全部封面校验成功后才激活新快照；内容未变化时记录
`not_modified`；任何下载、数量或解析异常都保留当前快照。图片写入发布目录之外的持久目录，
由 Nginx 的 `/prompt-images/` alias 提供，浏览器不直接访问 YouMind 资源。旧快照图片至少保留
7 天，只清理不被当前或保留快照引用的孤立文件。

上游 Markdown 和图片下载仅允许配置的 HTTPS 地址与主机，不跟随到非白名单域名，并限制响应大小、
超时和并发；图片按签名与可解码性校验。数据来源、许可和图片权利边界见
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)，部署与回滚步骤见 [runbook.md](./runbook.md)。

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
5. 任务创建时预留积分并写入 `source=image_generate` 流水；后台生成失败或超时时，仅对未完成图片写入一次 `source=image_refund` 流水。

GPT Image2 流程：

1. 后端校验 `prompt`、分辨率编码、质量编码、画幅比例编码和最多 6 个 `referenceImageUrls`。
2. 按 `modelCode + resolutionCode + route_role` 从 `ai_image_model_config` 解析启用的主、备路由，并通过 `ai_image_point_price` 查询扣分价格。
3. 使用 Redis Lua 原子占用幂等键、用户每日额度、用户活动任务位和全局积压位；成本熔断打开时拒绝创建。
4. 按 `imageCount` 创建同等数量的单图 `ai_image_task`（每条 `image_count=1`），在同一 MySQL 事务中锁定用户余额、扣除整批积分并写入逐任务唯一预留流水，然后把全部任务 id 写入后台队列。
5. `AiImageTaskWorker` 原子把每个任务从 `status=0` 认领为 `status=3`，并通过 Redis 租约限制全局 Provider 并发；同一批任务可并发执行。GPT 先调用启用的 `primary` 路由，失败后切换到同槽位启用的 `fallback` 路由；禁用主路由可强制直连备用。
6. 成功时把任务和 `billing_status` 确认为完成；失败时在同一事务内按未完成图片数退款并写入唯一退款流水，重复回调不会二次退款。
7. 接口侧最多等待 5 分钟并行轮询本批任务结果；完成后返回 `taskId`、`taskIds` 和 `/api/media/ai/...` 鉴权图片 URL。
8. 如果用户关闭网页，后台任务仍继续执行，完成后把私有图片 URL 写入 `result_urls`，历史记录接口仍可查询。

Nano Banana2 流程：

1. `POST /api/ai/images/nanoBananaImage/generate` 与 `POST /api/ai/images/nanoBananaImage` 都先走统一的持久化任务、幂等、额度和积分预留状态机；前者等待任务并返回图片内容，后者立即返回任务 id。
2. 价格表匹配使用 `model_code = modelCode`、业务分辨率档位作为 `resolution_code`；Nano Banana2 官方无 `quality` 参数，`quality_code` 不参与匹配（库中存 `''` 或 `NULL` 都不影响），画幅比例同样不参与积分价格匹配。
3. 不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图。
4. 当请求 `aspectRatioCode=auto` 时，后端不读取参考图尺寸，也不计算具体画幅比例；上游请求的 `size` 直接传 `auto`，由上游服务自行决定画幅。

后台任务流程：

1. 创建 `ai_image_task`，记录参数快照、幂等摘要、积分快照、完成图片数及预留结算状态，状态为待处理。
2. GPT 多图请求已拆成多个单图任务并发调用外部生图服务；每个任务复用本批请求中的参考图 URL。
3. 每张图按任务 `user_id` 落盘到 `private-media/ai/{userId}/...`，把鉴权图片 URL 写入 `result_urls` JSON 数组，并通过任务列表和详情接口返回 `resultUrls`；任务 owner 可继续把结果图作为参考图。
4. 任务状态为 `0待处理/3处理中/1成功/2失败`；失败时按未完成图片数退款，失败记录保留在列表中便于审计。
5. 普通用户只能查询或删除自己的任务；超级管理员可查看全部任务。
6. `AiImageTaskRecoveryWorker` 周期性把进程重启后遗留的待处理任务重新入队，并对超时的处理中任务执行同一个幂等退款状态机。

媒体边界：博客图片和头像分别通过公开 `/blog`、`/avatar` 前缀提供；AI 参考图与生成图不进入 `wwwroot`，下载接口再次检查登录主体和任务/路径所有权。后台任务入队前完成参考图所有权校验，避免 worker 无请求主体时跨用户读取。

提示词安全边界：创建阶段在 Redis 准入和积分预留前通过 `IAiPromptFilter` 检查，Worker 在取得
Provider 租约前按最新快照复检。MySQL 保存权威规则，进程内不可变快照执行关键词匹配，Redis
只发布版本通知；系统不部署或调用语义审核模型。第三方词库只进入禁用审核队列，已启用的覆盖
缺口词使用独立项目来源。分类、来源和迁移细节见 [ai-prompt-filter.md](./ai-prompt-filter.md)。


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
- `GET /api/blog/categories`
- `GET /api/blog/summary`
- `GET /api/blog/titles/latest`
- `GET /api/blog/comments/latest`
- `GET /api/blog/site/info`
- `GET /api/prompts`
- `GET /api/prompts/{id}`
- `POST /api/prompts/{id}/events`
- `POST /api/dev/bootstrap/super-admin`（仅 Development 且要求 `X-Bootstrap-Secret`）

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
