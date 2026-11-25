# AI 生图积分定价后台与 VIP 专属模型 / VIP 优惠价 PRD

## 1. 文档信息

| 项 | 内容 |
| --- | --- |
| 文档版本 | v0.2（产品决策已确认，待评审） |
| 日期 | 2026-11-24 |
| 涉及仓库 | `Jokester.Admin`（API）、`Jokester-Ai-Image`（Web 前台 + 管理页）、Android / iOS 客户端（只读契约变化） |
| 关联文档 | `docs/ai-model-config-status-prd.md`、`docs/ai-image-size-mode-prd.md`、`docs/point-recharge.md`、`docs/user-admin-prd.md` |
| 关联表 | `ai_image_point_price`、`ai_image_model_release_price`、`ai_image_model_config`、`sys_user_membership_entitlement`、`ai_image_task` |

变更记录：

| 版本 | 内容 |
| --- | --- |
| v0.1 | 初稿 |
| v0.2 | 确认 D1–D6：超管不受 VIP 专属限制但按自身身份计价；VIP 撤销不补收差额；非 VIP 在所有客户端都能看到 VIP 模型但不可用；VIP 价默认 8 折并由管理员调整默认折扣；现有标准价不变 |

## 2. 结论摘要

1. **标准价后台可调**：超级管理员在页面上直接修改 `ai_image_point_price` 的标准价。legacy 计费每次请求都查库、无缓存，保存后**下一笔新任务**立即按新价计费，多实例无需重启；已创建任务按原快照结算和退款，不追溯改价。
2. **VIP 价默认 8 折**：新增“默认 VIP 折扣”全局设置，初始值 80（即 8 折），可在页面修改；每个模型可单独覆盖折扣；每个价格档还可以手动指定 VIP 价。上线迁移**不改任何现有标准价**，只按 8 折为所有现有价格档初始化 VIP 价。
3. **VIP 价落库为绝对积分**：折扣只是计算规则，实际 VIP 价存成 `vip_points` 整数（向上取整），计费直接读这个值；审计里能看到每一档的具体价格。
4. **VIP 专属模型**：新增模型级策略表，每个模型 `all` 或 `vip_only`。例如 `gpt-image-2.6-sunburst` 设为 `vip_only` 后：
   - 有效 VIP 可用，按 VIP 价扣分；
   - 超级管理员可用，**有 VIP 按 VIP 价、无 VIP 按标准价**；
   - 其他非 VIP 用户在所有客户端都能看到该模型，但不能使用，提交返回 403 `VIP_REQUIRED`。
5. **VIP 撤销不补收**：按 VIP 价完成的任务，之后 VIP 到期、被 Apple 退款撤销，都不追收差价，也不退还已扣积分（积分追扣仍按现有 Apple 退款规则处理，与本需求无关）。
6. **扣分一致性**：价格与 VIP 身份在积分预留事务内最终判定；客户端可传 `expectedUnitPoints`，实付单价高于预期时返回 409 `PRICE_CHANGED`，保证用户不会被按高于页面显示的价格扣分。
7. **范围**：生产环境未启用 `size-mode-v1`（2026-11-24 确认），所有线上流量走 legacy 计费，一期即覆盖全部线上流量。`size-mode-v1` 发布价格的 VIP 价放到二期，且必须在开启 size-mode 前完成。

## 3. 背景

- 当前生图价格只能靠 SQL 迁移改（例如 `20261105-add-gpt-image-26-models.sql` 复制 2.5 的价格给 2.6），运营调价要开发介入、走发布流程。
- 月卡 VIP（`monthly` 套餐，`sys_user_membership_entitlement`）目前只是“5000 积分 + 身份”，生图侧没有差异化权益，缺少续费理由。
- 运营需要：在页面上调整各模型各档位价格并实时生效；把部分模型设为仅 VIP 可用；给所有模型配置 VIP 优惠价，默认 8 折。

现状要点（来自代码）：

| 位置 | 现状 |
| --- | --- |
| `PointService.GetImageGenerateCostAsync` | legacy 查价：GPT 用 `model + resolution + quality`，Nano 只用 `model + resolution`；直接查库，无缓存 |
| `AiImageService`（约 975 行）/ `NanoBananaImageService`（约 469 行） | 在**进入预留事务之前**查价，把单价写进 `task.PointCost` |
| `PointService.ReserveImageTasksAsync` | 事务内锁用户、校验幂等、扣分，但不重新查价 |
| versioned 预留 | 事务内 `TranLock` 锁定 release price 行 |
| `MembershipEntitlementLedger.GetActiveAsync` | 判定有效 `monthly_vip`，取最晚到期 |

## 4. 目标与非目标

### 4.1 目标

- G1 超级管理员可在页面上查看、修改、启停各模型各档位的标准价，保存后新任务立即按新价计费。
- G2 超级管理员可调整全局默认 VIP 折扣（初始 8 折）和单个模型的 VIP 折扣，也可对单个价格档手动指定 VIP 价。
- G3 超级管理员可把任意模型设为“全员可用”或“仅 VIP 可用”，立即生效。
- G4 有效 VIP 用户生图时自动按 VIP 价扣分；超级管理员不受 VIP 专属限制，但计价只看本人是否 VIP。
- G5 所有用户都能看到 VIP 专属模型；非 VIP 看到锁定状态并能跳转购买月卡；页面显示本人实付价和原价。
- G6 所有价格与策略变更都有审计和乐观并发；改价、VIP 到期、权益撤销等边界下扣分正确，不多扣、不追溯。
- G7 上线后现有标准价完全不变。

