# AI 模型配置与模型状态 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v0.2 |
| 日期 | 2026-11-22 |
| 需求状态 | P0 后端已在当前工作树实现并通过单元测试，迁移已在 MySQL 8.4 临时库演练（未合并、未部署）；前端未开始；第 14 节待决策项按建议值实施 |
| 适用端 | 用户 Web（`Jokester-Ai-Image`）、Jokester Admin API；Android / iOS 首期不接入 |
| 后端基线 | `ai_image_model_config` 路由表、`ai_image_task` 任务表、`IAiImageModelConfigService`、`IAiImageCatalogService`、`IAiImageProviderGate` |
| 关联契约 | [integration-guide.md](./integration-guide.md)、[ai-image-size-mode-prd.md](./ai-image-size-mode-prd.md)、[security-hardening-prd.md](./security-hardening-prd.md)、[runbook.md](./runbook.md) |

## 2. 推荐结论

1. **模型配置直接读写现有 `ai_image_model_config`，不另建配置表。** 该表已经按 `model_code + resolution_code + route_role` 保存每条路由的地址、Key、路径和请求格式，`AiImageModelConfigService` 每次调用都实时读库，保存后对下一次 Provider 调用立即生效，无需重启或发版。
2. **API Key 只写不读。** 任何接口（包括超级管理员接口）都不返回明文 Key，只返回掩码和“已配置”标记；编辑时留空表示不修改，与 `SaveUserRequest.password` 的约定一致。
3. **模型状态是“只读、聚合、脱敏”的公开信息。** 登录用户都能看，但只暴露模型展示名、可用性、耗时和红绿序列，不暴露地址、Key、供应商模型 ID、错误详情、用户或任务标识。
4. **状态只统计“模型自身的成败”。** 提示词拦截、授权缺失、素材无效等用户侧失败不计入样本，否则一个用户反复提交违规提示词就能把模型刷成“异常”。
5. **状态带时间窗。** 取“近 24 小时内最近 60 次”而不是“历史最近 60 次”。否则模型一旦显示“暂不可用”，用户不再使用，就不会有新样本，状态永远恢复不了。
6. **已发布的 `size-mode-v1` 路由要特殊处理。** 发布快照 `ai_image_model_release_route` 冻结了地址并绑定了 Key 的哈希；后台换 Key 必须同事务同步哈希，否则该模型会立刻 503。详见 9.4。

## 3. 背景

当前新增或调整模型路由只能靠手写 SQL 或迁移脚本（例如 `20260909-add-gpt-image-25-models.sql`、`20261105-add-gpt-image-26-models.sql`），中转站地址失效或 Key 轮换时需要有数据库权限的人登录服务器处理，响应慢且容易写错。

同时用户无法判断某个模型当前是否可用：中转站波动时，用户只能反复提交、等待超时、拿到退款，体验差并且产生客服咨询。

参考截图为“模型状态”页：

- 左侧导航在“定价公示”下新增“模型状态”入口。
- 页头：标题“模型状态”，副标题“展示当前模型状态和近期生图情况。”，右上角“刷新状态”按钮。
- 每个模型一张卡片，四列栅格：
  - 模型名（过长省略）与状态徽标：`正常`（绿）、`波动`（橙）、`异常`（红）。
  - 状态说明：“当前运行正常。”/“近期成功率波动，请谨慎使用。”/“近期成功率较低，暂不可用。”
  - 可用性百分比大字。
  - “最快成功”“最慢成功”两格，单位秒。
  - “近 60 次请求”红绿条，左 `PAST` 右 `NOW`；样本不足 60 时条数变少、条变宽（截图中 2.6-Flare 只有 11 条）。

## 4. 目标与非目标

### 4.1 目标

1. 超级管理员可以在 Web 后台查看、编辑、新增、启停模型路由，直接填写 API 地址和 Key。
2. 配置保存后立即对新的 Provider 调用生效，不需要重启、发版或手写 SQL。
3. API Key 不出现在任何响应、日志、操作审计和错误信息中。
4. 所有登录用户可以查看每个已启用模型的状态卡片，内容与截图一致。
5. 配置变更可追溯：谁、何时、改了哪条路由的哪些字段。

### 4.2 非目标（首期）

- ~~在后台新建全新模型编码及其价格~~：2026-11-24 已支持。模型配置页“新增模型”创建模型编码与第一条主路由，价格在“生图定价”页新增；未定价的模型不会出现在用户模型列表中。
- 在后台发布 / 撤销 `size-mode-v1` catalog release（仍只走 `--configure-ai-image-catalog`）。
- 按模型或按路由独立熔断（当前 `IAiImageProviderGate` 是全局熔断，不在本需求改动）。
- 根据模型状态自动禁用模型或自动切换路由。
- 积分价格、参数档位、敏感词的后台管理。
- 匿名（未登录）访问模型状态。
- Android / iOS 客户端接入。

## 5. 术语

