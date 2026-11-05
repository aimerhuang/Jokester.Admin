# Docker 部署

整套系统只通过 Docker 运行（本地测试与生产共用 `compose.yaml`），不依赖 Caddy 或宿主机上的
MySQL / Redis / Node / .NET：

```
浏览器 ──► web (nginx + 前端 dist)  :8080
             ├─ /api/*、/avatar/*、/blog/*、/prompt-images/*、/swagger ──► api:8080
             └─ 其余路径 ──► SPA index.html
           api ──► mysql:3306、redis:6379、mailpit:1025(本地)
```

| 服务 | 镜像 | 来源 |
| --- | --- | --- |
| `web` | `jokester-web:local` | 前端仓库 `Jokester-Ai-Image/Dockerfile`（Node 构建 → nginx） |
| `api` | `jokester-admin:local` | 本仓库根目录 `Dockerfile` |
| `mysql` | `mysql:8.4` | 首次启动自动执行 `jokester.admin.sql` 或导入 `mysql/import/*.sql` |
| `redis` | `redis:7.4-alpine` | 带密码、AOF 持久化 |
| `mailpit` | `axllent/mailpit` | 仅本地，接收注册验证码邮件 |

生产服务器的发布与迁移流程见 [docs/teamcity-docker-deploy.md](../docs/teamcity-docker-deploy.md)，
下文“生产服务器”一节是摘要。

## 前提

- Docker Desktop（Linux 容器），可用内存建议 ≥ 4 GB
- 前端仓库与本仓库放在同一父目录（默认 `../../Jokester-Ai-Image`，可在 `.env` 的 `FRONTEND_DIR` 修改）

## 本地快速开始

在 `docker/` 目录执行（PowerShell）：

```powershell
.\jokester.ps1 init        # 生成 .env / api.env，并填入随机密码和 JWT 密钥
.\jokester.ps1 up          # 构建镜像、启动并等待全部健康
.\jokester.ps1 seed-admin  # 交互创建超级管理员
```

| 地址 | 用途 |
| --- | --- |
| http://localhost:8080 | 前端（经 nginx） |
| http://localhost:5050/swagger | API 直连 / Swagger |
| http://localhost:8025 | Mailpit，查看注册验证码 |
| `127.0.0.1:3307` / `127.0.0.1:6380` | MySQL / Redis（账号密码见 `docker/.env`） |

`.\jokester.ps1 help` 查看全部命令。脚本只是 `docker compose` 的薄封装；在 `docker/` 目录也可以直接执行
`docker compose up -d --build`、`docker compose logs -f api` 等命令，`.env` 中的 `COMPOSE_FILE` 会自动叠加
`compose.yaml` 与 `compose.local.yaml`。

## 配置文件

| 文件 | 作用 | 是否提交 |
| --- | --- | --- |
| `compose.yaml` | 基础编排（生产与本地共用） | 是 |
| `compose.local.yaml` | 本地叠加：源码构建、调试端口、Mailpit | 是 |
| `compose.production.yaml` | 生产叠加：发布镜像、80/443、证书、`./data` 目录 | 是 |
| `jokester.ps1` / `jokester.sh` | Windows / Linux 管理脚本（命令相同，`jokester.sh` 另含发布命令） | 是 |
| `.env.example` → `.env` | 编排变量：域名、端口、子网、镜像、MySQL/Redis 密码 | 否 |
| `api.env.example` → `api.env` | API 业务配置：JWT、SMTP、AI、积分、提示词库等 | 否 |
| `.env.production.example` / `api.env.production.example` | 生产模板（`./jokester.sh init production`） | 否（生成的文件） |
| `legal.env.example` → `legal.env` | 法律文档版本与 URL，仅 `legal` 命令使用 | 否 |

连接串、`AllowedHosts`、CORS 主来源、可信代理 IP、媒体目录由 `compose.yaml` 根据 `.env` 生成，
不要在 `api.env` 中重复配置。修改 `api.env` 后执行 `.\jokester.ps1 up` 即可重建 API 容器。