### 4.2 非目标（首期）

- 不在页面上修改 `size-mode-v1` 发布价格，不在页面上发布 catalog release（仍走 `--configure-ai-image-catalog`）。
- 不做定时调价、限时折扣、优惠券、按用户分组定价。
- 不做多档会员（只有 `monthly_vip`）；表结构预留 `required_tier_code`，首期不实现。
- 不做价格推送（WebSocket/SSE）；前台用重新拉取 + 提交时校验保证一致。
- 不修改充值套餐定价（见 `docs/point-recharge.md`）。

## 5. 术语

| 术语 | 含义 |
| --- | --- |
| 标准价 `points` | 价格档的普通用户单图积分 |
| VIP 价 `vip_points` | 价格档的 VIP 单图积分；NULL 表示该档 VIP 无优惠 |
| 折扣 `discountPercent` | VIP 支付标准价的百分比，`80` 即 8 折，`100` 即不打折 |
| 默认 VIP 折扣 | 全局设置，初始 `80` |
| 模型 VIP 折扣 | 单个模型的覆盖值；为空时沿用默认 VIP 折扣 |
| VIP 价模式 `vipPriceMode` | `follow`（跟随折扣自动计算，默认）或 `manual`（手动指定） |
| 实付单价 `effectivePoints` | 当前用户实际被扣的单图积分 |
| 计价档 `pricingTier` | `standard` 或 `vip` |
| VIP 专属模型 | 访问策略 `access_level=vip_only` 的模型 |
| 有效 VIP | 存在 `tier_code=monthly_vip`、`status=active`、未撤销、`starts_at <= now < expires_at` 的权益 |
| 有效超管 | 数据库中启用、未删除、`is_super_admin` 为真的用户（不只看 Token 声明） |

## 6. 用户与权限

| 角色 | VIP 专属模型 | 计价 | 后台 |
| --- | --- | --- | --- |
| 超级管理员（非 VIP） | 可用 | 标准价 | 定价、折扣、访问策略全部读写，查看审计 |
| 超级管理员（同时是有效 VIP） | 可用 | VIP 价 | 同上 |
| 有效 VIP 用户 | 可用 | VIP 价 | 无 |
| 普通登录用户 | 可见、不可用 | 标准价 | 无 |

- 后台接口沿用 `/api/ai/model-routes` 的做法：只允许超级管理员，写操作在事务内锁定并复核操作者仍是有效超管，否则 403 `RESOURCE_FORBIDDEN`。
- 不新增菜单权限码（`ai_operator` 只能挂 `ai_image` 站点菜单，后台页靠超管声明保护）。
- 生图时的超管豁免同样在事务内按数据库复核，不能只信 Token：Token 签发后被取消超管的用户，最晚在复核时失去豁免。

## 7. 功能需求：定价后台（超级管理员）

### 7.1 定价页（`/admin/pricing`）

页面顶部：**默认 VIP 折扣**（例如“VIP 默认 8 折”），带「修改」按钮（见 §7.4）。

下方按模型分组的卡片列表，每个模型一张卡：

- 卡头：模型展示名、`model_code`、协议（GPT / Nano）、契约（legacy / size-mode-v1）、启用路由数、「仅 VIP 可用」开关（见 §8）、模型 VIP 折扣（显示“跟随默认 8 折”或“单独 7 折”，可修改，见 §7.4）。
- 卡体：价格矩阵。GPT 为“分辨率 × 画质”，Nano 为“分辨率”一列。每格显示：
  - 标准价（可编辑）
  - VIP 价（显示数值和“自动 / 手动”标记；可切换为手动并填写，或恢复自动）
  - 实际优惠幅度（只读，例如“-20%”）
  - 启用开关
  - 折算金额 `price_amount`（可编辑，仅展示用，不参与扣分）
- 缺格提示：模型有启用路由但缺少对应价格档时标红“未定价，该档位无法生图”，可一键新增。新增价格档默认 `vipPriceMode=follow`，VIP 价自动按折扣算出。
- `size-mode-v1` 模型：额外展示当前 release 的价格（只读），标注“发布价格需通过 catalog 发布流程变更，VIP 价二期支持”。
- 顶部筛选：模型、仅看未定价、仅看手动 VIP 价。

### 7.2 修改标准价与手动 VIP 价

