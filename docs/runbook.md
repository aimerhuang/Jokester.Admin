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

GPT Image2 生图接口 `POST /api/ai/images/generate` 和后台任务接口 `POST /api/ai/images` 需要登录和 `AiImage.Generate` 权限。生成图片会保存到 `wwwroot/ai-images/{yyyyMM}/`，并通过 `app.UseStaticFiles()` 暴露为静态 URL。请求体可选 `referenceImageUrls`，最多 6 张；有参考图时后端会调用 OpenAI `/images/edits`，把内部 URL 解析为 `wwwroot` 下的本地文件并以 multipart `image[]` 提交；无参考图时调用 `/images/generations`。

直接生成接口会先写入 `ai_image_task`，再由后台 worker 调用外部生图服务；接口侧最多等待 5 分钟返回图片结果。用户关闭网页只会断开 HTTP 响应，不会取消已经入队的后台任务，完成后的图片仍可在历史记录中按 `taskId` 查询。

必要配置：

- `OpenAI.ApiKey`

可选配置：

- `OpenAI.BaseUrl`，默认 `https://api.openai.com/v1`，如果使用中转服务可改成对应 `/v1` 基地址；不要配置成 `/images/generations` 或 `/images/edits` 完整端点，后端会按是否有参考图自动追加对应路径
- `OpenAI.ImageModel`，默认 `gpt-image-2`

模型映射约束：请求中的 `modelCode` 是业务模型编码，后端必须按 `ai_image_model_config.model_code` + `resolution_code` 查询配置，并把匹配记录的 `provider_model` 原样作为上游请求的 `model` 参数；不要在代码里把 `provider_model` 归一化或替换成 `model_code`。例如 `modelCode=gpt-image-2`、`resolutionCode=4k` 应读取数据库中 4K 配置行的 `provider_model`（如 `gpt-image-2-4k`），图生图 `/images/edits` 和文生图 `/images/generations` 都遵循该规则。

示例：

```powershell
dotnet user-secrets set "OpenAI:ApiKey" "<your-openai-api-key>" --project .\jokester.admin\jokester.admin.csproj
dotnet user-secrets set "OpenAI:BaseUrl" "https://api.openai.com/v1" --project .\jokester.admin\jokester.admin.csproj
dotnet user-secrets set "OpenAI:ImageModel" "gpt-image-2" --project .\jokester.admin\jokester.admin.csproj
```

排查要点：

- 直接生成响应应包含 `taskId` 和 `url`，访问该 URL 应返回图片文件；如果前端中途退出，应通过历史记录接口确认同一个 `taskId` 最终写入 `resultUrls`。
- 传 `referenceImageUrls` 调试时，确认数组长度不超过 6，且每个 URL 都是内部静态图片 URL，并能解析到 `wwwroot` 下的本地文件。
- 后台任务成功后，`GET /api/ai/images/{id}` 应返回 `status=1` 和非空 `resultUrls`；数据库中的 `ai_image_task.result_urls` 应是图片 URL 的 JSON 数组。
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

Nano Banana2 生图接口 `POST /api/ai/images/nanoBananaImage/generate` 需要登录和 `AiImage.Generate` 权限。不传 `imageUrls` 或传空数组时执行文生图；传 `imageUrls` 时执行图生图。请求体可传 `size`，也可传 `resolutionCode` + `aspectRatioCode`；`aspectRatioCode=auto` 时后端不读取参考图尺寸或推导具体画幅比例，而是把上游 `size` 直接传为 `auto`，画幅由上游服务自行决定。积分价格仍按业务 `resolutionCode` 档位匹配，和画幅比例无关。生成图片会保存到 `wwwroot/nano-banana2-images/{yyyyMM}/`，并通过 `app.UseStaticFiles()` 暴露为静态 URL。

必要配置：

- `NanoBanana2.ApiKey`
- `NanoBanana2.BaseUrl`

可选配置：

- `NanoBanana2.ImageModel`，默认 `nano-banana-2`
- `NanoBanana2.TextToImagePath`，默认 `/images/generations`
- `NanoBanana2.ImageToImagePath`，默认 `/images/edits`

`.env` 示例：

```dotenv
NanoBanana2__BaseUrl=https://your-nano-banana2-provider.example/v1
NanoBanana2__ApiKey=<your-nano-banana2-api-key>
NanoBanana2__ImageModel=nano-banana-2
NanoBanana2__TextToImagePath=/images/generations
NanoBanana2__ImageToImagePath=/images/edits
```