| 术语 | 定义 |
| --- | --- |
| 模型 | 一个业务模型编码 `model_code`，如 `gpt-image-2.6-flare`，对用户展示 `model_name` |
| 路由 | `ai_image_model_config` 的一行：`model_code + resolution_code + route_role` 唯一，拥有自己的地址、Key、供应商模型和路径 |
| 主 / 备路由 | `route_role=primary` / `fallback`；主路由失败后回落到同模型同分辨率的备用路由 |
| 通用路由 | `resolution_code=''`，对该模型所有分辨率生效；有同分辨率专属路由时专属优先 |
| 发布快照 | `ai_image_model_release_route`，`size-mode-v1` 发布时对路由的冻结副本 |
| 样本 | 一条计入模型状态的终态单图任务（见 8.2） |

## 6. 用户与权限

| 角色 | 模型状态 | 模型配置 |
| --- | --- | --- |
| 未登录 | 不可见（401） | 不可见 |
| 普通登录用户 | 可见，无需菜单权限 | 不可见，接口返回 403，导航不出现入口 |
| 超级管理员 | 可见 | 可见，可读写 |

- 模型状态接口只要求登录，不新增权限码，做法与 `/api/ai/canvases` 一致。
- 模型配置不新增菜单权限码，按超级管理员身份判断，做法与 `POST /api/points/recharge/admin/codes` 一致。写操作必须在事务内重新查询并锁定当前用户，确认其仍为启用、未删除的超级管理员，避免已签发的 Access Token 在降权后 15 分钟内仍可改配置。
- 前端用登录 / 资料响应中的 `isSuperAdmin` 决定是否显示“模型配置”入口；前端隐藏只是体验，鉴权以后端为准。

## 7. 功能需求：模型配置（超级管理员）

### 7.1 列表页（`/admin/models`）

- 导航在“模型状态”下方增加“模型配置”，仅超级管理员可见。
- 按模型分组展示，组头显示模型名、模型编码、该模型当前状态徽标（复用模型状态数据）。
- 组内每条路由一行：

| 列 | 内容 |
| --- | --- |
| 分辨率 | `通用` 或 `1k / 2k / 4k` |
| 角色 | `主路由` / `备用路由` |
| 协议 | 由 `provider` 推导：`openai-image` / `gemini-image` |
| 供应商模型 | `provider_model` |
| API 地址 | `base_url`，过长中间省略，可复制 |
| API Key | 掩码，例如 `••••a1B2`；未配置显示红色“未配置” |
| 请求格式 | `openai-size` / `prompt-aspect-ratio` |
| 状态 | 启用 / 禁用开关 |
| 发布占用 | 被当前 `size-mode-v1` 发布引用时显示“已发布 {catalogVersion}”标记 |
| 更新时间 | 最近修改时间与修改人 |
| 操作 | 编辑、测试连接、变更记录 |

- 支持按模型名 / 编码搜索，按启用状态筛选。
- 列表默认包含已禁用路由，不包含已逻辑删除路由。

### 7.2 编辑 / 新增路由

表单字段：

| 字段 | 必填 | 规则 |
| --- | --- | --- |
| 模型 `modelCode` | 是 | 新增时只能从已有模型中选择；编辑时只读 |
| 模型展示名 `modelName` | 是 | 1–100 字符；修改后同步到该模型的所有路由（展示名是模型级属性） |
| 分辨率 `resolutionCode` | 否 | 空（通用）或 `ai_image_parameter` 中已启用的分辨率编码；编辑时只读 |
| 路由角色 `routeRole` | 是 | `primary` / `fallback`；编辑时只读 |
| 供应商协议 `provider` | 是 | 下拉，取值限定为后端支持的协议；编辑时只读 |
| 供应商模型 `providerModel` | 是 | 1–100 字符 |
| API 地址 `baseUrl` | 是 | 见 11.2 校验规则 |
| API Key `apiKey` | 新增必填 | 编辑时留空表示不修改；输入框为密码类型，不回显 |
| 文生图路径 `textToImagePath` | 否 | 以 `/` 开头，默认 `/images/generations` |
| 图生图路径 `imageToImagePath` | 否 | 以 `/` 开头，默认 `/images/edits` |
| 请求格式 `requestFormat` | 是 | `openai-size`（默认）/ `prompt-aspect-ratio` |
| 排序 `sort` | 否 | 整数 |
| 状态 `status` | 是 | 新增时选择启用 / 禁用；编辑表单不改状态，启停走 7.3 的独立接口 |

交互规则：

1. `modelCode + resolutionCode + routeRole` 是唯一键，编辑时不可改；要换分辨率或角色就新增一条。
2. 保存使用乐观并发：请求带上读取时的 `revision`，不一致返回 409，前端提示“配置已被他人修改，请刷新后重试”。
3. 修改地址、Key、供应商模型或路径时弹二次确认，文案说明“保存后立即对新的生图请求生效，排队中的任务也会使用新配置”。
4. 保存成功后提示是否立即“测试连接”。
5. 首期不提供删除，只提供禁用。原因：唯一键包含已逻辑删除的行，且发布快照对该表有外键。