- 页面内可同时改多格，点「保存」后一次性提交为**一个批量请求**，服务端同一事务写入，全部成功或全部失败。
- 保存前弹出差异确认框：列出每一格“旧价 → 新价”（标准价和 VIP 价分别列出，包括因标准价变化而自动重算的 VIP 价），并要求填写**变更原因**（必填，1–200 字）。
- 自动 VIP 价：`vipPriceMode=follow` 的档位在标准价变化时，服务端在同一事务内按该模型的有效折扣重算 VIP 价（§7.5）。
- 校验规则（前后端一致，以后端为准）：
  - `points`：整数，1–100,000。
  - 手动 VIP 价：NULL（该档无优惠）或 1 ≤ `vipPoints` < `points`。
  - 标准价调低到不高于手动 VIP 价时，返回 422 `VIP_PRICE_NOT_LOWER`，要求同时调整或改回自动。
  - 单格标准价或手动 VIP 价变化超过 ±50%（阈值配置项 `AiImagePricingAdmin.LargeChangePercent`，默认 50），需要勾选“确认大幅调价”，请求带 `confirmLargeChange=true`，否则 422 `PRICE_CHANGE_TOO_LARGE`。用于防止 50 误输成 5。
  - 禁用某模型某分辨率下**最后一个**启用价格档，而该档仍有启用路由时，需要 `confirmDisableLastPrice=true`，否则 409 `PRICE_LAST_ENABLED`。
- 每行带 `revision`，任何一行版本不匹配则整批 409 `PRICE_REVISION_CONFLICT`，页面提示刷新后重做。
- 不提供硬删除；价格档只能启停。

### 7.3 价格调整的生效语义（“实时更新”）

- 保存成功即对**之后进入预留事务的新任务**生效；多实例下无缓存，不需要等待或重启。
- 已创建（已预留积分）的任务按创建时的快照结算、退款，不追溯改价。
- 同一幂等 key 重放返回原任务，不按新价重新计费。
- 前台价格展示可能短暂滞后，靠 §9.5 的 `expectedUnitPoints` 保证用户不会被按高于所见的价格扣分。

### 7.4 修改 VIP 折扣

两个入口，规则相同：

| 入口 | 作用范围 |
| --- | --- |
| 页面顶部「默认 VIP 折扣」 | 所有**未设置模型折扣**的模型中、`vipPriceMode=follow` 的价格档 |
| 模型卡头「VIP 折扣」 | 该模型中 `vipPriceMode=follow` 的价格档；可选“恢复跟随默认” |

流程：

1. 输入折扣（整数 1–100；`100` 表示不打折，自动档 VIP 价全部为空）。
2. 调用预览接口，展示受影响的模型数、价格档数和每一档“旧 VIP 价 → 新 VIP 价”；手动档不受影响，单独列出“以下手动价格不会变化”。
3. 填写变更原因并确认；服务端在一个事务内更新折扣设置并重算全部受影响的自动档，逐档写审计（同一 `batch_id`）。
4. 预览返回 `previewToken`（基于折扣设置 revision 和受影响行 revision 的摘要），确认时必须带回；期间有人改过任何相关数据则 409 `PRICE_REVISION_CONFLICT`，要求重新预览。

折扣调整不走 ±50% 大幅调价校验，由预览确认代替。

### 7.5 VIP 价计算规则

- 有效折扣 = 模型 VIP 折扣 ?? 默认 VIP 折扣。
- 自动档：`vipPoints = ceil(points × 有效折扣 / 100)`，最小 1；结果 ≥ `points` 时存 NULL（该档无优惠，例如标准价 1 积分的档位）。
- 向上取整，保证优惠不超过配置的折扣幅度，也不会出现小数积分。
- 示例（默认 8 折）：标准价 40 → VIP 32；25 → 20；12 → 10（9.6 向上取整）；3 → 3 → 存 NULL（无优惠）。
- 手动档：直接使用管理员填写的值，折扣变化不影响。

### 7.6 变更记录

- 定价页底部抽屉展示审计列表：时间、操作者（系统迁移显示为“系统”）、模型、档位、字段、旧值 → 新值、原因、批次号。
- 支持按模型、操作者、时间范围、变更类型（标准价 / VIP 价 / 折扣 / 访问策略）筛选，分页。

## 8. 功能需求：VIP 专属模型

### 8.1 规则

- 每个模型一条访问策略：`all`（默认，无记录时等同 `all`）或 `vip_only`。
- `vip_only` 模型的可用性：

| 用户 | 能否使用 | 计价 |
| --- | --- | --- |
| 有效 VIP | 能 | VIP 价（该档无 VIP 价时按标准价） |
| 有效超管（非 VIP） | 能 | 标准价 |
| 有效超管且有效 VIP | 能 | VIP 价 |
| 其他用户 | 不能，403 `VIP_REQUIRED` | — |

- 策略作用于该 `model_code` 的所有入口：`POST /api/ai/images`、`POST /api/ai/images/generate`、Nano 的两个兼容入口；画布生成走 `POST /api/ai/images`，自然覆盖。
- 对 legacy 与 `size-mode-v1` 两套契约都生效（策略是模型级，不进 release 快照）。
- 修改访问策略需要填写原因，带 `revision` 乐观并发，写审计；保存后对新请求立即生效。

### 8.2 判定顺序

在现有请求处理顺序上插入访问和价格判定，不改变已有约束：

1. 解析请求、校验参数。
2. **durable 幂等事实优先**：同用户同 key 同 payload 已有任务 → 直接返回原任务，不做 VIP 判定（VIP 到期后重放也返回原结果）。
3. 提示词过滤（`IAiPromptFilter`）。
4. **模型访问判定**：`vip_only` 且既非有效 VIP 也非有效超管 → 403 `VIP_REQUIRED`。
5. 计算实付单价，用于 Redis 每日积分额度 admission。
6. Redis admission。
7. 预留事务：锁用户 → 幂等复核 → **复核访问策略、VIP 身份和超管身份** → 事务内查价并锁价格行 → 校验 `expectedUnitPoints` → 扣分、写任务与 `reserve` 流水。

