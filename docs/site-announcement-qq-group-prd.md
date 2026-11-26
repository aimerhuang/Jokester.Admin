# 站内公告与 QQ 群引流弹窗 PRD

## 1. 文档信息

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v0.2 |
| 日期 | 2026-11-26 |
| 需求状态 | Web 与 API 已实现，2026-11-26 完成本地验收。上线顺序见 `docs/runbook.md` |
| 适用端 | 用户 Web（`Jokester-Ai-Image`）、Jokester Admin API；Android / iOS 首期不接入 |
| 后端基线 | 超管接口写法参照 `AdminUsersController`、`AiImagePricingAdminService`；图片校验复用 `ImageUploadValidator`；公开静态前缀参照 `/avatar`、`/blog` |
| 前端基线 | `AppLayout.vue` 顶栏（`topbar-actions`）与侧边导航、`stores/auth.ts`、`router/index.ts` 的 `requiresSuperAdmin` |
| 关联契约 | [integration-guide.md](./integration-guide.md)、[user-admin-prd.md](./user-admin-prd.md)、[ai-pricing-vip-prd.md](./ai-pricing-vip-prd.md)、[docker-deploy.md](./docker-deploy.md) |

## 2. 推荐结论

1. **两个功能共用一个超管后台页 `/admin/announcements`**，分“公告”“QQ 群弹窗”两个页签；后端各一张表，互不依赖，可以分开上线。
2. **QQ 群配置存数据库，不放 `api.env`。** 改群号不需要重新部署；群满换群时把“提醒版本”加一，点过“不再提醒”的用户会再看到一次。
3. **“不再提醒”和公告已读状态首期存浏览器本地（按用户 ID 分键）。** 不建用户维度的表，账号删除流程不用改。代价是换设备或清缓存后弹窗会再出现一次、红点会再亮一次。
4. **公告正文首期只支持纯文本，外加一张封面图和一个跳转按钮。** 前端没有 Markdown 依赖，富文本需要另做 XSS 过滤；截图里的两类公告（新模型上线、会员优惠）用“封面 + 摘要 + 正文 + 按钮”已能表达。
5. **弹窗不是每次登录都弹。** 点“稍后再说”后默认 3 天内不再出现（后台可配 1–30 天），避免每次打开页面都被打断。
6. **两个功能都不能影响主流程。** 接口失败时不弹窗、不亮红点，不报错提示。

## 3. 背景与现状

- 站内没有任何公告渠道：新模型上线、价格调整、活动只能靠用户自己发现；没有官方社群入口。
- 前端顶栏（`AppLayout.vue`）现有：签到、积分 / 充值、语言切换、主题切换，没有通知入口。
- 后端没有公告或站点级运营配置表；`MobileController` 的配置来自部署配置，不适合运营随时修改。
- 公开图片目前只有 `/avatar`、`/blog`、`/prompt-images` 三个静态前缀；AI 图片在 `private-media` 下需要带 JWT，不适合做公告封面。
- 参考截图来自同类产品。截图里的群名“无限AI官方交流群”和群号 `517146250` 不是本项目数据，迁移脚本不写入任何群号，弹窗默认关闭，由超管配置后开启。

## 4. 目标与非目标

### 4.1 目标

1. 用户登录后看到 QQ 群引流弹窗，可复制群号、跳转加群、稍后再说、不再提醒。
2. 超管可在后台修改群名、群号、加群链接、弹窗文案和开关，保存后 1 分钟内生效。
3. 顶栏右上角有铃铛，有新公告时亮红点；点击从右侧滑出公告栏，按卡片展示公告。
4. 超管可在后台新增、编辑、发布、下线、置顶、删除公告，并上传封面图。

### 4.2 非目标（首期）

- 服务端已读记录、已读人数统计、弹窗曝光 / 点击统计。
- Markdown / 富文本正文、公告内多图、视频。
- 定时发布、自动过期下线。
- 页面顶部的重要公告横幅（截图中标题下方那一条）、重要公告自动弹出。
- 按用户分组定向（VIP / 新用户 / 平台）。
- 中英双语公告内容。
- 微信群、Discord 等其他社群；群二维码图片。
- Android / iOS 客户端接入；登录页、注册页展示公告。
- 把公告管理授权给非超级管理员角色。

