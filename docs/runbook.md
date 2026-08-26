# 运维手册

## 本地启动

1. 准备 MySQL 和 Redis。
2. 执行根目录 `jokester.admin.sql` 初始化数据库。
3. 配置运行时密钥和连接串。
4. 启动 API：

```powershell
cd .\jokester.admin
dotnet run --launch-profile http
```

默认地址：

- API：`http://localhost:5049`
- Swagger：`http://localhost:5049/swagger`

Production 默认不开放 Swagger。如需临时公网调试，在环境配置中设置
`Swagger__Enabled=true`；调试完成后应恢复为 `false`。该开关只开放 Swagger
UI 和 OpenAPI 文档，不会开放仅限 Development 的管理员引导接口。

## 必要配置

仓库内 `appsettings.json` 只保留占位，实际运行需要通过本地配置文件、环境变量或 Secret Manager 提供：

- `Jwt.Issuer`
- `Jwt.Audience`
- `Jwt.SecretKey`
- `Database.ConnectionString`
- `Redis.ConnectionString`
- `Redis.InstanceName`
- `BootstrapAdmin.UserName`
- `BootstrapAdmin.Password`
- `BootstrapAdmin.Secret`

移动端基础配置使用 `Mobile.MinimumSupportedVersion`、`Mobile.LatestVersion`、`Mobile.MaintenanceMode` 和 `Mobile.Features.*`。默认版本为 `0.0.0`，Apple IAP 功能还会与 `AppleAppStore.Enabled` 运行时状态相与；未配置 Apple 凭据时公开配置不会误报 IAP 可用。

MySQL 连接串建议包含：

```text
SslMode=None;AllowPublicKeyRetrieval=True;
```

Redis 连接串示例：

```text
localhost:6379,abortConnect=false
```

## AI 提示词关键词过滤

敏感词过滤只使用 MySQL 词库和进程内不可变关键词快照，不部署或调用本地模型服务。
既有数据库按顺序执行：

```text
docs/migrations/20260811-add-ai-prompt-sensitive-words.sql
docs/migrations/20260811-expand-ai-prompt-sensitive-words-houbb.sql
```

启动后确认日志出现 `Prompt filter snapshot loaded`，并核对版本号和规则数。生产配置至少包括：

```dotenv
AiPromptFilter__Enabled=true
AiPromptFilter__RefreshIntervalSeconds=30
AiPromptFilter__MaxSnapshotAgeMinutes=15
AiPromptFilter__MinimumActiveWordCount=1
```

词库首次加载失败、有效阻断词数不足或快照超过允许陈旧时间时，新生图请求会返回 HTTP 503。
新增或调整词条后，应通过管理接口测试常规提示词、拆字/插标点变体和误杀样例；完整规则见
`docs/ai-prompt-filter.md`。

扩充迁移执行后核对来源、分类和启用状态：

```sql
SELECT source_code, category_code, action, status, COUNT(*) AS rule_count
FROM ai_prompt_sensitive_word
WHERE is_deleted = 0
  AND source_code IN ('houbb-sensitive-word-data', 'project-curated')
GROUP BY source_code, category_code, action, status
ORDER BY source_code, category_code, action, status;

SELECT revision, updated_at
FROM ai_prompt_sensitive_word_revision
WHERE id = 1;
```

在仅执行过基础迁移的数据库上，预期新增 60 条
`houbb-sensitive-word-data/audit/status=0` 候选和 68 条
`project-curated/block/status=1` 规则。`强奸`、`炸弹制作` 与内置词重复，只调整现有分类，
不会伪装成 houbb 来源。应先在测试库记录总行数和 revision，连续执行扩充迁移两次；第二次
不应增行或递增 revision。运行中的实例只加载启用规则，版本变化会由轮询触发快照刷新。

## 管理员初始化

命令行：

```powershell
cd .\jokester.admin
dotnet run --no-build -- --seed-admin <admin-user-name> <admin-password>
```

开发环境接口：

```http
POST /api/dev/bootstrap/super-admin
X-Bootstrap-Secret: <BootstrapAdmin.Secret>
```

## 冒烟检查

```powershell
dotnet build .\jokester.admin\jokester.admin.csproj
```

启动后可检查：

```bash
curl http://localhost:5049/api/sites/site_code
curl http://localhost:5049/api/blog/articles
```

以上两个接口不需要授权。

## 积分充值上线检查

已有数据库先执行：

```text
docs/migrations/20260809-add-point-recharge.sql
docs/migrations/20260812-ios-api-upgrade.sql
docs/migrations/20260819-add-expiring-point-buckets.sql
docs/migrations/20260820-add-user-membership-entitlements.sql
```