第 4、5 步只用于提前拒绝、减少 Redis 占位；第 7 步是权威判定。第 7 步拒绝时撤销本请求的 Redis 占位。

### 8.3 已创建任务与权益变化

- 任务创建时已满足条件并已扣分，之后 VIP 到期、超管被取消、管理员把模型改成 `vip_only`、Apple 退款撤销月卡，**都不中断该任务**，Worker 照常处理和结算。
- **不补收差价、不退还费用**：按 VIP 价完成的任务，在 VIP 撤销或到期后既不追收“标准价 − VIP 价”，也不因权益撤销退还已扣积分。任务失败/超时的退款仍按原规则退回实际扣除的积分。
- Apple 退款本身的积分追扣沿用现有 `sys_user_point_bucket_usage` 延后追扣逻辑，不受本需求影响。

### 8.4 前台展示

- **所有客户端、所有用户都能在模型列表看到 VIP 专属模型**，不做隐藏。
- 新版客户端：VIP 专属模型带「VIP」角标；非 VIP 用户选择时显示锁定态并弹出开通引导，跳转月卡购买（`monthly` 套餐）；超管看到「VIP」角标但可正常使用，并提示“管理员体验，按标准价计费”。
- 旧版客户端：不认识 `access` 字段，会把 VIP 模型当普通模型展示；非 VIP 用户提交时收到 403 `VIP_REQUIRED`，错误消息为可直接展示的中文：“该模型为 VIP 专属，开通月卡后可用”。
- 模型状态页（`/model-status`）照常展示 VIP 专属模型，附 `vipOnly` 标记，不额外暴露其他信息。

## 9. 后端设计

### 9.1 数据表变更

迁移：`docs/migrations/20261124-add-ai-pricing-admin-and-vip-models.sql`，同步 `jokester.admin.sql`。API 会读新列和新表，**先迁移再部署**。

**`ai_image_point_price` 新增列**

```sql
ALTER TABLE `ai_image_point_price`
  ADD COLUMN `vip_points` INT DEFAULT NULL COMMENT 'VIP单图积分，NULL表示无VIP优惠' AFTER `points`,
  ADD COLUMN `vip_price_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'follow' COMMENT 'VIP价模式：follow跟随折扣 manual手动' AFTER `vip_points`,
  ADD COLUMN `revision` INT NOT NULL DEFAULT 0 COMMENT '后台乐观并发版本',
  ADD COLUMN `updated_by` BIGINT DEFAULT NULL COMMENT '最近修改人用户ID，0表示系统迁移',
  ADD CONSTRAINT `chk_ai_image_point_price_vip_points`
    CHECK (`vip_points` IS NULL OR (`vip_points` > 0 AND `vip_points` < `points`)),
  ADD CONSTRAINT `chk_ai_image_point_price_vip_mode`
    CHECK (`vip_price_mode` IN ('follow', 'manual'));
```

**新表 `ai_image_pricing_setting`**（单行，`id=1`）

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `id` | TINYINT PK | 固定 1 |
| `default_vip_discount_percent` | TINYINT | 1–100，初始 80，CHECK 约束 |
| `revision` | INT | 仅用于默认 VIP 折扣的乐观并发和 `previewToken`；只在默认折扣变化时递增 |
| `pricing_revision` | BIGINT | 全局定价版本，即前台的 `pricingRevision`；任何价格、折扣、访问策略变更都在同一事务内递增，初始 1 |
| `updated_by` / `updated_at` | | |

`revision` 与 `pricing_revision` 分开：如果共用一个版本号，改一个无关的价格或访问策略也会让正在进行的默认折扣预览返回 409。

全局版本规则：

- 所有后台写操作（价格批量修改、新增价格档、默认/模型折扣、访问策略）在事务内**最先**锁定 `ai_image_pricing_setting` 行，执行 `pricing_revision = pricing_revision + 1`。锁顺序固定为：设置行 → 模型策略行 → 按 `id` 升序的价格行，避免后台写操作之间死锁。后台写入频率低，串行化没有性能问题。
- **生图预留事务不锁也不读这一行**，只锁价格行，避免每次生图都争抢同一行。`pricingRevision` 只在价格查询接口中无锁读取。
- 迁移脚本改价时除写系统审计外，也必须递增 `pricing_revision`，否则前台不会刷新。
- `pricingRevision` 只反映定价配置变化，不反映单个用户 VIP 身份变化：用户开通或失去 VIP 时它不变，前台在购买成功后应主动重新拉价格；提交时的 `expectedUnitPoints` 仍是最终保护。`size-mode-v1` 发布价格的变化继续由 `catalogVersion` 表示，不计入 `pricingRevision`。

**新表 `ai_image_model_policy`**（一个模型一行）

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `model_code` | VARCHAR(100) PK | 规范化后的模型编码 |
| `access_level` | VARCHAR(16) ascii | `all` / `vip_only`，CHECK 约束，默认 `all` |
| `required_tier_code` | VARCHAR(32) | 首期固定 `monthly_vip`，为多档会员预留 |
| `vip_discount_percent` | TINYINT NULL | 模型 VIP 折扣，NULL 表示跟随默认 |
| `revision` | INT | 乐观并发 |
| `updated_by` / `updated_at` | | |

无记录的模型等同 `access_level=all`、折扣跟随默认。

**新表 `ai_image_pricing_audit`**（价格、折扣、访问策略共用）

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `id` | BIGINT PK | |
| `batch_id` | CHAR(32) | 同一次保存共享 |
| `target_type` | VARCHAR(16) | `price` / `setting` / `model_policy` |
| `target_id` | VARCHAR(100) | 价格档 ID、`1`、或 `model_code` |
| `model_code` / `resolution_code` / `quality_code` | | 冗余，便于筛选；不适用时为空 |
| `action` | VARCHAR(24) | `create` / `update` / `enable` / `disable` / `vip_recalculate` / `system_init` |
| `before_json` / `after_json` | TEXT | 变化字段 |
| `reason` | VARCHAR(200) | 变更原因 |
| `operator_user_id` | BIGINT | `0` 表示系统迁移 |
| `operator_ip` | VARCHAR(50) | |
| `created_at` | DATETIME | UTC |

**`ai_image_task` 新增列**

```sql
ALTER TABLE `ai_image_task`
  ADD COLUMN `pricing_tier` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL COMMENT '计价档：standard/vip，历史任务为NULL',
  ADD COLUMN `list_unit_point_cost` INT DEFAULT NULL COMMENT '标准价单图积分快照，用于展示VIP节省',
  ADD COLUMN `access_basis` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL COMMENT 'VIP专属模型的准入依据：vip/super_admin，非专属模型为NULL';
