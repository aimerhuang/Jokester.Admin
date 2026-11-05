# AI 无限画布 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v0.2 |
| 日期 | 2026-11-03 |
| 需求状态 | P0 后端已在当前工作树实现并通过单元测试（未合并、未在真实 MySQL 演练、未部署）；待决策项按建议值实施；前端未开始 |
| 适用端 | 用户 Web（桌面浏览器优先）、Jokester Admin API；Android / iOS 首期不接入 |
| 前端基线 | `Jokester-Ai-Image`（Vue 3 + Vite），现有 `/playground`、`/images`、`/credits`、`/pricing` |
| 后端基线 | `POST /api/ai/images` 统一创建入口、`size-mode-v1`、`media_asset` 私有素材、`ai_image_task_result` 逐图结果 |
| 交互参考 | `infinite-canvas` / `infinite-canvas-hufakai`（React/Next，仅作交互参考，见 13.3 许可说明） |
| 关联契约 | [integration-guide.md](./integration-guide.md)、[ai-image-size-mode-prd.md](./ai-image-size-mode-prd.md)、[ai-prompt-filter.md](./ai-prompt-filter.md)、[point-recharge.md](./point-recharge.md) |

## 2. 推荐结论

1. **画布项目存服务端，不只存浏览器本地。** 参考项目把画布存在 IndexedDB、图片存 Blob；本系统的图片已经是服务端私有媒体（`/api/media/ai/...`、`media_asset`），画布只需保存“节点 + 连线 + 视口 + 素材引用”的 JSON 文档。存服务端可以跨设备打开，后续接入移动端也不需要迁移数据。
2. **画布不另建生图通道。** 生成节点直接调用现有 `POST /api/ai/images`（`size-mode-v1`、`idempotencyKey`、`referenceAssetIds`），计费、幂等、提示词过滤、AI consent、Redis 准入、退款状态机全部复用，不新增任何扣分路径。
3. **画布内图片一律以 `assetId` 引用。** 生成结果需要能作为下游节点的参考图，因此新增“生成结果登记为 Asset”能力。画布文档禁止保存任意 URL、base64 或 Data URL。
4. **画布参数严格消费 `/api/ai/images/models`。** 截图中的 `5:4`、`4:5` 当前后端不支持，`Auto` 只在 `size-mode-v1` auto 已发布且用户 cohort 命中时出现；前端不得自行补出能力。

## 3. 背景

现有“无限绘画”（`/playground`）是单次表单式生图：一个提示词、一组参考图、一批结果。用户在连续迭代时（例如“用图 1 的构图 + 图 2 的参数再出一版”）需要反复下载、重新上传、手工记住哪张图来自哪次生成。

无限画布把每次生成变成画布上的节点，用连线表达“谁是谁的参考图”，用户在一张可平移缩放的画布上沉淀整个创作过程：

- 截图 1：左侧图片节点（标签“图片1”，标题为该图提示词）通过连线接入右侧“新建生图”节点；下方是生成面板：参考图缩略图、提示词输入（支持 `@引用素材`）、模型选择、参数摘要 `Low · 2K · 16:9`、张数 `1张`、按钮 `生成 (20)`（20 为预计消耗积分）。
- 截图 2：参数浮层“图像设置”：质量 Low/Medium/High、分辨率 1K/2K/4K、宽高比 Auto/1:1/16:9/9:16/4:3/3:4/5:4/4:5/3:2/2:3/21:9；左侧“我的画廊”抽屉，分“工作台 / 无限画布”两个页签，列出历史生成（缩略图、提示词、`模型-分辨率-比例`），顶部计数 `23 / 75 张`；右上角保存状态“已保存”。
- 截图 3：主导航新增“无限画布”入口；列表页含“开始创作”卡片和项目卡片（封面、名称、日期、素材体积），卡片操作为下载、重命名、删除；页头显示“画布素材 0 KB”。

## 4. 目标与非目标

### 4.1 目标

1. 用户可以创建、打开、重命名、删除多个画布项目，并在任意设备登录后继续编辑。
2. 画布支持图片节点、文本节点、生图节点和节点间连线；生图节点以上游连接的图片作为参考图生成。
3. 生成结果自动落到画布上，并能继续作为下一次生成的输入，形成链式迭代。
4. 自动保存，保存状态对用户可见；多标签页并发编辑不静默覆盖。
5. 生成的计费、幂等、失败退款与现有 AI 生图完全一致，画布本身不收费。
6. 画布只能读写自己拥有的素材，跨用户访问与不存在统一 404。

