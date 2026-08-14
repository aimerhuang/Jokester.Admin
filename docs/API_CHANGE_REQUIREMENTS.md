# Jokester AI iOS：API 改造需求清单

> 文档状态：实施基线（本地已验证，待合并与部署）
> 更新日期：2026-08-13
> 当前 API：`https://ai.jokester.cc:8011/api/`
> 当前接口文档：`https://ai.jokester.cc:8011/swagger/index.html`

> 截至 2026-08-13，API-01 至 API-14 已在当前工作区实现并通过本地自动化测试、Release 构建和发布物检查，但这些改动仍未提交、未合并到远端，也未部署。本文中“当前没有”“需要修改”等表述保留为改造前基线；现役接入契约以 [integration-guide.md](./integration-guide.md) 为准，内部机制以 [architecture.md](./architecture.md) 为准，上线步骤以 [runbook.md](./runbook.md) 为准。
>
> 2026-08-13 生产核查：公网 Swagger 仍是 76 路由的旧服务且不含法律文档接口，生产 `legal_document` 为空。当前 Release 暂存产物已验证 90 路由；恢复 Web 注册必须先取得隐私政策和服务条款正式审批值，按 runbook 运行 `--configure-legal-documents`，再以管理员权限部署该产物并完成公网验收。AI processing 告知可延后，但在其审批并启用前所有第三方 AI 生图保持 fail-closed。

## 1. 改造目标

本清单用于解决 iOS 客户端当前存在的 App Store 支付与账号合规、第三方 AI 数据授权、Token 刷新竞争、图片域名鉴权泄露、图片上传隐私和动态 JSON 难以维护等问题。

优先级定义：

- **P0**：上架阻断、安全高风险或可能造成用户资产错误，必须在首个 TestFlight 外测版本前完成。
- **P1**：显著影响稳定性、性能或运维效率，正式发布前完成。
- **P2**：体验和长期维护优化，可在后续小版本实施。

## 2. API 改造总览

| 编号 | 优先级 | 模块 | 改造前接口/状态 | 目标改造 |
|---|---|---|---|---|
| API-01 | P0 | iOS 内购 | `/points/recharge/packages`、`/orders` 返回外部支付地址 | 套餐增加 Apple Product ID；新增 StoreKit 交易验证接口；iOS 不再返回或使用外部支付链接 |
| API-02 | P0 | App Store Server | 当前没有 Apple 服务端通知接口 | 新增 App Store Server Notifications V2 接收、验签和幂等履约接口 |
| API-03 | P0 | 账户删除 | 当前没有用户自助删除账户接口 | 新增删除申请、状态查询和撤销接口；同时删除或匿名化用户关联数据 |
| API-04 | P0 | 法律文档 | 当前没有隐私政策/条款版本接口 | 新增当前法律文档接口，注册请求携带已同意的版本号 |
| API-05 | P0 | AI 数据授权 | 当前没有第三方 AI 处理授权记录 | 新增授权查询/提交接口；生图接口强制校验有效授权版本 |
| API-06 | P0 | 会话刷新 | `/auth/refresh` 缺少单会话标识和稳定错误码 | 返回 Session ID、双 Token 过期时间；支持安全轮换和短暂并发宽限；规范会话失效错误 |
| API-07 | P0 | 图片安全 | 生图和上传接口以任意 URL 作为图片标识 | 改为服务端 Asset ID；只返回同源地址或短期签名 URL；校验资源归属，避免 SSRF/越权 |
| API-08 | P1 | 模型路由 | GPT 与 Nano Banana 使用不同创建接口，客户端靠模型名称猜测 | 统一任务创建入口，由服务端按模型配置路由；模型接口返回能力字段 |
| API-09 | P1 | 图片上传 | 只返回不固定的 URL 字段，Schema 不明确 | 校验真实文件类型、像素和大小，剥离 EXIF；返回强类型 Asset Schema 和缩略图 |
| API-10 | P1 | 任务轮询 | 固定 3 秒轮询，状态使用数字且缺少建议间隔 | 返回字符串状态、进度、`pollAfterSeconds` 或 `Retry-After` |
| API-11 | P1 | 响应 Schema | 多数响应在 Swagger 中没有明确 Schema | 所有移动端接口补齐强类型请求、响应、分页和错误模型 |
| API-12 | P1 | 限流/风控 | 客户端仅本地倒计时，缺少统一限流契约 | 登录、验证码、生成、兑换返回 `429`、`Retry-After` 和机器可读错误码 |
| API-13 | P1 | 内容安全 | 客户端无法获得结构化拦截结果 | 服务端强制审核提示词和生成结果；返回可展示的内容安全错误码 |
| API-14 | P2 | 移动端配置 | Base URL 和部分能力在客户端硬编码 | 新增公开的移动端配置接口，支持最低版本、维护模式、法律文档版本和功能开关 |