### 7.3 启用 / 禁用保护

禁用或保存为禁用前，后端检查并拒绝（409）以下情况：

- 禁用后该模型在某个仍有启用价格的分辨率上没有任何启用的路由（主、备均计入，因为运行时主路由缺失会直接走备用；通用路由 `resolution_code=''` 覆盖所有分辨率）。此时返回 409 `MODEL_ROUTE_OFFLINE_CONFIRM_REQUIRED`，`details.resolutionCodes` 列出将下线的分辨率；管理员勾选“我确认下线”后带 `confirmOffline=true` 重试即可。
- 该路由被已发布且未撤销的 `size-mode-v1` 发布引用。必须先通过 `--configure-ai-image-catalog` 发布不含该路由的新版本，首期不允许后台绕过（`confirmOffline` 也不能绕过）。

仅有备用路由而没有主路由的状态允许保存，列表分组返回 `missingPrimaryRoute=true`，前端给出黄色提示“缺少主路由”。

启用前后端会确认该路由已有 Key、协议可识别，且地址满足 11.2 的校验。

### 7.4 测试连接

- 对已保存的路由发起一次轻量探测：按协议请求模型列表端点（OpenAI 协议 `GET {baseUrl}/models`，Gemini 协议对应的模型列表端点），携带该路由的 Key，超时 10 秒。
- 结果三态：`passed`（2xx）、`auth_failed`（401/403）、`unreachable`（其它状态码、超时、DNS 失败、地址被拦截，前端文案为“不可达 / 不支持探测”）。只向前端返回三态、HTTP 状态码和耗时，不透传上游响应体。
- 探测不产生生图费用、不扣积分、不写任务、不计入模型状态样本、不计入全局熔断计数。
- 部分中转站不实现模型列表端点，所以“不支持探测”不等于不可用；界面文案需要说明这一点。真实出图验证放 P1。
- 探测接口按用户和 IP 限流（建议每分钟 10 次）。

### 7.5 变更记录

- 每次新增、编辑、启停都写一条审计记录：操作人、时间、IP、路由标识、变更字段列表、非敏感字段的前后值。
- Key 变更只记录“已更换”以及新旧掩码，不记录明文、不记录完整哈希。
- 列表页“变更记录”抽屉按时间倒序展示最近 50 条，支持按路由过滤。

## 8. 功能需求：模型状态（所有登录用户）

### 8.1 页面（`/model-status`）

- 导航“定价公示”下方增加“模型状态”，所有登录用户可见。
- 页头：标题、副标题、“刷新状态”按钮（点击后重新请求，按钮 5 秒内不可重复点击）。
- 卡片栅格：桌面 4 列，平板 2 列，手机 1 列。
- 展示范围与顺序：与 `GET /api/ai/images/models` 返回的模型一致（已启用、有价格、有路由），按模型 `sort` 排序。管理员禁用的模型不出现在状态页。
- 页面每 60 秒自动刷新一次；页面不可见（切到后台标签）时暂停。
- 加载失败时保留上一次数据并提示“状态更新失败”，不清空卡片。

### 8.2 统计口径

**样本定义**：`ai_image_task` 中满足以下条件的任务，一条任务算一次请求。

- 结算时记录的统计模型编码（见 9.1 `health_model_code`）等于该模型；
- 已到终态：`status=1`（成功）或 `status=2`（失败）；
- 完成时间在最近 24 小时内；
- 属于“模型侧结果”（见下表）；
- **包含用户已软删除的任务**（用户删除自己的历史不应改变模型统计）。

每个模型取满足条件、按完成时间倒序的最近 60 条。GPT 多图请求已拆成单图任务，每张图算一次请求；Nano Banana 任务可能 `image_count > 1`，仍按一条任务算一次。

| 结果 | 是否计入 | 计为 |
| --- | --- | --- |
| 生成成功 | 是 | 成功 |
| Provider 返回错误、超时、输出无效、结果未知后超时结算 | 是 | 失败 |
| 提示词在 Worker 复检时被拦截（`PROMPT_BLOCKED`） | 否 | — |
| AI 授权失效（`AI_CONSENT_REQUIRED`） | 否 | — |
| 输入素材无效 / 被删除（`INPUT_ASSET_INVALID`） | 否 | — |
| 模型发布被撤销（`MODEL_RELEASE_REVOKED`） | 否 | — |
| 未派发即结算（Outbox 绑定超时、Redis 准入撤销等平台侧原因） | 否 | — |

主路由失败但备用路由成功的任务计为成功：状态页回答的是“用户现在用这个模型能不能出图”，不是单条路由的健康度。

**指标计算**：

| 指标 | 算法 |
| --- | --- |
| 可用性 | `成功样本数 / 样本数`，四舍五入取整百分比 |
| 最快成功 / 最慢成功 | 成功样本的 `completed_at − created_at`，取最小 / 最大值，单位秒取整；即用户从提交到拿到图的实际等待时间，含排队 |
| 近 60 次请求 | 样本按完成时间从旧到新排列的成败序列 |