### 4.2 非目标（首期）

- 多人实时协作、分享链接、公开画布。
- 画布助手 / Agent、文本生成节点、工作流模板（参考项目有，首期不做）。
- 图片编辑工具：裁剪、遮罩重绘、超分、“智能抠图”（导航中的其它入口另立需求）。
- 移动端触控适配、Android / iOS 客户端接入。
- 服务端导入外部画布包、与参考项目数据格式互通。
- 画布存储计费或按体积收费。

## 5. 术语

| 术语 | 定义 |
| --- | --- |
| 画布项目 | 用户拥有的一份画布，服务端以 `canvasId`（不可枚举字符串）标识 |
| 画布文档 | 项目的 JSON 内容：节点、连线、视口、背景等，带 `schemaVersion` |
| 修订号 `revision` | 每次成功保存递增，用于乐观并发控制 |
| 图片节点 | 展示一张图片，必须绑定当前用户的 `assetId` |
| 文本节点 | 纯文本便签，不参与生成，首期也不作为提示词输入 |
| 生图节点 | 保存一组生成参数和提示词，执行时调用 `POST /api/ai/images`，结果以图片节点或节点内结果呈现 |
| 连线 | `fromNodeId → toNodeId`，允许“图片节点 → 生图节点”和“生图节点 → 生图节点”（取上游节点当前选中的结果图）；另允许“图片/生图节点 → 图片节点”表示派生来源（切图、裁剪、标记结果自动连回原图），每个图片节点最多 1 条输入，不能手动拖出 |
| 输入序号 | 生图节点按 `inputOrder` 给上游图片编号“图片1…图片N”，与 `referenceAssetIds` 顺序一一对应 |
| 生成型 Asset | 由 `ai_image_task_result` 登记出来的 `media_asset`，`asset_type=generated` |

## 6. 用户流程

### 6.1 新建并生成

1. 用户进入“无限画布”列表页，点击“开始创作”，后端创建空项目并返回 `canvasId`，前端跳转 `/canvas/{canvasId}`。
2. 用户通过工具栏“上传”或从“我的画廊”拖入图片，得到图片节点。
3. 用户从图片节点右侧端口拖出连线，松手时在空白处创建“新建生图”节点；或先建生图节点再连线。
4. 选中生图节点时，下方展开生成面板：已连接的参考图按输入序号显示；提示词中输入 `@` 弹出“图片1…图片N”供引用。
5. 用户选择模型与参数，按钮实时显示预计积分 `单价 × 张数`；点击生成。
6. 节点显示排队/生成中状态；成功后结果图出现在节点中（多张为图片组），失败显示失败原因与“已退还积分”。
7. 结果节点可再连出新的生图节点，继续迭代。

### 6.2 从画廊复用

左侧“我的画廊”抽屉：

- “工作台”页签：当前用户全部生图历史（复用 `GET /api/ai/images`，分页加载，顶部显示 `已加载 / 总数 张`），条目展示缩略图、提示词首行、`模型-分辨率-比例`。
- “无限画布”页签：当前画布内出现过的生成结果（来源于画布文档，按时间倒序），便于在大画布中定位。
- 条目拖入画布或点击“插入”时，先调用 6.4 的登记接口拿到 `assetId`，再生成图片节点。

### 6.3 保存

- 任何节点、连线、视口变化在停止操作 1.5 秒后自动保存；保存中、已保存、保存失败三态显示在右上角。
- 关闭页面前若有未保存修改，先尝试保存，失败则浏览器提示离开确认；未保存内容同时写入本地 IndexedDB 草稿，下次打开同一画布时提示恢复。
- 撤销 / 重做只在前端内存中保留最近 50 步，不入库。

### 6.4 生成结果作为下游输入

生成成功后，前端对每张结果调用 `POST /api/assets/from-ai-result`（见 9.3）拿到 `assetId`，图片节点只保存 `assetId`；下游生图节点据此组装 `referenceAssetIds`。该接口幂等，同一 `(taskId, resultOrdinal)` 永远返回同一 `assetId`。

