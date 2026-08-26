# AI 生图尺寸模式与 GPT 自动尺寸 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.4 |
| 日期 | 2026-08-21 |
| 需求状态 | 后端实现已落在当前工作树并通过定向测试；尚未合并、真实 MySQL 演练或部署，auto 路由/价格仍未发布，前端、产品、DBA、安全与运维验收待完成 |
| 适用端 | 用户 Web、Android、iOS、Jokester Admin API、AI Worker |
| 前端基线 | `Jokester-Ai-Image` 当前 Playground、草稿和任务历史实现 |
| 后端基线 | 本文设计时基线为 `POST /api/ai/images` 统一入口、GPT 主备路由、固定尺寸算法与积分预留状态机；当前工作树已加入第二阶段兼容实现 |
| 关联契约 | [integration-guide.md](./integration-guide.md)、[architecture.md](./architecture.md)、[runbook.md](./runbook.md)、[point-package-frontend-prd.md](./point-package-frontend-prd.md) |

## 2. 推荐结论

采用“两阶段、能力驱动”的实现。

1. 第一阶段立即恢复稳定性。前端严格消费 `GET /api/ai/images/models` 返回的模型级比例能力，不再从全局参数目录或 Provider 名称为 GPT 补出 `auto`。该阶段不改变后端尺寸、计费和路由契约，前端可单独部署。
2. 第二阶段正式引入 `sizeMode=explicit|auto`。只有路由能力、独立价格、任务记录和实际图片尺寸回填全部就绪后，才向客户端开放 GPT 自动尺寸。
3. 能力未知时一律按不支持处理。不得仅凭 `provider=openai-image`、模型业务编码或官方模型说明推断某条生产路由支持 `auto`。

## 3. 背景与问题

当前系统同时存在三层不同含义的数据，并且仓库代码、干净初始化脚本与已部署数据库可能不完全一致：

- 前端全局 fallback、历史 fixture 或部分已部署数据库可能包含供 Nano/Gemini 使用的 `auto`；当前后端代码会从 `GET /api/ai/images/parameters` 临时过滤 `auto`，而仓库完整 SQL 也尚未 seed `auto`，不能把生产库存在该行当作已证明事实；
- `GET /api/ai/images/models` 返回当前启用模型的有效比例能力，GPT 当前只返回显式比例；
- 前端在模型能力缺失时仍可能回退到全局比例目录，使未声明 `auto` 的模型得到 `auto` 选项。

这会造成前端展示能力与后端实际路由能力不一致。GPT 请求提交 `aspectRatioCode=auto` 后，当前后端在进入 Provider 前拒绝请求，公网用户无法正常生成。现有错误文案又把该限制描述为 GPT Image 2 的官方限制，掩盖了真正原因：当前站点的生产路由、计费和任务数据尚未完成自动尺寸适配。

即使上游官方接口接受 `size="auto"`，当前系统也不能直接全局放开：

- 计费仍按 `modelCode + resolutionCode + qualityCode` 固定匹配；
- GPT 主路由和 fallback 可能使用不同 `provider_model`，现有第三方 `gpt-image-2-1k` / `gpt-image-2-4k` 别名没有经过 auto 验证；
- 排队任务在调用 Provider 前就写入固定 `width`、`height` 和 `size`，无法表达“请求自动、输出尚未知”；
- 幂等、草稿恢复和任务找回都把分辨率与比例作为固定字段处理。

## 4. 目标与非目标

### 4.1 目标

1. 第一阶段消除前端虚构能力，恢复 GPT 显式比例生成的稳定性。
2. 模型切换、旧草稿恢复和复用历史参数时，始终得到当前模型支持的合法默认值。
3. 第二阶段用明确的尺寸模式表达自动尺寸和显式尺寸，避免字段组合含糊。
4. auto 只能通过已验证的生产路由发送，并按独立价格在任务创建前完成确定性扣费。
5. 任务区分请求尺寸与实际输出尺寸，排队期间不再长期保存或展示 `0x0`。
6. 新旧客户端在兼容期内得到一致的规范化请求、幂等结果和计费结果。
7. 升级期间完整保留现役 Nano/Gemini legacy auto、历史 GPT auto、旧请求默认值和历史幂等任务，不把它们错误迁移为新 GPT auto 或 explicit。
8. Worker、恢复线程、账户删除和队列派发在竞态下不能出现“已退款后又发布结果”、本地可避免的重复 Provider 调用或未结算预留；上游不支持幂等/状态查询时，未知在途调用必须隔离而不是自动二次外发。

### 4.2 非目标

- 第一阶段不修改积分价格、数据库结构、Provider 请求或主备切换规则。
- 不根据最终输出像素动态补扣或退款；auto 价格在请求前确定。
- 不保证任何第三方模型别名天然支持 auto，必须逐路由验证。
- 第二阶段首个正式开放对象是 GPT Image2“一行任务对应一张图”的流程；请求仍可 `imageCount>1` 并拆成有序单图任务，直接生成响应按逐图 `results[]` 表达。Nano/Gemini 旧 auto 契约继续兼容，迁移到统一 `sizeMode` 前必须先完成一任务多图的逐图尺寸模型。
- 不改变提示词过滤、AI processing consent、参考图所有权、Redis 准入、积分扣款顺序和失败退款规则。
- 不在本 PRD 中确定 auto 的具体积分数值；价格由产品和运营在第二阶段发布前审批。

### 4.3 现役不变量与第二阶段新增能力

以下是现役实现或项目级契约，第一、二阶段均不得削弱；它们不是本 PRD 新增实现的事实：

1. Redis admission 不可用时，任何新的 AI admission 必须 fail-closed；已提交请求的 durable 幂等重放属于数据库事实读取，不依赖 Redis。Redis Lua 继续负责削峰、额度、活动任务、全局积压和临时幂等占位。
2. 任务插入、余额扣减和逐任务 `image:{taskId}:reserve` 流水必须在同一数据库事务中；任务成功/失败结算、余额返还和唯一 `image:{taskId}:refund` 流水也必须在同一事务中完成。auto 失败、超时、恢复、配置失效和紧急停用均只能走现有统一结算/退款状态机，不得另建绕过路径。
3. 创建时必须按候选路由链的 Provider code 并集校验 AI processing notice 与用户 consent，Worker 在每次实际候选外发前再次校验；引用 Asset 必须属于任务用户、未删除、源文件存在且仅从私有媒体路径读取。`sizeMode` 不得放宽 URL、所有权、提示词过滤、私有落盘或 JWT 下载边界。
4. GPT 当前仅支持显式比例，缺少 `resolutionCode` 的现役解析会默认归一为 `1k`；第二阶段必须隔离 auto 路径，禁止让 auto 复用该默认逻辑。
5. 现有主备路由、固定价格键、任务尺寸字段、错误文案和 API 响应尚不具备本 PRD 所述 auto 能力；`sizeMode`、auto 路由槽位、独立价格、路由/价格版本快照、输出实际尺寸和服务端灰度均为第二阶段待实现的新增能力。

第二阶段新增能力必须以任务创建时不可变的请求、候选 Provider code、路由和价格 release 为边界。快照冻结“需要什么授权”和执行/计费语义，不冻结用户授权状态本身；用户撤回 consent 或当前法律告知失效时 Worker 仍须停止外发并退款。不得因普通配置变更、旧客户端兼容或 Worker 延后执行而改用另一计费/路由语义。

## 5. 术语与权威数据源

| 术语 | 定义 |
| --- | --- |
| `explicit` | 客户端明确选择业务分辨率档位和画幅比例，后端计算合法 `WIDTHxHEIGHT` |
| `auto` | `size-mode-v1` 新契约：客户端不选择分辨率和比例，后端向 Provider 发送 `size="auto"`，使用独立 auto 价格 |
| `legacy_auto` | 历史/未迁移 Nano、Gemini 或旧 GPT 的兼容语义分类：Provider 收到 `size="auto"`，但请求仍可能保留业务 `resolutionCode` 并按分辨率价计费；持久化为 `size_mode=auto + size_contract_version=legacy-aspect-auto`，不是新 API 可提交的 `sizeMode` 值 |
| 路由能力 | `ai_image_model_config` 中一条实际 Provider 路由经过验证后声明的能力 |
| 有效模型能力 | 当前请求可能使用的主、备路由能力交集，并同时满足功能开关和至少一个有效价格的运营要求 |
| 模型发布版本 | 一次不可变发布，原子绑定模型能力、确定路由链、价格集合、Provider/consent code 与 Secret 具体版本；以下简称 `modelReleaseId` / `catalogVersion` |
| `requestedSize` | 用户请求语义；auto 为字符串 `auto`，explicit 为计算后的 `WIDTHxHEIGHT` |
| 输出尺寸 | Provider 返回图片经服务端解码后得到的实际宽高 |
| 执行 fencing | 通过任务 claim epoch/token 和租约约束 Worker、恢复线程与结算，过期执行者不得再发起新 Provider 尝试、提交结果或改变账务 |

权威关系：

- `/models` 决定某个模型可以选择什么；
- `/parameters` 只提供参数目录、显示名称和显式尺寸算法所需元数据，是否包含 `auto` 都不能扩大或缩小模型能力；
- 同一 `(modelCode, catalogVersion)` 下的 v2 `/pricing-options` 决定当前组合是否可计费；
- Provider 名称、业务模型编码和前端静态常量都不是能力来源。

阶段一发布前必须在目标环境留存 `/parameters`、`/models`、`ai_image_parameter`、启用路由和价格行的脱敏快照，并与 `jokester.admin.sql` 对账。第一阶段 GPT 止血不依赖新增数据库行；若产品要求干净初始化环境也公开 Nano/Gemini auto，则必须另补幂等 migration 并同步完整 SQL，不能依赖生产库手工漂移。

## 6. 阶段范围

| 阶段 | 优先级 | 主要交付 | 数据库变更 | 可独立发布 |
| --- | --- | --- | --- | --- |
| 第一阶段：稳定性止血 | P0 | 前端严格使用模型比例、合法默认值、旧草稿迁移、反向 E2E；后端纠正错误文案 | 无 | 前端可以 |
| 第二阶段：正式 auto | P1 | `sizeMode` 契约、路由能力、独立价格、任务输出尺寸、兼容与灰度 | 有 | 不可以，需后端先兼容部署 |

## 7. 第一阶段：稳定性止血

### 7.1 前端模型能力

1. 比例候选必须严格来自当前选中模型的 `/api/ai/images/models[].aspectRatios`。
2. `/api/ai/images/parameters.aspectRatios` 只用于把能力编码映射为标签和图形，不得把其中的 `auto` 合并到模型能力。
3. 当前模型明确返回比例列表时，保持服务端顺序；普通空值或无效值选择第一项。旧 GPT auto 草稿是一次性兼容特例：优先迁移为 `1:1`，不支持 `1:1` 时再取第一项。
4. 模型的 `aspectRatios` 缺失或为空时按配置不可用处理：不合成选项、禁止提交并提供重试，不按 Provider、模型名称或全局目录猜测。
5. 切换模型、异步加载能力、从图片详情复用参数以及从充值页返回时，都必须重新校验当前比例。
6. `/models` 请求失败、返回空列表或能力响应过期时，可以保留上一次数据用于只读展示，但生成必须 fail-closed；成功刷新前不得用缓存或 fallback 提交。
7. 模型明确声明某个能力编码、但 `/parameters` 暂时没有对应显示元数据时，前端可用编码本身或本地化文案显示；这只补显示信息，绝不能据此新增能力。
8. 阶段一前端必须同时兼容“全局目录含 auto”和“当前后端临时过滤 auto”两种滚动发布状态。严格消费模型能力的前端上线并稳定后，后端可恢复完整全局参数目录；恢复目录不得使 GPT 获得 auto。

### 7.2 草稿与默认值

新草稿在模型能力加载前不应默认保存 `auto`。能力加载完成后按以下规则归一化并立即回写草稿，同时保留提示词、参考图、画质、图片数量和来源提示词 ID：

| 草稿状态 | 当前模型能力 | 结果 |
| --- | --- | --- |
| `aspectRatioCode=auto` | 模型明确包含 `auto` | 保留 `auto` |
| `aspectRatioCode=auto` | 模型不包含 `auto` | 优先迁移为 `1:1`；不支持 `1:1` 时取第一项 |
| 比例为空或不受支持 | 模型有有效比例 | 取第一项 |
| 任意比例 | 模型比例缺失或为空 | 保留其他草稿内容，但禁止提交并提示配置不可用 |