## 5. 用户与权限

| 角色 | 弹窗 / 公告栏 | 后台管理 |
| --- | --- | --- |
| 未登录 | 不展示；接口 401 | 401 |
| 普通登录用户 | 可见 | 导航无入口；接口 403 |
| 超级管理员 | 可见（与普通用户相同，便于验收） | 可读可写 |

- 用户侧接口只要求登录，不新增菜单权限码（做法同 `GET /api/ai/images/model-status`）。
- 后台接口按超级管理员身份判断，不新增菜单权限码。读接口校验 Token 中的超管声明；写接口在事务内锁定并复核操作者仍是启用、未删除的超级管理员（做法同 `/api/admin/users`、`/api/ai/model-pricing`）。
- 前端入口用 `auth.user.isSuperAdmin` 控制，路由加 `requiresSuperAdmin`；鉴权以后端为准。

## 6. 功能需求：QQ 群引流弹窗

### 6.1 弹窗内容

按截图 1 的布局，自上而下：

| 元素 | 来源 | 说明 |
| --- | --- | --- |
| 图标 | 前端固定 | 聊天气泡图标 |
| 标题 | 后台配置 | 默认“加入 QQ 交流群” |
| 说明 | 后台配置 | 默认“加入官方 QQ 交流群，与更多 AI 绘画爱好者交流技巧、分享作品，并获取最新功能更新和活动资讯。” |
| 群名 | 后台配置 | |
| 群号 | 后台配置 | 显示为“群号：xxxxxxxxx” |
| “复制群号” | 前端固定 | 复制成功提示“群号已复制”；剪贴板不可用时提示手动复制，群号文本可选中 |
| “稍后再说” | 前端固定 | 次按钮 |
| “立即加入” | 前端固定 | 主按钮 |
| “不再提醒” | 前端固定 | 复选框，默认不勾选 |
| 右上角 × | 前端固定 | 等同“稍后再说” |

按钮、复选框文案进 `locales/zh-CN.ts`、`en-US.ts`；后台配置的文案原样显示，不翻译。

### 6.2 触发时机

进入带顶栏的应用主框架（`AppLayout`）且已登录时判断一次，包括两种情况：刚登录成功跳转进来；带着有效会话重新打开或刷新页面。每次页面加载最多弹一次。

不弹的情况：

- 后台开关关闭，或群号为空。
- 配置接口失败或超时。
- 当前路由是画布编辑器（`/canvas/:id`）或充值页（`/recharge`），这两个页面不在 `AppLayout` 内。
- 页面上已有其他弹窗打开。本次跳过，下次进入再判断，不排队。
- 命中 6.3 的本地抑制规则。

主框架加载完成后延迟约 800ms 再弹，避免和首屏渲染抢焦点。

### 6.3 用户操作与再次出现规则

状态存 `localStorage`，键 `community.qqPrompt.<userId>`，内容为 `{ promptVersion, action, at }`。

| 用户操作 | 记录 | 之后 |
| --- | --- | --- |
| 点“立即加入” | `joined` | 当前提醒版本内不再弹 |
| 勾选“不再提醒”后点“稍后再说”或 × | `never` | 当前提醒版本内不再弹 |
| 未勾选，点“稍后再说”、× 或按 ESC | `later` | `remindIntervalDays` 天后再弹（默认 3） |
| 点“复制群号” | 不记录 | 弹窗保持打开 |

- 点击遮罩不关闭弹窗，防止误触后被记为“稍后再说”。
- 后台把提醒版本加一后，本地记录的 `promptVersion` 与服务端不一致，视为没有记录，所有用户重新弹一次。
- 同一浏览器切换账号时按各自的用户 ID 分别记录。

### 6.4 “立即加入”

- 配置了加群链接：新标签页打开（`target="_blank"`，`rel="noopener noreferrer"`），弹窗关闭。
- 没配加群链接：主按钮文案改为“复制群号”，点击后复制并关闭弹窗，同样记为 `joined`；卡片内的小“复制群号”按钮隐藏，避免两个按钮做同一件事。

### 6.5 常驻入口