只执行目标数据库尚未应用的脚本。`20260819-add-expiring-point-buckets.sql` 必须在 `20260820-add-user-membership-entitlements.sql` 前完成；执行前先结算活动生图任务并停止所有 API/Worker 写入。`20260820` 会回填仍有效的 Web/Apple 月卡权益，执行后抽查 `sys_user_membership_entitlement` 的 `business_key`、`expires_at` 和 Apple 退款状态。随后确认 `monthly/trial/basic/value` 四档均为 `status=1 AND is_deleted=0`，`monthly.points=5000`、`monthly.validity_days=30`、`monthly.repeat_points IS NULL`，且 `trial/basic/value.validity_days IS NULL`，再按支付平台地址配置
`point_recharge_package.purchase_url`。该字段支持 `{orderNo}`、
`{packageCode}`、`{userId}` 占位符；初始化数据默认值为 `NULL`，此时前端只能
创建并展示待支付订单，不能跳转支付平台。

充值接口和安全约束见 [point-recharge.md](./point-recharge.md)。

## iOS API 升级与发布检查

已有数据库在 `20260809-add-point-recharge.sql`、提示词/AI 路由等前置迁移完成后执行：

```text
docs/migrations/20260812-ios-api-upgrade.sql
```

该迁移创建法律文档、用户授权、账户删除、私有 Asset、Apple 商品/交易/通知/负债表，不写入部署专属法律版本、URL、Product ID 或密钥。根目录 `jokester.admin.sql` 已包含同样的 8 个建表块，不能同时对同一新库重复使用两种初始化路径。

注册接口已经与法律文档配置解耦，恢复和验收注册不需要先配置法律版本。`POST /api/auth/register/email-code` 只接收 `email`，不使用图片验证码；`POST /api/auth/register` 只接收 `email`、`emailCode`、`password`。后端从规范化邮箱账号部分生成用户名和昵称并自动处理用户名冲突，注册用户可直接用邮箱登录。

`privacy_policy`、`terms_of_service` 和 `ai_processing` 仍由独立法律文档系统维护。未启用 AI processing 时 `GET /api/legal/documents/current` 返回 `aiProcessingNotice=null`，客户端不显示 AI 授权提示；所有第三方 AI 生图入口则在 Provider 调用、Redis 准入和积分预留前 fail-closed，返回 HTTP 503、`SERVICE_UNAVAILABLE`。告知已启用但用户未授权当前 Provider 时返回 HTTP 412、`AI_CONSENT_REQUIRED`。小范围使用或 TestFlight 不得绕过该限制。本轮已启用独立 AI 数据处理授权声明。

前端仓库的 `public/legal/privacy/index.html`、`public/legal/terms/index.html` 和 `public/legal/ai-processing/index.html` 已构建并部署。本轮版本分别为 `privacy-2026-08-14`、`terms-2026-08-14` 和 `ai-processing-2026-08-14`，正式页面位于 `https://ai.jokester.cc:8011/legal/privacy/index.html`、`https://ai.jokester.cc:8011/legal/terms/index.html` 和 `https://ai.jokester.cc:8011/legal/ai-processing/index.html`。运行配置命令前应确认三个地址仍可匿名访问；`Approved=true` 是操作者对该次审批结果的明确确认。

从项目根目录执行下面的幂等维护命令，两次配置共用同一个实际 UTC 生效时间。`Approved=true` 只表示操作者确认本次启用的输入已经完成业务/法务审批；命令不会替代审批。由于数据库可能存在旧的 `web/zh-CN` 精确版本，先更新 `web`，再写入 `all`，避免精确版本遮蔽通用版本：