GPT 旧草稿中的 `auto` 必须迁移为显式比例；Gemini/Nano 只有在 `/models` 明确声明 `auto` 时才继续保留。

### 7.3 请求与计费

第一阶段不新增 `sizeMode`：

- GPT 继续提交 `resolutionCode + qualityCode + aspectRatioCode`，且比例必须是显式值；
- Gemini/Nano 明确支持时可继续提交 `aspectRatioCode=auto`；
- GPT 计费继续匹配 `modelCode + resolutionCode + qualityCode`；
- Nano 继续按当前业务分辨率价格匹配；
- 第一阶段不得修改幂等 payload、路由解析或任务表字段。

### 7.4 后端错误口径

后端仍保留当前 GPT 显式比例策略，但拒绝 auto 时不得再归因于 GPT Image 2 官方限制。

推荐错误契约：

```json
{
  "code": "AUTO_SIZE_NOT_SUPPORTED",
  "message": "当前站点配置不支持自动尺寸",
  "requestId": "01J...",
  "details": null
}
```

HTTP 状态使用 `400`。前端止血可以先于后端独立发布，但第一阶段后端交付的验收必须同时增加 `MachineErrorCodes.AutoSizeNotSupported` 并返回上述机器码，不能把“暂不增加”变成长期契约。仅改用户文案不算后端 P0 完成。

### 7.5 第一阶段测试

核心 E2E 必须构造以下反例：全局 `/parameters` 含 `auto`，GPT `/models` 只声明 `1:1`。验收结果必须是页面没有 auto，生成请求提交 `aspectRatioCode="1:1"`。

| 编号 | 场景 | 预期 |
| --- | --- | --- |
| P0-FE-001 | GPT 只声明 `1:1`，全局目录含 auto | 不展示 auto，请求提交 `1:1` |
| P0-FE-002 | version 1 GPT 草稿保存 auto | 页面、回写草稿和请求均迁移为合法显式比例 |
| P0-FE-003 | Gemini/Nano 草稿保存 auto，模型明确支持 | 页面、草稿和请求继续为 auto |
| P0-FE-004 | 从支持 auto 的模型切到仅显式模型 | 自动切换为新模型第一项，不携带旧 auto |
| P0-FE-005 | 模型未返回 `aspectRatios` | 不从全局目录补值，生成按钮不可用 |
| P0-FE-006 | 复用历史 GPT auto 参数 | 其他参数保留，比例被纠正后才能提交 |
| P0-FE-007 | `/models` 请求失败但本地仍有旧能力缓存 | 可只读展示，生成按钮不可用，重试成功后才恢复 |
| P0-FE-008 | Nano/Gemini 明确声明 auto，但 `/parameters` 暂无 auto 元数据 | 仍只展示模型声明的 auto，并以编码/本地文案显示；不扩展其他模型能力 |

以上 E2E 必须能在干净克隆环境通过仓库声明的依赖和固定命令复现。前端项目需补齐 Playwright 开发依赖、测试脚本和浏览器安装说明，移除对本机 Chrome 绝对路径的依赖，并明确 fixture 与真实后端集成用例的边界；在此之前，这些用例只能作为本机验收证据，不能宣称为 CI 发布门禁。真实后端集成验收必须先完成目标数据库能力快照，不能用 stub 中存在 auto 证明完整 SQL 已配置 auto。

## 8. 第二阶段：正式支持 auto

### 8.1 新请求契约

自动尺寸：

```json
{
  "sizeMode": "auto",
  "catalogVersion": "imgcat_20260821_01"
}
```

显式尺寸：

```json
{
  "sizeMode": "explicit",
  "catalogVersion": "imgcat_20260821_01",
  "resolutionCode": "2k",
  "aspectRatioCode": "16:9"
}
```

以上片段与现有 `idempotencyKey`、`prompt`、`modelCode`、`qualityCode`、`imageCount` 和参考图字段组合使用。`size-mode-v1` 的 resolve 一律必须提交 `catalogVersion`；create/generate 只在 durable 事实未命中的新 key 上强制提交，缺少或为空返回 `400 VALIDATION_ERROR`，未知或已非 current published 版本返回 `409 IMAGE_CATALOG_CHANGED`，不能按用户未见过的新价格扣费。已有同 key 重放先按 8.2 查询事实，catalog 不参与客户端意图指纹，即使版本已切换也返回原批次。未提交 `sizeMode` 的 legacy 新请求不要求客户端传 catalog，由服务端按 legacy 语义绑定当时 current release。外部 `catalogVersion` 对应服务端内部不可变 `model_release_id`。GPT 的 `qualityCode` 在两种尺寸模式下仍按模型能力提供；不支持画质参数的 Gemini/Nano 继续不提交该字段。

#### 8.1.1 首期端点与协议范围

| 端点 | 第二阶段首期行为 |
| --- | --- |
| `GET /api/ai/images/models` | 对声明理解新协议且达到协议最低版本的 GPT 客户端返回 `sizeContractVersion=size-mode-v1`、模型级 `catalogVersion` 与至少包含 explicit 的 `sizeModes`；只有再满足账号/租户 auto cohort 时才加入 auto。旧 GPT 客户端保持 legacy schema；未迁移 Nano/Gemini 返回 `sizeContractVersion=legacy-aspect-auto`，不返回可被解释为新 GPT auto 的 `sizeModes` |
| `GET /api/ai/images/parameters` | 继续作为全局显示/算法目录；响应是否暂含 auto 均不构成模型能力 |
| `GET /api/ai/images/pricing-options` | v2 客户端必须传 `modelCode + catalogVersion` 并按同一模型 release 得到 wrapper 内的 explicit/auto 价格；旧调用继续返回现役扁平 schema，只含 `resolutionCode` 非空的 legacy/explicit 行，避免把 `string` 突然改为 `null` |
| `POST /api/ai/images/parameters/resolve` | 新协议必须传 `modelCode + sizeMode + qualityCode`，按当前灰度和 `catalogVersion` 校验；legacy 请求仍按现役 GPT 显式解析规则处理 |
| `POST /api/ai/images` | GPT explicit/auto 的正式异步入口；v1 新 key 强制 `catalogVersion`，多图仍拆成有序单图任务。Nano/Gemini 在单独迁移前继续走 legacy 字段语义 |
| `POST /api/ai/images/generate` | 与统一入口复用同一规范化、幂等、灰度、计费和任务状态机；v1 新 key 强制 `catalogVersion`，接口只多等待一段受控时间，所有 v1 多图响应统一使用逐图 `results[]` |
| `POST /api/ai/images/nanoBananaImage[/generate]` | 兼容期只接受现役 legacy 字段，显式提交 `sizeMode` 返回 `400 INVALID_SIZE_MODE_COMBINATION`；不在本期获得 GPT auto 计费语义 |

`sizeContractVersion` 必须参与客户端解析分支，但不由客户端自由选择。服务端根据端点、字段形态和已发布模型契约判定；客户端 Header 只能声明其能理解 `size-mode-v1`。Web、Android、iOS 都必须实现同一端点矩阵，不能只更新 Web 类型定义。

### 8.2 字段校验与兼容

| 输入 | 规范化结果 | 行为 |
| --- | --- | --- |
| `sizeMode=auto` 且尺寸字段省略或为 JSON `null` | auto | 接受，Provider `size="auto"`；空字符串不是 `null`，按非法组合拒绝 |
| `sizeMode=auto` 且同时含非空 `resolution`、`resolutionCode`、`aspectRatioCode` 或直接 `size` | 无 | `400 INVALID_SIZE_MODE_COMBINATION` |
| `sizeMode=explicit` 且分辨率、比例齐全 | explicit | 使用现有算法计算并校验 `WIDTHxHEIGHT` |
| `sizeMode=explicit` 缺分辨率或比例 | 无 | `400 VALIDATION_ERROR` |
| `sizeMode=explicit` 使用 legacy `resolution`/直接 `size`，或 `resolution` 与 `resolutionCode` 冲突 | 无 | 新协议拒绝；只接受规范 `resolutionCode`，避免静默优先级 |
| 已传 `sizeMode`，但 `catalogVersion` 缺少、null 或空字符串 | 无 | `400 VALIDATION_ERROR`；不得自动绑定 current catalog |
| 已传 `sizeMode`，但 `catalogVersion` 未知或已非 current | 无 | `409 IMAGE_CATALOG_CHANGED`；不得按其他版本继续 |
| 未传 `sizeMode`，但单独传 `catalogVersion` | 无 | `400 INVALID_SIZE_MODE_COMBINATION`；catalog 不能单独把 legacy 请求升级成新协议 |
| 未传 `sizeMode`，GPT Image2 且 `aspectRatioCode=auto` | auto | 默认拒绝；只有显式批准的旧客户端白名单同时满足服务端灰度时才映射，并忽略旧 `resolution/resolutionCode` 的尺寸与计费语义，否则 `400 AUTO_SIZE_NOT_SUPPORTED` |
| 未传 `sizeMode`，Nano/Gemini 且 `aspectRatioCode=auto` | 现役 legacy auto | 在另行迁移前保持当前 Provider auto 和按业务 `resolutionCode` 计费语义 |
| 未传 `sizeMode` 且比例为显式值或缺少旧尺寸字段 | explicit | 旧客户端兼容期保留现役默认：缺分辨率为 `1k`、缺比例为 `1:1`、GPT 缺画质为 `med`；显式传入的冲突 legacy alias 继续按现役优先级处理并记录弃用指标 |

上表的 catalog 校验行只作用于 durable 新事实；create/generate 的已有 key 重放必须先执行下述事实查询。resolve 不创建事实，因此每次都按新/legacy 字段形态校验 catalog。

兼容映射必须按已迁移模型、服务端灰度资格、稳定账号分桶/白名单和最低客户端版本共同判定，第二阶段首期只对 GPT Image2 启用。批准 profile 还必须固定一个 current `(modelCode, catalogVersion)`，并有证据证明该客户端已经展示并确认同一 auto 价格；缺少价格确认能力时一律拒绝旧 GPT auto，不能按服务端 current 静默扣费。catalog 切换会自动停用旧 profile，重新审批前不得跟随新价格。`X-Client-Capabilities` 只表示客户端能够理解新契约，不能作为授权、灰度或计费资格依据；平台、版本 Header 同样可伪造，只能用于兼容判断，最终资格必须包含服务端维护的账号/租户 cohort。规则必须先归一化，再生成幂等指纹；同一语义的新版 GPT auto 请求与满足服务端兼容资格的旧版 GPT `aspectRatioCode=auto` 请求应得到同一规范化 payload；auto 的幂等指纹不得包含被忽略的旧分辨率。Nano/Gemini 在单独迁移前不得改变现役路由、幂等和计费语义。

所有尺寸输入字段必须改为 nullable、移除会掩盖“字段未出现”的 DTO 初始化默认值，或使用等价的 JSON presence 标记。服务端先按“是否出现 `sizeMode`”分流：新 `size-mode-v1` 严格校验字段，legacy 分支再显式应用现役 `1k/1:1/med` 默认值。不得让 DTO 默认值把合法 auto 误判为字段冲突，也不得把新 explicit 缺少必填字段静默补齐。新协议下 GPT `modelCode` 与 `qualityCode` 必填；默认值只属于版本化 legacy normalizer。

统一入口 Controller 必须保持无业务分流：不得在 durable 幂等查询之前调用实时 `ResolveAsync` 决定 GPT/Nano Provider。请求协调层先校验认证身份和幂等 key 语法并查询事实表，只有确定为新请求后才读取当前模型发布版本、解析 Asset/提示词来源、检查价格与灰度并分派 Provider 协议。

幂等处理顺序固定如下：

