# 积分充值与兑换接口

本文是积分套餐、Web/Android 外部订单、兑换码和 iOS StoreKit 履约流程的接口契约。所有接口都使用统一的 `{ code, message, requestId, data }` 响应结构，并要求：

```http
Authorization: Bearer <accessToken>
```

## 用户接口

### 查询套餐

```http
GET /api/points/recharge/packages?platform=web
```

返回当前启用套餐。关键字段：

- `code`：套餐编码，当前为 `monthly`、`trial`、`basic`、`value`
- `points`：当前用户购买或兑换该套餐实际可获得的积分
- `priceAmount` / `priceMinorUnits` / `currency`：展示金额、最小货币单位和币种；结算不得依赖浮点金额
- `validityDays`：展示用有效期；`null` 表示永久
- `badgeCode` / `isFeatured`：前端推荐标识
- `isFirstPurchaseEligible`：当前用户是否仍满足首充积分资格
- `purchaseEnabled`：套餐是否配置了有效的 HTTP(S) 购买地址
- `benefits`：套餐权益文案数组

首充体验包首次兑换为 200 积分，同一用户再次兑换该套餐时为 100 积分。

### iOS StoreKit 套餐

```http
GET /api/points/recharge/packages?platform=ios
```

只返回已在 `apple_iap_product` 启用映射的套餐。每项包含：

- `purchaseMethod=apple_iap`
- `appleProductId` 和 `appleProductType=consumable`
- 服务端固定的 `points`、`sort`、`enabled`

iOS 响应不会返回 `purchaseUrl`。接口中的价格仅供运营展示，客户端展示和购买价格以 StoreKit `Product.displayPrice` 为准。`data` 当前仍直接返回数组，以兼容已有 Web/Android 客户端。

### iOS 交易履约

```http
POST /api/points/recharge/apple/transactions
Authorization: Bearer <accessToken>
Idempotency-Key: <UUID>
Content-Type: application/json

{
  "transactionId": "<StoreKit transaction id>",
  "productId": "<configured product id>",
  "appAccountToken": "<profile appleAppAccountToken>"
}
```

后端按 `transactionId` 调用 App Store Server API，并验证 JWS 证书链、Bundle ID、Product ID、环境、消耗型商品、数量、撤销状态和当前用户确定性 `appAccountToken`。客户端不能提交积分、价格或交易环境。履约结果包含 `orderNo`、`addedPoints`、`availablePoints` 和 UTC `fulfilledAt`；只有服务端确认成功后，iOS 才调用 StoreKit `transaction.finish()`。

`transaction_id` 全局唯一；同用户、同幂等键、同 payload 返回首次履约结果，不重复入账。同 key 不同 payload 返回 `409 IDEMPOTENCY_CONFLICT`，交易已属于其他用户返回 `409 APPLE_TRANSACTION_OWNED_BY_OTHER_USER`。交易行、用户余额和 `source=apple_iap` 积分流水在同一 MySQL 事务提交。

### 创建充值订单

```http
POST /api/points/recharge/orders
Content-Type: application/json

{
  "packageCode": "basic"
}
```

该接口只供 Web/Android 使用。它创建一个 24 小时有效的待支付订单，不会直接增加积分，也不会伪造支付成功。
响应中的 `purchaseUrl` 有值时，前端可跳转到该地址；没有配置购买地址时该字段为
`null`，前端应展示 `orderNo` 和待支付状态。

订单状态：

- `0`：待支付
- `1`：已确认支付并签发兑换码
- `2`：兑换码已核销
- `3`：已取消
- `4`：已过期

### 兑换积分

```http
POST /api/points/recharge/redeem
Content-Type: application/json

{
  "code": "JAI-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX"
}
```

兑换码区分大小写，只能成功核销一次。成功响应返回 `addedPoints`、
`availablePoints` 和 `redeemedAt`。核销、余额增加及
`sys_user_point_detail` 的 `source=recharge` 流水在同一数据库事务中完成。

## 管理员签发兑换码

自定义积分批量签发：

```http
POST /api/points/recharge/admin/codes
Content-Type: application/json

{
  "points": 500,
  "count": 20,
  "expiresAt": "2026-09-01T00:00:00+08:00"
}
```

按套餐签发或履约订单：

```http
POST /api/points/recharge/admin/codes
Content-Type: application/json

{
  "packageCode": "basic",
  "count": 1,
  "orderNo": "R...",
  "expiresAt": "2026-09-01T00:00:00+08:00"
}
```

该接口只允许超级管理员调用：

- `count` 范围为 1 到 100
- `points` 与 `packageCode` 必须二选一；自定义 `points` 范围为 1 到 1,000,000
- 服务端会在数据库事务内重新核验调用者仍为启用、未删除的超级管理员
- 按用户限制为每小时 5 次、每天 20 次；按 IP 限制为每小时 10 次、每天 40 次
- 自定义积分码不绑定套餐或订单，兑换时直接按 `points` 入账
- 传 `orderNo` 时 `count` 必须为 1，订单必须待支付、未过期且套餐一致
- 传 `orderNo` 时必须使用 `packageCode`，不能同时传自定义 `points`
- `expiresAt` 可选；不传表示兑换码不设置过期时间
- 传有效订单后，接口会将订单置为已确认并绑定兑换码
- 响应中的 `codes` 是明文兑换码唯一一次返回，必须由调用方安全交付和保存

服务端只保存兑换码的 SHA-256 哈希和掩码，不保存明文。

## Apple 服务端通知与退款

App Store Server Notifications V2 回调地址：

```http
POST /api/integrations/apple/app-store-server-notifications/v2
```

此入口匿名但不信任调用方：完整验证 Apple `signedPayload` 的 ES256 签名、`x5c` 证书链、Bundle ID 和环境。服务端只保存通知 UUID、类型、交易 ID 和 payload SHA-256，不保存原始 JWS；通知 UUID 唯一保证幂等。

- `TEST` 通知安全接收后直接完成。
- `REFUND` / `REVOKE` 再验证内层交易 JWS，并在事务中扣回积分、更新交易和通知。
- 余额不足时扣到 0，并在 `apple_iap_debt` 记录差额；存在未结清债务时拒绝新生图任务。
- 内部处理失败标记为 `failed`，后台 Worker 重试；缺少退款交易 ID 的通知不会误标记为完成。

## 购买地址配置

购买地址来自 `point_recharge_package.purchase_url`，只接受绝对 HTTP(S) URL，
并支持以下占位符：

- `{orderNo}`
- `{packageCode}`
- `{userId}`

示例：

```sql
UPDATE point_recharge_package
SET purchase_url = 'https://pay.example.com/checkout?orderNo={orderNo}&packageCode={packageCode}&userId={userId}'
WHERE package_code = 'basic';
```

当前初始化脚本中的四个套餐默认都未配置 `purchase_url`。接入真实支付平台时，
应增加签名校验和服务端支付回调，再由可信回调确认订单并签发兑换码；不能由前端
直接把待支付订单改为已支付。

## 数据库升级

新数据库直接执行根目录 `jokester.admin.sql`。已有数据库执行：

```text
docs/migrations/20260809-add-point-recharge.sql
docs/migrations/20260812-ios-api-upgrade.sql
```

第一个迁移创建 `point_recharge_package`、`point_recharge_order`、`point_redeem_code` 并写入四档默认套餐。第二个迁移创建 Apple 商品、交易、通知和退款负债表；不会写入任何真实 Product ID。部署方必须在 Apple 后台建好消耗型商品后，人工将审核确认的 Product ID 映射到现有套餐。