**状态判定**：

| 状态 `state` | 条件 | 徽标 | 说明文案 |
| --- | --- | --- | --- |
| `normal` | 样本数 ≥ 5 且可用性 ≥ 90% | 正常（绿） | 当前运行正常。 |
| `degraded` | 样本数 ≥ 5 且 70% ≤ 可用性 < 90% | 波动（橙） | 近期成功率波动，请谨慎使用。 |
| `down` | 样本数 ≥ 5 且可用性 < 70% | 异常（红） | 近期成功率较低，暂不可用。 |
| `unknown` | 样本数 < 5 | 暂无数据（灰） | 近期请求较少，暂无足够数据。 |

阈值与截图一致：100%、92%、90% 为正常，89% 为波动，66%、45% 为异常。阈值、样本上限、时间窗、最小样本数做成配置项（见 9.6）。

`unknown` 时可用性、最快、最慢显示“—”，红绿条按实际样本数展示或留空。

### 8.3 展示细节

- 模型名取 `model_name`，单行省略，悬浮显示全名。
- 可用性数字颜色随状态变化（截图中统一为绿色，建议改为随状态变色，见 14 节）。
- 红绿条：等宽均分卡片宽度，样本少时条变宽；悬浮某一条显示“成功 / 失败 · 完成时间”，不显示任何任务或用户信息。
- 卡片底部小字显示“数据更新于 HH:mm:ss”。

### 8.4 与生图入口的联动

- 首期状态仅作展示，不拦截生成。用户仍可选择“异常”模型提交，失败按现有流程退款。
- 生图页模型下拉中，对 `degraded` / `down` 的模型在名称后追加对应徽标（P1）。

## 9. 后端设计

### 9.1 数据表变更

`ai_image_model_config` 新增列：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `revision` | `INT NOT NULL DEFAULT 0` | 乐观并发版本，每次后台保存加 1 |
| `api_key_updated_at` | `DATETIME NULL` | Key 最近一次更换时间（UTC） |
| `updated_by` | `BIGINT NULL` | 最近修改人用户 ID |

`ai_image_task` 新增列与索引：

| 项 | 定义 | 说明 |
| --- | --- | --- |
| `health_model_code` | `VARCHAR(100) NULL` | 结算时写入归一化后的业务模型编码；不计入样本的任务保持 `NULL` |
| `health_sample` | `TINYINT NULL` | 结算时写入：`1` 模型侧成功、`2` 模型侧失败；不计入样本或历史数据为 `NULL` |
| 索引 | `idx_ai_image_task_health (health_model_code, completed_at)` | 支撑按模型取最近样本；不计入的任务两列均为 `NULL`，不占用查询范围 |

- 两列只在后端内部使用，不进入任何任务 DTO，不改变 legacy 与 `size-mode-v1` 的响应结构。
- 之所以不复用现有 `model_code`：legacy GPT 与 Nano Banana 任务创建时不写 `model_code`，而是把模型编码写进了 `model_name`；只有 `size-mode-v1` 任务两列都写。直接按 `model_code` 统计会漏掉全部 legacy 任务，而补写 legacy 的 `model_code` 会改变现有任务 DTO 的 `modelCode` 输出。
- 之所以不复用 `failure_code`：legacy 任务结算时 `failure_code` 固定为 `NULL`，无法区分“模型挂了”和“提示词被拦截”。
- `health_model_code` 取值：`size-mode-v1` 任务取 `model_code`，legacy 任务取 `model_name`，统一经过 `AiImageModelConfigService.NormalizeModelCode`（处理模型别名）。
- 写入点：`PointService` 的任务终态结算（成功、失败退款）与 `AiImageTaskRecoveryWorker` 的超时结算，必须与状态更新在同一条 UPDATE / 同一事务内，不能另起一次更新。
- 历史数据：迁移脚本对最近 24 小时的终态任务做一次回填（`health_model_code = COALESCE(model_code, model_name)`，成功记 `1`、失败记 `2`），让状态页上线即有数据。回填无法识别用户侧失败，这部分误差会在 24 小时内滑出时间窗。不回填更早的数据。

新增审计表 `ai_image_model_config_audit`（时间存 UTC）：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `id` | `BIGINT` 主键 | |
| `config_id` | `BIGINT NOT NULL` | 路由 ID |
| `model_code` / `resolution_code` / `route_role` | 冗余快照 | 便于查询 |
| `action` | `VARCHAR(16)` | `create` / `update` / `enable` / `disable` / `rotate_key` |
| `changed_fields` | `VARCHAR(500)` | 逗号分隔的字段名 |
| `before_json` / `after_json` | `TEXT` | 仅非敏感字段；Key 以掩码表示 |
| `operator_user_id` | `BIGINT NOT NULL` | |
| `operator_ip` | `VARCHAR(50)` | |
| `created_at` | `DATETIME NOT NULL` | UTC |