1. 以 `(user_id, idempotency_key_hash)` 查询新请求表；命中时使用记录保存的 `canonicalization_version`、`size_contract_version` 和兼容 profile 规范化本次原始输入，而不是使用当前灰度或当前配置。相同指纹返回原有序批次，不查路由、价格、Redis、Asset 当前状态或提示词来源；不同指纹返回 409。
2. 新表未命中时，在兼容窗口按旧算法双读 `ai_image_task`。先按本次 root key 和 `imageCount` 派生 GPT 拆单 key；全部完整命中才按 split batch 补建。若仅 root key 命中、指纹一致且该行 `image_count=requestedImageCount`，则识别为历史 `single-task-multi-output`（包括拆单前 GPT 与 Nano/Gemini 单任务），返回一个 task id 并保留预期结果数，不能伪造逐图任务。旧指纹重建可读取已软删除 Asset/提示词行的历史标识，但不得重新要求其当前有效。其余部分命中、指纹冲突或历史输入已无法可靠重建时返回 `409 LEGACY_IDEMPOTENCY_UNVERIFIABLE`，绝不按创建时间猜批次，也绝不当成新请求重扣。
3. 新旧事实均未命中时，按本次服务端 rollout、模型 `sizeContractVersion` 和字段 presence 选择版本化 normalizer，生成客户端意图指纹。指纹包含规范化 prompt/negativePrompt、模型、图片数、模式、画质、有序参考输入、蒙版和来源提示词 ID；不包含 `catalogVersion`、当前路由、价格、灰度 cohort、被 legacy auto 忽略的分辨率或 Worker 执行结果。catalog 是首次创建时锁定的执行/计费快照，不改变同 key 的客户端意图身份。参考图顺序如果会影响 Provider 结果就不得排序。
4. 只有新请求继续校验当前发布版本、价格、用户/法律/consent、提示词、Asset 与 Redis admission。每次 Redis reservation 必须生成不可猜测的 owner token，值中保存 `fingerprint + token + state`；成功提交时把对应 `admission_reservation_id` 保存到 durable 请求/派发事实，供崩溃恢复和最终释放。数据库提交发生唯一键冲突时，失败竞争者只能在 token 仍属于自己时撤销自己的 admission/额度；不得按用户/key 直接删除而误伤赢家，随后读取胜者记录并按指纹返回原批次或 409。

新任务必须使用 `ai_image_request_idempotency` 和有序任务批次明细表或等价结构。请求表至少保存 `request_id`、用户、key hash、`canonical_payload_hash`、`canonicalization_version`、`normalization_profile/compatibility_profile`、`size_contract_version`、可空 `model_release_id`、`admission_reservation_id`、`requested_image_count`、`task_count`、`legacy_batch_shape`、状态和创建时间；明细以 `(request_id, task_ordinal)` 唯一绑定 task id。新 `size-mode-v1` 强制 release 非空且一图一 task；历史/legacy `single-task-multi-output` 允许请求图片数大于任务明细数。幂等记录、批次明细、任务插入、已发布价格锁定、积分预留和 reserve 流水必须在同一数据库事务中提交。Redis Lua 可继续进行削峰和临时占位，但不得替代数据库中的幂等与账务事实。

Redis reservation 必须按批次子任务结算，而不是只有请求级“绑定/完成”布尔值：`CancelUncommitted(token)` 仅能撤销尚未绑定数据库批次的当前 owner；`BindBatch(token, requestId, ordered taskId/ordinal/image/point units)` 一次性绑定整批；`CompleteTask(reservationId, taskId, ordinal, completedImages, refundedPoints)` 对每个子项恰好生效一次。Redis 记录已完成 ordinal、剩余活动任务/全局积压、每日图片与积分使用量；成功保留日额度，失败按实际未完成图片和退款积分释放，乱序、重复回调和恢复重放不得重复增减。数据库提交同时写 admission-bind outbox，只有 BindBatch 成功后才派发；提交后 Redis 暂不可用时任务保持 pending 并重试绑定，超过运维 deadline 才按统一失败退款路径终态化，不能丢任务或直接执行。

幂等事实及其引用的版本化 normalizer/profile 不得早于关联任务、积分流水和审计保留期清理；只保留 hash 却删除旧 normalizer 后将无法可靠判断重放。保留期内同一用户的 key 永不重新解释为新请求。异步 `POST /api/ai/images` 创建/重放响应增加 `requestState=active|partially_deleted|deleted`，并保持现役四个 ID 字段：`id/taskId` 都是首任务，`ids/taskIds` 都是完整有序任务列表；v2 以 `ids` 为权威，其他三个是保持现役类型的 deprecated alias。single-task-multi-output 的两个列表都只含一个 task id。该状态按批次任务的软删除投影计算，分别表示无、部分或全部任务已删除；相同 key 仍返回原 ID，且不重建不扣费。同步 `/generate` 不新增 `id/ids` alias，继续使用其现役 `taskId/taskIds` 加 8.7 的 `requestState/results` envelope。账户依法删除时按账号删除策略匿名化/清理，不能仅删除幂等头表而遗留任务唯一键。

兼容映射应有服务端监控、最低版本和明确下线日期。默认不启用旧 GPT auto 映射；只有经批准的白名单/稳定 cohort 才开启。旧字段使用率达到约定阈值后，另行评审移除。未满足服务端 auto 灰度资格的旧 GPT 客户端不得因携带或伪造 capability Header 而被映射为 auto，必须返回 `AUTO_SIZE_NOT_SUPPORTED`。

### 8.3 参数解析响应

`POST /api/ai/images/parameters/resolve` 同步接受 `sizeMode`。新协议请求必须包含 `modelCode`、`qualityCode` 和客户端刚读取的 `catalogVersion`：

```json
{
  "modelCode": "gpt-image-2",
  "sizeMode": "auto",
  "qualityCode": "med",
  "catalogVersion": "imgcat_20260821_01"
}
```

该端点复用创建接口的字段组合、模型协议、服务端灰度和已发布能力判定；成功只表示参数在该发布版本下可解析，不预留价格、不创建任务。新协议缺少 catalog 返回 400，传入未知或已过期版本返回 `409 IMAGE_CATALOG_CHANGED`。客户端重新取得 `/models` 中目标模型的版本后，必须以 `modelCode + catalogVersion` 调用 v2 pricing；不能把 pointer 切换前后的两次 GET 拼成一个视图，也不得静默用另一版本返回结果。未传 `sizeMode` 的 legacy 请求继续按现役 GPT 显式规则解析且可省略 catalog；本期不把该端点用于 Nano/Gemini legacy auto。

auto 响应：

```json
{
  "sizeMode": "auto",
  "modelCode": "gpt-image-2",
  "catalogVersion": "imgcat_20260821_01",
  "requestedSize": "auto",
  "resolutionCode": null,
  "aspectRatioCode": null,
  "width": null,
  "height": null,
  "size": "auto",
  "providerQuality": "medium"
}
```

explicit 响应继续返回业务分辨率、比例、计算宽高和 `size="WIDTHxHEIGHT"`。宽高必须满足现有 16px 对齐、最大边、比例和总像素约束。

### 8.4 模型与路由能力

`GET /api/ai/images/models` 的模型项增加：

```json
{
  "sizeContractVersion": "size-mode-v1",
  "catalogVersion": "imgcat_20260821_01",
  "capabilities": {
    "sizeModes": ["explicit", "auto"],
    "defaultSizeMode": "explicit",
    "supportsAutoSize": true
  }
}
```

`catalogVersion` 的作用域固定为一个 `modelCode` 的不可变 model release，不是整站全局版本；所有比较与唯一引用都使用 `(modelCode, catalogVersion)`。v2 pricing 不再返回裸数组，而返回确定 envelope：

```json
{
  "modelCode": "gpt-image-2",
  "catalogVersion": "imgcat_20260821_01",
  "items": [
    {
      "modelCode": "gpt-image-2",
      "sizeMode": "auto",
      "resolutionCode": null,
      "qualityCode": "med",
      "points": 100,
      "priceAmount": 10.00,
      "priceMinorUnits": 1000,
      "currency": "CNY",
      "sort": 10
    }
  ]
}
```

同一路径通过 `X-Client-Capabilities: ai-size-mode-v1` 加必填的 `modelCode/catalogVersion` 查询参数选择 v2 envelope，并在 OpenAPI 中用明确的 oneOf/版本说明表达；无该能力声明的调用保持现役扁平 v1 schema。v2 `items[]` 在新增 `sizeMode` 和可空 resolution 的同时，必须保留现役 `modelCode:string`、`qualityCode:string`、`points:int`、`priceAmount:decimal`、`priceMinorUnits:int64`、`currency:string`、`sort:int` 字段与语义；`currency` 继续使用大写 ISO 4217，金额与 minor units 必须按该币种小数位一致，`points` 才是服务端积分预留的权威值。不得把 envelope 升级变成价格展示字段的隐式 breaking change。`/parameters.pointPrices` 仅保留 legacy 展示兼容，不参与 v2 catalog 一致性或新协议计费。

规则：

1. 只有 `sizeContractVersion=size-mode-v1` 时 `sizeModes` 才是权威字段；`supportsAutoSize` 是兼容派生值，必须等于 `sizeModes` 是否包含 `auto`，不能独立维护出不同结果。未迁移 Nano/Gemini 使用 `legacy-aspect-auto` 并继续以模型级 `aspectRatios` 为准。
2. 默认模式保持 `explicit`。`defaultSizeMode` 不在有效 `sizeModes` 中时，后端配置视为无效，前端按第一项安全降级并上报。
3. 每条物理路由有独立的“已验证尺寸能力集合”，默认只有 `explicit`；未配置、未知或未验证一律视为不支持 auto。
4. auto 的有效能力是该模型 auto 路由链中所有可能参与主备切换的启用路由能力交集。任一主路由或 fallback 未验证 auto，就不向前端开放。
5. 首期只有文生图 `/images/generations`、无蒙版参考图 `/images/edits`、带蒙版 edits 这三条实际启用功能路径都验证通过时，才发布统一 auto 能力。任一现役操作不支持就整体不开放，不在本期引入操作级能力分支。
6. 当前第三方 `gpt-image-2-1k` / `gpt-image-2-4k` fallback 默认不支持 auto。不能仅凭其协议是 `openai-image` 判定支持。
7. auto 必须拥有确定的主路由和可选 fallback 路由链。请求没有业务分辨率，路由解析不得随机选择现有 1K/2K/4K 槽位。
8. 后端在任务创建事务中锁定当前 published `modelReleaseId`，该发布版本已原子绑定路由链、能力验证和价格；后续配置变化只能影响新任务。Worker 只能按任务 release 执行，不得再次按 model/resolution 查询当前配置；若 release 因安全事件被紧急吊销，必须禁止 Provider 调用并按现有失败结算退款。发布版本及其 Secret 具体版本必须保留到所有引用任务终态后才可归档或删除。

模型发布必须使用“不可变 release + 原子 current pointer”，不能靠多张可变表分别读取后在内存拼成所谓同一版本。推荐由 release 头表绑定 capability、route set、price set 和审计状态，明细表只允许在 draft 状态写入；发布事务校验完成后冻结明细并原子切换 current pointer。`/models`、v2 `/pricing-options`、resolve 与创建事务均返回或锁定同一模型级 `catalogVersion`。任务保存 release 外键及必要的非敏感执行快照，不复制 API Key。

路由设计必须明确区分三个维度：物理连接/Secret、每条物理路由经过验证的能力集合、某个 release 下无分辨率 auto 请求对应的确定主备链。路由绑定应有 `route_size_mode`、专用 auto 槽位或等价结构；实现可以复用同一物理连接配置，但不能把能力声明本身当作路由选择键。明细唯一键和解析索引必须包含发布版本，例如 `model_release_id + model_code + route_size_mode + resolution_code_normalized + route_role`，否则无法同时保留多个不可变版本。解析时先按 release 和模式隔离，只有 explicit 模式再执行“精确分辨率优先、通用分辨率兜底”，避免 explicit 与 auto 互相选中。auto 请求缺少分辨率时，禁止沿用当前“空分辨率默认归一为 1k”的逻辑。

GPT auto 仅向同时满足以下条件的客户端开放：客户端声明能够理解新契约、服务端按账号/租户稳定 cohort 和应用最低版本判定其处于目标灰度、模型 release 及独立价格均已发布。`X-Client-Capabilities: ai-size-mode-v1` 仅用于请求/响应的协议兼容，不能单独授予 auto 权限，也不得被服务端 admission 信任为灰度凭据。缺少该能力标记的客户端只得到 GPT explicit 能力；满足灰度且被显式批准旧字段兼容及价格确认 profile 的客户端才可将 `aspectRatioCode=auto` 映射为 auto。Nano/Gemini 的 `aspectRatios` 可在兼容期继续包含现役 `auto`，其 `sizeContractVersion` 仍为 `legacy-aspect-auto`，客户端不得套用“auto 禁止 resolution”规则。

客户端元数据 Header 固定为 `X-Client-Platform: web|android|ios`、`X-Client-Version` 和 `X-Client-Build`；缺失/非法值按未知旧客户端处理。版本比较规则、Web build 生成方式和移动端 build 映射必须由共享测试向量固定。这些值可伪造，只用于协议兼容，不能替代服务端 cohort。