```powershell
$env:LegalDocuments__Approved = 'true'
$env:LegalDocuments__Locale = 'zh-CN'
$env:LegalDocuments__EffectiveAt = '2026-08-14T06:31:54Z'
$env:LegalDocuments__PrivacyPolicy__Version = 'privacy-2026-08-14'
$env:LegalDocuments__PrivacyPolicy__Url = 'https://ai.jokester.cc:8011/legal/privacy/index.html'
$env:LegalDocuments__PrivacyPolicy__RequiresReconsent = 'false'
$env:LegalDocuments__TermsOfService__Version = 'terms-2026-08-14'
$env:LegalDocuments__TermsOfService__Url = 'https://ai.jokester.cc:8011/legal/terms/index.html'
$env:LegalDocuments__TermsOfService__RequiresReconsent = 'false'
$env:LegalDocuments__AiProcessing__Enabled = 'true'
$env:LegalDocuments__AiProcessing__Version = 'ai-processing-2026-08-14'
$env:LegalDocuments__AiProcessing__Url = 'https://ai.jokester.cc:8011/legal/ai-processing/index.html'
$env:LegalDocuments__AiProcessing__RequiresReconsent = 'true'
$env:LegalDocuments__AiProcessing__ProviderCodes__0 = 'openai'
$env:LegalDocuments__AiProcessing__ProviderCodes__1 = 'google'

foreach ($platform in @('web', 'all')) {
  $env:LegalDocuments__Platform = $platform
  dotnet run --project .\jokester.admin --configuration Release --no-build --no-launch-profile -- --configure-legal-documents
  if ($LASTEXITCODE -ne 0) { throw "Legal document configuration failed for $platform." }
}

Remove-Item Env:LegalDocuments__Approved,Env:LegalDocuments__Platform,Env:LegalDocuments__Locale,
  Env:LegalDocuments__EffectiveAt,
  Env:LegalDocuments__PrivacyPolicy__Version,Env:LegalDocuments__PrivacyPolicy__Url,
  Env:LegalDocuments__PrivacyPolicy__RequiresReconsent,
  Env:LegalDocuments__TermsOfService__Version,Env:LegalDocuments__TermsOfService__Url,
  Env:LegalDocuments__TermsOfService__RequiresReconsent,
  Env:LegalDocuments__AiProcessing__Enabled,Env:LegalDocuments__AiProcessing__Version,
  Env:LegalDocuments__AiProcessing__Url,Env:LegalDocuments__AiProcessing__RequiresReconsent,
  Env:LegalDocuments__AiProcessing__ProviderCodes__0,
  Env:LegalDocuments__AiProcessing__ProviderCodes__1 -ErrorAction SilentlyContinue
```

命令在同一事务内停用目标 scope 的旧活动版本、插入或重新启用本次目标版本，并回读验证当前集合；`AiProcessing.Enabled=false` 还会停用相同 scope 的活动 AI 告知。同一个 `version` 的 URL、生效时间、重授权标记和 provider 集合不可变；内容变化必须使用新版本。审批标记缺失、非 HTTPS URL、未来生效时间或同版本内容冲突时零写入。完成后再做独立审计查询：

`Platform=all` 会分别回读 `ios`、`android` 和 `web`；若仍有其他活动的精确平台版本遮蔽通用版本，命令会回滚并指出冲突平台，应先用相同版本更新该精确 scope 后再重试 `all`。`ProviderCodes` 必须覆盖 `ai_image_model_config` 当前全部启用路由映射出的 `openai` / `google`。AI 声明使用 `RequiresReconsent=true`；以后更换声明内容必须发布新版本并重新取得授权。

```sql
SELECT document_type, version, platform, locale, url, provider_codes_json,
       effective_at, requires_reconsent, status
FROM legal_document
WHERE status = 1
ORDER BY document_type, platform, locale, effective_at DESC;
```

Apple IAP 默认关闭。启用前通过环境变量、Secret Manager 或部署密钥提供：

- `AppleAppStore.Enabled=true`
- `BundleId`、App Store Connect `IssuerId`、`KeyId`
- P-256 `.p8` 私钥 PEM（必须保留真实换行）
- 至少 32 字节的随机 `AppAccountTokenKey`
- `Environment=Sandbox`（TestFlight/Sandbox 验收阶段）

仓库的简单 `.env` 加载器只支持单行值，因此多行 `PrivateKeyPem` 不应通过仓库 `.env` 文件配置；使用 `dotnet user-secrets` 或部署平台的多行 Secret。不得提交 `.p8`、真实 Product ID、Bundle ID、Issuer ID 或 `AppAccountTokenKey`。

Apple JWS 信任根固定随项目发布为 `certificates/apple/AppleRootCA-G3.pem`。发布产物中必须存在该文件；可额外通过 `TrustedRootCertificatePaths` 配置审计过的根证书。启用前确认系统时间正确，证书链校验依赖 UTC 有效期。

将每个现有积分套餐映射到一个真实消耗型 StoreKit 商品，且 `package_id` 和 `apple_product_id` 都唯一。生产映射不要写回仓库 SQL。核对：

```sql
SELECT p.package_code, p.points AS package_points,
       a.apple_product_id, a.points AS apple_points,
       a.product_type, a.environment, a.status, a.is_deleted
FROM point_recharge_package p
LEFT JOIN apple_iap_product a ON a.package_id = p.id
ORDER BY p.sort, p.id;
```

`monthly` 的 `package_points` 和 `apple_points` 必须都为 5000；任一值配错时 iOS 套餐查询或 Apple 履约会 fail-closed。其他套餐的 `apple_points` 可按已审批的商品映射独立配置。

Sandbox/TestFlight 门禁：