迁移脚本 `docs/migrations/20261122-add-ai-model-admin.sql`，同步 `jokester.admin.sql`。`ai_image_task` 加列和加索引需评估表体量，使用在线 DDL；API 读取新列，必须先迁移再部署。

### 9.2 模型状态接口

`GET /api/ai/images/model-status`，要求登录，无权限码。

```json
{
  "generatedAt": "2026-11-22T08:30:00Z",
  "windowHours": 24,
  "sampleLimit": 60,
  "items": [
    {
      "modelCode": "gpt-image-2.6-flare",
      "modelName": "GPT Image 2.6 Flare",
      "state": "down",
      "availability": 45,
      "sampleCount": 11,
      "fastestSuccessSeconds": 18,
      "slowestSuccessSeconds": 66,
      "recent": [1, 1, 0, 0, 0, 0, 0, 1, 0, 1, 1],
      "recentCompletedAt": ["2026-11-22T07:02:11Z"],
      "lastSampleAt": "2026-11-22T08:21:40Z"
    }
  ]
}
```

- `recent` 从旧到新，`1` 成功、`0` 失败；`recentCompletedAt` 与其等长，用于悬浮提示。
- `state=unknown` 时 `availability`、`fastestSuccessSeconds`、`slowestSuccessSeconds` 为 `null`。
- 状态文案由前端按 `state` 映射，接口不返回中文文案。
- 响应不得包含：地址、Key 及其掩码、`provider_model`、路由角色、错误信息、失败码、用户 ID、任务 ID。

### 9.3 状态计算与缓存

- 后台 Worker `AiImageModelStatusWorker` 每 30 秒计算一次所有已启用模型的快照，保存为进程内不可变对象，并写入 Redis（TTL 120 秒）供多实例共享。
- 接口只读快照，不在请求路径上查 `ai_image_task`，因此“刷新状态”按钮不会放大数据库压力。
- Redis 不可用时退回本实例的进程内快照；启动后尚无快照时同步计算一次并缓存。
- 快照超过 5 分钟未更新时，接口仍返回旧数据，由前端根据 `generatedAt` 显示“数据可能延迟”。模型状态是展示信息，采用 fail-open，与 AI 创建路径的 fail-closed 相互独立。
- 接口按用户限流（建议每分钟 30 次）。

### 9.4 模型配置接口

前缀 `/api/ai/model-routes`，全部要求超级管理员。不放在 `/api/ai/images` 下：该前缀的 POST 已被生图创建限流（每用户每分钟 2 次）覆盖。

| 方法与路径 | 说明 |
| --- | --- |
| `GET /` | 列表，按模型分组；支持 `keyword`、`status` |
| `GET /{id}` | 单条详情 |
| `POST /` | 新增路由 |
| `PUT /{id}` | 编辑路由，请求体带 `revision`；不改启停状态 |
| `PUT /{id}/status` | 启用 / 禁用，带 `revision`、可选 `confirmOffline` |
| `POST /{id}/test` | 测试连接 |
| `GET /audits` | 变更记录，支持 `configId`、分页 |

响应中的路由对象：

```json
{
  "id": 12,
  "revision": 3,
  "modelCode": "gpt-image-2.6-flare",
  "modelName": "GPT Image 2.6 Flare",
  "resolutionCode": "",
  "routeRole": "primary",
  "provider": "openai",
  "providerProtocol": "openai-image",
  "providerModel": "gpt-image-2.6-flare",
  "baseUrl": "https://relay.example.com/v1",
  "apiKeySet": true,
  "apiKeyMask": "••••a1B2",
  "apiKeyUpdatedAt": "2026-11-20T03:12:00Z",
  "textToImagePath": "/images/generations",
  "imageToImagePath": "/images/edits",
  "requestFormat": "openai-size",
  "sort": 20,
  "status": 1,
  "publishedCatalogVersions": [],
  "updatedAt": "2026-11-20T03:12:00Z",
  "updatedBy": "admin"
}
```

写请求体包含 `apiKey`（明文；编辑时为空表示不修改）；任何响应都没有 `apiKey` 字段。`publishedCatalogVersions` 列出引用该路由的已发布、未撤销 catalog 版本，非空即表示 9.4 的冻结规则生效。列表接口返回按模型分组的 `[{ modelCode, modelName, state, missingPrimaryRoute, routes[] }]`。

**与 `size-mode-v1` 发布快照的关系**（必须实现，否则换 Key 会造成线上 503）：

`AiImageCatalogService` 解析已发布路由时，地址、路径、供应商模型取自快照 `ai_image_model_release_route`，Key 取自 `ai_image_model_config`，并校验 `SHA-256(api_key)` 等于快照的 `secret_version_hash`，不等则抛 503。因此：