## 3. 通用接口规范

### 3.1 统一响应包

成功响应：

```json
{
  "code": 0,
  "message": "success",
  "requestId": "01J...",
  "data": {}
}
```

失败响应：

```json
{
  "code": "SESSION_REVOKED",
  "message": "登录状态已失效，请重新登录",
  "requestId": "01J...",
  "details": null
}
```

约束：

- 时间统一使用带时区的 ISO 8601 UTC 字符串。
- 金额不得使用浮点数作为结算依据；返回 `priceMinorUnits` 和 `currency`。
- 所有写操作支持 `Idempotency-Key` 请求头或明确的 `clientRequestId`。
- 错误响应必须使用稳定的机器可读 `code`，不能要求客户端解析中文 `message`。
- Swagger 必须声明每个 `200/400/401/403/409/412/429/500` 响应 Schema。
- 分页统一使用 `pageIndex/pageSize/items/total/hasMore`，字段大小写保持一致。

### 3.2 建议错误码

| HTTP | code | 使用场景 |
|---|---|---|
| 400 | `VALIDATION_ERROR` | 参数格式或范围错误 |
| 401 | `ACCESS_TOKEN_EXPIRED` | Access Token 过期，可尝试刷新 |
| 401 | `REFRESH_TOKEN_EXPIRED` | Refresh Token 过期，必须重新登录 |
| 401 | `SESSION_REVOKED` | 会话被退出、删除或风控撤销 |
| 403 | `RESOURCE_FORBIDDEN` | 资源不属于当前用户 |
| 409 | `IDEMPOTENCY_CONFLICT` | 相同幂等键对应不同请求 |
| 409 | `APPLE_TRANSACTION_OWNED_BY_OTHER_USER` | Apple 交易已绑定其他账户 |
| 412 | `AI_CONSENT_REQUIRED` | 缺少当前版本的第三方 AI 数据授权 |
| 422 | `PROMPT_BLOCKED` | 提示词未通过内容安全检查 |
| 429 | `RATE_LIMITED` | 请求过于频繁，并返回 `Retry-After` |

## 4. P0 接口详细设计

### API-01：iOS StoreKit 套餐与交易履约

#### 4.1.1 修改套餐接口

保留：

```http
GET /api/points/recharge/packages?platform=ios
Authorization: Bearer <access-token>
```

建议响应：

```json
{
  "code": 0,
  "data": {
    "items": [
      {
        "code": "credits_100",
        "name": "100 积分",
        "points": 100,
        "purchaseMethod": "apple_iap",
        "appleProductId": "cc.jokester.ai.credits.100",
        "appleProductType": "consumable",
        "sort": 10,
        "enabled": true
      }
    ]
  }
}
```

修改规则：

