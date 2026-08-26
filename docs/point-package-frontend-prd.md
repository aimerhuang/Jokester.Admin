# 积分套餐与前端改造 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.0 |
| 日期 | 2026-08-20 |
| 适用端 | 管理 Web、用户 Web、Android、iOS |
| 需求状态 | 待产品、前端、iOS、测试评审 |
| 后端基线 | 当前 Jokester Admin API 与 `20260819-add-expiring-point-buckets.sql` |
| 关联契约 | [point-recharge.md](./point-recharge.md)、[integration-guide.md](./integration-guide.md) |

## 2. 背景与目标

后端已经支持四档积分套餐、限时积分批次、扣款优先级和失败退款。前端需要把这些能力变成完整且不误导用户的购买、兑换、余额和生图体验。

本期目标：

1. 管理员生成兑换码时，可在 `monthly`、`trial`、`basic`、`value` 四档套餐和自定义积分之间选择。
2. 用户能够区分可用总积分、限时积分、永久积分，并看见最近到期提醒。
3. `monthly` 明确展示为 5000 积分，自兑换核销或 Apple 履约到账后 30 天有效。
4. 生图页展示预计消耗和余额不足入口，并正确说明后端固定扣款顺序。
5. Web/Android 外部订单与 iOS StoreKit 使用各自正确的购买和履约流程。
6. 充值、兑换、生图预留和任务退款后，所有积分展示及时刷新且口径一致。

### 2.1 非目标

- 前端不计算或控制积分批次的实际扣减顺序。
- 前端不允许用户选择“扣月卡、签到或永久积分”。
- 创建外部订单不等于支付成功，也不等于积分到账。
- 本期不凭现有接口建设订单历史、逐批次积分明细或 Apple 欠款处理页。
- 前端不保存或尝试找回管理员签发后已经关闭的一次性明文兑换码。

## 3. 业务规则基线

### 3.1 四档套餐

| code | 当前套餐 | 积分规则 | 积分有效期 | 前端口径 |
| --- | --- | --- | --- | --- |
| `monthly` | 特惠月卡 | 固定 5000 | 到账后 30 天 | 显著展示到期规则和具体到账后的 `expiresAt` |
| `trial` | 首充体验包 | 首次 200，后续 100 | 永久 | Web/Android 兑换场景使用当前用户的 `points` 和 `isFirstPurchaseEligible` |
| `basic` | 基础套餐 | 当前 1000 | 永久 | 展示“永久有效” |
| `value` | 超值套餐 | 当前 3600 | 永久 | 展示“永久有效” |

除 `monthly` 的 5000 分和 30 天合同外，套餐名称、价格、权益、角标、推荐状态及实际积分必须以接口返回值为准。前端不得维护第二份套餐配置。

`trial` 在管理端有特殊语义：套餐接口中的 `points` 和 `isFirstPurchaseEligible` 针对当前管理员账号，不代表最终核销人。管理端只能展示“首次核销 200，后续核销 100，实际到账以后端按核销用户判断为准”。

### 3.2 有效期与扣款

- `monthly` 的 30 天从兑换码实际核销或 Apple 实际履约到账时开始，不从发码、下单或打开支付页面时开始。
- `trial`、`basic`、`value` 为永久积分。
- 签到积分当天有效，以签到响应的 `expireAt` 为准。
- 生图扣款由后端固定按以下顺序执行：限时套餐积分、当日签到积分、永久积分。
- 同类批次优先使用更早到期的批次。
- 前端只展示该规则，不提交任何积分来源或批次参数。

### 3.3 失败退款

- 生图失败或超时后，未完成图片的积分按原扣款批次结算。
- 退款不会重置或延长原批次有效期。
- 原批次已到期或已被 Apple 退款撤销时，积分流水可能存在但可用余额不增加。
- 前端不得承诺“失败必然全额退回可用余额”，应在任务最终结算后重新读取余额和流水。

## 4. 用户与平台范围