用户关掉弹窗后还要能找到群。公告栏底部固定一行“官方 QQ 群：xxxxxxxxx”和“复制”“加入”两个操作（见 7.3）。后台开关关闭时这一行也不显示。

## 7. 功能需求：公告铃铛与公告栏

### 7.1 铃铛

- 位置：顶栏 `topbar-actions` 内，积分 / 充值按钮之后、语言切换之前。窄屏下保留，不折叠进菜单。
- 有未读时铃铛右上角显示红点，不显示数字。
- 没有任何已发布公告时铃铛仍显示，点开是空状态。
- 无障碍：按钮带 `aria-label`“更新公告”；有未读时读作“更新公告，有新内容”。

### 7.2 未读判断

- 列表接口返回 `latestPublishedAt`（全部已发布公告里最晚的发布时间）。
- 本地记录 `announcements.lastSeenAt.<userId>`。`latestPublishedAt` 晚于本地记录，或本地没有记录且存在公告时，亮红点。
- 打开公告栏时把本地记录更新为本次响应里的 `latestPublishedAt`（用服务端时间，不用浏览器时钟），红点消失。
- 本次打开期间，发布时间晚于“打开前的本地记录”的卡片显示“NEW”角标；关闭后再打开不再显示。
- 编辑已发布公告不改发布时间，不会重新亮红点。
- 进入主框架时请求一次；页面从后台切回前台且距上次请求超过 5 分钟时再请求一次。不做轮询。

### 7.3 公告栏

从右侧滑出的抽屉，桌面宽 420px，窄屏全宽。按截图 2：

- 头部：图标、标题“更新公告”、副标题“了解最近新增与优化的功能”、关闭按钮。
- 列表：公告卡片，置顶在前，其余按发布时间倒序。首次加载 20 条，滚动到底加载下一页。
- 底部固定：QQ 群常驻入口（6.5）。
- 空状态：“暂无公告”。加载失败：“加载失败”与“重试”按钮。

卡片内容：

| 元素 | 说明 |
| --- | --- |
| 封面图 | 可选；固定 20:9 裁切显示（`object-fit: cover`），懒加载；加载失败时隐藏图片区域 |
| 标签行 | 依次为“置顶”“重要”、分类（新功能 / 活动 / 维护 / 通知）、版本号（如 `v2.3`，可选）；右侧为发布日期 `YYYY-MM-DD`（浏览器本地时区） |
| 标题 | 最多 2 行，超出省略 |
| 摘要 | 最多 2 行，超出省略 |
| 箭头 | 有正文或跳转按钮时显示，表示可点开 |

### 7.4 公告详情

点击卡片在抽屉内切换到详情视图，顶部有“返回”：

- 封面图（完整宽度）、标签行、标题、发布日期。
- 正文：纯文本，保留换行（`white-space: pre-wrap`），用文本插值渲染，不用 `v-html`。
- 跳转按钮（可选）：文案由后台填写。站内路径用路由跳转并关闭抽屉；外部链接新标签页打开。
- 没有正文也没有跳转按钮的公告，卡片不可点击。
- 公告在查看期间被下线：详情接口返回 404，提示“公告已下线”并返回列表、刷新列表。

## 8. 功能需求：管理后台（`/admin/announcements`）

侧边导航在“用户管理”下方增加“公告管理”，`superAdminOnly`。页面两个页签。

### 8.1 公告列表页签

| 列 | 内容 |
| --- | --- |
| 封面 | 缩略图，无则留空 |
| 标题 | 标题 + 标签（置顶 / 重要 / 分类 / 版本号） |
| 状态 | 草稿（灰）/ 已发布（绿）/ 已下线（黄） |
| 发布时间 | 未发布显示“—” |
| 最后修改 | 时间与操作人 |
| 操作 | 编辑、发布 / 下线、置顶 / 取消置顶、删除 |

- 筛选：状态（全部 / 草稿 / 已发布 / 已下线）、标题关键字。默认按最后修改时间倒序，每页 20 条。
- 右上角“新建公告”。

### 8.2 公告表单