- 当 `platform=ios` 时，不返回 `purchaseUrl/paymentUrl/payUrl`。
- 服务端套餐积分与 Apple Product ID 建立唯一映射；积分数不能由客户端提交。
- iOS 价格和币种以 StoreKit `Product.displayPrice` 为准，接口中的价格只允许作为运营展示参考。
- 旧的 `/points/recharge/orders` 保留给 Web/Android，但 iOS 客户端不得调用。

#### 4.1.2 新增 Apple 交易履约接口

```http
POST /api/points/recharge/apple/transactions
Authorization: Bearer <access-token>
Idempotency-Key: <UUID>
Content-Type: application/json
```

请求：

```json
{
  "transactionId": "2000000123456789",
  "productId": "cc.jokester.ai.credits.100",
  "appAccountToken": "4c6e04ff-2f53-4e0c-a5e4-4fb3af3ac83a"
}
```

响应：

```json
{
  "code": 0,
  "data": {
    "transactionId": "2000000123456789",
    "orderNo": "IOS202608120001",
    "status": "fulfilled",
    "productId": "cc.jokester.ai.credits.100",
    "addedPoints": 100,
    "availablePoints": 360,
    "fulfilledAt": "2026-08-12T15:30:00Z"
  }
}
```

服务端实现要求：

1. 不信任客户端提交的价格、积分、交易环境和购买时间。
2. 使用 App Store Server API 按 `transactionId` 获取并验证交易。
3. 校验 Bundle ID、Product ID、交易状态、撤销状态和 `appAccountToken`。
4. `transactionId` 建立数据库唯一索引，同一用户重复提交返回首次履约结果。
5. 同一交易绑定其他用户时返回 `409 APPLE_TRANSACTION_OWNED_BY_OTHER_USER`。
6. 积分入账和交易落库必须处于同一数据库事务。
7. 服务端确认履约成功后，iOS 才调用 StoreKit `transaction.finish()`。

### API-02：App Store Server Notifications V2

新增：

```http
POST /api/integrations/apple/app-store-server-notifications/v2
Content-Type: application/json
```

请求由 Apple 发送：

```json
{
  "signedPayload": "eyJ..."
}
```

实现要求：

- 此接口不使用用户 Bearer Token，但必须完整验证 Apple JWS 证书链和 Payload。
- 保存通知 UUID 并建立唯一索引，保证重复通知幂等。
- 处理退款、撤销、测试通知和生产/沙盒环境。
- 消耗型积分退款策略需产品和财务确认：余额足够时扣回；不足时记录负债或冻结生成能力，不能静默忽略。
- 返回 `200` 仅表示通知已安全接收；内部处理失败进入可重试队列和告警。

### API-03：账户删除

新增删除申请：

```http
POST /api/auth/account-deletion/requests
Authorization: Bearer <access-token>
Content-Type: application/json
```

```json
{
  "currentPassword": "用户当前密码",
  "confirmation": "DELETE",
  "clientRequestId": "UUID",
  "reason": "optional"
}
```

```json
{
  "code": 0,
  "data": {
    "requestId": "ADR202608120001",
    "status": "scheduled",
    "requestedAt": "2026-08-12T15:30:00Z",
    "scheduledDeletionAt": "2026-08-19T15:30:00Z",
    "canCancel": true
  }
}
```

新增查询和撤销：

```http
GET    /api/auth/account-deletion/requests/current
DELETE /api/auth/account-deletion/requests/{requestId}
```

删除范围至少包括：

- 用户资料、头像、Access Token、Refresh Token 和所有设备会话。
- AI 任务、生成图片、上传的参考图片和收藏记录。
- 提示词使用事件及可识别的操作日志。
- 可依法保留的财务记录必须匿名化，并在隐私政策中说明保留范围和期限。

安全要求：

- 要求近期登录；超过规定时间返回 `REAUTH_REQUIRED`。
- 创建申请后立即撤销全部会话，或明确说明宽限期内的账户状态。
- 后台删除任务必须可重试、可审计，并在完成后发送邮件通知。

### API-04：法律文档和注册同意

新增公开接口：