| 角色/平台 | 主要能力 |
| --- | --- |
| 普通用户 Web | 积分中心、四档套餐、外部订单、兑换码、生图积分提示 |
| 普通用户 Android | 与 Web 相同，套餐请求使用 `platform=android` |
| 普通用户 iOS | 积分中心、StoreKit 套餐购买、服务端履约、生图积分提示 |
| 超级管理员 | 动态选择套餐或自定义积分并签发兑换码 |

所有积分、套餐、兑换和 Apple 履约接口都要求登录。管理端即使隐藏发码入口，后端仍会再次校验当前用户是启用且未删除的超级管理员。

## 5. 页面改造矩阵

| 页面/模块 | 端 | 优先级 | 类型 | 主要调整 |
| --- | --- | --- | --- | --- |
| 兑换码/积分生成 | 管理 Web | P0 | 调整或新增 | 套餐与自定义积分双模式，四档动态选项，一次性明文码结果 |
| 积分中心 | 全端 | P0 | 调整或新增 | 总积分、限时/永久拆分、最近到期、签到、充值和流水入口 |
| 充值套餐 | Web/Android | P0 | 调整或新增 | 四档动态套餐、创建外部订单、购买地址可用状态 |
| StoreKit 充值 | iOS | P0 | 调整或新增 | Apple 商品匹配、购买、服务端履约、unfinished 恢复 |
| 兑换码 | 全端 | P0 | 调整或新增 | 保留大小写提交，展示到账积分与真实到期时间 |
| AI 生图 | 全端 | P0 | 调整 | 预计积分、余额预检、充值入口、扣款顺序说明 |
| 积分明细 | 全端 | P1 | 调整或新增 | 充值、到期、生图、退款和 Apple 流水展示 |
| 全局积分角标/个人中心 | 全端 | P1 | 调整 | 统一刷新余额，不长期依赖登录快照 |
| AI 任务历史/详情 | 全端 | P1 | 调整 | 展示 `pointCost`、`billingStatus`，失败结算后刷新积分 |

## 6. 页面详细需求

### 6.1 管理端兑换码/积分生成页

#### 6.1.1 模式与字段

使用分段控件切换两种互斥模式：

| 字段 | 套餐模式 | 自定义积分模式 |
| --- | --- | --- |
| `packageCode` | 必填，动态下拉 | 不提交 |
| `points` | 不提交 | 必填，1 到 1,000,000 |
| `count` | 1 到 100 | 1 到 100 |
| `orderNo` | 可选；填写后 `count` 固定为 1 | 不可用 |
| `expiresAt` | 可选 | 可选 |

套餐下拉调用：

```http
GET /api/points/recharge/packages?platform=web
```

页面按接口 `sort` 排序，仅使用返回的 `monthly`、`trial`、`basic`、`value`。不得在前端静态维护套餐价格、名称或权益作为接口失败时的兜底。

`expiresAt` 的字段名称必须是“兑换码截止核销时间”。它控制兑换码何时失效，不是到账积分的有效期。

套餐预览：

- `monthly`：5000 积分，核销到账后 30 天有效。
- `trial`：首次核销 200 积分，后续核销 100 积分，永久有效。
- `basic`、`value`：显示接口当前积分和“永久有效”。
- `orderNo` 有值时，提示该操作会同时确认对应待支付订单并签发一张兑换码。

#### 6.1.2 提交与防重

提交接口：

```http
POST /api/points/recharge/admin/codes
```

该接口当前没有幂等键。前端必须：

1. 点击后立即锁定提交按钮，直到收到明确成功或失败响应。
2. 禁止网络层自动重试。
3. 网络超时或连接中断时显示“签发结果未知，请勿重复提交，需由管理员核对”，不能自动再次发码。
4. `429 RATE_LIMITED` 时读取 `Retry-After` 并倒计时禁用。

#### 6.1.3 一次性明文码结果

成功后使用阻断式结果弹窗或独立结果页，提供：

- 单条复制；
- 复制全部；
- 本地导出 TXT；
- 套餐、单码积分规则、数量和兑换码截止核销时间摘要；
- “我已安全保存”确认后关闭。