```

已有 `unit_point_cost` / `point_cost` 继续表示实付，退款逻辑不变。

**迁移中的数据初始化**（同一事务）：

1. 插入 `ai_image_pricing_setting`：`default_vip_discount_percent=80`、`revision=0`、`pricing_revision=1`。
2. **不修改任何 `points`**。
3. 所有未删除价格档：`vip_price_mode='follow'`，`vip_points = CEIL(points * 80 / 100)`，结果不小于 `points` 时为 NULL。
4. 为每个被初始化的价格档写一条 `action=system_init`、`operator_user_id=0`、原因“上线初始化 VIP 默认 8 折”的审计。
5. 不插入任何 `ai_image_model_policy` 记录（全部模型默认 `all`）。

迁移脚本今后若再改价（例如新模型复制价格），也写 `operator_user_id=0` 的审计。

### 9.2 价格解析服务

新增 `IAiImagePriceResolver`，替代散落在 `AiImageService` / `NanoBananaImageService` 中、在事务外决定最终价的 `GetImageGenerateCostAsync` 用法：

```csharp
Task<ResolvedImagePrice> ResolveAsync(
    long userId, string modelCode, string resolutionCode, string? qualityCode,
    bool inReservationTransaction, CancellationToken ct);

record ResolvedImagePrice(
    long PriceId, int StandardPoints, int? VipPoints,
    int EffectivePoints, string PricingTier,
    bool IsVip, bool IsSuperAdmin, string AccessLevel, string? AccessBasis);
```

- 事务外调用：用于前置 `VIP_REQUIRED` 拒绝和 Redis admission。
- `PointService.ReserveImageTasksAsync` 事务内调用：对价格行加锁读取，复核访问策略、VIP、超管，用结果覆盖每个 task 的 `PointCost`、`UnitPointCost`、`PricingTier`、`ListUnitPointCost`、`AccessBasis`。后台改价也锁同一价格行，改价与预留串行化。
- 计价：`IsVip && VipPoints != null` → `EffectivePoints = VipPoints`、`PricingTier = vip`；否则标准价、`standard`。超管身份**只影响准入，不影响计价**。
- 准入：`AccessLevel = vip_only` 时，`IsVip` → `AccessBasis = vip`；否则 `IsSuperAdmin` → `super_admin`；都不满足 → `VIP_REQUIRED`。
- 查价匹配规则保持现状：GPT `model + resolution + quality`，Nano `model + resolution`。
- VIP 判定复用 `MembershipEntitlementLedger.GetActiveAsync`；超管判定查 `sys_user`（启用、未删除、超管）；均在锁用户行之后查询。
- 扣分仍统一走 `IPointService`，不在 Controller 中计算。
- `size-mode-v1`（生产未启用）：首期仍按 release price 计费，`PricingTier` 固定 `standard`；访问策略照常判定。

### 9.3 后台接口

前缀 `/api/ai/model-pricing`。**不要放在 `/api/ai/images` 下**（POST 会命中生图创建限流）。全部仅限有效超管。

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/ai/model-pricing` | 默认 VIP 折扣；按模型分组的价格档（`points`、`vipPoints`、`vipPriceMode`、`priceAmount`、`status`、`revision`）、模型策略（访问级别、模型折扣、有效折扣）、缺价档位、`size-mode-v1` 只读 release 价格 |
| `POST /api/ai/model-pricing/prices` | 新增价格档（模型 + 分辨率 + 画质唯一，已存在 409 `PRICE_ALREADY_EXISTS`）；默认自动 VIP 价 |
| `PUT /api/ai/model-pricing/prices/batch` | 批量修改标准价、VIP 价模式与手动值、折算金额、启停，单事务 |
| `POST /api/ai/model-pricing/discount/preview` | 预览默认折扣或模型折扣变更，只读 |
| `PUT /api/ai/model-pricing/discount` | 应用默认折扣变更，带 `previewToken` |
| `PUT /api/ai/model-pricing/models/{modelCode}/discount` | 应用模型折扣变更（或恢复跟随默认），带 `previewToken` |
| `PUT /api/ai/model-pricing/models/{modelCode}/access` | `{ accessLevel, reason, revision }` |
| `GET /api/ai/model-pricing/audits` | 审计分页筛选 |