| 字段 | 规则 |
| --- | --- |
| 标题 | 必填，1–80 字符 |
| 摘要 | 必填，1–200 字符；显示在卡片上 |
| 正文 | 选填，≤5000 字符，纯文本 |
| 封面图 | 选填；PNG / JPEG / WebP，≤2MB，建议 1280×576；只能通过本页上传，不能填外部图片地址 |
| 分类 | 必选：新功能 `feature` / 活动 `activity` / 维护 `maintenance` / 通知 `notice` |
| 版本号 | 选填，≤20 字符，如 `v2.3` |
| 重要 | 开关 |
| 置顶 | 开关；已发布的置顶公告最多 3 条 |
| 跳转按钮 | 选填；文案（≤20 字符）与链接必须同时填写或同时为空。链接为站内路径（以单个 `/` 开头）或 `https://` 绝对地址 |

- 表单右侧实时预览卡片效果。
- 底部按钮：“保存草稿”“发布”。编辑已发布公告时只有“保存”，保存后立即对用户生效（最长 1 分钟）。
- 发布、下线、删除需二次确认。发布确认文案：“发布后所有登录用户的铃铛会亮起红点。”

### 8.3 公告状态规则

| 操作 | 规则 |
| --- | --- |
| 草稿 → 已发布 | 发布时间记为当前时间 |
| 已发布 → 已下线 | 用户侧立即不可见；发布时间保留 |
| 已下线 → 已发布 | 沿用原发布时间，不重新亮红点 |
| 删除 | 只能删除草稿和已下线的公告，软删除；删除已发布的返回 409，需先下线 |
| 置顶第 4 条已发布公告 | 409，提示“最多置顶 3 条” |

### 8.4 QQ 群弹窗页签

| 字段 | 规则 |
| --- | --- |
| 启用弹窗 | 开关；群号为空时不能开启 |
| 群名 | 开启时必填，1–50 字符 |
| 群号 | 开启时必填，5–12 位数字 |
| 加群链接 | 选填；`https://`，主机名限 `qm.qq.com`、`qun.qq.com`、`jq.qq.com` |
| 弹窗标题 | 必填，1–40 字符 |
| 弹窗说明 | 必填，1–200 字符 |
| 再次提醒间隔 | 1–30 天，默认 3 |
| 重新提醒所有用户 | 复选框，默认不勾；勾选后保存会把提醒版本加一。修改群号时默认勾上并提示原因，可手动取消 |

- “预览弹窗”按钮：用当前表单内容打开弹窗，不写本地记录、不保存。
- 页面显示最后修改时间和操作人。

## 9. 后端设计

### 9.1 接口

**用户侧**（登录即可）：

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/announcements` | 已发布公告列表 |
| `GET /api/announcements/{id}` | 公告详情；未发布、已下线、已删除统一 404 |
| `GET /api/community/qq-group` | QQ 群弹窗配置 |

`GET /api/announcements`，查询参数 `pageIndex`（默认 1）、`pageSize`（1–50，默认 20）。排序 `is_pinned DESC, published_at DESC, id DESC`。

```json
{
  "total": 12,
  "pageIndex": 1,
  "pageSize": 20,
  "latestPublishedAt": "2026-11-11T02:00:00Z",
  "items": [
    {
      "id": 7,
      "title": "Image 2.6 新模型上线通知",
      "summary": "平台现已上线全新图像生成模型。",
      "coverUrl": "/site-media/announcement/202611/3f2a…c1.webp",
      "category": "feature",
      "versionLabel": "v2.3",
      "isPinned": true,
      "isImportant": true,
      "hasDetail": true,
      "publishedAt": "2026-11-11T02:00:00Z"
    }
  ]
}
```

- 列表不返回正文；`hasDetail` 表示有正文或跳转按钮。
- 项目的全局 JSON 配置会省略值为 `null` 的字段：没有封面、版本号时 `coverUrl`、`versionLabel` 不出现；没有任何已发布公告时 `latestPublishedAt` 不出现。客户端按“缺失即空”处理。
- 详情在列表字段之外返回 `content`、`linkUrl`、`linkText`。
- 响应不含创建人、修改人、`revision`、状态。

`GET /api/community/qq-group`：

```json
{
  "enabled": true,
  "promptVersion": 3,
  "title": "加入 QQ 交流群",
  "description": "加入官方 QQ 交流群，……",
  "groupName": "……",
  "groupNumber": "123456789",
  "joinUrl": "https://qm.qq.com/q/xxxx",
  "remindIntervalDays": 3
}
```

关闭时只返回 `{ "enabled": false }`，不带群号。

**后台**（超级管理员）：

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/admin/announcements` | 全部状态的列表；参数 `pageIndex`、`pageSize`（1–100）、`status`、`keyword`（≤80 字符） |
| `GET /api/admin/announcements/{id}` | 详情，含 `revision`、状态、创建 / 修改人 |
| `POST /api/admin/announcements` | 新建；`status` 传 `0`（草稿）或 `1`（直接发布） |
| `PUT /api/admin/announcements/{id}` | 修改内容与置顶 / 重要标记；必须带 `revision` |
| `PUT /api/admin/announcements/{id}/status` | 发布 / 下线；请求体 `{ "status": 1, "revision": 4 }` |
| `DELETE /api/admin/announcements/{id}` | 软删除；参数 `revision` |
| `POST /api/admin/announcements/images` | 上传封面，`multipart/form-data` 字段 `file`；返回 `{ "url": "/site-media/announcement/…" }` |
| `GET /api/admin/community/qq-group` | 读取配置，含 `revision`、修改时间、修改人 |
| `PUT /api/admin/community/qq-group` | 保存配置；带 `revision` 与 `bumpPromptVersion` |