## 7. 功能需求

### 7.1 画布列表页（`/canvas`）

| 编号 | 需求 |
| --- | --- |
| L-1 | 导航新增“无限画布”，位于“无限绘画”之前；未登录跳转登录页 |
| L-2 | 页头显示“自由编排创作节点 · 画布素材 {总体积}”，总体积为当前用户全部未删除画布 `storageBytes` 之和（口径见 9.5） |
| L-3 | 首个卡片为“开始创作”，点击创建空画布；超过项目数上限时禁用并提示 |
| L-4 | 项目卡片：封面、名称、更新日期（本地时区格式化）、体积；按 `updatedAt` 倒序，分页或无限滚动 |
| L-5 | 卡片操作：下载（导出，见 7.6）、重命名（1–100 字符，去首尾空白）、删除（二次确认，软删除） |
| L-6 | 封面取画布中最近一张生成结果；无生成结果取第一张图片节点；都没有显示占位图 |

### 7.2 画布编辑器（`/canvas/{canvasId}`）

| 编号 | 需求 |
| --- | --- |
| E-1 | 标题区：显示并可内联编辑画布名称；右上角保存状态 |
| E-2 | 画布：背景网格；左键在空白处拖动为框选，中键或 `Space`+左键拖拽平移；滚轮 / 触控板缩放（10%–400%），左下显示当前百分比；“适应画布”按钮；“定位”按钮回到选中节点 |
| E-3 | 左下工具：“我的画廊”抽屉开关、导航（小地图，P1）、适应画布、缩放比例 |
| E-4 | 底部工具栏：撤销、重做、文本节点、插入图片（从画廊）、上传图片、背景样式、整理布局（P1）、删除选中、清空画布（二次确认）、快捷键说明 |
| E-5 | 节点：可选中、多选（框选 / Shift）、拖拽、缩放（图片节点保持比例）、删除；多选后可“绑组”，选中组可“解组”（`Ctrl/Cmd+G` / `Ctrl/Cmd+Shift+G`），组以节点 `groupId` 表示，点击组内节点先选中整组、再次点击选中单个节点，切图结果默认成组；图片节点左上角显示输入标签（“图片1”），节点上方显示标题（提示词或文件名，单行截断） |
| E-6 | 连线：从节点右侧端口拖出；连线中点显示剪刀按钮，点击断开；不允许成环、不允许连到自身、不允许重复连线 |
| E-7 | 生图节点空态显示占位图；生成中显示进度态；成功显示结果，多张结果显示为可展开的图片组并可切换“主图”（下游引用主图） |
| E-8 | 图片节点右键 / 悬浮菜单：下载原图、复制提示词、用作参考（自动新建生图节点并连线）、在画廊中查看、删除节点 |
| E-9 | 删除节点只删除画布节点和相关连线，不删除 Asset、不删除生图任务 |
| E-10 | 引用的 Asset 已被删除或无权访问时，节点显示“素材已失效”，不可用作生成输入 |

### 7.3 生成面板

| 编号 | 需求 |
| --- | --- |
| G-1 | 面板锚定在选中的生图节点下方，可展开为大编辑框 |
| G-2 | 参考图区：显示已连接上游图片缩略图与序号；“参考”按钮可直接上传 / 从画廊选择，并自动创建图片节点与连线；参考图总数上限 6（与后端一致），超出时禁止继续连线并提示 |
| G-3 | 提示词：最长 4000 字符；`@` 弹出当前可用输入（图片1…N）；提交时以纯文本“图片1”等标签发送，不发送任何内部 ID |
| G-4 | 模型选择：来自 `GET /api/ai/images/models`，只展示当前启用模型 |
| G-5 | 图像设置浮层：质量、分辨率、宽高比三组选项全部来自所选模型能力；模型能力缺失时禁止生成 |
| G-6 | `Auto` 宽高比只在模型 `sizeContractVersion=size-mode-v1` 且 `capabilities.sizeModes` 含 `auto` 时显示，选中后以 `sizeMode=auto` 提交，省略分辨率和比例 |
| G-7 | 张数：1–4 张（以模型能力上限为准）；GPT 按现有规则拆成单图任务 |
| G-8 | 按钮显示 `生成 ({积分})`，积分来自 v2 `pricing-options`（`modelCode + catalogVersion`），单价 × 张数；无价格时禁用 |
| G-9 | 余额不足时按钮旁提示并提供“去充值”，返回画布后保留节点和面板状态 |
| G-10 | 生成前未授予当前 Provider 的 AI processing consent 时，按现有 `412 AI_CONSENT_REQUIRED` 流程弹出授权 |
| G-11 | 每次点击生成前先生成新的 `idempotencyKey` 并写入节点、触发保存，再发请求；页面刷新或网络失败后重试使用同一 key，避免重复扣分 |
| G-12 | 生成中的节点在刷新、重新打开画布后，用节点里保存的 `taskIds` 继续轮询 `GET /api/ai/images/{id}` 恢复状态 |
| G-13 | 提示词命中过滤返回 `422 PROMPT_BLOCKED` 时，只提示“提示词包含不允许的内容”，不回显命中词 |