1. `GET /api/legal/documents/current` 返回三类当前文档，时间带 `Z`。
2. `GET /api/mobile/config` 在 Apple 配置有效时才返回 `features.appleIap=true`。
3. `GET /api/points/recharge/packages?platform=ios` 只返回已映射商品且不含外部购买 URL，其中 `monthly.points=5000`。
4. 同一 Sandbox Transaction ID 连续/并发重放只增加一次积分；同 key 改 payload 返回 409。
5. App Store Server `TEST`、退款和撤销通知验签成功；通知重放不重复扣分，处理失败保持可重试。
6. 追扣尚未过期的可撤销积分时，余额不足会产生 open debt，随后生图返回 403；当前仓库没有自动偿还或关闭 debt 的流程，产品/财务确认人工或专门清偿流程后再开放生产。
7. 用月卡积分创建一个处理中任务后模拟退款：退款不能扣除无关永久积分或立即形成该预留部分的 debt；任务失败不恢复月卡额度，任务成功才追扣并在不足时增加 debt，重复结算不重复追扣。
8. 未授权当前 Provider 时生成请求返回 412；敏感提示词返回 422；限流返回 429 和 `Retry-After`。
9. 账户删除申请立即撤销会话，完成后无法登录、私有 Asset 不可访问，完成邮件失败会重试。

切换生产前把商品映射和 Apple 环境改为 `Production`，再启用 `Mobile.Features.AppleIap`。生产通知处理失败、open debt、账户删除 `failed/notification_pending` 都应接入告警；本仓库只实现状态和重试，不包含外部告警平台配置。

## 提示词库上线与回滚

已有数据库先执行：

```text
docs/migrations/20260810-add-prompt-library.sql
```

启用前至少配置 `PROMPT_LIBRARY_ENABLED=true`、`PROMPT_SOURCE_API_URL`、
`PROMPT_TARGET_COUNT=126` 和绝对路径 `PROMPT_IMAGE_ROOT`。默认源是经过审计的
YouMind 官方仓库固定中文快照；如果替换为需要授权的源，通过 Secret Manager 或
部署环境设置 `PROMPT_SOURCE_API_TOKEN`，不要写入仓库。图片目录必须位于发布目录
之外，并与 Nginx `/prompt-images/` 的 `alias` 指向同一目录。

同步使用官方仓库固定提交中的 `README_zh.md`，以 `?id=` 的 CMS ID 作为稳定标识，
只发布标题、描述和正文均以中文说明为主且封面准备完整的 126 条数据。原始语言徽章
不会用于过滤，发布语言统一为 `zh-CN`。任一条件不满足时同步运行记为 `failed`，
当前激活快照保持不变。

使用具备对应权限的 Access Token 验收：

```http
GET  /api/admin/prompt-sync/status
POST /api/admin/prompt-sync/run
POST /api/admin/prompt-sync/snapshots/{snapshotId}/activate
```

状态接口应显示 `activeItemCount=126`、`missingCoverCount=0`，当前内容哈希非空。
重复同步相同内容应产生 `not_modified` 运行。激活历史成功快照后，可调用最后一个接口
切回；后续同步会以当前激活快照而不是最近运行作为哈希比较基准。

## 博客缩略图排查

如果文章列表没有 `thumbnailUrl`：

1. 检查文章是否设置了 `cover_url`。
2. 检查正文是否包含 `<img src="...">`。
3. 如果希望使用 `blog_article_media`，确认图片 URL 来自 `blog_media.url`。
4. 创建或更新文章后，检查 `blog_article_media` 是否写入了该文章和媒体的关联。

如果正文显示 `<p>`、`<img>` 字符串而不是渲染后的图片，是前端按纯文本展示了 HTML。前端应将文章 `content` 按 HTML 渲染，并做好 XSS 清洗或可信来源控制。

## 评论验证码排查

`GET /api/blog/comments/captcha` 返回 `imageBase64` 和 `mimeType`，前端应拼成 `data:${mimeType};base64,${imageBase64}` 作为图片地址展示。

验证码答案是图片中的 6 位大写字母/数字。评论提交和登录失败后的二次验证通过 `captchaId`、`captchaAnswer` 传回；验证码校验后会一次性失效，过期时间由 `expiresInSeconds` 返回（当前为 5 分钟）。注册邮件发送不使用图片验证码。

注册邮件发送成功时检查 `data.retryAfterSeconds=60`。重复请求若返回 429，应同时包含 `Retry-After`、`code=RATE_LIMITED` 和 `details.retryAfterSeconds`；不要只依赖客户端本地倒计时。

## 已知环境问题

- 本地启动可能出现 DataProtection DPAPI 或 key 文件权限告警，通常不影响 API 启动。
- Redis 首次不可达时不会阻塞服务启动；刷新令牌可在开发环境启用进程内存兜底，但不适合正式多实例部署。

## AI 生图积分配置与排查

legacy 生图会从 `ai_image_point_price` 查询价格并扣除 `points * imageCount`。价格缺失会导致接口返回“当前模型、分辨率、画质未配置积分价格”。

价格表配置要点：