批量修改请求示例：

```json
{
  "reason": "2.6 Sunburst 上线定价调整",
  "confirmLargeChange": false,
  "confirmDisableLastPrice": false,
  "items": [
    { "id": 101, "revision": 3, "points": 40, "vipPriceMode": "follow", "priceAmount": 0.40, "status": 1 },
    { "id": 102, "revision": 1, "points": 60, "vipPriceMode": "manual", "vipPoints": 45, "status": 1 }
  ]
}
```

- `vipPriceMode=follow` 时忽略 `vipPoints`，由服务端计算。
- `vipPriceMode=manual` 时请求中必须出现 `vipPoints`：整数为手动 VIP 价，显式 `null` 表示该档无优惠；省略返回 422 `VALIDATION_FAILED`，避免只改标准价时把手动优惠静默清空。
- 新增价格档时若同一“模型 + 分辨率 + 画质”只有软删除记录，则原地恢复该行（保留 `id`，`revision + 1`，VIP 价按自动模式重算），审计 `action=create` 且 `before_json` 带 `isDeleted=true`；未删除记录已存在才返回 409 `PRICE_ALREADY_EXISTS`。
- 单批最多 200 行；`id` 不可重复。
- 事务内：锁定并复核操作者 → 按 `id` 升序锁价格行（避免死锁）→ 校验 revision 与规则 → 更新、重算自动 VIP 价、`revision + 1` → 写审计。
- 折扣应用：锁设置行（或模型策略行）→ 按 `id` 升序锁受影响价格行 → 核对 `previewToken` → 更新并写审计。模型尚无策略行时，必须在锁价格行**之前**插入策略行：生图预留读取不存在的策略行会持有间隙锁再等价格行，先锁价格行再插入会与之形成死锁。
- 响应返回更新后的行与新的 `pricingRevision`。
- `model_code` 必须存在于 `ai_image_model_config`（未删除），否则 404。

### 9.4 前台接口变化

所有变化均为**新增字段**，旧客户端不受影响。

**`GET /api/ai/images/models`**：每个模型增加

```json
"access": {
  "level": "vip_only",
  "available": false,
  "reasonCode": "VIP_REQUIRED",
  "basis": null
}
```

- `available`：当前用户能否使用；`basis`：可用时为 `vip` / `super_admin` / `null`（全员模型）。
- 所有用户、所有客户端都返回 VIP 专属模型，不按能力头过滤。

**`GET /api/ai/images/parameters` 的 `pointPrices`、`GET /api/ai/images/pricing-options`**：每个价格项增加

| 字段 | 说明 |
| --- | --- |
| `vipPoints` | VIP 价，无则 `null` |
| `effectivePoints` | 当前用户实付单价 |
| `pricingTier` | `standard` / `vip` |

原 `points` 字段语义不变（标准价）。顶层增加 `pricingRevision`（取自 `ai_image_pricing_setting.pricing_revision`，规则见 §9.1），前台用来判断是否需要刷新。这些响应按用户不同，必须带 `Cache-Control: private, no-store`。

**创建与直接生成请求**新增可选字段 `expectedUnitPoints`：

- 预留事务内若 `effectivePoints > expectedUnitPoints` → 409 `PRICE_CHANGED`，响应体带当前 `effectivePoints`、`pricingTier`；不扣分、不建任务、撤销 Redis 占位。
- `effectivePoints <= expectedUnitPoints`（降价或刚开通 VIP）→ 按实付价正常创建。
- 不传则不校验（兼容旧客户端）。
- 该字段**不进入幂等指纹**（与“catalog 不进入客户端意图指纹”一致）。

**创建响应、任务列表/详情**增加 `pricingTier`、`unitPointCost`、`listUnitPointCost`，前台可展示“VIP 价 32 积分（原价 40）”。

**积分流水**：`image:{taskId}:reserve` 的备注写明计价档，例如“AI 生图预留（VIP 价）”。

### 9.5 前台“实时”刷新策略

- 生图页面在：进入页面、窗口重新获得焦点、切换模型、提交前、**月卡购买成功后**，重新拉取价格（无缓存）；`pricingRevision` 与本地缓存不同即整体刷新价格和模型列表。
- 提交时总是带 `expectedUnitPoints`；收到 `PRICE_CHANGED` 时刷新价格并提示“价格已更新为 X 积分，请确认后重试”，不自动重提。
- 收到 `VIP_REQUIRED` 时刷新模型列表并弹出开通引导。

### 9.6 服务与代码落点