### 7.4 “我的画廊”抽屉

| 编号 | 需求 |
| --- | --- |
| D-1 | 可折叠、可调宽，折叠状态写入本地偏好 |
| D-2 | “工作台”页签分页加载 `GET /api/ai/images`，仅成功任务可拖入；计数显示 `已加载 / total 张` |
| D-3 | 列表缩略图优先使用 Asset 缩略图（`/api/assets/{assetId}/thumbnail`），尚未登记的结果使用原图 URL 并带 JWT 加载 |
| D-4 | “无限画布”页签只读取当前画布文档，不额外请求后端 |

### 7.5 快捷键（首期）

左键拖拽框选、`Shift+拖拽` 追加框选、中键或 `Space+拖拽` 平移、`Ctrl/Cmd+G` 绑组、`Ctrl/Cmd+Shift+G` 解组、`Ctrl/Cmd+Z` 撤销、`Ctrl/Cmd+Shift+Z` 重做、`Delete/Backspace` 删除选中、`Ctrl/Cmd+A` 全选、`Ctrl/Cmd+0` 适应画布、`Ctrl/Cmd+Enter` 在生成面板内提交。输入框聚焦时不拦截编辑快捷键。

### 7.6 导出（P1）

前端生成 zip：`canvas.json`（画布文档，`assetId` 替换为包内相对路径）+ `images/` 原图。图片通过带 JWT 的请求下载。首期不做导入。

## 8. 画布文档规范（`schemaVersion=1`）

```jsonc
{
  "schemaVersion": 1,
  "viewport": { "x": 0, "y": 0, "zoom": 0.73 },
  "background": "lines",            // lines | dots | blank
  "nodes": [
    {
      "id": "n_7f3a…",              // 客户端生成，画布内唯一，<= 40 字符
      "type": "image",              // image | text | generate
      "x": 320, "y": 200, "width": 276, "height": 206,
      "title": "实拍工程人员使用内窥镜…",
      "data": {
        "assetId": "ast_…",
        "sourceTaskId": 123,        // 可选，来自生成结果时填写
        "sourceResultOrdinal": 0
      }
    },
    {
      "id": "n_b21c…",
      "type": "generate",
      "x": 815, "y": 235, "width": 276, "height": 155,
      "title": "新建生图",
      "data": {
        "prompt": "…",
        "modelCode": "gpt-image-2",
        "catalogVersion": "…",
        "sizeMode": "explicit",     // explicit | auto
        "resolutionCode": "2k",
        "qualityCode": "low",
        "aspectRatioCode": "16:9",
        "imageCount": 1,
        "inputOrder": ["n_7f3a…"],
        "pendingIdempotencyKey": "…", // 未得到任务 ID 前保存
        "runs": [                     // 最近 20 次，旧的截断
          { "idempotencyKey": "…", "taskIds": [456], "createdAt": "2026-11-03T08:00:00Z" }
        ],
        "primaryResult": { "taskId": 456, "resultOrdinal": 0, "assetId": "ast_…" }
      }
    },
    { "id": "n_c9…", "type": "text", "x": 0, "y": 0, "width": 200, "height": 80,
      "data": { "content": "备注", "fontSize": 14 } }
  ],
  "connections": [
    { "id": "e_…", "from": "n_7f3a…", "to": "n_b21c…" }
  ]
}
```