- legacy GPT Image2：`resolution_code` 使用业务分辨率档位，如 `1k`、`2k`、`4k`；`quality_code` 使用 `low`、`med`、`high`，价格按 `modelCode + resolutionCode + qualityCode` 匹配。
- GPT Image2 的 2K 单张价格固定为 `low=15`、`med=30`、`high=60` 积分，对应 `price_amount=0.15/0.30/0.60 CNY`；价格排序位于 1K 之后、4K 之前。
- Nano Banana2：官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality_code` 列不参与查询（存 `''` 或 `NULL` 都不影响）。`resolution_code` 使用业务分辨率档位，如 `1k`、`2k`、`4k`；`aspectRatioCode=auto` 时上游 `size` 会传 `auto`，但积分价格仍按 `resolutionCode` 匹配。
- 只启用 `status=1` 且 `is_deleted=0` 的价格行。
- legacy 价格匹配逻辑见 `PointService.GetImageGenerateCostAsync`：仅当调用方显式传入 `quality` 时才把 `quality_code` 加入查询条件。

`size-mode-v1` 不使用上述可变价格表直接查价。创建事务会锁定客户端提交的 current `modelCode + catalogVersion`，并从 `ai_image_model_release_price` 选择 explicit 或 auto 独立价格；auto 行的规范化 `resolution_code` 为空，API 返回 `null`。迁移不发布 release 或 auto 价格，必须按“AI 尺寸模式升级与回滚”完成受控发布。

既有数据库执行 `docs/migrations/20260821-add-gpt-image-2-2k-prices.sql` 可幂等新增或修正上述价格。该迁移只维护 `ai_image_point_price`，不会创建或启用 `ai_image_model_config` 的 2K 路由。确认当前启用的 1K 主路由使用官方通用 `provider_model=gpt-image-2` 后，再执行 `docs/migrations/20260821-add-gpt-image-2-2k-primary-route.sql`；后者会复制该主路由的连接配置并启用精确 2K 路由，不会为第三方 1K/4K 专用模型伪造 2K 备用路由。

冒烟检查建议：

1. 注册新用户后确认 `sys_user.point_balance=50`，并存在 `source=register` 的积分流水。
2. 登录后调用 `POST /api/points/sign-in`，确认余额增加 25；同日重复调用应返回“今日已签到”。
3. 调用 `GET /api/points/balance`，确认返回 `availablePoints`、`permanentPoints`、`expiringPoints`、`nextExpiringPoints`、`nextExpireAt`、`hasSignedInToday`、`todaySignInPoints`。
4. 核销 `monthly` 套餐码，确认创建 5000 分的 `sys_user_point_bucket`，`expires_at` 为核销时间后 30 天。
5. 同时存在月卡和永久积分时创建生图任务，确认先减少月卡批次并写入 `source=image_generate` 流水。
6. 模拟任务失败时，确认写入 `source=image_refund` 返还流水，批次剩余恢复且 `expires_at` 不变。
7. 发布升级时先结算所有活动生图任务，再停止全部 API/Worker 写入并执行 `20260819-add-expiring-point-buckets.sql`；禁止旧、新节点同时写积分余额与批次表，并人工核对历史月卡和 Apple 履约流水。

## GPT Image2 生图配置

GPT Image2 生图接口 `POST /api/ai/images/generate` 和后台任务接口 `POST /api/ai/images` 需要登录和 `AiImage.Generate` 权限。参考图和生成图保存在非静态目录 `private-media/ai`，通过 `/api/media/ai/...` 鉴权下载；有参考图时后端会先校验当前用户所有权，再调用 OpenAI `/images/edits` 并以 multipart `image[]` 提交；无参考图时调用 `/images/generations`。

直接生成接口会先写入 `ai_image_task`，再由后台 worker 调用外部生图服务；接口侧最多等待 5 分钟返回图片结果。用户关闭网页只会断开 HTTP 响应，不会取消已经入队的后台任务，完成后的图片仍可在历史记录中按 `taskId` 查询。

GPT Image2 显式尺寸的上游 `size` 宽高都必须为 `16px` 倍数且不超过 `3840`，长短边比例不超过 `3:1`，总像素必须在 `655,360` 到 `8,294,400` 之间。业务档位按长边 `1k=1024`、`2k=2048`、`4k=3840` 解析；典型结果为 `1k + 1:1 = 1024x1024`、`2k + 16:9 = 2048x1152`、`4k + 1:1 = 2880x2880`、`4k + 16:9 = 3840x2160`。legacy GPT 请求必须显式传入画幅比例，不接受 `aspectRatioCode=auto`；`size-mode-v1` auto 使用独立契约，Nano Banana2 的 legacy auto 行为不变。

主备路由统一配置在 `ai_image_model_config`：

- `route_role=primary`：主路由；`route_role=fallback`：备用路由
- `model_code + resolution_code + route_role` 唯一，每个分辨率最多各一条主、备记录
- 每条路由独立保存 `provider_model`、`base_url`、`api_key`、`text_to_image_path` 和 `image_to_image_path`
- 主备均启用时先请求主路由；`OpenAI.PrimaryTimeoutSeconds` 默认 `180` 秒，超时、网络异常、HTTP 错误、无效响应或不可用图片都会触发备用路由
- 该 `180` 秒只约束 Jokester 对主路由的等待，不能延长上游网关或中转服务自己的超时；如果上游在约 `90` 秒返回 `504` 或 `context canceled`，应调整上游网关超时，或使用后台任务接口并轮询结果
- 禁用主路由且保持备用启用时，请求直接走备用；禁用备用时只请求主路由

既有数据库先停 API/Worker 并执行 `docs/migrations/20260811-add-ai-image-route-role.sql`。迁移创建的 GPT 主路由是禁用占位，必须在目标数据库写入真实地址和 Key 后启用：

```sql
UPDATE ai_image_model_config
SET base_url = '<primary-base-url>',
    api_key = '<primary-api-key>',
    status = 1,
    updated_at = CURRENT_TIMESTAMP