| 落点 | 改动 |
| --- | --- |
| `Application/Services/AiImagePriceResolver.cs`（新） | 查价、VIP/超管判定、准入判定 |
| `Application/Services/AiImagePricingAdminService.cs`（新） | 后台价格、折扣、访问策略读写；预览；审计；并发 |
| `Controllers/AiModelPricingController.cs`（新） | 后台接口 |
| `PointService.ReserveImageTasksAsync` / versioned 预留 | 事务内调用价格解析，覆盖任务价格快照；versioned 路径加入访问复核 |
| `AiImageService` / `NanoBananaImageService` | 去掉事务外决定最终价；插入访问判定；接收 `expectedUnitPoints` |
| `AiImageCatalogService.GetModelsAsync` / `AiImageModelConfigService` | 模型列表附 `access`，价格项附 VIP 字段 |
| `AiImageModelStatusService` | 输出 `vipOnly` |

## 10. 错误契约

| HTTP | 机器码 | 场景 |
| --- | --- | --- |
| 403 | `VIP_REQUIRED` | 非 VIP 且非超管使用 `vip_only` 模型；消息：“该模型为 VIP 专属，开通月卡后可用” |
| 409 | `PRICE_CHANGED` | 实付单价高于 `expectedUnitPoints` |
| 400 | （现有） | 档位未定价：“当前模型、分辨率、画质未配置积分价格” |
| 403 | `RESOURCE_FORBIDDEN` | 后台接口操作者非有效超管 |
| 409 | `PRICE_REVISION_CONFLICT` | 价格、折扣或访问策略乐观并发冲突，或 `previewToken` 失效 |
| 409 | `PRICE_ALREADY_EXISTS` | 新增重复价格档（仅未删除记录；软删除档位会被恢复） |
| 409 | `PRICE_LAST_ENABLED` | 禁用档位最后一个启用价格且未确认 |
| 422 | `PRICE_CHANGE_TOO_LARGE` | 变化超阈值且未确认 |
| 422 | `VIP_PRICE_NOT_LOWER` | 手动 VIP 价不低于标准价 |
| 422 | `VALIDATION_FAILED` | 积分或折扣越界、原因为空等 |

## 11. 安全与一致性

- **不信任客户端价格**：客户端只能传 `expectedUnitPoints` 作为上限保护，实付价永远由服务端在事务内计算。
- **身份每次查库**：VIP 和超管身份都不只依赖 JWT；Access Token 有效期内权益撤销或超管被取消，也会在预留事务中生效。
- **改价与扣分串行**：预留事务对价格行加锁，后台改价、折扣重算同样加锁，不会出现读到一半新价的情况。
- **审计不可绕过**：价格、折扣、访问策略的所有写操作同事务写审计；迁移脚本改价写系统审计。
- **幂等**：重放永远返回原任务与原价；`PRICE_CHANGED` / `VIP_REQUIRED` 不写 durable 请求，不消耗幂等 key。
- Redis 不可用时 AI 创建仍 fail-closed（不变）；VIP、超管、价格判定只依赖 MySQL。

## 12. 分期

| 期 | 内容 |
| --- | --- |
| 一期 | §7 定价后台（标准价、默认/模型折扣、手动 VIP 价、审计）、§8 VIP 专属模型（legacy + size-mode-v1 都生效）、legacy VIP 计价、前台展示与 `expectedUnitPoints`。生产未启用 size-mode，一期覆盖全部线上流量 |
| 二期 | `size-mode-v1` 发布价格增加 `vip_points` 并纳入 catalog 发布审批。**必须在生产开启 `AiImageSizeMode.Enabled` 之前完成**，否则开启后 VIP 用户在 versioned 模型上会退回标准价 |
| 后续 | 多档会员、限时折扣、定时调价 |

## 13. 实施影响

### 13.1 后端

- 迁移（含 8 折初始化与系统审计）+ 同步 `jokester.admin.sql`。
- 新服务、新 Controller、`PointService` 预留路径改造。
- `AGENTS.md` 的 “Points And AI Image Billing” 一节补充：legacy 价格在事务内查价、VIP 价与折扣规则、VIP 专属模型准入（含超管豁免）与判定顺序、VIP 撤销不补收。

### 13.2 前端（`Jokester-Ai-Image`）

- 新增 `/admin/pricing`（标准价、默认/模型折扣、手动 VIP 价、访问策略开关、审计）。
- 生图页：VIP 角标与锁定态、超管体验提示、实付价/原价展示、刷新策略、`PRICE_CHANGED` / `VIP_REQUIRED` 处理。
- 任务历史：展示计价档与节省积分。

### 13.3 移动端

- 不升级也能用：价格字段兼容；VIP 专属模型会显示但非 VIP 提交时收到可展示的 403 提示。
- 升级后读取 `access` 展示锁定态，读取 `effectivePoints` 展示 VIP 价。

### 13.4 部署

1. 执行迁移：新增列可空或有默认值，旧版 API 忽略 `vip_points`，迁移后到 API 部署前行为不变。
2. 部署 API：**部署完成即生效 VIP 8 折**，有效 VIP 用户所有模型按 VIP 价扣分；标准价不变；所有模型仍是全员可用。
3. 部署 Web。
4. 运营按需把 `gpt-image-2.6-sunburst` 等模型设为仅 VIP，调整折扣或个别档位。

上线前需要通知运营：API 部署时点即 VIP 优惠开始时点。

## 14. 已确认决策