```http
GET /api/legal/documents/current?platform=ios&locale=zh-CN
```

```json
{
  "code": 0,
  "data": {
    "privacyPolicy": {
      "version": "2026-08-01",
      "url": "https://ai.jokester.cc/legal/privacy/ios",
      "effectiveAt": "2026-08-01T00:00:00Z",
      "requiresReconsent": false
    },
    "termsOfService": {
      "version": "2026-08-01",
      "url": "https://ai.jokester.cc/legal/terms/ios",
      "effectiveAt": "2026-08-01T00:00:00Z",
      "requiresReconsent": false
    },
    "aiProcessingNotice": {
      "version": "2026-08-01",
      "url": "https://ai.jokester.cc/legal/ai-processing/ios",
      "providerCodes": ["openai", "google"]
    }
  }
}
```

修改注册请求 `/api/auth/register`：

```json
{
  "userName": "demo2026",
  "nickName": "Demo",
  "password": "***",
  "email": "demo@example.com",
  "emailCode": "123456",
  "acceptedPrivacyPolicy": true,
  "privacyPolicyVersion": "2026-08-01",
  "acceptedTermsOfService": true,
  "termsOfServiceVersion": "2026-08-01"
}
```

服务端必须拒绝缺少同意或版本失效的注册请求，不能只依赖客户端勾选状态。

### API-05：第三方 AI 数据处理授权

新增查询：

```http
GET /api/users/me/consents
Authorization: Bearer <access-token>
```

新增或更新授权：

```http
PUT /api/users/me/consents/ai-processing
Authorization: Bearer <access-token>
Content-Type: application/json
```

```json
{
  "accepted": true,
  "documentVersion": "2026-08-01",
  "providerCodes": ["openai", "google"],
  "clientPlatform": "ios"
}
```

```json
{
  "code": 0,
  "data": {
    "accepted": true,
    "documentVersion": "2026-08-01",
    "acceptedAt": "2026-08-12T15:30:00Z",
    "providerCodes": ["openai", "google"]
  }
}
```

约束：

- `/ai/images` 在调用任何第三方 AI 前必须验证当前用户已接受最新必要版本。
- 未同意返回 `412 AI_CONSENT_REQUIRED`，同时返回当前文档版本和 URL。
- 记录授权版本、时间、用户、客户端平台和撤回时间，不记录不必要的设备指纹。
- 若不同模型对应不同提供商，授权提示必须与实际选择的提供商一致。

### API-06：会话刷新契约

现有接口保留：

```http
POST /api/auth/refresh
```

建议响应：

```json
{
  "code": 0,
  "data": {
    "sessionId": "SES202608120001",
    "accessToken": "***",
    "refreshToken": "***",
    "accessTokenExpiresAt": "2026-08-12T16:00:00Z",
    "refreshTokenExpiresAt": "2026-09-11T15:30:00Z"
  }
}
```

服务端修改要求：

- Refresh Token 哈希存储、单次轮换，并保留一个很短的并发宽限窗口。
- 同一旧 Refresh Token 在宽限窗口内重复请求时，应返回同一轮换结果，而不是撤销刚生成的新会话。
- 超过窗口的 Token 重放应撤销该 Session，并返回 `SESSION_REVOKED`。
- 登录和刷新响应都返回 `sessionId`、Access/Refresh 过期时间。
- 401 明确区分 Access Token 过期、Refresh Token 过期和 Session 被撤销。
- 可选新增 `POST /api/auth/logout-all`，用于密码修改和安全设置。

### API-07：图片 Asset ID、安全地址和资源归属

#### 上传接口修改

```http
POST /api/ai/images/upload
Authorization: Bearer <access-token>
Content-Type: multipart/form-data
```

建议响应：