| 后台操作 | 对被当前发布引用的路由 | 处理 |
| --- | --- | --- |
| 更换 Key | 快照哈希失配 → 该模型 503 | 同一事务内把引用该路由、且属于已发布未撤销发布的快照行 `secret_version_hash` 更新为新 Key 的哈希；已撤销的发布不再解析，保持原值。Key 轮换不改变 catalog 的能力、价格或端点，不视为改写已发布 catalog |
| 修改地址 / 路径 / 供应商模型 | 快照不变，改了也不生效，后台显示与实际执行不一致 | 首期拒绝，返回 409 `MODEL_ROUTE_LOCKED_BY_RELEASE`，提示通过 `--configure-ai-image-catalog` 发布新版本 |
| 修改请求格式、排序、展示名 | 请求格式取自配置表，立即生效 | 允许 |
| 禁用 | 快照仍会解析到该路由 | 首期拒绝，同上 |

未被发布引用的路由（当前仓库默认 `AiImageSizeMode.Enabled=false`，即全部 legacy 路由）不受上述限制，所有字段都可以直接改。

**生效时机**：

- 新的 Provider 调用立即使用新配置；已排队未处理的任务在 Worker 处理时解析路由，也使用新配置。
- 正在进行中的 Provider 请求不受影响。
- 保存时不清除全局熔断状态；管理员修好路由后，熔断按现有 `ProviderCircuitOpenSeconds` 自然恢复。

### 9.5 服务与代码落点

- 新增 `IAiImageModelRouteAdminService`（列表、保存、启停、探测、审计）和 `IAiImageModelStatusService`（读快照，单例）；地址校验集中在 `AiImageRouteEndpointPolicy`，DNS 解析经 `IHostAddressResolver` 注入便于测试；成败归类集中在 `AiImageModelHealth`。
- Controller 只做参数绑定，超级管理员校验、事务、审计都在 Service 内。
- 数据访问继续使用 `SqlSugar + Mapster`，不引入手写 SQL 风格。
- 探测请求使用独立命名的 `HttpClient`，禁止自动跟随重定向，10 秒超时。
- `provider` 取值白名单与 `AiImageModelConfigService.ResolveProviderProtocol` 保持同一来源，避免后台能保存出运行时无法识别的协议。

### 9.6 配置项

新增配置节 `AiImageModelStatus`，全部有代码内默认值（这些是展示参数，不属于“缺配置即失败”的密钥类配置）：

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `SampleLimit` | 60 | 每模型最多样本数 |
| `WindowHours` | 24 | 样本时间窗 |
| `MinSampleCount` | 5 | 低于该值为 `unknown` |
| `NormalThresholdPercent` | 90 | 正常下限 |
| `DegradedThresholdPercent` | 70 | 波动下限 |
| `RefreshSeconds` | 30 | 快照刷新间隔 |

新增配置节 `AiImageModelAdmin`：

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `AllowedHosts` | 空 | 非空时，`baseUrl` 主机必须在名单内 |
| `AllowPrivateNetworkHosts` | 空 | 允许指向内网 / 容器网络的主机名白名单（见 11.2） |

## 10. 错误契约

| HTTP | 机器码 | 场景 |
| --- | --- | --- |
| 401 | `UNAUTHORIZED` | 未登录 |
| 403 | `RESOURCE_FORBIDDEN` | 非超级管理员访问配置接口；事务内复核发现已不是启用的超级管理员 |
| 404 | `RESOURCE_NOT_FOUND` | 路由不存在或已删除 |
| 400 | `VALIDATION_ERROR` | 字段校验失败；`baseUrl` 不合法或不在允许范围 |
| 409 | `CONFLICT` | `modelCode + resolutionCode + routeRole` 已存在 |
| 409 | `MODEL_ROUTE_REVISION_CONFLICT`（新增） | `revision` 不一致 |
| 409 | `MODEL_ROUTE_LOCKED_BY_RELEASE`（新增） | 修改或禁用被当前发布引用的路由的冻结字段 |
| 409 | `MODEL_ROUTE_OFFLINE_CONFIRM_REQUIRED`（新增） | 禁用将导致模型 / 分辨率无主路由且未带 `confirmOffline` |
| 429 | `RATE_LIMITED` | 写接口、探测接口、状态接口限流 |

校验错误信息不得回显请求中的 Key。

## 11. 安全与隐私

### 11.1 API Key

1. **只写不读**：没有任何接口返回明文 Key。掩码只保留末 4 位；长度不足 8 位时全掩码。
2. **不进日志**：`OperationLogMiddleware` 当前不记录请求体，需保持；新增代码不得把请求对象整体序列化进日志、异常消息或审计记录。Provider 调用失败日志不得打印 `Authorization` 头。
3. **不进前端状态持久化**：前端表单提交后清空 Key 输入框，不写入 `localStorage` 或 URL。
4. **传输**：配置接口仅在 HTTPS 下可用（生产由 nginx 终止 HTTPS，已满足）。
5. **存储**：首期沿用现状，Key 以明文保存在 `ai_image_model_config.api_key`（Provider 调用需要可还原的明文，无法只存哈希）。落盘加密放 P1，依赖先治理 DataProtection 密钥持久化（见 AGENTS.md 已知问题），否则容器重建会导致全部 Key 无法解密。
6. 仓库、迁移脚本、文档示例中不得出现真实地址和 Key。

