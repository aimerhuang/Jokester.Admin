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

Refresh Token 以 SHA-256 哈希作为 Redis 定位键，明文只返回给客户端。每个登录有独立 `sessionId`/token family；轮换时旧 Token 原子消费，并在 10 秒宽限内缓存加密后的同一轮换结果。宽限后重放会撤销 family。登出全部设备、禁用、改密、角色/站点/超级管理员上下文变化和账户删除都会撤销用户 Refresh Token 会话。

移动端错误响应使用独立 `ApiErrorResponse`，`code` 是稳定字符串而非成功响应的数值 `0`。Swagger Operation Filter 为移动端操作统一声明错误 Schema；业务时间在 DTO 边界转换为 UTC，旧积分/充值表的本地时间存储约定不被业务重构破坏。

## iOS 合规与 StoreKit 数据流

相关表：

- `legal_document`：按文档类型、平台、语言和版本保存已审批文档。
- `user_consent`：追加式保存隐私/条款/AI 处理的接受或撤回历史。
- `account_deletion_request`：保存删除申请、计划、数据删除、邮件通知和重试状态。
- `apple_iap_product`：一对一映射现有积分套餐与 StoreKit 消耗型商品。
- `apple_transaction`：已验证 Apple 交易的幂等履约账本，只保存 JWS 哈希。
- `apple_server_notification`：App Store Server Notifications V2 接收和重试账本。
- `apple_iap_debt`：退款时余额不足的待清偿积分负债。

注册只接收 `email`、`emailCode`、`password`，不读取或记录隐私政策和服务条款版本。服务端用规范化邮箱 `@` 前的账号部分生成 `userName` 和 `nickName`，在用户名冲突时自动消歧；登录查询同时支持用户名和邮箱。

法律文档与注册保持解耦。AI processing 告知仍按独立流程查询，不依赖同平台的隐私政策或服务条款是否存在。AI 授权写入时使用请求的真实客户端平台，后续生图校验按用户最近一条 AI 授权记录的平台解析精确 scope 或 `all` 告知，不再固定读取 `ios`。生图创建和 Worker 调 Provider 前都用实际路由的 `ConsentProviderCode` 检查最新 AI 告知和授权：告知未配置时服务不可用，告知已配置但授权缺失时要求用户同意，避免平台或 Provider 配置切换后发送未获授权的数据。

StoreKit 履约流程：

1. 客户端提交交易 ID、Product ID、确定性 `appAccountToken` 和幂等键，不提交积分或可信价格。
2. 服务端用 App Store Connect ES256 凭据读取交易并验证 Apple JWS 证书链、Bundle、环境、商品、数量、撤销状态和账户令牌。
3. 在一个 MySQL 事务内插入唯一交易行、锁定并更新余额、写入 `source=apple_iap` 流水；重复交易返回首次结果。
4. Apple 退款通知再次验证内层交易，在事务内扣回可用积分并更新交易。已被活动生图任务预留的月卡额度先记为延后追扣：失败不恢复已撤销额度，成功时再追扣；即时或延后追扣的余额差额写入唯一 open debt。存在负债或未结算延后追扣时，`IPointService` 拒绝新生图预留。
5. 通知安全接收与业务处理分离；UUID 唯一保证重放幂等，失败状态由 Worker 重试。

账户删除创建后立即撤销会话。Worker 可原子认领 `scheduled/failed` 或陈旧 `processing` 记录，删除用户私有数据并匿名化必须保留的财务/审计主体。数据已删除但邮件失败时进入 `notification_pending`，只重试通知，不重复执行数据删除。

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

- `ai_image_task`：后台生图任务，保存 `prompt`、参数编码、`size`、`quality`、`image_count`、参考图快照、`result_urls`、`status`
- `media_asset`：用户私有上传 Asset，保存不可枚举 ID、所有者、存储/缩略图 key、真实 MIME、尺寸、哈希和删除状态
- `ai_image_point_price`：按 `model_code + resolution_code + quality_code` 定义 legacy 出图积分价格；`size-mode-v1` 使用 release 价格明细
- `sys_user.point_balance`：用户当前可用积分余额
- `sys_user_point_detail`：积分流水，记录注册赠送、签到赠送、出图扣减、过期清理和失败返还
- `sys_user_point_bucket`：套餐和签到积分到账批次，记录剩余积分、原到期时间和扣减优先级；`expires_at=NULL` 表示永久套餐积分
- `sys_user_point_bucket_usage`：任务扣款到批次的分摊，并记录 Apple 退款与活动任务交错时的延后追扣，用于原批次退款和后续成功/失败结算

