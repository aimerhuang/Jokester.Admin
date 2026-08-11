# 积分充值与兑换接口

本文是积分套餐、充值订单和兑换码流程的接口契约。所有接口都使用统一的
`{ code, message, data }` 响应结构，并要求：

```http
Authorization: Bearer <accessToken>
```

## 用户接口

### 查询套餐

```http
GET /api/points/recharge/packages
```

返回当前启用套餐。关键字段：

- `code`：套餐编码，当前为 `monthly`、`trial`、`basic`、`value`
- `points`：当前用户购买或兑换该套餐实际可获得的积分
- `priceAmount` / `currency`：订单金额与币种
- `validityDays`：展示用有效期；`null` 表示永久
- `badgeCode` / `isFeatured`：前端推荐标识
- `isFirstPurchaseEligible`：当前用户是否仍满足首充积分资格
- `purchaseEnabled`：套餐是否配置了有效的 HTTP(S) 购买地址
- `benefits`：套餐权益文案数组

首充体验包首次兑换为 200 积分，同一用户再次兑换该套餐时为 100 积分。

### 创建充值订单

```http
POST /api/points/recharge/orders
Content-Type: application/json

{
  "packageCode": "basic"
}
```

接口创建一个 24 小时有效的待支付订单，不会直接增加积分，也不会伪造支付成功。
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
- 传 `orderNo` 时 `count` 必须为 1，订单必须待支付、未过期且套餐一致
- `expiresAt` 可选；不传表示兑换码不设置过期时间
- 传有效订单后，接口会将订单置为已确认并绑定兑换码
- 响应中的 `codes` 是明文兑换码唯一一次返回，必须由调用方安全交付和保存

服务端只保存兑换码的 SHA-256 哈希和掩码，不保存明文。

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
```

迁移会创建 `point_recharge_package`、`point_recharge_order`、
`point_redeem_code` 并写入四档默认套餐。