- 写接口用 `revision` 做乐观并发，冲突返回 409。
- `coverUrl` 只接受本服务上传接口返回的 `/site-media/announcement/` 路径，并检查文件存在。
- 限流：`/api/admin/announcements`、`/api/admin/community` 下的 POST / PUT / DELETE 按用户和 IP 各 30 次 / 分钟，加入 `SecurityRateLimitMiddleware`。

### 9.2 数据表

迁移 `docs/migrations/20261126-add-site-announcement.sql`，同步 `jokester.admin.sql`。新表时间列存 UTC。

```sql
CREATE TABLE `site_announcement` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `title` VARCHAR(80) NOT NULL,
  `summary` VARCHAR(200) NOT NULL,
  `content` TEXT DEFAULT NULL COMMENT '纯文本',
  `cover_url` VARCHAR(255) DEFAULT NULL,
  `category` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'notice',
  `version_label` VARCHAR(20) DEFAULT NULL,
  `is_pinned` TINYINT(1) NOT NULL DEFAULT 0,
  `is_important` TINYINT(1) NOT NULL DEFAULT 0,
  `link_url` VARCHAR(500) DEFAULT NULL,
  `link_text` VARCHAR(20) DEFAULT NULL,
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '0草稿 1已发布 2已下线',
  `published_at` DATETIME(6) DEFAULT NULL COMMENT 'UTC，首次发布时间',
  `revision` INT NOT NULL DEFAULT 0,
  `created_by` BIGINT NOT NULL,
  `updated_by` BIGINT DEFAULT NULL,
  `created_at` DATETIME NOT NULL COMMENT 'UTC',
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_site_announcement_feed` (`is_deleted`, `status`, `is_pinned`, `published_at`, `id`),
  CONSTRAINT `chk_site_announcement_category` CHECK (`category` IN ('feature', 'activity', 'maintenance', 'notice')),
  CONSTRAINT `chk_site_announcement_status` CHECK (`status` IN (0, 1, 2))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='站内公告';