积分规则：

1. 用户注册成功后获得 50 积分，写入 `source=register` 的赠送流水。
2. 登录用户可调用 `POST /api/points/sign-in` 每日签到一次，领取 25 积分。
3. 签到积分当天有效；第二天在查询余额、登录/刷新/读取资料、再次签到或出图扣分时，会把上一日未使用部分写为 `source=sign_in_expire` 的过期扣减流水。
4. `monthly` 套餐到账 5000 积分，从核销或 Apple 履约时起 30 天有效；扣分时先用限时套餐积分，再用签到积分，最后使用永久积分。
5. 月卡同时写入独立会员权益来源账本。登录、刷新和资料接口动态查询未撤销且未到期权益并返回最晚到期时间；该状态不依赖剩余积分，Apple 退款只撤销对应交易来源。
6. 创建生图任务前，服务先结算过期批次，再按价格表组合计算 `points * imageCount`；余额不足或价格缺失时拒绝创建任务。
7. 任务创建时预留积分并写入 `source=image_generate` 流水；后台生成失败或超时时，仅对未完成图片写入一次 `source=image_refund` 流水，并按扣款分摊退回原批次且不延长有效期。若原月卡已被 Apple 撤销，流水仍唯一保留，但撤销部分不再恢复为可用积分；任务成功部分才执行延后追扣。

统一任务路由与 GPT Image2 流程：

1. 移动端统一调用 `POST /api/ai/images` 并传 `modelCode`。后端按数据库配置的 `provider` 协议路由到 OpenAI Images 或 Gemini Images，不根据模型名称包含的单词猜测服务。
2. 后端校验 `prompt`、参数编码和最多 6 个 `referenceAssetIds`。Asset 必须属于当前用户；服务端只把自己解析出的私有文件交给 Provider。兼容期 URL 也只允许当前用户的同源私有媒体路径，从根源上阻止任意远程 URL/SSRF。
3. 上传先检查 magic bytes 和可解码格式，再限制文件字节、边长、总像素、解码时间与资源占用。HEIC/HEIF 通过受限的原生解码器读取主图并规范化为 PNG；JPEG、PNG、WebP 清除 EXIF/ICC/IPTC/XMP 后保持各自格式；生成 512px WebP 缩略图后才持久化 `media_asset`。
4. legacy 请求按 `modelCode + resolutionCode + route_role` 从 `ai_image_model_config` 解析启用路由，并通过 `ai_image_point_price` 查询扣分价格；`size-mode-v1` 的 release 路由和价格边界见下节。
5. 使用 Redis Lua 原子占用幂等键、用户每日额度、用户活动任务位和全局积压位；成本熔断打开时拒绝创建。
6. 按 `imageCount` 创建同等数量的单图任务，在同一 MySQL 事务中锁定用户余额、扣除整批积分并写入逐任务唯一预留流水，然后入队。
7. Worker 认领任务、复检授权和提示词，再取得 Redis Provider 租约。OpenAI 协议支持同槽位主备；Gemini 协议使用当前启用路由。
8. 成功确认预留；失败按未完成图片数退款，重复回调不二次退款。任务 DTO 返回字符串状态、进度、建议轮询间隔和 UTC 时间，同时保留数字状态兼容字段。

`size-mode-v1` 在上述 legacy 流程之上增加不可变执行边界：