```json
{
  "code": 0,
  "data": {
    "assetId": "AST202608120001",
    "url": "/api/assets/AST202608120001/content",
    "thumbnailUrl": "/api/assets/AST202608120001/thumbnail",
    "mimeType": "image/jpeg",
    "width": 1536,
    "height": 1024,
    "sizeBytes": 482931,
    "metadataStripped": true,
    "createdAt": "2026-08-12T15:30:00Z"
  }
}
```

生图请求逐步改为：

```json
{
  "prompt": "...",
  "modelCode": "gpt-image-1",
  "referenceAssetIds": ["AST202608120001"]
}
```

安全约束：

- 不允许客户端向 AI 服务提交任意远程 URL，避免服务端 SSRF。
- 每个 `assetId` 必须验证属于当前用户且未删除。
- 上传必须检查文件 Magic Bytes，而不是只信任扩展名和 MIME。
- 服务端限制文件大小、像素总数、宽高、帧数，拒绝图片炸弹和伪装文件。
- 服务端剥离 GPS、设备型号等 EXIF 信息后再持久化。
- 返回地址只能是当前 API 同源路径，或短期、只读、不可枚举的 HTTPS 签名 URL。
- 如果使用签名 URL，客户端不得附带 Bearer Token。
- 删除 AI 任务时需明确是否同步删除所有无引用的图片对象。

迁移方式：

1. 第一阶段同时接受 `referenceImageUrls` 和 `referenceAssetIds`。
2. 新版 iOS 只发送 Asset ID。
3. 监控旧字段使用率归零后，在下一个大版本移除 URL 输入。

## 5. P1 接口详细设计

### API-08：统一 AI 任务创建入口

目标：iOS 不再根据模型名称猜测调用 `/ai/images` 还是 `/ai/images/nanoBananaImage`。

修改建议：

- 统一使用 `POST /api/ai/images`。
- 服务端根据 `modelCode` 查找 Provider、能力和实际任务处理器。
- `/ai/images/nanoBananaImage` 保留一段兼容期并标记 Deprecated。

模型列表建议返回：

```json
{
  "code": "gpt-image-1",
  "displayName": "GPT Image",
  "providerCode": "openai",
  "capabilities": {
    "supportsReferenceImages": true,
    "maxReferenceImages": 6,
    "supportsQuality": true,
    "supportedImageCounts": [1, 2, 3, 4]
  },
  "resolutions": ["1k", "2k", "4k"],
  "qualities": ["low", "med", "high"],
  "aspectRatios": ["1:1", "16:9", "9:16"]
}
```

### API-09：图片上传处理

- 除 10 MB 文件限制外，新增像素总数和最大边限制。
- 统一将 HEIC/PNG/WebP 等转成后端标准存储格式，同时保留必要透明通道规则。
- 生成 256/512 像素缩略图供画廊使用，原图仅用于详情和下载。
- 响应必须具有 Swagger Schema，不再让客户端遍历任意 URL 字段。

### API-10：任务状态和轮询

任务详情增加：

```json
{
  "taskId": 123,
  "status": "processing",
  "progress": 45,
  "pollAfterSeconds": 5,
  "createdAt": "2026-08-12T15:30:00Z",
  "expiresAt": "2026-08-12T15:45:00Z",
  "assets": []
}
```

状态枚举：`queued / processing / succeeded / failed / cancelled`。

兼容期可以同时返回旧数字 `statusCode`，但新客户端只使用字符串 `status`。

### API-11：Swagger 强类型化

必须补齐 Schema 的移动端接口：

- `/auth/login`、`/auth/refresh`、`/auth/profile`、`/auth/register`。
- `/ai/images/models`、`parameters`、`pricing-options`、创建任务、任务详情、列表、上传和收藏。
- `/prompts`、`/prompts/{id}`、事件接口。
- `/points/balance`、签到、积分明细、充值套餐和 Apple 交易。
- 用户昵称、密码、头像和账户删除。
- 法律文档及用户授权。

### API-12：限流和验证码

以下接口必须由服务端限流，客户端倒计时不能作为安全控制：