明文 `codes` 只在本次响应中返回一次。不得写入 URL、localStorage、错误上报、前端日志、埋点属性或普通页面缓存。关闭后不提供“重新查看”。

### 6.2 用户积分中心

页面进入、回到前台以及完成积分变更后调用：

```http
GET /api/points/balance
```

展示规则：

| 字段 | 展示 |
| --- | --- |
| `availablePoints` | 可用总积分，主数字 |
| `permanentPoints` | 永久积分 |
| `expiringPoints` | 全部限时积分汇总 |
| `nextExpiringPoints` | 最近一个到期时点涉及的积分 |
| `nextExpireAt` | 最近到期时间，按用户本地时区展示 |
| `hasSignedInToday` | 签到按钮状态 |
| `todaySignInPoints` | 今日已领取签到积分 |

注意：`expiringPoints` 包含月卡、签到等所有限时积分，不能标成“月卡余额”。`nextExpiringPoints` 也不是全部限时积分，只代表最近一个到期时点。

页面固定展示简短规则：“生图优先使用限时套餐积分，其次使用当日签到积分，最后使用永久积分。”

登录、刷新 Token 和资料接口中的 `user.pointBalance` 只用于首屏快照。积分中心与全局角标应以最近一次 `/api/points/balance` 结果为权威值。

签到成功后使用响应中的 `points`、`expireAt`、`availablePoints` 更新首屏，并再刷新积分明细。重复签到的 400 响应应刷新余额，而不是继续显示可签到。

### 6.3 Web/Android 充值套餐页

套餐请求：

```http
GET /api/points/recharge/packages?platform=web
GET /api/points/recharge/packages?platform=android
```

套餐卡片按 `sort` 排列并使用：

- `name`、`description`、`points`；
- `priceAmount`、`priceMinorUnits`、`currency`；
- `validityDays`；
- `benefits`、`badgeCode`、`isFeatured`；
- `isFirstPurchaseEligible`；
- `purchaseEnabled`。

展示金额时保留服务端币种。业务计算和支付参数优先使用整数 `priceMinorUnits`，不要用浮点金额自行重算。

购买流程：

1. 用户选择套餐。
2. 调用 `POST /api/points/recharge/orders`，请求体只传 `packageCode`。
3. 响应 `purchaseUrl` 有值时，打开服务端返回的绝对 HTTP(S) 地址。
4. `purchaseUrl=null` 时只展示订单号、待支付状态和 24 小时有效期，不伪造购买成功。
5. 创建订单后不增加积分，不刷新为“充值成功”。积分仅在后续兑换码核销后到账。

`purchaseEnabled=false` 时禁用购买按钮并显示“暂未开放购买”，但仍可展示套餐信息。初始化数据库中的四档 `purchase_url` 默认均为空。

当前没有订单查询或支付状态接口，因此本期不能可靠实现订单轮询、订单历史和自动支付结果页。

### 6.4 iOS StoreKit 充值页

iOS 严禁调用外部订单接口，也不使用 `purchaseUrl`。

流程：

1. 调用 `GET /api/points/recharge/packages?platform=ios`。
2. 使用响应中的 `appleProductId` 向 StoreKit 加载 `Product`。
3. 只展示后端返回且 StoreKit 同时加载成功的商品。
4. 套餐名称、积分、有效期和权益取后端；购买价格使用 StoreKit `Product.displayPrice`。
5. 从登录或 `GET /api/auth/profile` 响应读取 `appleAppAccountToken`，作为 StoreKit `.appAccountToken`。
6. 用户购买且本地交易验证通过后，调用后端履约接口。
7. 后端明确履约成功后，才调用 `transaction.finish()`。
8. App 启动、登录完成和回到前台时扫描 unfinished transactions，并复用原请求继续履约。

履约请求：

```http
POST /api/points/recharge/apple/transactions
Idempotency-Key: <persisted UUID>
Content-Type: application/json

{
  "transactionId": "<StoreKit transaction id>",
  "productId": "<appleProductId>",
  "appAccountToken": "<profile appleAppAccountToken>"
}
```