CREATE TABLE `site_community_setting` (
  `id` TINYINT NOT NULL,
  `qq_popup_enabled` TINYINT(1) NOT NULL DEFAULT 0,
  `qq_group_name` VARCHAR(50) NOT NULL DEFAULT '',
  `qq_group_number` VARCHAR(12) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT '',
  `qq_join_url` VARCHAR(500) DEFAULT NULL,
  `popup_title` VARCHAR(40) NOT NULL,
  `popup_description` VARCHAR(200) NOT NULL,
  `remind_interval_days` TINYINT NOT NULL DEFAULT 3,
  `prompt_version` INT NOT NULL DEFAULT 1,
  `revision` INT NOT NULL DEFAULT 0,
  `updated_by` BIGINT DEFAULT NULL,
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  PRIMARY KEY (`id`),
  CONSTRAINT `chk_site_community_setting_id` CHECK (`id` = 1),
  CONSTRAINT `chk_site_community_setting_interval` CHECK (`remind_interval_days` BETWEEN 1 AND 30)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='社群引流配置（单行）';
```

迁移插入 `site_community_setting` 的 `id=1` 行：弹窗关闭、群名群号为空、标题和说明用 6.1 的默认文案。不插入任何公告。

单行配置表的做法与 `ai_image_pricing_setting` 一致。

### 9.3 封面图存储

- 新增公开静态前缀 `/site-media`，文件落在公开媒体根目录的 `site-media/announcement/{yyyyMM}/{guid}{ext}`，挂载方式同 `/avatar`、`/blog`，响应头 `Cache-Control: public, max-age=604800, immutable`（文件名是随机 GUID，不会被覆盖）。
- 上传走 `ImageUploadValidator`（解码图片内容来识别格式，不信任扩展名和 `Content-Type`），上限 2MB；该校验器也认 GIF，封面接口在其结果上只放行 PNG / JPEG / WebP。
- 不用 `media_asset`：它是用户私有素材，读取要带 JWT 并校验所有者。
- 换封面或删除公告后旧文件不删除，首期不做孤儿文件清理（超管手动上传，量很小）。

### 9.4 缓存

- 公告列表和用户侧群配置使用 60 秒进程内快照，写事务提交后本实例立即失效。公告详情按 ID、状态和软删除标志直接查库。
- 多实例部署时，其他实例最长 60 秒后看到变更，这是“1 分钟内生效”的来源。
- 读取失败时按现有全局异常处理返回 5xx，不返回过期缓存；前端按“无弹窗、无红点”处理。

### 9.5 服务与代码落点

- 新增：`AnnouncementsController`、`CommunityController`、`AdminAnnouncementsController`、`AdminCommunityController`；`IAnnouncementService` / `AnnouncementAdminService`、`CommunitySettingService`；`SiteAnnouncementEntity`、`SiteCommunitySettingEntity`；DTO；`Program.cs` 的 `/site-media` 静态挂载；限流规则。
- 数据访问用 `SqlSugar`，DTO 映射用 `Mapster`。
- 不改：认证、积分、生图、账户删除流程（没有用户维度的数据）。

### 9.6 时间

DTO 中的时间为带 `Z` 的 ISO 8601。发布日期由前端按浏览器本地时区格式化为 `YYYY-MM-DD`。

## 10. 错误契约

| HTTP | 机器码 | 场景 |
| --- | --- | --- |
| 401 | `UNAUTHORIZED` | 未登录 |
| 403 | `RESOURCE_FORBIDDEN` | 非超级管理员调用后台接口；事务内复核发现已不是启用的超级管理员 |
| 404 | `RESOURCE_NOT_FOUND` | 公告不存在、未发布、已下线或已删除（用户侧）；公告不存在或已删除（后台） |
| 400 | `VALIDATION_ERROR` | 字段不合法；跳转按钮文案和链接只填了一个；链接或加群链接不符合规则；开启弹窗但群号为空 |
| 409 | `ANNOUNCEMENT_REVISION_CONFLICT`（新增） | 公告 `revision` 不匹配 |
| 409 | `COMMUNITY_SETTING_REVISION_CONFLICT`（新增） | QQ 群配置 `revision` 不匹配 |
| 409 | `CONFLICT` | 删除已发布公告；置顶超过 3 条 |
| 429 | `RATE_LIMITED` | 限流 |

## 11. 安全与隐私

1. 公告标题、摘要、正文和弹窗文案一律按纯文本渲染，前端不使用 `v-html`。
2. 跳转链接只接受站内路径（以单个 `/` 开头，拒绝 `//`、反斜杠）或 `https://` 绝对地址；`javascript:`、`data:`、`http://` 一律拒绝。外部链接新标签页打开并带 `rel="noopener noreferrer"`。
3. 加群链接限定 QQ 官方域名。超管账号被盗后可以把用户引到别的群，域名白名单只能防止引到任意网站；防护依赖登录侧的限流与二次验证。
4. 封面图不接受外部地址，避免混入追踪像素或被外站替换内容。
5. 公告和 QQ 群号是面向全部登录用户的公开信息，不含个人数据；本需求不新增任何用户维度的存储。
6. 操作日志（`sys_operation_log`）记录谁在何时调用了哪个后台接口，不记录请求体；表内保留创建人、最后修改人。首期不建独立审计表（见第 13 节第 8 项）。

## 12. 分期

| 阶段 | 内容 |
| --- | --- |
| P0 | QQ 群弹窗与后台配置；铃铛、公告栏、公告详情；公告后台（增删改、发布 / 下线、置顶、封面上传）；公告栏底部 QQ 群常驻入口 |
| P1 | Markdown 正文；定时发布与到期自动下线；重要公告顶部横幅；“重新通知”选项（重新发布时刷新发布时间）；群二维码；弹窗与公告的曝光 / 点击统计 |
| P2 | 服务端已读记录与已读人数；按用户分组定向；双语内容；Android / iOS 接入；公告与群配置的变更审计表；自定义公告分类 |

## 13. 决策项

| # | 问题 | 建议 |
| --- | --- | --- |
| 1 | 是否有 QQ 群的邀请链接（`https://qm.qq.com/q/…`）？ | 运营事项，不阻塞开发：加群链接是选填字段，上线配置时填写。没有的话“立即加入”退化成复制群号，网页无法凭群号直接拉起加群 |
| 2 | 点“稍后再说”后多久再弹？ | **已确认（2026-11-26）**：3 天，后台可配 1–30 天 |
| 3 | “不再提醒”和已读状态存浏览器本地还是服务端？ | **已确认（2026-11-26）**：首期存浏览器本地；需要已读统计时再上服务端 |
| 4 | 公告正文用纯文本、Markdown 还是富文本？ | **已确认（2026-11-26）**：首期纯文本 + 封面 + 一个跳转按钮；Markdown 放 P1 |
| 5 | 重要公告是否自动弹出，或显示页面顶部横幅？ | **已确认（2026-11-26）**：首期只亮红点，不自动弹出；横幅放 P1 |
| 6 | 公告是否需要中英双语？ | **已确认（2026-11-26）**：首期单语，英文界面下原样显示 |
| 7 | 新注册用户首次登录是否立即弹 QQ 群弹窗？ | 按建议写入：弹。新用户是引流的主要对象（未单独确认，随规划评审一并确认） |
| 8 | 公告和群配置的修改是否需要独立审计表？ | **已确认（2026-11-26）**：首期不建，靠操作日志和“最后修改人”；修改前的值不留存，审计表放 P2 |
| 9 | 分类是否固定为新功能 / 活动 / 维护 / 通知四种？ | **已确认（2026-11-26）**：固定四种，不做分类管理 |
| 10 | 后台导航叫“公告管理”还是“运营配置”？ | 按建议写入：“公告管理”（未单独确认，随规划评审一并确认） |

## 14. 验收标准

### 14.1 后端测试

1. 未登录调用任一接口返回 401；普通用户调用 `/api/admin/announcements`、`/api/admin/community` 下任一接口返回 403；被降权但 Token 未过期的用户调用写接口返回 403。
2. 用户侧列表只含 `status=1` 且未删除的公告；草稿、已下线、已删除公告的详情返回 404。
3. 排序：置顶在前，其余按发布时间倒序；翻页无重复、无遗漏。
4. `latestPublishedAt` 等于全部已发布公告的最大发布时间，不受置顶和分页影响；没有公告时响应里没有该字段。
5. 编辑已发布公告不改变 `publishedAt`；下线后重新发布沿用原 `publishedAt`。
6. `revision` 不匹配返回对应的 409；删除已发布公告返回 409；置顶第 4 条已发布公告返回 409。
7. 链接校验：`javascript:alert(1)`、`//evil.example`、`http://example.com`、`data:` 被拒绝；`/pricing`、`https://example.com/a` 通过。加群链接主机名不在白名单时被拒绝。
8. 封面：外部地址、不存在的 `/site-media/` 路径被拒绝；上传非图片内容（扩展名伪装）、超过 2MB 被拒绝。
9. QQ 配置关闭时用户侧只返回 `enabled=false`，响应中搜索不到群号；群号为空时不能开启。
10. `bumpPromptVersion=true` 时 `promptVersion` 加一，否则不变。
11. 用户侧响应中搜索不到 `createdBy`、`updatedBy`、`revision`。
12. 按 `jokester.admin.sql` 初始化的新库与执行迁移后的旧库表结构一致；迁移可重复执行。

### 14.2 前端 E2E

1. 弹窗启用且无本地记录：登录后弹出，内容与配置一致。
2. 点“稍后再说”后刷新不再弹；把本地记录时间改到 4 天前再刷新，重新弹出。
3. 勾选“不再提醒”后关闭，刷新不再弹；服务端 `promptVersion` 加一后再刷新，重新弹出。
4. “复制群号”把群号写入剪贴板并提示；配置了加群链接时“立即加入”打开新标签页。
5. 配置接口返回 500 时不弹窗，页面无错误提示，其他功能正常。
6. 有比本地记录更新的公告时铃铛显示红点；打开公告栏后红点消失，刷新后仍不显示。
7. 公告栏内置顶公告排在最前；点击卡片进入详情，正文换行保留，含 `<script>` 的正文按文本显示。
8. 普通用户看不到“公告管理”，直接访问 `/admin/announcements` 被拦回。
9. 超管新建公告并发布后，普通用户刷新页面看到红点和新卡片；下线后卡片消失。
10. 画布编辑器和充值页不出现弹窗。

### 14.3 产品验收

1. 运营在后台修改群号并勾选“重新提醒所有用户”，1 分钟内，一个之前点过“不再提醒”的测试账号刷新页面后再次看到弹窗，显示新群号。
2. 运营从零开始，不接触数据库，5 分钟内发布一条带封面的公告，普通账号能在公告栏看到。

## 15. 实施影响

### 15.1 后端

- 新增：第 9.5 节所列的控制器、服务、实体、DTO；两个 `MachineErrorCodes`；限流规则；迁移脚本；`/site-media` 静态挂载。
- 修改：`jokester.admin.sql` 增加两张表和配置初始行；`docker/compose.yaml` 增加卷 `api-site-media:/app/wwwroot/site-media`，`docker/compose.production.yaml` 增加绑定挂载 `./data/site-media:/app/wwwroot/site-media`（与现有 `blog`、`avatar` 两行同样写法）。漏加挂载时封面图会在容器重建后丢失。
- 文档：实现后更新 `docs/integration-guide.md`、`docs/architecture.md`、`docs/runbook.md`，`AGENTS.md` 增加本契约小节并把本文加入 docs 列表。

### 15.2 前端（`Jokester-Ai-Image`）

- 新增：`components/common/AnnouncementBell.vue`、`AnnouncementDrawer.vue`、`QqGroupDialog.vue`；`stores/announcements.ts`；`services/announcements.ts`、`services/adminAnnouncements.ts`；视图 `AnnouncementAdminView.vue`；路由 `admin/announcements`（`requiresSuperAdmin`）；中英文案键；E2E 用例。
- 修改：`AppLayout.vue` 顶栏加铃铛、导航加一项、挂载弹窗；`vite.config.ts` 开发代理与 `docker/nginx/templates/default.conf.template` 增加 `/site-media/` 转发到 API。

### 15.3 部署

1. 执行 `20261126-add-site-announcement.sql`。
2. 部署 API（含新卷）。旧版前端不调用新接口，不受影响。
3. 部署 Web（含 nginx `/site-media/` 转发）。在此之前封面图地址会被 nginx 的 `try_files` 兜底成 `index.html`，图片显示不出来；公告后台本身随 Web 一起上线，所以不会提前有带封面的公告。
4. 超管在后台填写 QQ 群配置并开启弹窗；发布第一条公告。

回滚：先关闭弹窗开关、下线全部公告，再按需回退 Web 和 API；两张新表可保留。

## 16. 本地验证记录

2026-11-26，后端构建及 77 项定向测试通过，前端构建及 16 项定向 Playwright 测试通过。另用独立 MySQL、Redis、API 和 Web 对接真实浏览器验证了发布、上传封面、红点与 QQ 提醒。新库与升级库的两张表结构一致，重复迁移保留配置，容器重建后封面仍可读取。[逐项验收记录](../.trellis/tasks/11-26-site-announcement-qq-group/acceptance.md)