1. `ai_image_model_release` 原子绑定模型契约，`ai_image_model_current_release` 只保存当前指针；route/price 明细按 release 冻结。auto 与 explicit 使用独立路由槽位和价格键，auto 不回退到 1K/2K/4K 价格。
2. `ai_image_request_idempotency` 以用户和 key SHA-256 唯一保存规范化指纹；`ai_image_request_task` 按 ordinal 保存批次。已有事实重放不读取实时 catalog、Redis、Asset 或价格，并按任务软删除投影 `requestState`。
3. 新批次在一个事务内锁定 current release/price、写请求头、单图任务、输入快照、任务明细和 `ai_image_task_outbox`，再按原积分桶写逐任务预留流水。Redis owner token 只允许未提交的当前 owner 撤销，提交后绑定完整 request/task/ordinal/图片/积分批次。
4. Outbox 只有完成 Redis 批次绑定后才派发；内存队列部分写入不会整批退款，恢复线程继续处理 pending 行。超过 `AiCostControl.OutboxBindDeadlineMinutes` 的未派发任务通过统一失败结算退款。
5. Worker 以 `claim_epoch + claim_token_hash + lease` CAS 认领并持续心跳；每次外发前保存 `ai_image_provider_attempt`。失租或外部结果未知时进入 `provider_unknown`，不自动二次外发，最晚在 `reconcile_by` 强制退款。
6. 任务输入表保存 Asset/legacy URL 的角色、顺序、owner、storage key 和内容哈希。成功结算把实际解码宽高、MIME、URL 与 `ai_image_task_result`、账务、attempt 状态在同一事务提交；失败返回稳定 `failureCode/failureStage/retryable`。
7. release Endpoint 与 Provider 结果 URL 分别执行 HTTPS allowlist、DNS/IP 和禁止自动重定向校验；响应与下载使用有界流读取。Provider 全局租约按 owner token 续租，续租失败停止 fallback/新外发并进入未知结果隔离。

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
5. 普通用户只能查询或删除自己的任务；超级管理员可查看全部任务。任务删除只软删除任务记录，不级联删除上传 Asset 或生成文件，避免清除仍可能被其他任务引用的媒体；账户删除流程负责清理该用户的全部私有媒体。
6. `AiImageTaskRecoveryWorker` 周期性把进程重启后遗留的待处理任务重新入队，并对超时的处理中任务执行同一个幂等退款状态机。

媒体边界：博客图片和头像分别通过公开 `/blog`、`/avatar` 前缀提供；AI Asset 与生成图不进入 `wwwroot`。`/api/assets/{assetId}/...` 和 `/api/media/ai/...` 在下载时检查主体和所有权，跨用户资源对普通用户表现为不存在。Asset 所有者可调用 `DELETE /api/assets/{assetId}` 软删除记录并清理原图和缩略图；跨用户删除与不存在统一返回 404。路径解析先规范化并确认仍位于配置根目录，拒绝 `..` 和混合分隔符穿越。Worker 始终显式使用任务 `user_id`，不依赖后台线程中不存在的 `ICurrentUser`。

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
- `GET /api/legal/documents/current`
- `GET /api/mobile/config`
- `POST /api/integrations/apple/app-store-server-notifications/v2`（Apple JWS 验签）
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

`GET /api/blog/comments/captcha` 返回 6 位大写字母/数字的 SVG 图片验证码 Base64 数据，答案在 Redis 中保存 5 分钟并在校验时一次性消费。评论提交和登录失败后的二次验证共用该验证码接口；注册邮件发送不使用图片验证码。注册邮件发送成功返回 `retryAfterSeconds=60`；邮箱/IP 共享限流返回 `429 RATE_LIMITED` 和 `Retry-After`。

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
- `GET /api/ai/images/pricing-options`
- `POST /api/ai/images/parameters/resolve`
- `POST /api/ai/images/generate`
- `POST /api/ai/images/nanoBananaImage/generate`
- `POST /api/ai/images/nanoBananaImage`
- `POST /api/ai/images/upload`
- `GET /api/assets/{assetId}/content`
- `GET /api/assets/{assetId}/thumbnail`
- `DELETE /api/assets/{assetId}`
- `GET /api/points/balance`
- `GET /api/points/details`
- `POST /api/points/sign-in`
- `GET /api/points/recharge/packages`
- `POST /api/points/recharge/orders`
- `POST /api/points/recharge/redeem`
- `POST /api/points/recharge/admin/codes`
- `POST /api/points/recharge/apple/transactions`
- `GET/PUT /api/users/me/consents`
- `POST/GET/DELETE /api/auth/account-deletion/requests`
- `GET/DELETE /api/logs/login`
- `GET/DELETE /api/logs/operation`