### 11.2 API 地址（SSRF 防护）

后台填写的地址会被服务端主动请求，等同于给了超级管理员一个服务端请求入口，需要限制：

- 必须是绝对 URL，scheme 只允许 `https`；不允许带用户名密码、查询串和片段。
- 主机名解析后的所有地址不得落在回环、私有网段、链路本地（含云厂商元数据地址 `169.254.169.254`）、CGNAT、组播/保留、IPv6 本地地址。保存、启用和测试连接时各校验一次；测试连接使用的 HttpClient 在建立连接时自行解析并再校验一次（防 DNS 重绑定），且不跟随重定向。
- **P0 未覆盖**：生图调用（`AiImageService` / `NanoBananaImageService` 的 HttpClient）尚未在连接时复检地址。原因是存量路由可能指向内网中转，直接加会中断线上生图；需先核对存量地址并配置 `AllowPrivateNetworkHosts` 后再接入，列入 P1。
- `AiImageModelAdmin.AllowedHosts` 非空时改为白名单模式。
- 确实需要指向内网或同一 Docker 网络内的中转服务时，把主机名加入 `AllowPrivateNetworkHosts`，该主机允许 `http` 和私有地址。这属于部署决策，走部署配置而不是后台界面。
- 存量 `http` 路由继续可用，但再次保存时必须满足上述规则。
- 路径字段必须以 `/` 开头，不得包含 `..`、`//`、`?`、`#` 或完整 URL。

### 11.3 模型状态的信息暴露

- 状态接口对所有登录用户开放，返回内容限定为 9.2 列出的字段。
- 不暴露失败原因：失败原因可能包含上游返回的内部信息。
- 不暴露路由级数据：外部无法从状态页推断主备结构和供应商。
- 样本量很小时，单个用户可以从红绿条变化反推“刚才那次失败是不是我的”，这只涉及其本人的任务，可接受。

### 11.4 权限与审计

- 写接口在事务内复核超级管理员身份，并按用户和 IP 双重限流（建议每分钟 20 次）。
- 所有写操作写入 `ai_image_model_config_audit`，与配置更新同事务；审计写入失败则整体回滚。
- 更换 Key 是否需要二次输入登录密码见 14 节。

## 12. 分期

| 阶段 | 内容 |
| --- | --- |
| P0 | 模型状态接口与页面；`health_sample` 写入与快照 Worker；模型路由的列表、编辑、新增、启停；Key 只写不读；发布快照哈希同步与冻结字段保护；SSRF 校验；审计表；轻量测试连接 |
| P1 | 生图调用 HttpClient 连接时地址复检（见 11.2）；新建模型（从已有模型复制路由与价格）；真实出图测试（不扣积分、不计入状态）；管理员手动标记“维护中”及公告文案；管理员重置某模型的状态统计；生图页模型下拉显示状态徽标；Key 落盘加密 |
| P2 | 路由级健康度（主 / 备分别统计，仅管理员可见）；按模型熔断；状态异常时通知管理员；Android / iOS 接入状态接口 |

## 13. 实施影响

### 13.1 后端

- 新增：`AiModelRoutesController`（`/api/ai/model-routes`）、`AiModelStatusController`（`/api/ai/images/model-status`）、`IAiImageModelRouteAdminService`、`IAiImageModelStatusService`、`AiImageModelStatusWorker`、`AiImageModelConfigAuditEntity`、对应 DTO 与 `MachineErrorCodes` 三个新码；`SecurityRateLimitMiddleware` 增加写接口、测试连接和状态接口的限流规则。
- 修改：`AiImageModelConfigEntity`、`AiImageTaskEntity` 加列；`PointService` 任务结算统一写 `health_model_code` / `health_sample`。`IPointService.SettleImageTaskAsync` 新增可选参数 `modelSideFailure`（默认 `true`）：准入失败、未开始即过期的任务传 `false`，Worker 失败按异常类型归类；versioned 任务按 `failure_stage` 归类（`provider` / `output` 计入）。
- 不改：`AiImageModelConfigService` 的路由解析逻辑、`IAiImageProviderGate`、积分与幂等流程、所有现有任务 DTO。
- 文档：更新 `docs/integration-guide.md`、`docs/runbook.md`（Key 轮换改为后台操作的步骤）、`AGENTS.md` 增加本契约小节。

### 13.2 前端（`Jokester-Ai-Image`）

- 新增路由 `/model-status`（登录可见）与 `/admin/models`（`isSuperAdmin` 可见，路由守卫拦截）。
- 侧边导航增加两个入口；状态卡片、红绿条、配置表格与编辑抽屉组件。
- 状态页轮询与页面可见性处理。

### 13.3 部署