WHERE model_code = 'gpt-image-2'
  AND route_role = 'primary'
  AND is_deleted = 0;
```

不要把 `provider_model` 批量改成 `model_code`。例如主渠道可使用 `gpt-image-2`，备用渠道仍可按分辨率使用 `gpt-image-2-1k` / `gpt-image-2-4k`。

需要强制切到备用渠道时，一条语句同时禁用主路由并启用备用路由：

```sql
UPDATE ai_image_model_config
SET status = CASE route_role
        WHEN 'primary' THEN 0
        WHEN 'fallback' THEN 1
    END,
    updated_at = CURRENT_TIMESTAMP
WHERE model_code = 'gpt-image-2'
  AND route_role IN ('primary', 'fallback')
  AND is_deleted = 0;
```

恢复“主路由优先、失败自动切备用”时，把两种角色都启用：

```sql
UPDATE ai_image_model_config
SET status = 1,
    updated_at = CURRENT_TIMESTAMP
WHERE model_code = 'gpt-image-2'
  AND route_role IN ('primary', 'fallback')
  AND is_deleted = 0;
```

排查要点：

- 直接生成响应应包含 `taskId` 和 `url`；携带该用户 Access Token 访问 URL 应返回图片，匿名访问应为 `401`，其他用户访问应为 `404`。如果前端中途退出，应通过历史记录接口确认同一个 `taskId` 最终写入 `resultUrls`。
- 上传 HEIC/HEIF 后响应 `mimeType` 应为 `image/png`，文件内容可由 PNG 解码；JPEG、PNG、WebP 应保持对应真实 MIME。所有格式都应清除元数据，Asset 缩略图应为 WebP。Windows/Linux 发布目录必须包含对应 RID 的 `Magick.Native-Q8` 原生库，否则 HEIC/HEIF 会在运行时解码失败。
- Asset 所有者调用 `DELETE /api/assets/{assetId}` 后，内容和缩略图接口都应返回 404，数据库记录应为软删除，原图和缩略图应从私有目录移除；其他用户删除同一 Asset 也应返回 404 且文件保持不变。删除 AI 任务不会级联清理媒体。
- 传 `referenceImageUrls` 调试时，确认数组长度不超过 6，且每个 URL 都由当前用户的上传接口返回并能解析到 `private-media/ai` 下的文件。
- 后台任务成功后，`GET /api/ai/images/{id}` 应返回 `status=succeeded`、兼容字段 `statusCode=1` 和非空 `resultUrls`；数据库中的 `ai_image_task.result_urls` 应是图片 URL 的 JSON 数组。
- `imageCount=4` 时，创建接口应返回 4 个不同的 `ids`，数据库应新增 4 条 `image_count=1` 记录；四条预留流水与总积分扣减应在同一事务提交。
- legacy GPT 日志中的 `Size` 应是符合上述约束的具体 `宽x高`，不能为 `auto`；`aspectRatioCode=auto` 应在调用上游前返回参数错误。`size-mode-v1` auto 则记录 `SizeMode=auto`、请求尺寸 `auto`，成功后从图片解码结果记录实际输出尺寸。
- 既有数据库如果仍把 `4k` 配成 `4096`，或 GPT Image2 的 1K / 4K 配置展示名相同导致排查困难，需要执行：

```sql
UPDATE ai_image_parameter
SET value_int1 = 3840
WHERE param_type = 'resolution' AND param_code = '4k';

