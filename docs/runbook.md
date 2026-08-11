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
```

执行后确认四个套餐存在，并按支付平台地址配置
`point_recharge_package.purchase_url`。该字段支持 `{orderNo}`、
`{packageCode}`、`{userId}` 占位符；初始化数据默认值为 `NULL`，此时前端只能
创建并展示待支付订单，不能跳转支付平台。

充值接口和安全约束见 [point-recharge.md](./point-recharge.md)。

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

验证码答案是图片中的 4 位数字，提交评论时通过 `captchaAnswer` 传回。验证码校验后会一次性失效，过期时间由 `expiresInSeconds` 返回。

## 已知环境问题

- 本地启动可能出现 DataProtection DPAPI 或 key 文件权限告警，通常不影响 API 启动。
- Redis 首次不可达时不会阻塞服务启动；刷新令牌可在开发环境启用进程内存兜底，但不适合正式多实例部署。

## AI 生图积分配置与排查

生图前会按 `ai_image_point_price.model_code + resolution_code + quality_code` 查询积分价格并扣除 `points * imageCount`。价格缺失会导致接口返回“当前模型、分辨率、画质未配置积分价格”。

价格表配置要点：

- GPT Image2：`resolution_code` 使用业务分辨率档位，如 `1k`、`2k`、`4k`；`quality_code` 使用 `low`、`med`、`high`，价格按 `modelCode + resolutionCode + qualityCode` 匹配。
- Nano Banana2：官方无 `quality` 参数，价格只按 `modelCode + resolutionCode` 匹配，`quality_code` 列不参与查询（存 `''` 或 `NULL` 都不影响）。`resolution_code` 使用业务分辨率档位，如 `1k`、`2k`、`4k`；`aspectRatioCode=auto` 时上游 `size` 会传 `auto`，但积分价格仍按 `resolutionCode` 匹配。
- 只启用 `status=1` 且 `is_deleted=0` 的价格行。
- 价格匹配逻辑见 `PointService.GetImageGenerateCostAsync`：仅当调用方显式传入 `quality` 时才把 `quality_code` 加入查询条件。

冒烟检查建议：

1. 注册新用户后确认 `sys_user.point_balance=50`，并存在 `source=register` 的积分流水。
2. 登录后调用 `POST /api/points/sign-in`，确认余额增加 25；同日重复调用应返回“今日已签到”。
3. 调用 `GET /api/points/balance`，确认返回 `availablePoints`、`hasSignedInToday`、`todaySignInPoints`。
4. 用价格表中存在的组合创建生图任务，确认余额减少并写入 `source=image_generate` 流水。
5. 模拟任务失败时，确认写入 `source=image_refund` 返还流水。

## GPT Image2 生图配置

GPT Image2 生图接口 `POST /api/ai/images/generate` 和后台任务接口 `POST /api/ai/images` 需要登录和 `AiImage.Generate` 权限。参考图和生成图保存在非静态目录 `private-media/ai`，通过 `/api/media/ai/...` 鉴权下载；有参考图时后端会先校验当前用户所有权，再调用 OpenAI `/images/edits` 并以 multipart `image[]` 提交；无参考图时调用 `/images/generations`。

直接生成接口会先写入 `ai_image_task`，再由后台 worker 调用外部生图服务；接口侧最多等待 5 分钟返回图片结果。用户关闭网页只会断开 HTTP 响应，不会取消已经入队的后台任务，完成后的图片仍可在历史记录中按 `taskId` 查询。

主备路由统一配置在 `ai_image_model_config`：

- `route_role=primary`：主路由；`route_role=fallback`：备用路由
- `model_code + resolution_code + route_role` 唯一，每个分辨率最多各一条主、备记录
- 每条路由独立保存 `provider_model`、`base_url`、`api_key`、`text_to_image_path` 和 `image_to_image_path`
- 主备均启用时先请求主路由；网络异常、`OpenAI.PrimaryTimeoutSeconds` 超时、HTTP 错误、无效响应或不可用图片都会触发备用路由
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
- 传 `referenceImageUrls` 调试时，确认数组长度不超过 6，且每个 URL 都由当前用户的上传接口返回并能解析到 `private-media/ai` 下的文件。
- 后台任务成功后，`GET /api/ai/images/{id}` 应返回 `status=1` 和非空 `resultUrls`；数据库中的 `ai_image_task.result_urls` 应是图片 URL 的 JSON 数组。
- `imageCount=4` 时，创建接口应返回 4 个不同的 `ids`，数据库应新增 4 条 `image_count=1` 记录；四条预留流水与总积分扣减应在同一事务提交。
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

## Nano Banana2 生图配置

Nano Banana2 生图接口 `POST /api/ai/images/nanoBananaImage/generate` 需要登录和 `AiImage.Generate` 权限。不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图并验证私有媒体所有权。请求体可传 `size`，也可传 `resolutionCode` + `aspectRatioCode`；`aspectRatioCode=auto` 时后端不读取参考图尺寸或推导具体画幅比例，而是把上游 `size` 直接传为 `auto`，画幅由上游服务自行决定。积分价格仍按业务 `resolutionCode` 档位匹配，和画幅比例无关。生成图片同样保存到 `private-media/ai` 并通过鉴权接口下载。

Nano Banana2 没有官方备用渠道，当前只配置 `route_role=primary`。后端读取 `ai_image_model_config` 中当前启用的 `nano-banana-2` 渠道并直连。

## AI 成本控制发布检查

- 既有数据库先停掉所有 API/Worker 实例，再执行 `docs/migrations/20260808_p0_ai_cost_control.sql`；执行前必须人工处理历史 `status IN (0, 3)` 任务并核对其积分流水。
- 配置 `AiCostControl`：用户每日图片/积分上限、用户并发、全局积压、Provider 全局并发、幂等 TTL、租约 TTL 和熔断阈值。所有值必须大于 0；`MaxConcurrentTasksPerUser` 至少应覆盖允许的单次 GPT `imageCount`（当前最大为 10）。
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
