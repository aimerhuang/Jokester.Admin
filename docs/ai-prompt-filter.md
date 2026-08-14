# AI 生图提示词关键词过滤

## 部署

现有数据库按顺序执行：

```text
docs/migrations/20260811-add-ai-prompt-sensitive-words.sql
docs/migrations/20260811-expand-ai-prompt-sensitive-words-houbb.sql
```

第一条迁移创建中英文敏感词表、词库版本表、初始规则和管理权限，并给
`ai_image_task` 增加最近审核版本与时间字段。第二条迁移导入人工重分类的
houbb 禁用候选、启用项目自维护补充词，并调整已有重叠规则的分类。完整初始化脚本
`jokester.admin.sql` 已同步；扩充迁移重复执行不会重复插入，也不会在无变化时递增版本。

默认配置：

```json
{
  "AiPromptFilter": {
    "Enabled": true,
    "RefreshIntervalSeconds": 30,
    "MaxSnapshotAgeMinutes": 15,
    "MinimumActiveWordCount": 1
  }
}
```

启用过滤后，如果数据库词库无法完成首次加载、有效词条数不足，或快照超过允许陈旧时间，
新生图请求返回 HTTP 503。服务不会在词库异常时静默放行。

## 七类项目分类

下列编码是本项目的生图安全分类，不是第三方词库的原生标签：

| Category code | 含义 |
| --- | --- |
| `sexual_minors` | 未成年人色情与性剥削 |
| `non_consensual_nudity` | 非自愿性内容、真人裸照和裸聊 |
| `graphic_violence` | 血腥、肢解和严重暴力 |
| `self_harm` | 自残、自杀方法与明确行为 |
| `hate_extremism` | 仇恨宣传、极端主义宣传与招募 |
| `weapons_drugs` | 武器、爆炸物、毒品制作与交易 |
| `deepfake_privacy` | 真人深伪、伪造裸照和隐私侵害 |

原有普通成人色情规则继续保留 `sexual_content`，不会为了凑入七类而错误归入
“非自愿/真人裸照”。已有的性暴力、违法教程、极端主义和伪造裸照规则则在扩充迁移中
映射到对应的新分类，规则来源、启用状态和动作保持不变。

## 生图行为

- GPT Image2 和 Nano Banana2 的直接生成、队列生成均使用同一过滤服务。
- 同一幂等键和相同载荷仍优先返回已有任务；过滤只拦截真正的新请求。
- 新请求在 Redis 准入、创建任务和扣积分之前检查。
- Worker 在取得 Provider 租约之前按当前词库再次检查；排队期间新增的规则可以拦截旧任务，
  并沿现有结算逻辑退款。
- GPT `negativePrompt` 虽然当前不发送给 Provider，但会参与幂等和持久化，因此同样过滤。
- GPT 的 `prompt` 与 `negativePrompt` 使用同一份不可变快照审核，任务记录的版本覆盖两个字段。
- Nano 图生图即使允许空 `prompt`，也必须先确认词库快照可用，不能绕过 fail-closed。
- 过滤仅使用当前 MySQL 词库加载出的进程内关键词快照，不依赖本地或远程模型服务。
- 创建阶段在 Redis 准入和扣积分前检查一次，Worker 在 Provider 租约前按最新关键词
  快照复检一次；Provider 调用层不再重复过滤。
- 命中规则返回 HTTP 422、机器码 `PROMPT_BLOCKED`，不会返回具体命中词。

## 匹配模式

- `contains`：规范化后的连续短语包含，默认用于中文。
- `word`：带字母数字边界的单词或短语，默认用于英文；匹配时忽略词内插入的空白、
  标点和组合附加符，同时仍避免 `rape` 误命中 `grape` 一类子串。
- `compact`：去除空白、标点和符号后匹配，只用于明确需要防拆字绕过的高风险规则。

匹配前会执行 Unicode NFKC、英文小写化、零宽字符与组合附加符清理和分隔符归一化。
内置中文高风险规则使用 `compact`，防止逐字插入分隔符绕过。简繁体、拼音、
谐音和上下文隐喻不会自动等价，必须维护对应的独立关键词、变体词条和误杀/漏放回归样例。