- 登录：按账户、IP、设备会话维度限流，连续失败后启用 Swagger 已预留的验证码字段。
- 注册验证码：按邮箱和 IP 限流，响应返回 `retryAfterSeconds`。
- 生成任务：按用户并发数、分钟额度和积分余额限制。
- 充值兑换：按用户和 IP 限流，并记录失败审计日志。

### API-13：内容安全

- 敏感词和内容安全检查必须在服务端生图入口执行，不能只靠 iOS 预检。
- 拦截时返回 `422 PROMPT_BLOCKED` 和可展示的 `userMessage`，不得返回完整内部规则。
- 若提示词库包含第三方用户提交内容，应新增：

```http
POST /api/prompts/{id}/reports
POST /api/users/{id}/block
```

- 若提示词库全部是官方审核内容，应在数据模型中明确 `contentSource=curated`，并保留后台下架能力。

## 6. P2 移动端配置接口

```http
GET /api/mobile/config?platform=ios&appVersion=1.3.0&locale=zh-CN
```

```json
{
  "code": 0,
  "data": {
    "minimumSupportedVersion": "1.3.0",
    "latestVersion": "1.4.0",
    "maintenanceMode": false,
    "features": {
      "appleIap": true,
      "accountDeletion": true,
      "promptLibrary": true
    },
    "legalDocumentVersions": {
      "privacy": "2026-08-01",
      "terms": "2026-08-01",
      "aiProcessing": "2026-08-01"
    }
  }
}
```

该接口不能下发或改变原生可执行代码，只用于服务端能力、维护状态和版本提示。

## 7. 数据库与服务端内部改造

建议新增或调整的数据表：

- `user_sessions`：Session ID、Refresh Token 哈希、轮换状态、过期时间和撤销时间。
- `apple_iap_products`：套餐编码、Apple Product ID、积分、启用状态和环境。
- `apple_transactions`：Transaction ID 唯一索引、用户、Product ID、履约状态、原始 JWS 摘要。
- `apple_server_notifications`：通知 UUID 唯一索引、类型、处理状态和重试次数。
- `user_consents`：用户、授权类型、文档版本、Provider、接受/撤回时间。
- `legal_documents`：文档类型、版本、语言、平台、URL、生效时间。
- `account_deletion_requests`：申请状态、计划时间、完成时间和失败原因。
- `media_assets`：Asset ID、所有者、存储 Key、缩略图 Key、真实 MIME、尺寸、哈希和删除状态。

数据库必须保证：Apple 交易履约、积分流水和余额变更在同一事务内完成。

## 8. 兼容和发布策略

1. 新接口先部署，旧 Android/Web 接口保持兼容。
2. 所有新能力使用服务端 Feature Flag。
3. iOS TestFlight 首先启用法律文档、AI 授权、Asset ID 和单会话刷新。
4. Apple IAP 沙盒验收完成后再对生产用户开放。
5. iOS 正式版发布并达到目标升级率后，停止 iOS 外部订单创建。
6. 监控旧图片 URL 字段和 Nano Banana 专用接口使用率，确认归零后再废弃。

## 9. API 验收标准

- 同一 Apple Transaction ID 重放 100 次只能入账一次。
- 10 个并发 401 最多产生一次有效 Refresh Token 轮换结果，不得误撤销新会话。
- 任意用户不能读取、引用或删除其他用户的 Asset ID。
- 非图片、超大像素、伪造 MIME 和包含危险元数据的文件被安全拒绝或清洗。
- 未接受最新 AI 数据处理声明时，所有第三方 AI 请求均在服务端调用前被拒绝。
- 账户删除完成后，用户无法登录，所有会话失效，关联图片不可访问。
- 所有移动端接口均能从 Swagger 生成明确的 Swift Codable 类型。
- `429`、`401`、`409`、`412` 和 `422` 均有稳定错误码及自动化测试。