同一交易的所有重试必须复用同一个持久化 UUID。用户取消、待批准、未验证或 StoreKit 校验失败的交易不得提交为成功。

履约成功后：

- 展示 `addedPoints`、`availablePoints`；
- `expiresAt` 有值时显示具体到期时间，否则显示永久；
- 调用 `transaction.finish()`；
- 刷新余额、积分明细和套餐列表。

iOS 套餐接口只返回已有启用 Apple Product 映射的套餐，数量可能少于四档。前端不得合成缺失商品。产品若要求 iOS 也固定展示四档，发布前必须先配置四个 StoreKit 商品映射。

iOS 不使用 `isFirstPurchaseEligible` 决定 Apple 购买实际到账。该字段当前只基于兑换码历史计算，不会随 Apple 购买变化；iOS 应使用套餐 `points` 展示预计到账，并以履约响应的 `addedPoints` 为最终结果。

### 6.5 兑换码页

提交接口：

```http
POST /api/points/recharge/redeem
Content-Type: application/json

{
  "code": "<case-sensitive code>"
}
```

交互要求：

- 兑换码区分大小写，输入框不得自动转大写或改写内容。
- 去除用户误输入的首尾空白，但保留正文大小写。
- 提交期间锁定按钮，禁止并发提交。
- 网络超时后不得自动重试；显示结果未知并刷新余额和明细。
- 无效、已使用、已过期统一展示服务端的通用无效提示，不推断具体原因。

成功页展示：

- 本次到账 `addedPoints`；
- 最新可用余额 `availablePoints`；
- `expiresAt` 有值时显示“本次积分有效至”；为空时显示“本次积分永久有效”；
- `redeemedAt`。

成功后必须刷新余额、积分明细和套餐列表，确保 `trial` 的首充资格同步变化。

### 6.6 AI 生图页

进入页面时并行读取：

```http
GET /api/points/balance
GET /api/ai/images/models
GET /api/ai/images/parameters
GET /api/ai/images/pricing-options
```

预计积分按服务端定价中的单张 `points * imageCount` 展示。GPT Image2 按 `modelCode + resolutionCode + qualityCode` 匹配；Nano Banana2 按 `modelCode + resolutionCode` 匹配，不能要求其提供独立画质价格。当前没有独立的服务端报价接口，因此该值是基于动态价格表的预估，最终以创建任务时后端实际扣分为准。

提交区要求：

- 显示可用余额和本次预计消耗；
- 预计消耗大于当前余额时禁用生成并提供“获取积分”入口；
- 从充值页返回时保留提示词、参考图和生成参数；
- 不提供积分来源或扣款顺序选择器；
- 同一次生成重试复用原 `idempotencyKey`，参数变化后生成新 key；
- 创建任务成功后立即刷新余额；
- 任务进入最终成功、失败或超时状态后再次刷新余额和明细。

任务失败文案使用：“未完成图片的积分已按原积分批次结算，请以最新余额和积分明细为准。”不得使用“积分已全额退回”。

若同步生成请求超时但返回的任务仍在历史记录中，前端跳转任务历史继续轮询，不能把超时直接当成已经退款。

### 6.7 积分明细页

接口：

```http
GET /api/points/details?pageIndex=1&pageSize=20
```

服务端按创建时间和 ID 倒序返回。分页从 1 开始，`pageSize` 最大 100，可使用分页器或无限滚动。

来源映射：

| `source` | 显示名称 | 金额样式 |
| --- | --- | --- |
| `register` | 注册赠送 | 增加 |
| `sign_in` | 每日签到 | 增加 |
| `sign_in_expire` | 签到积分到期 | 减少 |
| `recharge` | 套餐兑换 | 增加 |
| `recharge_expire` | 套餐积分到期 | 减少 |
| `apple_iap` | Apple 内购 | 增加 |
| `apple_iap_expire` | Apple 套餐积分到期 | 减少 |
| `apple_refund` | Apple 退款撤销 | 减少或零 |
| `image_generate` | AI 生图 | 减少 |
| `image_refund` | 生图失败结算 | 增加或零 |
| `point_expire` | 积分到期 | 减少 |