`/models` 的模型项与 v2 `/pricing-options` 必须返回相同 `(modelCode, catalogVersion)`；价格端点按刚读取的版本查询仍在保留期内的不可变明细。若响应版本不同，客户端丢弃整组并重试。上述响应使用 `Cache-Control: private, no-store` 或等价的用户隔离缓存策略，`Vary` 至少包含 `Authorization`、`X-Client-Capabilities`、`X-Client-Platform`、`X-Client-Version`、`X-Client-Build`；CORS、反向代理和 API Gateway 必须允许并透传这些 Header。不得让 CDN、代理或浏览器将含 auto 的响应跨用户、跨灰度或跨能力版本复用。旧 schema 响应不得出现 `resolutionCode=null` 的 auto 行，并继续保持 non-null string。

### 8.5 Provider 请求与执行前安全门禁

- auto：JSON 和 multipart 请求都发送 `size="auto"`，不计算或发送 `WIDTHxHEIGHT`。
- explicit：继续调用现有尺寸算法，向 Provider 发送计算后的 `WIDTHxHEIGHT`。
- 两种模式都保留当前质量、图片数量、参考图、蒙版、授权、提示词过滤、超时和主备失败判定。
- 创建时必须校验不可变候选路由链所需 `consentProviderCode` 的并集，缺少任一候选授权时不得预留积分；传输协议和实际数据处理方必须分别配置，不能因为第三方使用 OpenAI-compatible 协议就把 consent provider 推断为 `openai`。
- Worker 必须先 CAS 取得任务 claim 并持续心跳，只有当前 epoch/token 的 claimant 才能在 Provider 租约外执行提示词复检、用户状态、法律告知、候选 consent、任务输入 Asset/legacy URL、源文件、release/Secret 可用性等本地 preflight。preflight 通过后才取得全局 Provider 租约，并在每次主路由或 fallback 外发前复核 claim epoch、release 吊销和该候选 consent。固定顺序为 `task claim -> local preflight -> Provider lease -> lightweight recheck -> durable attempt inflight -> external call`，避免多个 Worker 无 claim 并行预检，也不长期占用全局租约。
- 全局 Provider lease 必须包含不可猜测 owner token，并在完整 attempt（Provider 响应及必要的结果获取）期间以小于 TTL 的固定间隔通过 Lua compare-and-renew 续租；释放同样校验 owner。TTL 必须覆盖至少一个续租周期、运行时暂停和网络抖动，并受 attempt absolute deadline 约束。续租失败后 claimant 禁止 fallback/新外发，当前调用按 outcome unknown 隔离；不得让固定 TTL 静默过期后仍继续占用 Provider 并突破全局并发上限。
- 所有引用 Asset 必须通过有序任务输入表保留原始 `asset_id`、owner、角色（reference/mask）、ordinal 和创建时内容哈希/不可变存储标识；不能只把 Asset 解析成 URL 后丢弃 ID。Worker 复核未删除、仍归任务用户、文件存在且内容标识一致。legacy 私有 URL 必须标明 `input_kind=legacy_url` 并按现役同源 owner 路径规则复核，不能伪装成 Asset。
- preflight、consent、提示词、Asset、claim 失效和本地配置错误都不计入 Provider 熔断失败。fallback 必须按实际候选 Provider code 校验；确定性的 4xx、认证/配置错误、prompt 拒绝、输出安全拒绝不得盲目切换，只有发布策略列明且已得到“确定未成功”结果的瞬时网络、429、5xx 和超时类别才可 fallback。外部结果未知的超时进入 durable provider attempt 隔离规则；不能仅靠换 claim epoch 就再次调用。
- 路由 Endpoint 仅允许 HTTPS 和受控域名 allowlist。DNS 解析和建立连接时都必须拒绝 loopback、私网、链路本地、云 metadata 与保留地址，防止 DNS rebinding；禁止自动跨域重定向，或对每个跳转重新校验协议、域名和目标 IP。路由表只保存固定到具体 Secret version 的 `secretRef` 或等价不可导出凭据引用，禁止保存可读 API Key 或会原地漂移的 `latest` alias。
- 路由配置必须经“草稿 → 预发布逐路由、逐 generations/edits 验证 → 双人审批 → 不可变发布”状态机；审计记录至少包含操作者、审批者、验证证据、版本差异和回滚原因。日志仅记录 `SizeMode`、`RequestedSize`、路由版本/ID 与 Provider 模型，不得记录提示词、图片内容、API Key、Secret 引用解析值或内部 Endpoint。
- Provider 返回的图片 URL 是独立 SSRF 输入面，不能沿用“路由 Endpoint 已可信”的结论。结果 URL 必须为 HTTPS 且命中该路由单独审批的下载域 allowlist，DNS/连接 IP 与每次重定向都重复执行私网、metadata、保留地址和 DNS rebinding 防护；也可以在首期只接受 base64。URL 下载必须以受限流式读取实施响应头、总字节和超时上限，禁止 `ReadAsByteArray` 式无上限读取。
- auto 输出必须定义并在 base64 解码或 URL 下载前强制执行每条路由的最大编码长度/字节数、宽高/总像素、允许 MIME/真实格式/帧数与下载/解码超时。超限、畸形或解码失败的文件不得发布、不得暴露 URL，并按 Provider 异常结果结算退款。auto 定价须按该路由可能产生的最高成本输出审批，并持续监控成本偏差。

#### 8.5.1 任务执行、恢复与派发 fencing

1. 任务增加 `claim_epoch`、不可猜测 `claim_token_hash`、`lease_expires_at` 和心跳时间。认领通过 CAS 增加 epoch；Provider 前、fallback 前、写文件前、发布结果前和数据库结算前都必须确认当前 epoch/token 仍有效。
2. 新增 `ai_image_provider_attempt` 或等价事实表，至少保存 `attempt_id/task_id/claim_epoch/route_release_id/upstream_idempotency_key/state/started_at/deadline/reconcile_by/completed_at`。外发前必须在 claim 条件下把状态从 prepared 改为 inflight；同一 attempt 的状态迁移使用 CAS。
3. 恢复线程不得看到租约超时就直接与失租 Worker 竞争结算。prepared 可由新 claimant 接管；inflight 未到 deadline 时禁止第二次外发。Provider 支持幂等键或状态查询时，只能用原 attempt key 对账/重试；不支持时，deadline 后将 attempt 标记为 `provider_unknown`，不得自动再次调用。任务继续保持处理中并占用积分预留、用户活动数和全局积压，进入有告警的对账队列；产品/运维必须为每条路由审批 `reconcile_by` SLA，不能无限期隔离。
4. 在 `reconcile_by` 前查到确定成功/失败时按正常状态机结算；仍未知时必须强制失败退款，释放 Redis 活动/积压并按失败规则回退日额度，同时把潜在 Provider 成本记入单独运营对账，不转嫁用户。此后任何晚成功结果只能隔离并清理，不能发布、重新扣费、再次退款或覆盖终态。
5. 最终状态、`billing_status`、退款流水、结果 URL/尺寸、provider attempt 和 claim epoch 必须在同一数据库结算事务中条件更新。结算 CAS 失败的执行者不得把已落盘文件暴露给客户端。
6. 任务/批次提交后的派发使用事务 outbox 或“pending task 由恢复扫描可靠派发”的等价机制。内存队列部分入队失败不能立即把整批任务退款，因为前面已入队任务可能已被 Worker 认领；任务保持 pending，由 outbox/恢复线程继续派发或在从未认领的超时路径统一结算。
7. Provider 文件先写同一存储卷临时路径，校验后原子 rename，再执行数据库结算；结算失败、claim 失效或进程在发布边界崩溃时，由带 task/attempt 标识的补偿队列或 sweeper 删除孤儿/隔离文件。数据库成功但响应丢失仍通过 durable 幂等返回原任务。

### 8.6 独立计费

auto 必须有独立价格，且在调用 Provider 前完成确定性预留。推荐把价格键扩展为：

| 模式 | 价格键 |
| --- | --- |
| explicit GPT | `modelCode + sizeMode=explicit + resolutionCode + qualityCode` |
| auto GPT | `modelCode + sizeMode=auto + qualityCode` |
| legacy Nano/Gemini | `modelCode + pricingMode=legacy_resolution + resolutionCode`，同时服务现役显式比例与 legacy auto |
| explicit Nano | 后续统一契约时使用 `modelCode + sizeMode=explicit + resolutionCode` |
| auto Nano | 后续统一契约时使用 `modelCode + sizeMode=auto`；是否按质量分档仍服从模型能力 |

数据库价格明细增加 `pricing_mode`（或能够表达同等语义的字段），取值至少为 `explicit`、`auto`、`legacy_resolution`。现有 GPT 价格迁移为 explicit；未迁移 Nano/Gemini 价格迁移为 legacy_resolution，不能因为其上游可能收到 auto 就误套 GPT auto 独立价。auto 行不绑定 1K/2K/4K。明细唯一键必须包含不可变 price release，例如 `price_release_id + model_code + pricing_mode + resolution_code_normalized + quality_code_normalized`；current pointer 另表维护，不能用不含版本的唯一键阻止历史版本共存。数据库参与唯一键的可选编码必须归一为非 NULL 空字符串或使用等价生成列，避免 MySQL UNIQUE 允许多条 NULL。v2 API 对 auto 的 `resolutionCode` 返回 `null`；旧 schema 过滤 auto 行并保持 non-null string。

强制规则：

1. `/models` 只有在路由、功能开关和至少一个已发布画质的 auto 价格均有效时才包含 auto。客户端选择 auto 后，画质选项必须与 `/pricing-options` 的 auto 价格取交集；某画质缺价时只禁用该组合，所有画质均缺价时隐藏整个 auto 模式。
2. 若客户端因接口竞态或部分配置看到 auto，但当前画质没有启用价格，前后端仍须 fail-closed，不能回退任意显式价格。
3. 经批准且已确认同一价格的旧 GPT 兼容 profile 即使提交 `resolutionCode=4k` 与 `aspectRatioCode=auto`，也按 profile 固定 catalog 的 auto 独立价格计费，4K 只作为被忽略字段；未固定/未确认价格或 catalog 已切换时必须拒绝。Nano/Gemini 在另行迁移前继续按现役业务分辨率计费。
4. `pointCost`、流水以及任务路由/价格快照必须保存任务创建时的 `price_id`、`price_release_id`、单位积分和总积分；最终输出尺寸不会触发补扣或退款。v1 请求查找并锁定客户端提交的 current `(modelCode, catalogVersion)`；legacy 请求在同一事务内锁定服务端 current release。选择价格、插入幂等批次/任务、余额扣减和预留流水必须合并到同一事务接口，不能在事务外调用查价方法后只把整数积分传入预留事务。价格变更只能影响新 key；v1 客户端还必须先确认新 catalog。
5. 多图仍按 auto 单张价格乘 `imageCount`，GPT 继续拆为逐图任务和逐任务预留流水。

### 8.7 任务记录与实际尺寸

对 `sizeContractVersion=size-mode-v1` 的新任务，DTO 和持久化模型增加以下语义：

| 字段 | 排队时 | 终态 |
| --- | --- | --- |
| `sizeContractVersion` | `size-mode-v1` | 不变 |
| `modelCode` | 规范业务模型编码，并与 catalog release 一起锁定 | 不变 |
| `sizeMode` | `explicit` 或 `auto` | 不变 |
| `requestedSize` | explicit 为 `WIDTHxHEIGHT`，auto 为 `auto` | 不变 |
| `requestedResolutionCode` | explicit 有值，auto 为 `null` | 不变 |
| `requestedAspectRatioCode` | explicit 有值，auto 为 `null` | 不变 |
| `requestedWidth/requestedHeight` | explicit 为计算值，auto 为 `null` | 不变；`requestedSize` 与这两个字段必须一致 |
| `outputWidth/outputHeight/outputSize` | `null` | 成功从实际图片解码结果回填；失败保持 `null` |
| `catalogVersion` | 创建事务锁定的外部版本 | 不变；内部只保存/关联 `model_release_id`，API 不暴露内部 id |
| `billingStatus/refundedPoints` | `0`（预留）/ `0` | 成功为 `1`（确认）/ `0`；失败返回 `2`（部分退款）或 `3`（全额退款）及实际退款积分 |
| `failureCode/failureStage/retryable` | `null` | 成功为 `null`；失败写受控机器值，不暴露 Provider 原文 |

表中“auto 的分辨率、比例和请求宽高为空”只适用于新 `size-mode-v1`。`legacy-aspect-auto` 任务仍可保留业务 `resolutionCode`、`aspectRatioCode=auto` 并按 legacy resolution 计费；API 必须先按 `sizeContractVersion` 解释，不得套用新 auto 不变量。新任务的 `refundedPoints` 是非负整数并与结算事务内的唯一 refund 流水一致；历史任务无法从流水可靠重建时可为 `null`，不能猜成 0。

