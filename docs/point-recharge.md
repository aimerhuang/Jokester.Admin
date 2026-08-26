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

Web/Android 固定返回当前启用的四档套餐：`monthly`、`trial`、`basic`、`value`。管理员签发兑换码时，前端也必须以该接口返回的四档套餐作为 `POST /api/points/recharge/admin/codes` 的 `packageCode` 动态选项，不要在管理端另行硬编码套餐列表。关键字段：

- `code`：套餐编码，当前为 `monthly`、`trial`、`basic`、`value`
- `points`：当前用户购买或兑换该套餐实际可获得的积分
- `priceAmount` / `priceMinorUnits` / `currency`：展示金额、最小货币单位和币种；结算不得依赖浮点金额
- `validityDays`：到账积分有效期；`null` 表示永久
- `badgeCode` / `isFeatured`：前端推荐标识
- `isFirstPurchaseEligible`：当前用户是否仍满足首充积分资格
- `purchaseEnabled`：套餐是否配置了有效的 HTTP(S) 购买地址
- `benefits`：套餐权益文案数组

套餐积分和到账后的有效期如下：

- `monthly`：5000 积分，自兑换核销或 Apple 履约到账时起 30 天有效
- `trial`：首次兑换 200 积分，同一用户续充为 100 积分，积分永久有效
- `basic`：1000 积分，永久有效
- `value`：3600 积分，永久有效

`monthly` 到账时还会在同一事务中创建独立的 `monthly_vip` 会员权益。该权益不依赖剩余积分：积分提前用完后仍持续到原到期时间；签到和其他套餐不会产生 VIP。登录、刷新和资料接口只返回当前有效权益，并在多笔有效权益中返回最晚到期时间。

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

后端按 `transactionId` 调用 App Store Server API，并验证 JWS 证书链、Bundle ID、Product ID、环境、消耗型商品、数量、撤销状态和当前用户确定性 `appAccountToken`。客户端不能提交积分、价格或交易环境。履约结果包含 `orderNo`、`addedPoints`、`availablePoints`、可空 UTC `expiresAt` 和 UTC `fulfilledAt`；只有服务端确认成功后，iOS 才调用 StoreKit `transaction.finish()`。

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
`availablePoints`、可空 UTC `expiresAt` 和 `redeemedAt`。核销、余额增加及
`sys_user_point_detail` 的 `source=recharge` 流水在同一数据库事务中完成。

### 积分有效期与扣减顺序

套餐积分按到账批次保存剩余数量和有效期；永久套餐批次的到期时间为空。`monthly` 的 5000 积分从兑换核销或 Apple 履约到账时开始计算 30 天，不以兑换码签发或 Web/Android 外部订单创建为起点；其他三档套餐积分永久有效。

生图扣分默认按以下顺序消费：

1. 尚未过期的限时套餐积分
2. 当日签到积分
3. 永久积分

同一优先级存在多个批次时，优先扣除最早到期的批次；无到期时间的套餐永久批次按到账顺序消费，历史和非套餐永久余额最后消费。余额查询、登录/刷新/资料、签到和生图扣分入口都必须在各自业务事务内先结算已到期批次，再读取余额、赠送或扣减积分，避免过期积分继续参与可用余额和生图预留。

生图任务失败或超时退款时，积分必须按原扣减明细退回原积分批次，不能退成新的永久积分，也不能重置或延长原批次有效期。原批次已到期的退款仍保留其原到期时间，并在同一事务中按过期规则结算。

Apple 退款发生时，如果对应月卡积分已被尚未结算的生图任务预留，该部分先记录为延后追扣，不立即扣除用户其他积分，也不立即形成欠款。任务失败时只写唯一的 `image_refund` 审计流水，不恢复已撤销的月卡额度；任务成功或部分成功时，再对实际完成部分追扣当前余额，不足部分写入 `apple_iap_debt`。延后追扣未结算期间拒绝创建新的生图任务。

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
- `REFUND` / `REVOKE` 再验证内层交易 JWS，并在事务中追扣该 Apple 入账尚未因有效期结算掉的可撤销积分、更新交易和通知；已过期额度不再追扣，也不形成债务。
- 月卡退款同时按原 Apple 交易业务键撤销对应会员权益，不影响用户通过其他兑换码或交易获得的有效月卡。
- 追扣可撤销积分时余额不足则扣到 0，并在 `apple_iap_debt` 记录差额；存在未结清债务时拒绝新生图任务。
- 已由活动生图任务预留的月卡额度记入 `sys_user_point_bucket_usage` 的延后追扣字段；任务结算前同样拒绝新生图，避免继续扩大退款信用敞口。
- 内部处理失败标记为 `failed`，后台 Worker 持续重试，并优先处理重试次数较少的通知；不得用固定重试次数上限把真实退款永久丢弃。缺少退款交易 ID 的通知不会误标记为完成，也不会阻塞后续新通知。

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
docs/migrations/20260819-add-expiring-point-buckets.sql
docs/migrations/20260820-add-user-membership-entitlements.sql
```

- `20260809-add-point-recharge.sql` 创建充值套餐、订单、兑换码并写入四档默认套餐。
- `20260812-ios-api-upgrade.sql` 创建 Apple 商品、交易、通知和退款负债等移动端表，不写入真实 Product ID。
- `20260819-add-expiring-point-buckets.sql` 增加套餐/签到积分批次、有效期、原批次退款和 Apple 延后追扣结构；空到期时间表示永久批次。
- `20260820-add-user-membership-entitlements.sql` 创建独立会员权益来源账本，并从仍有效的 Web 月卡核销及未退款 Apple 月卡履约回填动态 VIP 权益。

执行 `20260819` 前必须结算活动生图任务并停止全部 API/Worker 写入，禁止旧、新节点滚动混写。迁移只按保守下界回填停写时点当天仍能证明属于签到赠送的积分，不会自动重分类历史月卡或 Apple 履约；legacy Apple 退款只扣未追踪余额，不会扣后来到账的新批次，不足部分进入 `apple_iap_debt`。生产库存在历史月卡核销、Apple 履约或未结算生图任务时，发布前必须人工对账。

同时确认 `monthly` 为 5000 分、30 天且没有 `repeat_points`，`trial/basic/value.validity_days` 为空。与永久权益冲突的历史订单或兑换码快照会被新后端 fail-closed，必须先人工校正。Apple 后台审核通过的真实 Product ID 仍由部署方维护到现有套餐映射，不写入仓库。