未知 `source` 使用 `changeType` 的通用标签并保留记录，不能丢弃。`changePoints=0` 的退款结算也是有效审计流水，必须展示。

`remark` 作为服务端说明按纯文本展示，不解析为业务状态，也不渲染为 HTML。

### 6.8 全局积分角标与 AI 任务历史

以下事件后刷新统一积分状态：

- 登录或 Token 刷新完成；
- 签到成功或重复签到；
- 兑换成功或结果未知；
- Apple 履约成功；
- AI 任务创建成功；
- AI 任务完成、失败、超时；
- 页面重新回到前台。

AI 任务列表和详情已返回 `pointCost` 与 `billingStatus`。显示映射：

| `billingStatus` | 显示 |
| --- | --- |
| `0` | 积分已预留，待结算 |
| `1` | 已确认扣除 |
| `2` | 部分退款 |
| `3` | 全额退款结算 |

该状态只描述任务账务结算，不等于可用余额实际增加了相同数额。最终金额以积分中心和积分明细为准。

任务详情当前在记录不存在或当前用户无权查看时可能返回 HTTP 200 且省略 `data`。前端应把缺少 `data` 作为“任务不存在或不可访问”，停止轮询并返回列表。

排队任务使用 3 秒、处理中任务使用 5 秒轮询间隔。多图任务需限制并发轮询，避免超过普通 GET 每 IP 120 次/分钟的限制。

## 7. 接口集成矩阵

| 接口 | 页面用途 | 前端重试策略 |
| --- | --- | --- |
| `GET /api/points/balance` | 积分中心、全局角标、生成前预检 | 可安全重试 |
| `GET /api/points/details` | 积分流水 | 可安全重试 |
| `POST /api/points/sign-in` | 每日签到 | 不自动重试；失败后刷新余额 |
| `GET /api/points/recharge/packages` | 套餐和管理端选项 | 可安全重试，不用静态套餐兜底 |
| `POST /api/points/recharge/orders` | Web/Android 创建订单 | 无幂等，禁止自动重试 |
| `POST /api/points/recharge/redeem` | 核销兑换码 | 禁止自动重试；结果未知时刷新余额/流水 |
| `POST /api/points/recharge/apple/transactions` | iOS 服务端履约 | 使用同一 `Idempotency-Key` 安全重试 |
| `POST /api/points/recharge/admin/codes` | 超级管理员发码 | 无幂等，禁止自动重试 |
| `GET /api/ai/images/pricing-options` | 生图预计积分 | 可安全重试 |
| `POST /api/ai/images` | 统一创建生图任务 | 相同 payload 复用同一 `idempotencyKey` |
| `GET /api/ai/images/{id}` | 任务状态与退款结算 | 按 `pollAfterSeconds` 轮询 |

成功响应结构为 `{ code: 0, message, requestId, data }`。失败响应的 `code` 是字符串机器码，结构为 `{ code, message, requestId, details }`。前端类型定义必须区分成功数值码和失败字符串码。服务端默认省略值为 `null` 的 JSON 字段，因此字段缺失和显式 `null` 必须按相同语义处理。

## 8. 数据展示与兼容规则

- 所有移动端时间按带 `Z` 或 UTC offset 的 ISO 8601 解析，再转换为用户本地时区。
- `validityDays=null` 表示永久；兼容当前后端边界，收到 `validityDays<=0` 也按永久展示并上报配置告警。
- 不根据 `code` 之外的名称、价格或积分反推套餐类型。
- 不用 `expiringPoints` 反推月卡独立余额。
- 不用订单 `expiresAt` 作为积分到期时间。
- 不用套餐 `priceAmount` 替代 iOS StoreKit 展示价格。
- 不使用服务端英文 `message` 作为唯一业务分支条件；用户提示需要前端本地化并保留 `requestId`。