任务 DTO 新增稳定 `modelCode`。新任务写入独立 `model_code`，不能继续借 `model_name` 承载业务 code；旧 `modelName` 保持现役非空 string 类型和值并标记 deprecated，不能在本期偷偷改成展示名。历史任务只可通过版本化 legacy model/protocol 映射投影 `modelCode`，未知映射进入隔离。

服务端保存生成图片时已经执行真实图片解码校验，必须把该步骤得到的宽高沿调用链传回 Worker。结果 URL、实际尺寸、任务最终状态和账务结算应在同一次数据库结算事务中更新；文件系统本身不属于数据库事务，必须按 8.5.1 的临时文件、原子发布和孤儿清理规则处理。

本期 `outputWidth/outputHeight` 首先用于一任务一图的 GPT 流程。若 Nano/Gemini 仍允许一条任务产生多张图片，必须把实际尺寸记录到逐图 Asset/结果项；在该结构完成前不得用首图尺寸冒充整条任务的统一输出尺寸。任务输入另使用有序 input asset 表，不能与生成结果项混用。

`POST /api/ai/images/generate` 的所有 `size-mode-v1` 多图（explicit 与 auto）必须返回同一个有序结果数组；一旦 durable 批次已创建，等待完成、部分完成或等待超时都返回 HTTP 200 的同一批次 envelope，不能再用 4xx/5xx 隐藏已经存在的任务：

```json
{
  "requestState": "active",
  "batchStatus": "partial",
  "results": [
    {
      "ordinal": 0,
      "taskId": 101,
      "status": "succeeded",
      "isDeleted": false,
      "url": "/api/media/ai/.../a.png",
      "outputWidth": 1536,
      "outputHeight": 1024,
      "outputSize": "1536x1024",
      "mimeType": "image/png",
      "failureCode": null,
      "failureStage": null,
      "retryable": null,
      "refundedPoints": 0
    },
    {
      "ordinal": 1,
      "taskId": 102,
      "status": "failed",
      "isDeleted": false,
      "url": null,
      "outputWidth": null,
      "outputHeight": null,
      "outputSize": null,
      "mimeType": null,
      "failureCode": "PROVIDER_UNAVAILABLE",
      "failureStage": "provider",
      "retryable": true,
      "refundedPoints": 100
    }
  ]
}
```

新 `size-mode-v1` 的 `results` 长度必须等于请求图片数，每个 ordinal 恰好一项；`status` 固定为 `queued|processing|succeeded|failed`，`batchStatus` 固定为 `processing|succeeded|partial|failed`。对 `isDeleted=false` 的项，queued/processing 输出和失败字段为空，failed 返回稳定失败字段与最终退款，succeeded 返回自己的 URL 和真实尺寸。软删除与执行状态正交：`isDeleted=true` 保留底层 status 和原 ordinal，但作为上述字段规则的明确例外，URL、base64、尺寸和失败细节全部脱敏为空，不能借幂等重放恢复已从任务视图删除的内容；`batchStatus` 仍按底层执行 status 聚合，删除覆盖范围只由 `requestState` 表达。`results[].taskId/url/尺寸` 必须逐项对应，即使同批图片尺寸不同也不能错位。只有在 durable 创建前发生的认证、校验、catalog、价格或 admission 错误才使用同步 4xx/5xx。

以下 deprecated 兼容别名规则只适用于 `size-mode-v1` 新任务；legacy 任务先按其 contract version 投影：

- 新客户端只使用 `requested*` 与 `output*` 字段，并把它们视为任务尺寸的唯一权威值；
- 旧 `width/height/size` 保持非空 JSON 类型，避免旧强类型客户端因 `int -> null` 解码失败，并标记为 deprecated；
- explicit 任务的旧字段继续表示请求尺寸；auto 排队时旧字段可临时返回 `0/0/auto` 作为兼容占位，但 `0/0` 只能表示输出暂时未知，不得在 UI 展示为 `0x0`，成功后必须覆盖；
- auto 成功后旧字段回填实际尺寸，使 `0/0` 只存在于输出未知的活动期。新契约不依赖这组状态相关兼容别名；
- auto 的旧 `resolutionCode/aspectRatioCode` 字符串别名分别返回空字符串和 `auto`；新客户端只读取可空的 `requestedResolutionCode/requestedAspectRatioCode`；
- `/generate` 现有字段保持现役 JSON 类型并标记 deprecated：`taskId/taskIds` 返回首任务/完整有序任务，`url/base64/dataUrl/revisedPrompt/mimeType` 取第一个未删除的 succeeded result，`urls` 只列未删除成功项，`width/height/size` 按前述 explicit/auto 规则投影，其他请求元数据不变；尚无可返回成功项时非空结果字符串返回空字符串。旧字段不能表达批次部分失败，v2 客户端只读取 `results[]`；
- `legacy-explicit-v1` 投影为 `sizeMode=explicit`，仅返回迁移验证通过的 `requested*`；未重新解码时 `output*` 为空，UI 显示“实际尺寸未知”。复用时只能把历史参数交给当前 capability/price 校验并使用新 key 创建，不能重新执行旧任务；
- `legacy-aspect-auto` 保留业务分辨率、auto 比例和 legacy 计费显示，不套用新 auto UI。Nano/Gemini 只有当前模型仍声明该 legacy 能力时才可按新 key 复用；历史 GPT auto 必须按当前 v1 能力迁成新 auto，或让用户选择合法 explicit 组合，不能原样重放 legacy body；
- 迁移隔离记录投影为 `sizeContractVersion=legacy-unknown`、`sizeMode=null`、`requested*=null`，原 deprecated 字段仅作只读证据，禁止一键复用或重新执行。历史成功任务不能把请求宽高冒充实际输出宽高；只有重新解码文件后才能回填 `output*`。

异步 Worker 失败通过 `GET /api/ai/images/{id}` 的任务 DTO 返回 HTTP 200 + `status=failed`，不能把下表中的同步 HTTP 状态误当成轮询响应。`failureCode` 至少覆盖 `AI_CONSENT_REQUIRED`、`ACCOUNT_UNAVAILABLE`、`LEGAL_NOTICE_UNAVAILABLE`、`INPUT_ASSET_INVALID`、`MODEL_RELEASE_REVOKED`、`PROVIDER_OUTPUT_INVALID`、`PROVIDER_UNAVAILABLE`、`PROVIDER_OUTCOME_UNKNOWN`、`EXECUTION_LEASE_LOST`；`failureStage` 取 `preflight/provider/output/settlement`，`retryable` 只表示使用新 key 重新提交是否可能成功。任务同时返回最终 `billingStatus` 和实际退款积分；`errorMessage` 仅为本地化安全文案。

### 8.8 第二阶段前端交互

1. 客户端先按模型或历史任务的 `sizeContractVersion` 选择解析器。只有 `size-mode-v1` 使用本节模式 UI；`legacy-explicit-v1` 展示经验证的请求尺寸且复用时重新走当前能力/价格；`legacy-aspect-auto` 的 Nano/Gemini 继续显示业务分辨率和模型声明的 auto 比例，历史 GPT auto 则迁入当前 v1 选择；`legacy-unknown` 只读且禁用复用。legacy 新提交体不含 `sizeMode`。
2. `size-mode-v1` 模型只有一种模式时直接使用，不显示无意义的模式切换；同时支持两种模式时使用“尺寸模式”分段控件。
3. 新 auto 模式隐藏分辨率和比例控件，保留模型支持且有 auto 价格的画质控件；提交体中完全省略或传 JSON null 的分辨率和比例，禁止空字符串。
4. 新 explicit 模式显示现有分辨率和比例控件，并要求两者有效。
5. 模式切换可在本地保留上一次 explicit 选择，但不得把它带入 auto 请求。切回 explicit 或切换到 explicit-only 模型时，必须按服务端顺序重新校验分辨率、比例和画质；没有完整可计费组合时 fail-closed。
6. 模型或当前模式没有有效价格时禁用生成，不允许前端估算或硬编码价格兜底。
7. 草稿升级到 version 2 并保存 `sizeContractVersion`、可空 `sizeMode` 和最近一次 `catalogVersion`。v2 reader 必须接受 v1：GPT 非 auto 比例迁移为 explicit；GPT auto 在模型能力加载后按支持情况迁移为新 auto 或 explicit + 合法比例；Nano/Gemini legacy auto 保留业务分辨率与比例，不错误迁成新 auto。只有迁移成功后才回写 v2，且必须保留 owner、prompt、references、sourcePromptId、imageCount 和其他合法参数。
8. 创建结果未知恢复指纹和本地幂等 payload 使用前端可知的规范化请求字段：auto 使用 `sizeMode`，explicit 使用 `sizeMode + resolutionCode + aspectRatioCode`，并继续包含其他业务字段。`requestedSize` 只用于后端任务快照和响应，不要求前端提交前额外调用 resolve。
9. 图片详情复用必须保存模式；auto 不恢复分辨率/比例，explicit 恢复并重新校验两者。
10. 收到 `AUTO_SIZE_NOT_SUPPORTED` 时刷新 `/models`；若当前模式已失效则切回有效默认模式并保留提示词、参考图和上一次 explicit 选择，同时展示本地化错误和 `requestId`。
11. auto 排队记录显示“自动尺寸，等待生成”；成功后显示实际 `outputWidth x outputHeight`，不得展示 `0x0`。
12. 收到 `IMAGE_CATALOG_CHANGED` 时先刷新 `/models`，再以目标模型返回的 catalog 查询 v2 pricing；任一版本不一致就丢弃并重试，匹配后重新展示价格并要求用户再次触发创建。不得自动按新价格复用新 key 提交；相同旧 key 的未知结果恢复仍先走 durable 幂等查询。
13. Web build id、Android versionCode/versionName、iOS build/short version 映射到 8.4 固定的客户端 Header；网关、CORS、Swagger 和客户端网络层必须透传。版本 Header 只做兼容判断，灰度资格仍来自服务端 cohort。

### 8.9 实施影响矩阵

| 组件 | 阶段 | 主要职责 |
| --- | --- | --- |
| 前端 `PlaygroundView.vue` | 第一、二阶段 | 模型能力、合法默认值、模式 UI、请求和错误恢复 |
| 前端 `imageOptions.ts`、`playgroundDraft.ts` | 第一、二阶段 | 区分全局目录与模型能力、v1/v2 草稿迁移 |
| 前端 `types/image.ts` | 第二阶段 | `sizeMode`、能力、价格和任务尺寸 DTO |
| 前端 `imageTaskRecovery.ts`、`ImagesView.vue`、`ImageEditView.vue` | 第二阶段 | 规范化恢复指纹、任务展示和参数复用 |
| 前端 `ai-image-assets.e2e.spec.ts` | 第一、二阶段 | GPT 无 auto、Gemini 保留 auto 及新模式回归 |
| Android / iOS 网络层、DTO、草稿和任务详情 | 第二阶段 | capability/版本 Header、`sizeContractVersion` 分支、catalog 绑定、逐图结果和异步 failure DTO；各自具备商店版本回滚策略 |
| `AiImagesController` 与统一请求协调层 | 第二阶段 | Controller 不预解析路由；先 durable 幂等、再对新请求做 catalog/协议分派 |
| 后端 `AiImageModelConfigService` | 第二阶段 | 路由级能力、模式隔离、主备交集、版本化路由链、发布验证和 `/models` 聚合/缓存隔离 |
| 后端 `AiImageService` 与请求 DTO | 第一、二阶段 | 错误口径、字段存在性、规范化、durable 幂等批次、服务端灰度、尺寸解析和 Provider payload |
| `IAiImageService` / `IPointService` 与 MachineErrorCodes / Swagger | 第二阶段 | 事务接口、catalog/version/failure 新字段、稳定机器码与 OpenAPI nullable/兼容 schema |
| `IAiSizeModeRolloutPolicy` 或等价灰度服务 | 第二阶段 | 服务端 cohort、最低版本、Header 解析、POST 重判和审计；不信任客户端 Header 授权 |
| 后端 `PointService` 与价格 DTO/实体 | 第二阶段 | 事务内锁定发布价格、auto 独立查价、积分预留、价格快照和既有结算退款不变量 |
| 后端 `AiImageTaskProcessor` 与任务 DTO/实体 | 第二阶段 | 稳定 modelCode、按任务 release 执行、claim fencing、执行前用户/consent/Asset 复核、实际尺寸传递和原子结算 |
| `AiImageTaskRecoveryWorker`、任务 outbox/队列 | 第二阶段 | 可靠派发、claim 吊销/接管、晚结果隔离、pending 超时和账务释放 |
| Provider attempt、`IAiImageProviderGate` 与 Redis admission 状态机 | 第二阶段 | 持久化外发事实、未知在途隔离、Provider lease owner/续租、稳定上游幂等键、批次 reservation 与逐任务精确补偿 |
| 任务输入 Asset/结果项与媒体保存链路 | 第二阶段 | 保留有序 Asset ID/内容标识、逐结果尺寸、原子文件发布和孤儿文件清理 |
| `UserService`、`AccountDeletionService`、账户删除 Worker | 第二阶段 | 先阻断新任务并终态化/对账活动预留，再删除用户、任务输入和私有媒体 |
| 后端 Provider HTTP/Secret 适配层 | 第二阶段 | Endpoint allowlist/SSRF 与重定向防护、Secret 引用解析、受限下载/解码与输出资源限制 |
| 后端 `ImageUploadValidator` 返回链路 | 第二阶段 | 提供实际解码宽高，禁止只使用请求尺寸 |
| migrations 与 `jokester.admin.sql` | 第二阶段 | 路由、价格、版本、幂等批次、任务字段和历史数据的 expand-migrate-contract 迁移 |