- 先执行迁移，再部署 API，最后部署前端。
- 生产环境确认 `AiImageModelAdmin.AllowedHosts` / `AllowPrivateNetworkHosts` 是否需要配置（取决于现有中转站地址是否为内网或 `http`）。上线前先核对存量路由地址，避免上线后第一次保存就被校验拦住。

## 14. 待决策项

| # | 问题 | 建议 |
| --- | --- | --- |
| 1 | “所有人可见”是否包含未登录访客？ | 仅登录用户。匿名开放会暴露平台实时成功率给竞品和爬虫，且状态页入口在登录后的导航里 |
| 2 | 耗时口径：含排队（提交到完成）还是只算生成（开始到完成）？ | 含排队，这是用户实际等待时间；截图中 1553s 这类长尾也更符合含排队的口径 |
| 3 | 状态是否拦截生成（“暂不可用”时禁止提交）？ | 首期不拦截，仅展示。自动拦截会让模型无法靠新样本恢复 |
| 4 | 可用性数字是否随状态变色？ | 变色。截图中 45% 仍为绿色，容易误读 |
| 5 | 被发布引用的路由，后台改地址的处理 | 首期拒绝并引导走发布命令；若运营上确实需要后台改，再评估“后台触发发布新版本” |
| 6 | 更换 Key 是否要求二次输入登录密码？ | 首期不要求（已有事务内超管复核 + 限流 + 审计）；如果超管账号多于 2 人建议开启 |
| 7 | 时间窗 24 小时是否合适？ | 合适。冷门模型一天不足 5 次请求时显示“暂无数据”，比展示一周前的失败更诚实 |
| 8 | 是否提供删除路由？ | 首期不提供，只禁用 |
| 9 | 测试连接是否做真实出图？ | 首期只做轻量探测；真实出图会产生上游费用，放 P1 并加确认 |

## 15. 验收标准

### 15.1 后端测试

1. 非超级管理员调用任一配置接口返回 403；被降权但 Token 未过期的用户调用写接口返回 403。
2. 列表、详情、保存响应、审计记录、操作日志中均搜索不到明文 Key。
3. 编辑时 `apiKey` 留空，数据库中的 Key 不变；填写新值后下一次 Provider 调用使用新 Key。
4. 两个会话基于同一 `revision` 先后保存，后者返回 409 `MODEL_ROUTE_REVISION_CONFLICT`。
5. 对被当前发布引用的路由更换 Key 后，`size-mode-v1` 创建与 Worker 解析路由不出现“secret version is unavailable”。
6. 对被当前发布引用的路由修改地址或禁用，返回 409 `MODEL_ROUTE_LOCKED_BY_RELEASE`。
7. `baseUrl` 为 `http://`、回环地址、私有网段、`169.254.169.254`、带凭据的 URL 时保存失败；加入 `AllowPrivateNetworkHosts` 的主机可以保存。
8. 禁用某模型某分辨率唯一的启用路由：未带 `confirmOffline` 返回 409；带上后成功。仍有通用备用路由时禁用主路由无需确认，列表标记 `missingPrimaryRoute=true`。
9. 状态统计：提示词拦截、授权失效、素材无效的失败任务不改变可用性；用户软删除任务后可用性不变；24 小时之前的任务不计入；legacy GPT、Nano Banana、`size-mode-v1` 三类任务都能计入对应模型，且任务列表 / 详情接口的响应字段与上线前一致。
10. 样本数为 0、4、5 时状态分别为 `unknown`、`unknown`、按阈值判定；可用性 90 / 89 / 70 / 69 分别为 `normal` / `degraded` / `degraded` / `down`。
11. Redis 不可用时状态接口仍返回数据；状态接口不触发对 `ai_image_task` 的同步查询（启动首个请求除外）。
12. 状态接口响应中不包含地址、Key、供应商模型、错误信息、用户 ID、任务 ID。

### 15.2 前端 E2E

1. 普通用户看得到“模型状态”，看不到“模型配置”，直接访问 `/admin/models` 被拦回。
2. 超级管理员修改地址并保存后，列表立即显示新地址，Key 列仍为掩码。
3. 状态页卡片与截图布局一致；样本不足 60 时红绿条变宽；样本不足 5 时显示“暂无数据”。
4. 点击“刷新状态”重新请求，5 秒内按钮不可用；切到后台标签后停止轮询。

### 15.3 产品验收

1. 运营在不接触数据库的情况下完成一次 Key 轮换和一次中转站地址切换，全程不中断生图。
2. 人为把某模型主备路由都指向无效地址，至少 5 次失败后状态页在 1 分钟内显示“异常”；恢复配置后，随着新成功样本进入，状态回到“正常”。

## 16. 监控

- 指标：每模型可用性、样本数、状态切换次数；配置写操作次数；探测结果分布；快照刷新耗时与延迟。
- 日志：配置写操作记录操作人、路由 ID、变更字段名（不含值）；快照刷新失败记录异常类型。
- 告警（P2）：任一模型进入 `down` 持续 10 分钟；快照延迟超过 5 分钟；被发布引用的路由出现哈希失配。