## 9. 错误与状态处理

| HTTP/机器码 | 场景 | 前端处理 |
| --- | --- | --- |
| `400 VALIDATION_ERROR` | 参数错误、兑换码无效、积分不足等 | 保留表单；展示本地化提示；积分相关场景刷新余额 |
| `401` | Token 过期或会话撤销 | 按统一刷新/重新登录流程处理 |
| `403 RESOURCE_FORBIDDEN` | 无权限、Apple 欠款或退款等待任务结算导致不可生图 | 禁止继续提交，显示通用不可用提示和 `requestId` |
| `409 IDEMPOTENCY_CONFLICT` | 同一幂等键被不同 payload 使用 | 停止重试，要求重新发起操作 |
| `409 APPLE_TRANSACTION_OWNED_BY_OTHER_USER` | Apple 交易归属其他账号 | 不 finish 交易，引导联系客服 |
| `412 AI_CONSENT_REQUIRED` | 缺少当前 Provider 授权 | 进入 AI processing 授权流程 |
| `422 PROMPT_BLOCKED` | 提示词被拦截 | 展示合规提示，不回显敏感命中词 |
| `429 RATE_LIMITED` | 限流 | 读取 `Retry-After`，禁用按钮并倒计时 |
| `500 SERVER_ERROR` | 套餐或服务端配置错误 | 显示“服务暂不可用”，不得回退硬编码数据 |
| `503 SERVICE_UNAVAILABLE` | Redis、队列、AI 合规或依赖不可用 | 保留用户输入，提供稍后重试 |

当前积分不足、Apple 欠款和延后追扣没有各自独立机器码。前端可先做余额预检，但不能只依赖预检保证提交成功。专用机器码列为后端增强项。

### 9.1 当前限流基线

| 操作 | 当前限制 |
| --- | --- |
| 兑换码核销 | 用户 5 次/分钟、20 次/日；IP 10 次/分钟 |
| 创建充值订单 | 用户 20 次/小时 |
| 超级管理员发码 | 用户 5 次/小时、20 次/日；IP 10 次/小时、40 次/日 |
| AI 生图创建 | 用户 2 次/分钟 |
| 普通 GET | IP 120 次/分钟 |

前端必须统一处理 `Retry-After` 或 `details.retryAfterSeconds`。Redis 限流状态不可用时相关操作可能返回 503，不得退化为无限重试。

## 10. 埋点与隐私

建议事件：

- `points_center_view`
- `point_expiry_notice_show`
- `point_package_select`
- `recharge_order_create_result`
- `recharge_checkout_open`
- `redeem_submit`、`redeem_result`
- `admin_code_issue_submit`、`admin_code_issue_result`、`admin_code_export`
- `iap_product_load_result`
- `iap_purchase_state`
- `iap_fulfillment_result`
- `ai_insufficient_points`
- `ai_refund_balance_refresh`

允许的公共属性包括 `platform`、`packageCode`、`points`、`validityDays`、HTTP 状态、机器码和 `requestId`。

禁止采集：

- 明文兑换码；
- `appleAppAccountToken`；
- 完整 Apple transaction ID；
- 完整 `purchaseUrl`；
- 用户提示词和参考图地址；
- Access Token、Refresh Token 或请求 Authorization 头。

## 11. 当前后端缺口与影响

| 缺口 | 前端影响 | 建议优先级 |
| --- | --- | --- |
| 无充值订单查询/支付状态接口 | 不能可靠轮询支付结果、建设订单历史或自动支付成功页 | P0，接真实外部支付前完成 |
| `orders`、`admin/codes` 无幂等键 | 网络超时无法安全自动重试，可能重复下单或发码 | P0，生产运营前评估增强 |
| 无积分批次列表 | 只能展示限时积分汇总和最近到期，不能展示每张月卡剩余 | P1 |
| 无 Apple 欠款/延后追扣查询 | 无法建设明确欠款状态页，只能展示生图通用禁用提示 | P1 |
| 无管理员发码历史 | 无法核对结果未知的签发请求；明文码也不可找回 | P0/P1，取决于运营方式 |
| 积分不足与 Apple 财务阻断缺少专用机器码 | 前端无法稳定区分错误，只能预检和通用提示 | P1 |
| 套餐查询可能返回 `validityDays=0` | 与永久应为 `null` 的契约存在边界差异 | P1，后端归一化；前端临时兼容 |