## 9. 数据迁移要求

第二阶段至少包含以下数据库变更，并同时更新独立 migration 与 `jokester.admin.sql`：

1. 新增不可变 model/route/price release 及原子 published pointer。路由明细增加“已验证能力集合”和“路由尺寸模式/槽位”，唯一键包含 release id。现有 GPT 路由只迁移为 explicit；现有 Nano/Gemini 物理路由按当前已验证行为保留 legacy explicit/auto 绑定，不能全部改成 GPT explicit，也不能因此自动发布 `size-mode-v1` auto。
2. 价格明细增加 `pricing_mode` 与 price release。现有 GPT 行迁移为 explicit，现有 Nano/Gemini 行迁移为 `legacy_resolution`；auto 独立价由部署后受控发布创建。任务保存 `price_id/price_release_id`，明细唯一键包含 release id 和非 NULL 规范化编码。
3. 任务表在 expand 阶段增加可空的 `model_code`、`size_contract_version`、`size_mode`、`requested_*`、`output_*`、`model_release_id`、价格/Provider/consent 快照、`failure_*`、可空 `refunded_points`、claim fencing 与派发字段。同时必须把现有 `resolution_code`、`aspect_ratio_code` 物理列和实体属性改为 nullable；pre-expand 短窗口可暂保 legacy default，但新 auto 显式写 NULL，不能依赖空字符串冒充。legacy auto 仍可保留业务分辨率，二者由 contract version 区分。
4. 新增 durable 幂等请求头/有序任务批次明细、任务输入 Asset/legacy URL 明细、必要的逐图结果项、Provider attempt 和事务 outbox。以 `(user_id, idempotency_key_hash)`、`(request_id, task_ordinal)`、`(task_id, role, ordinal)` 建立稳定唯一约束；兼容期保留 split batch 与 single-task-multi-output 两种 legacy task 双读。历史任务只保存 URL 的，一律回填为 `input_kind=legacy_url`，不得反向猜测或伪造 Asset ID。
5. 历史任务先通过版本化映射从现有 `model_name`、Provider/路由痕迹识别当时的 model/protocol，再按该协议自己的 legacy normalizer 执行互斥真值表；模型无法识别时直接进入 `legacy-unknown`。GPT explicit 使用当时的固定比例、16px 对齐和像素约束，Nano/Gemini explicit 保留其现役“任意正整数 `W:H`、正 `WIDTHxHEIGHT`、逐轴上限和 GCD 归一”语义，例如 `1000x777 + 1000:777` 不得因不在 GPT 八个比例内被隔离。随后先隔离矛盾，再分类正常记录：
   - `aspect_ratio_code=auto` 但 `size` 为像素尺寸、显式比例但 `size=auto`、像素字符串与正宽高不一致、未知编码或其他字段互相矛盾：先写入迁移异常表/报告，设置 `size_contract_version=legacy-unknown`、`size_mode=NULL`、`requested_*=NULL` 并保持不可重执行；
   - 在排除矛盾后，`aspect_ratio_code` 为合法显式比例、`size` 为一致的合法 `WIDTHxHEIGHT` 且宽高为正：`size_mode=explicit`，`size_contract_version=legacy-explicit-v1`；
   - 在排除矛盾后，仅当 `aspect_ratio_code=auto` 且 `size=auto`，宽高为 0/NULL：`size_mode=auto`，`size_contract_version=legacy-aspect-auto`，保留原业务 `resolution_code` 作为 legacy 计费快照，`requested_size=auto`；
   - 其余无法证明语义的记录同样归入 `legacy-unknown`，禁止用 OR 条件猜测成 auto 或 explicit。发布前必须人工确认每类数量和读取投影。
6. 所有历史 `output*` 默认保持空，即使旧 `width/height` 非零也不能当作实际输出；只有通过受控脚本真实解码仍存在且归属可验证的结果文件后才能回填。历史 `result_urls` 必须用现有兼容解析器同时支持 JSON 数组和逗号分隔格式并保留原顺序；畸形项写报告，不能静默丢 URL 或伪造 ordinal。历史任务不伪造 model/price release，迁移后只能按 legacy 读取策略展示，不得重新执行。
7. migration 必须可在生产数据副本上演练，验证发布版本唯一键、NULL/空串归一化、历史分类真值表、两种旧多图形态的幂等双读/补建、Asset 输入回填边界、claim/attempt/outbox、断点恢复、账务对账、回滚边界和旧节点兼容性。

任务 API 与持久化映射：

| API 字段 | 持久化 | 空值/迁移规则 |
| --- | --- | --- |
| `sizeContractVersion` | 新增 `size_contract_version` | expand 可空；迁移后为 `legacy-explicit-v1`、`legacy-aspect-auto`、`legacy-unknown` 或 `size-mode-v1` |
| `modelCode` | 新增 `model_code` | 新任务必填；历史通过版本化 model/protocol 映射投影，未知为 NULL 并隔离；`model_name` 保持旧值 |
| `sizeMode` | 新增 `size_mode` | API 可提交值仅 explicit/auto；历史按形态分类，不引入可提交的 `legacy_auto` 枚举 |
| `requestedSize` | 新增 `requested_size` | explicit 从合法现有 `size` 回填；新/legacy auto 为 `auto`；异常记录隔离 |
| `requestedWidth/requestedHeight` | 新增可空列 | explicit 从已验证请求尺寸回填；auto 为 NULL；不等于输出尺寸 |
| `requestedResolutionCode` | 复用改为 nullable 的 `resolution_code` | 新 explicit 必填；新 auto 为 NULL；legacy auto 可保留业务分辨率并由 contract version 区分 |
| `requestedAspectRatioCode` | 复用改为 nullable 的 `aspect_ratio_code` | 新 explicit 必填且非 auto；新 auto 为 NULL；legacy auto 可保留 `auto` |
| `outputWidth/outputHeight/outputSize` | 新增 `output_width/output_height/output_size` | 排队和失败可为 NULL；成功必须来自真实图片解码 |
| `failureCode/failureStage/retryable` | 新增受控字段 | 仅失败任务可写，旧任务可空；不得保存 Provider 原始错误 |
| `billingStatus/refundedPoints` | 现有 `billing_status` + 新增可空 `refunded_points` | 新任务退款积分与唯一 refund 流水同事务写入；历史无法可靠重建时为 NULL |
| `catalogVersion` | 内部 `model_release_id` 外键映射 | 新 v1 请求必须由客户端提交；新栈创建的 legacy 任务由服务端绑定 current release；迁移前历史任务为空并禁止重新执行 |
| claim/outbox | `claim_epoch/token_hash/lease_expires_at` 与派发表 | 兼容/新 Worker 必须 CAS；pre-expand Worker 必须由基础设施停止 |
| deprecated `width/height/size` | 保留现有列 | 按兼容投影维护，不作为新逻辑的数据源 |

上述迁移采用 **expand → migrate → contract** 三阶段，而不是把停写迁表作为唯一恢复策略：

1. **Expand：** 先只上线兼容 schema、可空字段及 release/幂等/输入/attempt/outbox 表；此时 auto admission 仍关闭，旧字段语义不变。兼容二进制先以 API admission 和 Worker 消费均关闭的方式完成 staging；随后暂停旧 API 的 AI admission，让 pre-expand Worker 把已领取任务终态化或到达安全 deadline，再停机/断权全部旧 API/Worker。确认旧代次无法重连后才同时启用兼容 API/Worker，整个切换禁止两代 Worker 共同消费。不得给 `size_mode` 设置会把旧 Nano 写入误标为 explicit 的数据库默认值。
2. **Migrate：** 通过部署代次锁、Worker generation lease、凭据撤销或等价设施确认所有 pre-expand Worker 已退出且不能重新连接，旧进程发出的未知 Provider 请求已终态化或到达安全 deadline 后，才使用 advisory lock 按上述分类小批量、可重试回填。活动任务此时才可通过新 claim fencing 终态化或隔离。必须保留备份、生产数据副本演练结果、检查点、异常数据报告、失败恢复步骤和账务核对记录。
3. **Contract：** 所有 API/Worker 均通过 schema-version 检查且不存在旧账务语义节点写入后，才允许创建 `size-mode-v1` 任务并开启客户端灰度；只有在 auto 在途任务清零、账务核对完成、legacy 双读使用率达到门槛、回滚窗口结束后，才收紧约束或移除兼容字段/逻辑。pre-expand Worker 根本不认识新字段，必须由基础设施阻止运行，不能把 fail-closed 责任寄托在旧二进制。

物理列的 NULL/default、索引和检查约束必须在技术设计中与该 API 映射保持一致。任何会改变任务、价格或路由键语义的节点均不得与旧节点混部写入；但新版应在 expand 阶段具备受控恢复能力，而不是依赖未演练的停机回退。存在 v2 行后，允许回滚的最低目标只能是“已理解新 schema、但关闭 auto admission”的兼容版本，不能回滚到当前 pre-expand API/Worker。

数据库迁移不得写入真实 Provider URL、Key 或未经验证的 auto 能力。生产能力、价格、路由发布版本和 Secret 引用由部署后的受控配置步骤维护。

### 9.1 用户禁用与账户删除顺序

管理员删除、用户账户删除、禁用和其他会使结算找不到有效用户的流程都必须遵循：先设置“禁止新 AI admission”状态并撤销会话，再锁定并枚举活动 AI 任务，吊销 claim/阻止新 Provider 尝试，按统一状态机终态化并核对每笔预留与延后 Apple 追扣，最后才软删除用户、任务输入、Asset 和私有媒体。不能先把 `sys_user.is_deleted=1` 或任务/Asset 删除后再期待现有 `PointService` 完成退款。

若 Provider 请求已经在途，删除流程按 execution epoch 隔离晚结果，并等待受控的 attempt deadline/结算完成；不得删除输入/输出文件后让失租 Worker 继续读取或发布。整个删除请求保持可重试，任何活动预留未终态化时不得把数据删除阶段标记完成。管理员删除与七天后账户删除必须共用同一 AI 任务终态化服务和针对性并发测试。

## 10. 错误契约

| HTTP/code | 场景 | 客户端处理 |
| --- | --- | --- |
| `400 AUTO_SIZE_NOT_SUPPORTED` | 当前模型/有效路由链不支持 auto，或请求未满足服务端灰度/旧客户端兼容资格 | 切回 explicit，刷新模型能力；不得仅依赖 Header 自动重试 |
| `400 INVALID_SIZE_MODE_COMBINATION` | auto 同时携带尺寸字段，或模式值非法 | 保留表单并纠正字段，不自动重试 |
| `400 VALIDATION_ERROR` | explicit 缺分辨率/比例、v1 缺少/空 catalog 或尺寸算法校验失败 | 定位对应参数 |
| `400 IMAGE_PRICE_NOT_CONFIGURED` | 当前模式/画质没有独立价格 | 禁止生成并刷新定价，不回退硬编码价格 |
| `409 IDEMPOTENCY_CONFLICT` | 同一 key 对应不同规范化 payload | 创建新 key 后由用户重新提交 |
| `409 LEGACY_IDEMPOTENCY_UNVERIFIABLE` | 升级前任务仅部分命中或历史输入不足以安全重建指纹 | 不重建不扣费；保留原任务证据并转人工/历史记录查询 |
| `409 IMAGE_CATALOG_CHANGED` | 新请求携带的 `(modelCode, catalogVersion)` 未知或已非 current published 版本 | 先刷新模型，再按该版本读取价格，展示新价格后由用户重新触发；已有同 key 请求不走此错误 |
| `412 AI_CONSENT_REQUIRED` | 创建阶段发现用户缺少候选 Provider 的有效授权 | 保留草稿并引导授权；尚未创建任务或扣分 |
| `503 SERVICE_UNAVAILABLE` | 路由能力、关键依赖、版本快照或法律告知不可用 | 保留草稿，稍后重试 |