## 数据库

- 首次创建数据卷时，`mysql/initdb/10-jokester-init.sh` 自动初始化：
  - `mysql/import/` 下有 `*.sql`：按文件名顺序导入（迁移现有数据用）；
  - 否则执行根目录 `jokester.admin.sql`（已包含全部迁移），库名使用 `.env` 的 `MYSQL_DATABASE`。
- 已有数据卷不会重复初始化；对已有库执行增量迁移：

  ```powershell
  .\jokester.ps1 sql ..\docs\migrations\<migration>.sql
  ```

- 导出备份：`.\jokester.ps1 backup`（默认写入 `docker/backups/`）。
- 迁移本机现有数据：先用 `mysqldump` 导出（参考 `docs/teamcity-docker-deploy.md`），把文件放入
  `mysql/import/`，再 `.\jokester.ps1 reset` 后 `up`。

## 持久化

所有数据都在 Docker 命名卷中（项目名 `jokester-local`）：

| 卷 | 内容 |
| --- | --- |
| `mysql-data` / `redis-data` | 数据库、Redis AOF |
| `api-data` | `/data/private-media/ai` 私有 AI 图片、`/data/prompt-images` |
| `api-blog` / `api-avatar` | 博客媒体、头像 |
| `api-keys` | ASP.NET DataProtection Keys |

`down` 保留数据；`reset` 删除全部卷（需输入 `RESET` 确认）。

## AI 生图前置条件

空库中 Provider 密钥为空，并且法律文档/AI 告知未配置时，生图接口会按设计 fail-closed（503）：

1. 在 `ai_image_model_config` 中填入测试用的 Provider URL 与 Key（`.\jokester.ps1 mysql`）。
2. 复制 `legal.env.example` 为 `legal.env`，填入已审批的 HTTPS 版本与 URL，执行 `.\jokester.ps1 legal`。

## 网络受限时

`.env` 中可替换基础镜像和 npm 源：`DOTNET_SDK_IMAGE`、`DOTNET_ASPNET_IMAGE`、`NODE_IMAGE`、`NGINX_IMAGE`、
`MYSQL_IMAGE`、`REDIS_IMAGE`、`MAILPIT_IMAGE`、`NPM_REGISTRY`。

## 生产服务器

部署目录 `/opt/jokester-admin` 的内容由 TeamCity 上传的部署包维护，`.env`、`api.env`、`certs/`、`data/`
只存在于服务器：

```bash
./jokester.sh init production   # 全新服务器：生成 .env / api.env（600 权限，随机密钥）
./jokester.sh migrate-from-caddy # 旧 Caddy 部署：由 .env.production 生成 .env / api.env（deploy-api 会自动执行）
./jokester.sh deploy-api <tar> <image>   # TeamCity 调用：发布 API，失败自动回滚
./jokester.sh deploy-web <tar> <image>   # TeamCity 调用：发布前端/nginx，替换旧 Caddy，失败自动回滚
./jokester.sh seed-admin | backup | sql <file> | logs api | ps
```

### HTTPS 证书

生产 `.env` 默认 `TLS_MODE=on`：nginx 监听 443，证书文件为 `certs/tls.crt` 与 `certs/tls.key`
（目录可用 `TLS_CERT_DIR` 修改）。站点经 Cloudflare 代理（SSL 模式 Full (strict)）时，使用 Cloudflare
控制台 SSL/TLS → Origin Server 生成的 Origin Certificate（`johhai.com, *.johhai.com`，15 年有效）。
证书缺失时 `deploy-web` 会在切换流量前失败。`REAL_IP_MODE=cloudflare` 时只信任 Cloudflare 网段的
`CF-Connecting-IP`；不经 Cloudflare 时改为 `none`。

生产数据目录：MySQL / Redis 使用命名卷 `jokester-admin_mysql-data`、`jokester-admin_redis-data`；
上传文件与 DataProtection Keys 在 `./data/`（`private-media`、`prompt-images`、`blog`、`avatar`、`data-protection`）。