| 编号 | 问题 | 结论 |
| --- | --- | --- |
| D1 | 生产是否启用 `size-mode-v1` | 未启用（2026-11-24）。一期覆盖全部线上流量；二期是开启 size-mode 的前置条件 |
| D2 | 超管是否受 VIP 专属限制 | 不受限，可使用所有模型；计价只看本人是否有效 VIP，非 VIP 按标准价 |
| D3 | VIP 撤销/到期后是否补收或退费 | 都不做：不追收 VIP 优惠差额，不退还已扣积分 |
| D4 | 非 VIP 能否看到 VIP 模型 | 所有客户端都能看到，但不可使用 |
| D5 | 迁移改价是否写审计 | 写，`operator_user_id=0` |
| D6 | 大幅调价阈值 | ±50%，配置项 `AiImagePricingAdmin.LargeChangePercent` |
| D7 | VIP 默认价格 | 默认 8 折（向上取整），管理员可改默认折扣、模型折扣和单档 VIP 价；现有标准价不变 |

## 15. 验收标准

### 15.1 后端测试

- 迁移后所有 `points` 与迁移前一致；`vip_points = CEIL(points × 0.8)`，不低于标准价的档为 NULL；每档有一条 `system_init` 审计。
- 改价后下一笔 legacy 任务按新价扣分；已创建任务的结算/退款仍按旧价。
- 自动档标准价变化时 VIP 价同事务重算；手动档不变；标准价降到不高于手动 VIP 价 → 422 `VIP_PRICE_NOT_LOWER`。
- 默认折扣 80 → 70：只重算“跟随默认”模型的自动档；设置了模型折扣的模型和手动档不变；`previewToken` 过期 → 409。
- 模型折扣恢复跟随默认后，该模型自动档按默认折扣重算。
- 每类后台写操作都使 `pricing_revision` 恰好加 1；修改价格或访问策略不改变 `revision`，进行中的默认折扣预览仍可确认；校验失败回滚时 `pricing_revision` 不变。
- 生图预留路径不对 `ai_image_pricing_setting` 加锁（并发生图与后台改价不在该行上互相等待）。
- 同 key 同 payload 在改价后重放，返回原任务且不重复扣分。
- 并发：两个管理员基于同一 revision 提交，后者 409；改价与预留并发时任务价格要么全是旧价要么全是新价。
- 批量保存中任一行校验失败，整批不落库、不写审计；越界、原因为空 → 422；超阈值未确认 → 422；禁用最后启用价未确认 → 409。
- 有效 VIP 按 `vipPoints` 扣分，记录 `pricing_tier=vip`、`list_unit_point_cost`；`vipPoints` 为 NULL 时按标准价、`pricing_tier=standard`。
- VIP 在前置检查后、事务前到期：事务内按标准价（`vip_only` 模型且非超管则 403），不会按 VIP 价扣分。
- `vip_only` 模型：
  - 普通用户在四个生成入口都返回 403 `VIP_REQUIRED`，不占 Redis 配额、不扣分；
  - 有效 VIP 正常创建，`access_basis=vip`，VIP 价；
  - 非 VIP 超管正常创建，`access_basis=super_admin`，标准价；
  - 同时是 VIP 的超管：`access_basis=vip`，VIP 价；
  - Token 仍带超管声明、但数据库已取消超管的非 VIP 用户 → 403。
- 创建后 VIP 撤销 / 模型改为 `vip_only` / 超管被取消：已创建任务继续处理并正常结算，不产生差价追收或退款流水。
- `expectedUnitPoints` 低于实付价 → 409 `PRICE_CHANGED`，无任务、无流水、Redis 占位已释放；不低于 → 正常创建。
- 模型列表对普通用户、VIP、超管都包含 `vip_only` 模型，`access.available` 分别为 false / true / true。
- 后台接口：非超管 403；Token 有效但数据库中已被禁用或取消超管 → 403。

### 15.2 前端 E2E

- 管理员改价 → 普通用户切回窗口后看到新价 → 提交按新价扣分。
- 用户停留在旧价页面期间管理员涨价 → 提交收到价格变更提示，确认后按新价成功。
- 管理员把默认折扣改为 7 折 → VIP 用户刷新后看到新 VIP 价，普通用户价格不变。
- 非 VIP 点击 VIP 专属模型 → 锁定态与开通引导 → 购买月卡后可用，并显示 VIP 价。
- 非 VIP 超管可直接使用 VIP 专属模型，页面提示按标准价计费。

### 15.3 产品验收

- 上线后所有模型标准价不变，VIP 用户自动享受 8 折（向上取整）。
- 管理员能在页面上修改任一模型的标准价、默认 VIP 折扣、单个模型折扣和单档 VIP 价，修改后新任务立即按新价扣分。
- 把 `gpt-image-2.6-sunburst` 设为仅 VIP：普通账号能看到但不能用，VIP 账号可用并按 VIP 价，非 VIP 管理员可用并按标准价。

## 16. 监控

- 指标：`PRICE_CHANGED`、`VIP_REQUIRED` 次数（按模型）；VIP 价任务占比与节省积分总量；超管体验 VIP 模型的任务数；未定价档位被请求次数。
- 告警：某启用模型出现“有路由无启用价格”；单日价格审计条数异常（疑似误操作或脚本）。
- 运营报表：VIP 专属模型使用量与月卡转化（从锁定引导到购买）。