上表只描述同步 HTTP 请求失败。Worker 执行阶段发现 consent/用户/Asset/release 等问题时，任务按统一状态机失败退款，并通过 8.7 的 `failureCode/failureStage/retryable` 返回，轮询接口本身仍为 HTTP 200。普通错误响应和任务失败字段不得泄露 Provider 原始错误、内部 release 细节、密钥、Secret 引用、内部地址、提示词或图片内容。

## 11. 测试与验收

### 11.1 后端目标测试

- 请求规范化矩阵：新版 auto、explicit、旧 auto、旧 explicit、Nano legacy auto 和所有冲突组合；覆盖 missing/null/empty/non-empty、`resolution` alias 冲突、catalog 缺失/空/未知/过期/current 和 legacy `1k/1:1/med` 默认。
- DTO 字段存在性测试：省略或 null 尺寸字段的 auto 不被默认值误判，空字符串按新协议拒绝，explicit 缺字段不会被默认值静默补齐，legacy omission 仍保持现役默认。
- 异步创建在新建、durable 重放、部分/全部软删除及 legacy single-task-multi-output 下都保持 `id/taskId/ids/taskIds` 的别名关系与现役类型；同步 generate 只保留其现役 `taskId/taskIds`，不混入 create 专属 alias。
- 同一语义的新旧 auto 请求生成相同幂等指纹；auto 与 explicit 必须生成不同指纹；同一用户、同一 key、同一规范化 payload 的高并发请求只能产生一条持久化幂等记录、一个任务批次和一次积分预留。
- 持久化幂等唯一键冲突时，失败者只能用自己的 Redis reservation token 补偿额度/计数，不能撤销赢家；随后在不再次 admission、查价或扣分的条件下返回原任务批次，不同指纹稳定返回 409。批次 Bind、逐任务乱序完成、重复回调、部分成功/退款和进程崩溃恢复后，活动/积压/日图片/日积分计数均与数据库事实一致。
- 升级前 GPT split batch、GPT/Nano/Gemini single-task-multi-output 和部分/全部软删除任务通过 legacy 双读返回原有序任务/预期图片数/准确 `requestState` 并按规则补建；删除项保留 ordinal 但不重新暴露 URL/base64/失败细节，请求事实保留期内 key 不重用。
- 已有幂等任务在 auto 路由/价格关闭、Asset/来源提示词删除、catalog 切换或 Redis 不可用后仍优先返回，不再次查价、扣分或进入 Provider；Controller 不得先解析实时路由。
- 同一 `(modelCode, catalogVersion)` 的路由、能力和价格原子发布；v2 pricing envelope 回显相同作用域。并发切换时 `/models`、价格、resolve 和任务快照不出现混合 release；新 key 携带旧 catalog 返回 409，旧 key 仍返回原任务。
- 路由能力按主备交集聚合；Provider 协议相同但 fallback 未验证时不开放 auto。
- 路由解析先按尺寸模式隔离；auto 不落入 1K/2K/4K，explicit 不选中 auto 槽位；任务创建后切换路由、价格或能力配置，Worker 仍只使用任务快照版本。
- 任务快照版本被紧急安全吊销时，Worker 不调用 Provider，按统一失败结算/退款路径终态化；路由/价格版本在引用任务清零前不得删除。
- 文生图、无蒙版 edits 和带蒙版 edits 在 auto 下都向实际 Provider 发送 `size="auto"`；任一现役路径未验证时不发布统一 auto。
- auto 只匹配独立价格，不读取兼容请求中的 1K/2K/4K；重复请求不重复扣分。
- v1 任务排队时输出尺寸为空，base64 与 URL 两条保存链路成功后都使用真实解码尺寸回填；explicit/auto 同批成功、部分失败、部分 processing/等待超时及不同尺寸的多图，在 `results[]` 中按 ordinal/task/status/url/账务正确对应且 durable 创建后统一返回 HTTP 200。
- 价格选择与任务/幂等/积分预留在同一事务；定向回归积分桶扣款顺序、原批次/原到期时间退款、Apple 延后追扣和唯一 refund 流水。
- Worker 重启恢复、主路由失败切备用、配置变化和超时结算均不进入未验证的 auto 路由；所有本地 preflight 失败不计入 Provider 熔断。
- claim lease 过期、Provider lease 续租/owner 校验失败、恢复线程接管和旧 Provider 调用晚返回并发时，旧 epoch 不能 fallback、发布 URL 或结算；prepared attempt 可接管，inflight attempt 在 deadline 前不得二次调用，不支持上游幂等/查询时 deadline 后也不得自动重发。`provider_unknown` 在 SLA 内对账，超时强制退款/释放，退款后晚成功仍隔离且不重复结算；结算失败/失租产生的临时文件最终被清理。
- 10 图批次内存队列只接收部分任务时，已入队任务不被整批误退款，未派发任务由 outbox/恢复线程接续且不重复执行。
- 排队后撤回 consent、用户被禁用、法律告知失效、引用 Asset 被删除/转移/内容替换或文件缺失时，Worker 在任何外发前终止任务并原路退款；fallback 时按实际候选 Provider 复核。账户/管理员删除必须先终态化活动预留再删用户和媒体，删除流程失败可重试。
- 异步失败持久化稳定 `failureCode/failureStage/retryable`、最终 billing status 和退款积分；轮询响应不依赖解析自由文本。
- 未进入服务端灰度的用户即使伪造 `X-Client-Capabilities` 或绕过 `/models` 直接 POST auto 也必须被拒绝；模型列表在不同账号、能力 Header 与灰度状态下的缓存不得互相复用。
- 路由 Endpoint 与 Provider 结果 URL 分别拒绝 HTTP、非 allowlist 域名、loopback/私网/metadata/保留地址、DNS rebinding 和未经逐跳复验的重定向；base64/URL 超尺寸、超字节、畸形或超时输出不得公开并走退款。
- migration 在既有数据副本上执行后，GPT explicit、Nano/Gemini legacy_resolution 价格以及 legacy-explicit/legacy-auto 历史任务保持各自 model normalizer 语义；覆盖 Nano `1000x777 + 1000:777`、`result_urls` JSON/逗号双格式和未知 model 隔离。真实 MySQL 验证 `resolution_code/aspect_ratio_code` nullable、release 唯一键、advisory lock、幂等重跑、断点恢复、schema-version/Worker generation 拦截、账务核对和回滚边界。

### 11.2 前端目标 E2E

- explicit-only 模型不展示 auto，默认选择合法显式比例。
- 同时支持两种模式时可切换；auto 隐藏尺寸字段、使用 auto 价格且请求不含分辨率/比例。
- 所有 auto 价格缺失时 `/models` 不发布 auto；若模型与定价接口短暂不一致，前端仍禁用生成且不发送 POST。
- 即使 Provider 为 OpenAI、全局目录含 auto，只要有效模型能力不含 auto 就不展示。
- 从 auto 模型切换到 explicit-only 模型时，模式、分辨率、比例、画质和价格组合全部自动纠正。
- GPT 旧 auto 草稿按新能力迁移为新 auto 或 explicit；Nano/Gemini legacy auto 保留业务分辨率与 legacy 比例语义，不能错误隐藏分辨率。其他草稿字段不丢失，并断言存储已回写 version 2。
- `AUTO_SIZE_NOT_SUPPORTED` 会刷新能力、纠正失效模式并保留用户内容。
- `IMAGE_CATALOG_CHANGED` 会先刷新模型、再按该模型 catalog 读取价格并核对 envelope，展示新成本且不自动提交；已有 key 的未知结果恢复仍取回原任务。
- auto 任务从 queued 到 succeeded 的 UI 不出现 `0x0`，成功后展示实际尺寸并可正确复用。
- 历史 `legacy-explicit-v1`、`legacy-aspect-auto` 和 `legacy-unknown` 分别按只读 explicit、legacy auto、隔离不可复用规则展示；历史 output 未解码时不计入新任务尺寸完整率。

### 11.3 Android / iOS 目标测试

- 两端按 `sizeContractVersion` 区分 `size-mode-v1`、`legacy-explicit-v1`、`legacy-aspect-auto` 与 `legacy-unknown`，JSON nullable/省略字段及只读/复用规则与 Web 完全一致。
- capability、平台、版本 Header 经客户端网络层、CORS/网关与服务端正确透传；伪造版本不能越过服务端 cohort。
- 旧 app 收不到含 `resolutionCode=null` 的 price schema；新 app 能处理 catalog 切换、稳定 `modelCode`、逐图结果、可空历史字段和任务 `failureCode`。
- iOS/Android 最低支持版本、灰度扩大、商店审核延迟和服务端关闭 auto 的回滚路径均完成验收；旧版本始终能继续使用 explicit/legacy 契约。

### 11.4 产品验收标准

| 编号 | 验收项 |
| --- | --- |
| AI-SIZE-001 | 第一阶段 GPT 仅声明 `1:1` 时，页面无 auto 且提交 `1:1` |
| AI-SIZE-002 | 模型能力缺失时前端 fail-closed，不从全局参数或 Provider 名称补能力 |
| AI-SIZE-003 | GPT 旧 auto 草稿自动迁移，Gemini/Nano 只有明确支持时保留 auto |
| AI-SIZE-004 | 第二阶段新 auto 请求的尺寸字段只能省略/null，非空或空字符串均拒绝；新 explicit 必须有规范分辨率和比例，legacy omission 仍保留现役默认 |
| AI-SIZE-005 | 只有显式批准兼容白名单、满足服务端灰度且已固定 current catalog/确认同一展示价格的旧 GPT `aspectRatioCode=auto` 才按 auto 独立价格处理；catalog 切换即停用，其余默认拒绝；Nano/Gemini legacy resolution 计费不变 |
| AI-SIZE-006 | 任一可能参与切换的启用路由未验证 auto 时，`/models` 不开放 auto |
| AI-SIZE-007 | auto 无独立价格时前后端均拒绝，不按 1K/2K/4K 收费 |
| AI-SIZE-008 | auto 任务排队时保存 `requestedSize=auto`、路由/价格版本快照且输出宽高为空 |
| AI-SIZE-009 | 生成完成后从真实图片回填 `outputWidth/outputHeight`，列表和详情不展示 `0x0` |
| AI-SIZE-010 | auto 的 durable 幂等、积分预留、主备、失败退款、恢复与高并发重试流程通过针对性回归 |
| AI-SIZE-011 | 路由、能力或价格配置在任务创建后变更时，Worker 仅执行任务快照版本；紧急吊销的快照不外发并退款 |
| AI-SIZE-012 | 排队后撤回 consent、账户失效、法律告知失效或引用 Asset 失效时，Provider 未收到请求且任务退款 |
| AI-SIZE-013 | 直接 POST 或伪造 capability Header 不能越过服务端灰度；`/models` 缓存不跨账号/能力状态泄漏 auto |
| AI-SIZE-014 | 仅经审批发布的 HTTPS allowlist 路由和 Secret 引用可调用；SSRF、重定向和异常输出均被拦截且不落盘 |
| AI-SIZE-015 | expand-migrate-contract 演练可恢复，schema-version、在途任务、账务核对与回滚门禁全部通过 |
| AI-SIZE-016 | 历史 GPT/Nano/Gemini 任务按 explicit、legacy auto、异常三类迁移，未发生全量 explicit 误标；旧多图 key 可双读取回 |
| AI-SIZE-017 | 同一 `(modelCode, catalogVersion)` 原子绑定能力、路由、价格和 Secret 版本，v2 pricing envelope 回显同一作用域；新请求不因切换 catalog 被静默按新价扣费 |
| AI-SIZE-018 | Worker/恢复线程通过 claim fencing 竞争时，失租执行者不能 fallback、发布结果或结算，且孤儿文件最终清理 |
| AI-SIZE-019 | 队列部分入队由 outbox/恢复机制接续，不发生已领取任务整批退款或重复执行 |
| AI-SIZE-020 | 所有 v1 多图直出 `results[]`，每个 ordinal 都返回 task/status/输出或失败/退款；部分失败、软删除和等待超时仍为可恢复且不泄露已删除内容的 HTTP 200 批次，旧标量只代表首个未删除成功项 |
| AI-SIZE-021 | Worker 失败返回稳定 `failureCode/failureStage/retryable` 和最终账务结果，移动端不解析自由文本做流程判断 |
| AI-SIZE-022 | 管理员/账户删除先阻断 admission 并终态化所有活动预留，再删除用户与媒体；在途 Provider 晚结果不可访问 |
| AI-SIZE-023 | Provider 结果 URL 与路由 Endpoint 分别执行 HTTPS、域名、DNS/IP、逐跳重定向和流式上限校验 |
| AI-SIZE-024 | 所有 v1 resolve/create/generate 均强制 catalog；missing/empty/unknown/stale 与 legacy omission 按唯一错误契约验收 |
| AI-SIZE-025 | Redis reservation owner token 保证并发失败者只补偿自己；未知 inflight Provider attempt 不发生自动二次外发 |
| AI-SIZE-026 | 历史 explicit、legacy auto、矛盾异常按互斥真值表迁移和投影，异常记录禁止复用且不污染新任务尺寸完整率 |
| AI-SIZE-027 | migrate 前所有 pre-expand Worker 已被基础设施隔离；存在 v2 行后只能回滚到理解新 schema 的兼容版本 |
| AI-SIZE-028 | 批次 reservation 的 Bind/逐任务 Complete/未提交 Cancel 在部分成功、乱序、重复和崩溃恢复下保持所有 Redis 计数准确 |
| AI-SIZE-029 | Provider lease 按 owner 续租；续租失败进入 unknown，最晚在 `reconcile_by` 强制退款释放，晚成功不发布且不重复结算 |
| AI-SIZE-030 | 任务稳定返回独立 `modelCode`；迁移按 GPT/Nano 各自 legacy normalizer 分类，nullable 尺寸列和历史 URL 双格式在真实 MySQL 验收通过 |