UPDATE ai_image_model_config
SET model_name = CASE resolution_code
    WHEN '1k' THEN 'GPT Image 2 1K'
    WHEN '4k' THEN 'GPT Image 2 4K'
    ELSE model_name
END
WHERE model_code = 'gpt-image-2'
  AND resolution_code IN ('1k', '4k');
```

注意：不要把 `provider_model` 批量改成 `model_code`。如果中转供应商要求 1K/4K 使用不同模型 ID，应分别保留在数据库 `provider_model` 字段中。

## AI 尺寸模式升级与回滚

既有数据库在 `20260811-add-ai-image-route-role.sql`、`20260821-add-gpt-image-2-2k-prices.sql` 和 `20260821-add-gpt-image-2-2k-primary-route.sql` 已按需执行后，再执行：

```text
docs/migrations/20260821-add-ai-image-size-mode-v1.sql
```

该迁移只扩展 schema、建立 release/幂等/输入/结果/attempt/outbox 表并分类历史任务；不会 seed 已发布 release，不会开启 auto，也不会写真实 Provider URL、Key 或 Secret。新库使用根目录 `jokester.admin.sql`，不要再重复执行同一迁移。

发布顺序不可跳过：

1. 保持 `AiImageSizeMode.Enabled=false`、`AutoEnabled=false`，暂停所有新 AI admission。
2. 等待旧 Worker 已领取任务终态化或进入批准的安全 deadline，停止并断权所有 pre-expand API/Worker，确认旧代次无法重连。
3. 备份数据库，在生产数据副本演练迁移并核对 `legacy-explicit-v1`、`legacy-aspect-auto`、`legacy-unknown` 数量、NULL 尺寸列、账务和历史任务读取。
4. 执行 expand/migrate，部署理解新 schema 的兼容 API/Worker；先恢复 legacy 与 `size-mode-v1` explicit。此后不得回滚到 pre-expand 二进制。
5. 逐路由验证 generations、无蒙版 edits、带蒙版 edits，审批 HTTPS endpoint/result allowlist、Secret 具体版本和输出资源上限；再以事务发布 immutable release/current pointer 和独立 auto 价格。
6. 设置 `AiImageSizeMode.Enabled=true` 后先验证 explicit；只有完成全部审批后才设置 `AutoEnabled=true`，并通过 `AllowedUserIds` 或 `AutoCohortPercent` 小范围灰度。

目录发布使用一次性维护命令。配置可来自 Secret Manager 或进程环境；下面只展示键名，价格必须替换成产品、运营和财务已审批值：

```powershell
$env:AiImageCatalogRelease__Approved = 'true'
$env:AiImageCatalogRelease__CatalogVersion = 'imgcat_<approved-version>'
$env:AiImageCatalogRelease__ConsentProviderCode = 'openai'
$env:AiImageCatalogRelease__EnsureGptImage2TwoK = 'true'
$env:AiImageCatalogRelease__PublishAuto = 'true'
$env:AiImageCatalogRelease__AutoRouteSourceResolutionCode = '1k'
$env:AiImageCatalogRelease__AutoVerifiedGenerations = 'true'
$env:AiImageCatalogRelease__AutoVerifiedEdits = 'true'
$env:AiImageCatalogRelease__AutoVerifiedMaskEdits = 'true'
$env:AiImageCatalogRelease__AutoPoints__low = '<approved-points>'
$env:AiImageCatalogRelease__AutoPoints__med = '<approved-points>'
$env:AiImageCatalogRelease__AutoPoints__high = '<approved-points>'
$env:AiImageCatalogRelease__AutoPriceAmounts__low = '<approved-cny>'
$env:AiImageCatalogRelease__AutoPriceAmounts__med = '<approved-cny>'
$env:AiImageCatalogRelease__AutoPriceAmounts__high = '<approved-cny>'
dotnet run --project .\jokester.admin -- --configure-ai-image-catalog
```

`EnsureGptImage2TwoK=true` 会幂等补齐并按 `1k/2k/4k` 固定参数排序、补齐 2K legacy 价格，并从已启用的通用 GPT Image 2 1K primary 配置复制 2K primary 路由；执行前必须确认该 Provider 路由确实不按业务分辨率使用不同模型别名。命令在一个数据库事务中发布 release 和 current pointer；同版本同内容可重跑，不同内容不得覆盖已发布版本，必须使用新的 `CatalogVersion`。`Approved=true` 和三项 `AutoVerified*` 只允许在相应审批与逐路径证据已留存后设置，不能用本地联调结果代替生产审批。

关键配置：

- `AiImageSizeMode.ProviderAllowedHosts`、`ResultAllowedHosts` 必须是审批后的精确主机；空列表会使 versioned route fail-closed。
- `AiImageSizeMode.AttemptReconcileMinutes` 决定 unknown Provider attempt 最晚对账时间。
- `AiCostControl.OutboxBindDeadlineMinutes` 默认 120，范围 5–1440；Redis 批次绑定超过此时限的未派发任务会统一失败退款。
- `X-Client-Capabilities=ai-size-mode-v1` 只协商响应 schema，不授予 auto；服务端账号 cohort 才是灰度依据。

验收至少检查：同 key 重放不再次 Redis admission/扣分；auto 请求的 `resolution_code/aspect_ratio_code/requested_width/requested_height` 为 NULL；多图请求头、ordinal、任务、Outbox 和逐任务预留流水数量一致；部分软删除返回准确 `requestState`；Provider unknown 到 `reconcile_by` 后只有一条退款；Outbox 部分派发恢复不重复 Provider 调用。

回滚时先关闭 `AutoEnabled` 并从模型能力移除 auto，保留所有被在途任务引用的 release/route/price/Secret。API 最低只能回滚到理解新 schema 且 auto 关闭的兼容版本，Worker 继续按任务冻结 release 完成或退款；在途清零和账务核对前不得删除版本快照或执行 contract 收紧。

## Nano Banana2 生图配置

Nano Banana2 生图接口 `POST /api/ai/images/nanoBananaImage/generate` 需要登录和 `AiImage.Generate` 权限。不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图并验证私有媒体所有权。请求体可传 `size`，也可传 `resolutionCode` + `aspectRatioCode`；`aspectRatioCode=auto` 时后端不读取参考图尺寸或推导具体画幅比例，而是把上游 `size` 直接传为 `auto`，画幅由上游服务自行决定。积分价格仍按业务 `resolutionCode` 档位匹配，和画幅比例无关。生成图片同样保存到 `private-media/ai` 并通过鉴权接口下载。

Nano Banana2 没有官方备用渠道，当前只配置 `route_role=primary`。后端读取 `ai_image_model_config` 中当前启用的 `nano-banana-2` 渠道并直连。

## AI 成本控制发布检查

- 既有数据库先停掉所有 API/Worker 实例，再执行 `docs/migrations/20260808_p0_ai_cost_control.sql`；执行前必须人工处理历史 `status IN (0, 3)` 任务并核对其积分流水。
- 配置 `AiCostControl`：用户每日图片/积分上限、用户并发、全局积压、Provider 全局并发、幂等 TTL、租约 TTL、Outbox 绑定时限和熔断阈值。所有值必须在启动校验范围内；`MaxConcurrentTasksPerUser` 至少应覆盖允许的单次 GPT `imageCount`（当前最大为 10）。
- Redis 是成本准入的强依赖。生产应启用持久化和副本/故障转移，禁止会淘汰成本控制键的 eviction 策略；Redis 不可用时新任务按设计返回 503。
- 所有生成/创建请求必须提供新的 `idempotencyKey`。相同 key 重试应返回同一任务或同一批任务且余额只扣一次；改变 prompt 或参数后复用 key 应返回 409。
- 验收 `ai_image_task.billing_status` 与 `sys_user_point_detail.business_key`：成功任务应确认预留，失败任务只出现一条 `image:{taskId}:refund`，部分成功只退未完成图片。
- 设置本地 `JOKESTER_TEST_REDIS` 后执行 `dotnet test jokester.slnx -c Release --filter "Category=Integration"`，覆盖并发幂等、额度恢复、Provider 租约、Refresh Token 和限流 Lua。

既有数据库若仍保存 `/ai-images/...` 或 `/nano-banana2-images/...` URL，先停止所有 API/Worker 实例，再执行 AI 私有媒体迁移。命令会按 `ai_image_task.user_id` 复制被引用的文件，事务更新任务与收藏 URL，并保留旧静态文件作为回滚副本：

```powershell
dotnet run --project .\jokester.admin -- --migrate-ai-media --dry-run
dotnet run --project .\jokester.admin -- --migrate-ai-media
```

先确认 dry run 没有缺失源文件。正式执行成功后，输出中的 `unreferencedLegacyFiles` 是未被任务/收藏引用且无法可靠判断所有者的历史文件，不会迁入私有用户目录。未迁移的旧静态 URL 不会由新版本继续公开暴露。

检查当前 Nano Banana2 渠道：

```sql
SELECT model_code, route_role, provider_model, base_url, text_to_image_path, image_to_image_path,
       CASE WHEN api_key = '' THEN 0 ELSE 1 END AS has_api_key
FROM ai_image_model_config
WHERE model_code IN ('nano-banana-2', 'nano-banana-pro')
  AND status = 1
  AND is_deleted = 0;
```

`base_url`、`api_key`、`provider_model` 和两个请求路径都由该表提供。生产密钥由部署时写入数据库，不要写入 `appsettings.json`、`.env.example` 或 SQL 初始化脚本。