服务端保存时的校验规则：

1. JSON 合法、`schemaVersion` 为已知版本、文档 UTF-8 字节数不超过上限。
2. 节点 / 连线数量不超过上限；`id` 唯一；`type` 在白名单内；坐标、宽高为有限数值且在合理范围内。
3. 未知字段拒绝（不是静默丢弃），避免任意数据写进库。
4. 文档结构中没有任何图片 URL 字段，图片只能以 `assetId` 表达；标识类字段（节点 ID、模型编码等）只允许受限字符集。标题、提示词、文本内容是自由文本，可以包含网址字样，但前端只按纯文本渲染，不会据此加载资源。
5. 所有 `assetId` 必须属于当前用户；已删除的自有 Asset 允许保留（避免用户删素材后画布再也存不了），但不计入体积、不作为封面。`sourceTaskId` 与 `primaryResult.taskId` 必须属于当前用户；`runs[].taskIds` 只校验格式，读取状态时由任务接口自身做所有权校验。不满足返回 404（与 Asset 现有口径一致），不提示是哪一项。
6. 连线两端节点必须存在，类型组合合法，不成环。
7. 文本节点 `content` ≤ 5000 字符；生图节点 `prompt` ≤ 4000 字符；标题 ≤ 200 字符。
8. 画布文档中的提示词和文本**不在保存时过滤**，过滤只发生在真正提交生成时（现有 `IAiPromptFilter`）。前端渲染文本一律按纯文本处理，不解析 HTML。

## 9. 后端设计

### 9.1 数据表

新表时间字段直接存 UTC。

**`ai_canvas_project`**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | BIGINT PK | 内部主键 |
| `canvas_id` | VARCHAR(40) ascii_bin UNIQUE | 对外 ID，随机不可枚举，如 `cnv_` + 时间前缀 + 随机串 |
| `owner_user_id` | BIGINT FK `sys_user` | 所有者 |
| `title` | VARCHAR(100) | 名称 |
| `schema_version` | INT | 文档版本 |
| `revision` | BIGINT | 修订号，创建为 1 |
| `document` | MEDIUMTEXT | 画布文档 JSON |
| `document_bytes` | INT | 文档字节数 |
| `node_count` / `connection_count` | INT | 统计 |
| `cover_asset_id` | VARCHAR(40) NULL | 封面，保存时由服务端推导 |
| `storage_bytes` | BIGINT | 体积口径见 9.5 |
| `created_at` / `updated_at` | DATETIME | UTC |
| `deleted_at` / `is_deleted` | | 软删除 |

索引：`(owner_user_id, is_deleted, updated_at)`。

**`ai_canvas_project_asset`**（引用索引，每次保存全量重建）

| 字段 | 说明 |
| --- | --- |
| `canvas_project_id` + `asset_id` | 联合主键 |
| `owner_user_id` | 冗余，便于按用户清理 |
| 索引 | `(asset_id)`：删除素材时可查询被哪些画布引用 |

**`media_asset` 扩展**

| 变更 | 说明 |
| --- | --- |
| `asset_type` 新增取值 `generated` | 生成结果登记 |
| `source_task_id` BIGINT NULL | 来源任务 |
| `source_result_ordinal` INT NULL | 来源结果序号 |
| UNIQUE `(source_task_id, source_result_ordinal)` | 保证登记幂等 |

**`ai_image_task` 扩展（P1，可选）**：`canvas_project_id` BIGINT NULL + 索引，用于统计和“无限画布”来源筛选；不进入请求指纹。

迁移脚本：`docs/migrations/20261103-add-ai-canvas.sql`，已同步 `jokester.admin.sql`（Docker 空库初始化依赖它）。新版 API 读取 `media_asset` 新列，必须先迁移再部署。

### 9.2 画布接口