## houbb 候选与项目补充

候选源固定为 `houbb/sensitive-word-data` 的 commit
`fe6fc2921836217b8c90619db81b24af8b22d80f`，许可证为 Apache-2.0，源文件为
`src/main/resources/sensitive_word_tags.txt`。上游 Git blob 的 SHA-256 是
`37cea2687a1525a436aaa080e918f6c263310bd21b4bce8b05ba5185ee3e5ae8`；本次在 Windows
检出的 CRLF 审核副本 SHA-256 是
`d2ca6f91477238577743e8cfebee71e448b32d2477959c2aa7ba49482b3bd142`。换行转换之外的
内容必须一致。第三方声明和许可证副本见仓库根目录 `THIRD_PARTY_NOTICES.md`。

houbb 原生数字标签只有 `0政治 / 1毒品 / 2色情 / 3赌博 / 4违法犯罪`，不能机械映射成
七类。本次逐条审定 62 个候选并保留每条原标签；在只执行过基础迁移的数据库上，`强奸`、
`炸弹制作` 已有内置规则，因此实际新增 60 条 `action=audit,status=0` 的 houbb 候选，
不会进入运行时匹配快照。两个重叠规则仅更新为新分类，仍保留 `source_code=builtin`。
上游还包含 `色情`，但它属于普通成人内容且不在本次七类候选中，继续保留原分类。

houbb 对标准血腥、自残、仇恨复合语义和真人深伪覆盖明显不足。扩充迁移另行加入 68 条
`source_code=project-curated`、`action=block,status=1` 的项目自维护规则；这些词不标记为
houbb 来源。项目没有启用 `幼女`、`萝莉`、`少女`、`学生妹`、`枪`、`换脸` 等单独宽泛词，
以降低正常绘画、历史和工具语境的误杀。

离线工具 `tools/Jokester.PromptLexiconTool` 可以读取本地行式、逗号式或 houbb 数字标签
格式。每个 manifest 只允许一个来源和许可证，并要求不可变 commit 与逐文件预期
SHA-256；工具输出全部为禁用候选，不联网、不连接 MySQL，也不会自动启用规则。houbb 的
五个标签无法由一对一 `tagMappings` 自动拆成七类，最终分类必须逐条人工审核。项目自维护词
应使用独立 manifest 或受审迁移，不能混入 houbb provenance。

## 管理接口

接口前缀：`/api/ai/prompt-sensitive-words`

| Method | Path | Permission | Purpose |
| --- | --- | --- | --- |
| GET | `/` | `AiImage.SensitiveWord.View` | 分页查询规则 |
| GET | `/status` | `AiImage.SensitiveWord.View` | 查询数据库版本和有效规则数 |
| POST | `/` | `AiImage.SensitiveWord.Manage` | 新增规则 |
| PUT | `/{id}` | `AiImage.SensitiveWord.Manage` | 修改规则 |
| PUT | `/{id}/status` | `AiImage.SensitiveWord.Manage` | 启用或禁用规则 |
| DELETE | `/{id}` | `AiImage.SensitiveWord.Manage` | 逻辑删除规则 |
| POST | `/test` | `AiImage.SensitiveWord.Test` | 测试文本，只返回规则元数据 |

houbb 候选初始同时为 `action=audit,status=0`。完成业务复核后，应先用测试接口验证正常与
对抗样例，再通过完整 `PUT /{id}` 把 `action` 改为 `block`、`status` 改为 `1`，并原样保留
查询结果中的 `sourceCode`、`sourceVersion` 和 `remark`。仅调用状态接口启用候选不会把
`audit` 动作变成阻断，也不应清空 provenance。

管理操作会先锁定版本行，并在同一个数据库事务内校验最低有效规则数、写入规则和递增版本。
提交后当前实例立即尝试原子替换不可变匹配快照，并通过 Redis 发布版本通知；如果即时刷新
短暂失败，数据库变更仍返回成功，后台轮询会继续重试。其他实例同时订阅通知并定时轮询
数据库版本，以处理 Pub/Sub 丢消息。