Web/Android 真实支付还依赖生产配置有效 `purchase_url` 和可信支付回调。仅完成本 PRD 的前端页面不能替代支付平台服务端确认。

## 12. 验收标准

| 编号 | 验收项 |
| --- | --- |
| FE-POINT-001 | 管理端套餐选项来自接口，能选择且只提交四档中的一个 `packageCode` |
| FE-POINT-002 | 套餐模式与自定义积分模式参数严格互斥，`orderNo` 有值时 `count=1` |
| FE-POINT-003 | 管理端明确区分兑换码截止核销时间与到账积分有效期 |
| FE-POINT-004 | 发码成功仅展示一次明文码，支持复制/导出且日志、埋点、缓存均无明文 |
| FE-POINT-005 | 积分中心正确展示总积分、永久积分、限时积分和最近到期时间 |
| FE-POINT-006 | `monthly` 始终显示 5000 分、到账后 30 天；其余套餐显示永久有效 |
| FE-POINT-007 | `trial` 根据用户接口返回展示首次或续充积分，管理端不把管理员资格当成核销人资格 |
| FE-POINT-008 | Web/Android 创建订单后只显示待支付，不直接增加积分或显示充值成功 |
| FE-POINT-009 | `purchaseEnabled=false` 或 `purchaseUrl=null` 时不跳转支付平台 |
| FE-POINT-010 | iOS 使用 StoreKit 价格和 `appleAppAccountToken`，后端履约成功前不 finish 交易 |
| FE-POINT-011 | iOS unfinished transaction 在重试时复用原 `Idempotency-Key` |
| FE-POINT-012 | 兑换成功显示实际到账和 `expiresAt`，随后刷新余额、流水和首充资格 |
| FE-POINT-013 | 生图页展示预计积分且没有积分来源选择器，余额不足时可进入充值页并保留表单 |
| FE-POINT-014 | AI 任务最终失败后刷新余额/明细，文案不承诺必然全额回到可用余额 |
| FE-POINT-015 | `image_refund`、`apple_refund` 的零积分流水仍能正常展示 |
| FE-POINT-016 | 429、409、403、500/503 均有明确状态，禁止不安全自动重试和硬编码兜底 |

### 12.1 测试组合

至少覆盖：

- Web、Android、iOS 三个平台；
- 四档套餐及 `trial` 首次/续充两种用户；
- `monthly` 未到账、刚到账、部分使用、临近到期、已到期；
- 同时存在月卡、签到、永久积分；
- `purchaseEnabled` 开启/关闭；
- 兑换成功、无效、重复、过期、网络超时；
- 发码双击、429、网络超时和关闭一次性结果页；
- StoreKit 用户取消、pending、unverified、后端失败、履约重试和 App 重启恢复；
- AI 创建成功、余额不足、幂等重试、全部失败、部分退款、原批次到期后退款；
- 时间跨时区和跨自然日签到过期。

## 13. 发布依赖与顺序

1. 后端和目标数据库先完成 `20260819-add-expiring-point-buckets.sql` 部署与历史数据对账。
2. 确认 Web/Android 四档套餐均启用，`monthly=5000/30 天`，其他三档永久。
3. Web/Android 若开放购买，配置并验收 `purchase_url`、支付回调和订单后续处理。
4. iOS 配置实际 Apple Product 映射并完成 Sandbox/TestFlight 履约测试。
5. 前端先灰度积分中心、兑换码和生图积分展示，再开放真实购买入口。
6. 上线后监控套餐加载失败、兑换失败、重复提交、Apple 履约失败、余额不足和退款后余额刷新。