全部要求登录；跨用户与不存在统一 404；响应时间为带 `Z` 的 ISO 8601。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/ai/canvases?page=&pageSize=` | 列表摘要：`canvasId, title, coverUrl, storageBytes, nodeCount, revision, createdAt, updatedAt`，另返回 `totalStorageBytes` |
| POST | `/api/ai/canvases` | 创建：`{ title? }`，默认“无限画布 N”；返回完整项目 |
| GET | `/api/ai/canvases/{canvasId}` | 详情：摘要 + `document` |
| PUT | `/api/ai/canvases/{canvasId}/document` | 保存：`{ baseRevision, document }` → `{ revision, updatedAt, storageBytes, coverUrl }`；`baseRevision` 不等于当前值返回 `409 CANVAS_REVISION_CONFLICT` 并带 `currentRevision` |
| PATCH | `/api/ai/canvases/{canvasId}` | 重命名：`{ title }` |
| DELETE | `/api/ai/canvases/{canvasId}` | 软删除，不删除 Asset 和任务 |
| POST | `/api/ai/canvases/{canvasId}/duplicate` | 复制（P1） |

保存实现要点：

- `UPDATE … SET revision = revision + 1 … WHERE id = ? AND revision = ?`，影响行数为 0 即冲突；文档、引用索引、封面、体积在同一事务内更新。
- 画布写接口复用 `SecurityRateLimitMiddleware`，按用户 120 次/分钟；该中间件在 Redis 不可用时统一返回 503，画布保存与其它限流接口保持一致，前端按保存失败处理并保留本地草稿。
- 列表接口不返回 `document`。

### 9.3 生成结果登记为 Asset

`POST /api/assets/from-ai-result`，请求 `{ taskId, resultOrdinal }`，返回与上传接口一致的 Asset 结构（`assetId`、`width`、`height`、`thumbnailUrl` 等）。

- 任务必须属于当前用户、未删除、`status=1`，结果未被隔离（`is_quarantined=0`），否则 404。
- 幂等：已登记直接返回原 Asset；并发靠唯一键兜底。
- 存储：**复用原结果文件**（`storage_key` 指向结果所在的私有文件），只额外生成缩略图并计算 SHA-256，不复制原图。现有 `DeleteOwnedAsync` 会物理删除 Asset 文件，因此对 `source_task_id` 非空的生成型 Asset 只删除缩略图，原图仍归生图历史所有；删除后再次登记恢复同一 `assetId` 并重建缩略图。
- 定位：size-mode-v1 任务按 `ai_image_task_result(task_id, result_ordinal)`；没有逐图结果事实的 legacy 任务按 `result_urls` JSON 顺序。结果 URL 必须以 `/api/media/ai/{当前用户ID}/` 开头。
- 如果后续希望减少一次请求，可以让 Worker 在结果落盘时直接登记（P1），接口保留作为历史结果补登记。

### 9.4 复用的现有接口

| 能力 | 接口 |
| --- | --- |
| 上传参考图 | `POST /api/ai/images/upload`（产出 `media_asset`，`asset_type=reference`） |
| 模型能力 | `GET /api/ai/images/models` |
| 价格 | `GET /api/ai/images/pricing-options`（v2，`modelCode + catalogVersion`） |
| 创建生成 | `POST /api/ai/images`（`referenceAssetIds`、`idempotencyKey`、`size-mode-v1` 头） |
| 任务状态 | `GET /api/ai/images/{id}` |
| 生成历史 | `GET /api/ai/images` |
| 素材读取 | `GET /api/assets/{assetId}/content`、`/thumbnail` |
| 余额 | 现有积分接口 |

生成接口不增加画布专属分支。若 P1 接入 `canvasProjectId`，只做所有权校验和落库，不影响指纹、计费和路由。

### 9.5 体积口径

`storageBytes` = 文档字节数 + 该画布引用的、`asset_type=reference`（用户上传）的去重 Asset 字节数。生成结果已计入生图历史，不重复计入画布体积。列表页总量对用户全部画布引用的上传 Asset 去重后求和（同一张图被多个画布引用只算一次）。

### 9.6 生命周期与清理

- 账户删除：`AccountDeletionService` 增加软删除用户全部画布与引用索引的步骤，位于私有媒体清理之前。
- 删除 Asset：允许删除；画布中对应节点显示“素材已失效”，下次保存时引用索引自然更新。
- 删除生图任务：不影响已登记的生成型 Asset。
- 软删除画布保留 30 天后由后台任务物理清理（P1，首期仅软删除）。

### 9.7 默认上限（待产品确认）

| 项 | 建议值 |
| --- | --- |
| 每用户未删除画布数 | 50 |
| 每画布节点数 | 300 |
| 每画布连线数 | 600 |
| 文档大小 | 1 MB |
| 单个生图节点输入 | 6（与参考图上限一致） |
| `runs` 历史保留 | 20 条 |

上限放在配置 `AiCanvas` 节（`MaxProjectsPerUser`、`MaxNodesPerProject`、`MaxConnectionsPerProject`、`MaxDocumentBytes`），缺省使用上表值，超出安全范围时启动失败；单节点输入 6 与 `runs` 20 条为代码常量。超限返回 `422 CANVAS_LIMIT_EXCEEDED` 并带 `limit` 名称。

## 10. 错误契约

| HTTP | 机器码 | 场景 | 前端表现 |
| --- | --- | --- | --- |
| 404 | `NOT_FOUND` | 画布 / Asset / 任务不存在或不属于当前用户 | 列表页提示“画布不存在”；节点显示素材失效 |
| 409 | `CANVAS_REVISION_CONFLICT` | 其它标签页或设备已保存新版本 | 弹窗：“加载最新版本”或“用当前内容覆盖”（覆盖 = 以 `currentRevision` 重新提交） |
| 422 | `CANVAS_DOCUMENT_TOO_LARGE` / `CANVAS_LIMIT_EXCEEDED` | 超出上限，`details` 含 `limit` 与 `value` | 提示具体上限，阻止继续添加 |
| 400 | `VALIDATION_ERROR` | 文档结构非法，`message` 含字段路径 | 记录前端错误日志，提示保存失败 |
| 429 | `RATE_LIMITED` | 保存过于频繁 | 退避后自动重试 |
| 503 | `SERVICE_UNAVAILABLE` | 限流服务（Redis）不可用 | 保留本地草稿，稍后重试 |
| 生成相关 | 现有 `PROMPT_BLOCKED`、`AI_CONSENT_REQUIRED`、积分不足、`503` 等 | 与无限绘画一致 | 在生图节点上显示 |

## 11. 安全与隐私

1. 所有画布接口按 `owner_user_id` 过滤，超级管理员在用户端也只看自己的画布（管理端查看他人画布不在首期范围）。
2. 画布文档不得持有 URL，前端渲染图片只拼接本站 `/api/assets/...`，杜绝 SSRF 和外链追踪。
3. 文本按纯文本渲染，标题和提示词不使用 `v-html`。
4. 保存接口不记录文档正文到操作日志，只记录 `canvasId`、`revision`、字节数。
5. 生成时提示词过滤、consent 校验、Asset 所有权校验全部由现有生成链路执行，画布侧不做旁路。

## 12. 分期

| 阶段 | 范围 |
| --- | --- |
| P0（MVP） | 列表页、创建/重命名/删除；编辑器平移缩放、图片/文本/生图节点、连线与断开、上传与从画廊插入；生成面板（模型、质量、分辨率、比例、张数、积分预估、`@` 引用）；结果登记 Asset 与链式生成；自动保存与冲突处理；撤销重做；基础快捷键；账户删除清理 |
| P1 | 导出 zip、复制画布、小地图、整理布局、背景样式、图片组主图切换、“无限画布”页签、任务 `canvasProjectId` 来源标记、Worker 自动登记结果 Asset、软删除画布定期清理 |
| P2 | 画布助手、图片编辑工具、分享/只读链接、移动端 |

## 13. 实施影响

### 13.1 后端

| 模块 | 变更 |
| --- | --- |
| `Domain/Entities` | `AiCanvasProjectEntity`、`AiCanvasProjectAssetEntity`；`MediaAssetEntity` 增加来源字段 |
| `Application/Services` | `AiCanvasService`（CRUD、保存、文档校验、引用索引、封面与体积）；`MediaAssetService` 增加结果登记 |
| `Controllers` | `AiCanvasesController`；`AssetsController` 增加 `from-ai-result` |
| `AccountDeletionService` | 清理画布 |
| 配置 | `AiCanvas` 上限节 |
| SQL | 迁移脚本 + `jokester.admin.sql` 同步 |
| 文档 | `integration-guide.md` 增加画布接口；`AGENTS.md` 增加画布契约摘要 |

数据访问使用 SqlSugar + Mapster，不写回手工 SQL 风格。

### 13.2 前端（`Jokester-Ai-Image`）

- 新路由 `/canvas`、`/canvas/:canvasId`，导航项与 i18n。
- 画布渲染建议评估 Vue Flow（MIT）或自研 SVG/Canvas 层；节点数上限 300 下 DOM 方案可接受。
- 生成面板复用 `/playground` 现有的模型能力、价格、consent、轮询逻辑，抽成可复用 composable，避免两套计费展示。
- 本地 IndexedDB 只存未保存草稿，不存图片。

### 13.3 参考项目许可

`infinite-canvas` / `infinite-canvas-hufakai` 为 MIT 许可，README 另要求“二次开发保留原作者信息和前端页面标识”。本项目前端为 Vue，建议只参考交互、独立实现；如直接移植任何代码或素材（图标、样式），需保留 MIT 版权声明并由产品确认署名要求。

## 14. 待决策项

| 编号 | 问题 | 建议 |
| --- | --- | --- |
| D1 | 画布存服务端还是仅存浏览器本地 | 存服务端（见第 2 节） |
| D2 | 截图中的 `5:4`、`4:5` 是否要支持 | 首期不显示；如需支持，另走参数目录 + GPT 尺寸算法验证 + 价格确认，按模型能力发布 |
| D3 | 第 9.7 节各项上限 | 按建议值上线后观察 |
| D4 | 结果登记 Asset 复用原文件还是复制 | 已实施：复用原文件，删除生成型 Asset 不删原图（见 9.3） |
| D5 | 体积口径是否计入生成结果 | 不计入（见 9.5） |
| D6 | 画布接口是否需要新菜单权限码 | 不需要，登录即可，与收藏、Asset 接口一致；生成能力仍受 `AiImage.Generate` 约束 |
| D7 | 是否在 `ai_image_task` 记录 `canvasProjectId` | P1 再加 |
| D8 | “我的画廊”现有 `/images` 页面是否同步展示画布来源 | P1 随 D7 一起做 |
| D9 | 画布名称默认值与是否过滤敏感词 | 默认“无限画布 N”；名称不对外展示，首期不过滤 |

## 15. 验收标准

### 15.1 后端测试

1. 创建、读取、重命名、删除画布；跨用户访问四个接口均返回 404。
2. 保存成功 `revision` 递增；以旧 `baseRevision` 保存返回 409 且文档未变；并发两次保存只有一次成功。
3. 图片字段填 URL、未知字段、未知节点类型、悬空连线、环、超限节点数、超限字节数时分别被拒绝。
4. 文档引用他人 Asset 或已删除 Asset 返回 404；引用索引与文档中的 `assetId` 集合一致。
5. 结果登记：成功任务返回 Asset；重复调用返回同一 `assetId`；他人任务、失败任务、隔离结果返回 404；并发调用只产生一行。
6. 登记出的 Asset 可作为 `POST /api/ai/images` 的 `referenceAssetIds`，Worker 输入快照校验通过。
7. 账户删除后画布全部不可见。
8. `jokester.admin.sql` 空库初始化与迁移脚本升级后表结构一致。

### 15.2 前端 E2E

1. 新建画布 → 上传图片 → 连线 → 生成 → 结果出现 → 结果再连出生图节点并成功生成。
2. 生成中刷新页面，节点恢复轮询并最终显示结果；重试不产生第二次扣分（同 `idempotencyKey`）。
3. 模型不支持 auto 时宽高比不出现 Auto；切换模型后非法参数被重置为合法默认值。
4. 积分预估与 `pricing-options` 一致；余额不足时去充值再返回，画布状态保留。
5. 两个标签页编辑同一画布，后保存的一方看到冲突弹窗。
6. 提示词被拦截时只显示通用文案。
7. 删除被引用的上传素材后，节点显示素材失效且无法作为输入。

### 15.3 产品验收

- 截图 1–3 中的 P0 元素全部具备（`5:4`、`4:5` 按 D2 结论处理）。
- 300 节点画布在主流桌面浏览器平移缩放流畅（目标 ≥ 50 FPS），打开时间 < 2 秒（不含图片加载）。

## 16. 监控

- 保存成功率、409 冲突率、保存耗时 P95、文档大小分布。
- 画布来源生成量（P1，依赖 D7）、链式生成深度分布。
- 结果登记接口调用量与失败原因。