## 12. 发布、灰度与回滚

### 12.1 第一阶段

1. 完成前端能力收敛、草稿迁移和 P0 E2E。
2. 仅部署前端即可恢复 GPT 显式比例生成，不依赖数据库或后端发布。
3. 公网冒烟验证 GPT 生成请求使用服务端声明的比例；Nano/Gemini 只有目标 `/models` 明确声明 auto 时才验证 legacy auto，并留存数据库/接口能力快照。
4. 后端错误文案与 `AUTO_SIZE_NOT_SUPPORTED` 机器码可随后独立部署，但必须同时完成才算后端 P0 验收通过。
5. 监控 GPT 参数错误率、生成创建成功率和草稿恢复异常；出现回归时只回滚前端版本。

### 12.2 第二阶段

1. 仅执行 expand schema：增加兼容 nullable 字段、持久化幂等/有序批次、输入 Asset、provider attempt、release/pointer、claim/outbox 和任务失败/退款字段；auto 保持关闭，旧字段语义不变。
2. 以关闭 API admission/Worker 消费的状态 staging 兼容版本。暂停旧 API 的 AI admission，让 pre-expand Worker 把已领取 Provider 调用终态化或到达经审批的安全 deadline；随后停机并通过 deployment generation lock、Worker lease、凭据撤销确认全部 pre-expand API/Worker 无法重连。只有这一步完成后才启用兼容 API/Worker 的 schema-version、legacy 双读、claim/attempt/outbox，禁止新旧 Worker 混跑。
3. 保持新 AI admission 暂停，在兼容 Worker 独占后使用 advisory lock 执行可重试 migrate：分别回填 GPT/legacy 价格、历史 explicit/legacy auto/legacy unknown、两种旧批次形态。遗留活动任务此时才通过 fencing 终态化或隔离；完成备份、异常报告与账务核对后恢复 legacy admission。
4. 启用兼容 API/Worker 创建 v1 explicit 的能力，并部署支持 `sizeMode`、模型级 catalog 和客户端能力标记的前端/移动端；此时 `/models` 对新协议客户端仍只返回 GPT explicit，并启用账号/租户/最低版本的服务端决策与用户隔离缓存策略。
5. 在预发布环境逐条验证目标主路由、fallback 的 generations/edits/带蒙版 edits auto 能力、路由和结果 URL 防护、Secret 具体版本、输出资源限制、未知 attempt 隔离及路由发布审批证据。
6. 配置并审批绑定同一 model catalog 的 auto 独立价格和不可变 release；完成事务内价格锁定、积分预留、失败退款、并发 durable/legacy 幂等、reservation token、claim/attempt/outbox、账户删除和账务核对。
7. 打开模型级灰度开关，使 `/models` 仅向“能够理解新契约且服务端已授权 auto cohort”的验收客户端加入 GPT auto；其他新协议客户端继续只见 v1 explicit，旧客户端保持 legacy schema 或仅在已批准兼容策略下映射。
8. 先灰度内部账号，再逐步扩大。持续监控安全拒绝、账务、版本快照、Provider 成本和恢复指标；稳定后才评审是否调整 `defaultSizeMode`，本期保持 explicit。

允许的节点兼容矩阵：

| API | Worker | 数据状态 | 结论 |
| --- | --- | --- | --- |
| pre-expand | pre-expand | 仅 expand schema、无 v2 行 | 只允许步骤 1 的短暂窗口，auto 关闭 |
| 兼容 | 兼容 | auto 关闭 | 可执行 migrate 和 legacy/v1 explicit 受控读写 |
| 新 | 新 | auto 开启 | 正常灰度 |
| 任意 | pre-expand | 兼容写入已启用或存在 v2 行 | 绝对禁止；旧二进制不会自我 fail-closed，必须由基础设施停机/断权 |
| pre-expand | 兼容/新 | 兼容写入已启用 | 禁止；旧 API 会继续产生缺少新事实的写入 |
| 兼容（auto 关闭） | 新 | 存在 auto 在途 | 唯一允许的 API 回滚目标；Worker 继续按任务快照完成在途任务 |

回滚时先从 `/models` 移除 auto 并在 admission 拒绝新 auto 任务，但只能回到理解新 schema 的兼容 API；不得重新启动 pre-expand API/Worker。保留已发布的 auto 路由、价格、Secret 引用和版本快照，直到其引用的在途任务全部终态化。Worker 必须继续按每个任务自己的快照版本执行；不得提前禁用、删除或用新版路由替换在途任务的版本。若路由因安全或故障必须立即停用，只吊销目标发布版本：所有引用任务在真正 Provider 调用前停止并按失败状态机结算退款。数据库 contract/降级只能在任务清零、账务核对完成、旧版本兼容性确认和回滚窗口关闭后执行。

## 13. 监控与审计

至少增加以下不含提示词和图片内容的指标：

- 按模型、客户端版本、服务端灰度结果统计 `sizeMode` 请求量和成功率；
- `AUTO_SIZE_NOT_SUPPORTED`、`IMAGE_CATALOG_CHANGED`、组合校验失败、价格缺失、灰度拒绝和缓存隔离异常次数；
- auto 主路由失败、fallback 切换、Provider 超时和最终失败率；
- auto 任务预留积分、确认、退款、持久化幂等命中和幂等冲突数量；
- 新 `size-mode-v1` 成功任务 `outputWidth/outputHeight` 缺失率，目标为 0；历史未解码任务和明确隔离记录单独统计，不进入该 SLO；
- 旧 `aspectRatioCode=auto` 兼容映射使用率，用于确定下线时间；
- 按路由/价格/能力版本统计的在途任务数、Worker 快照版本命中、紧急吊销与因版本不可执行产生的退款数；
- Worker 执行前 consent、法律告知、账户状态、Asset 所有权/文件存在性校验的拒绝量；
- claim/Provider lease 续租失败、owner 不匹配、epoch 接管、失租晚结果、provider attempt prepared/inflight/provider_unknown、`reconcile_by` 超时强制退款、人工对账和潜在 Provider 成本，以及结算 CAS 冲突、outbox 延迟/重试/死信、孤儿文件发现与清理数量；
- Redis reservation token 的 reserve/CancelUncommitted/BindBatch/逐 ordinal CompleteTask、owner 不匹配拒绝、唯一键输家补偿、未绑定 pending 时长及活动/积压/日额度计数不平数量；
- legacy 幂等双读/补建、legacy auto 读取、迁移异常数据和软删除请求命中数量；
- 账户删除等待活动预留、终态化失败与账务不平数量；
- 路由发布验证和双人审批失败量、路由 Endpoint 与结果 URL 的 DNS/IP/重定向防护拦截量、Secret 解析失败量；
- Provider 输出超编码长度、超字节、超像素、格式/帧数不符、下载/解码超时数量，以及每个 auto 路由的实际成本与已审批价格偏差。

## 14. 依赖与待决策项

| 项目 | 责任方 | 发布前要求 |
| --- | --- | --- |
| auto 各画质独立积分价格 | 产品、运营、财务 | 审批具体积分和金额，确认不随实际输出追补 |
| 目标环境 baseline | 后端、DBA、测试 | 留存脱敏 `/parameters`、`/models`、参数/路由/价格查询；确认 Nano auto 是否为仓库外漂移，决定是否补 migration |
| 主、备 Provider auto 能力 | 后端、运维、测试 | 逐路由、逐 generations/edits/蒙版验收并留证；包含输出资源上限、路由与结果下载 allowlist、重定向防护和紧急吊销预案 |
| model/route/price release 表结构 | 后端、DBA、安全 | 技术设计评审，保证模型级 `(modelCode, catalogVersion)` 原子绑定确定路由链、价格、能力、Provider code 与 Secret 具体版本 |
| auto 价格与版本策略 | 产品、运营、财务、后端 | 审批最高可能成本下的具体积分和金额，确认不随实际输出追补，并发布 `price_id/price_release_id` |
| durable 幂等与批次保留期限 | 后端、DBA、产品 | 确定唯一键、事务边界、过期/清理策略和用户可重试窗口，不得早于任务与账务审计保留期删除 |
| Provider attempt、全局 lease 与 Redis reservation | 后端、DBA、运维、财务 | 确定 attempt/reconcile deadline、上游幂等/查询能力、unknown 强制终结、Provider lease owner/续租、批次/逐任务 reservation Lua、额度补偿和潜在成本对账 |
| 服务端 auto 灰度与旧客户端兼容期限 | 产品、Web/Android/iOS、后端、安全、财务 | 确定稳定 cohort、最低版本、正式 Header/CORS/网关透传、旧 GPT auto 默认关闭、固定 catalog/价格确认方式、使用率阈值和移除日期 |
| 路由配置审批与 Secret 管理 | 运维、安全、后端 | 确定具体 Secret version 引用、双人审批、审计保留、发布/撤销责任人和应急流程 |
| claim/outbox 与账户删除 | 后端、DBA、测试、安全 | 审批租约/心跳/attempt deadline、晚结果清理、可靠派发及“先结算预留再删账号/媒体”流程 |
| 历史输出尺寸回填 | 产品、运维 | 决定保持未知或执行受控图片解码任务 |
| 发布迁移与回滚演练 | DBA、运维、后端、财务 | 完成 expand-migrate-contract 演练、advisory lock/断点恢复、schema-version、在途任务和账务核对证明 |

## 15. 文档同步矩阵

本 PRD 是设计与验收权威；截至 2026-08-21，后端兼容实现已进入当前工作树，但尚未合并、部署或启用 auto。现役调用合同以 [integration-guide.md](./integration-guide.md) 为准，发布与回滚状态以 [runbook.md](./runbook.md) 为准。

| 时点 | 必须同步的文档或规则 |
| --- | --- |
| 第一阶段完成 | 目标环境 baseline 证据、前端接入说明、依赖可复现的 E2E 说明；后端机器码落地后同步当前错误说明 |
| 第二阶段后端上线 | `integration-guide.md`、`architecture.md`、`runbook.md`、MachineErrorCodes、Swagger/OpenAPI nullable 与版本化 schema |
| 第二阶段数据库上线 | 新 migration、`jokester.admin.sql`、数据库升级顺序、release/幂等/reservation/claim/attempt/outbox/账户删除运维手册 |
| 第二阶段全端上线 | `README.md`、根目录设计书、`AGENTS.md`、`point-package-frontend-prd.md`、Web 类型及 Android/iOS 契约/最低版本/回滚说明 |
