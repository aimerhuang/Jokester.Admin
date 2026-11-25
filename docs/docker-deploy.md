# Docker 单机生产部署

部署拓扑：

- 开发机：Windows（需要 Docker、OpenSSH 客户端 `ssh`/`scp`、`tar`），负责构建镜像并上传
- 目标服务器：Ubuntu，SSH 端口 22，需要 Docker Engine + Compose 插件、`bash`、`curl`、`flock`
- 域名：`johhai.com`，经 Cloudflare 代理
- 服务器上全部以 Docker 运行：`web`（nginx + 前端）、`api`、`mysql`、`redis`；不使用 Caddy
- 镜像传输：在开发机 `docker build` + `docker save`，通过 `scp` 发送到服务器，再由服务器上的
  `jokester.sh` 执行 `docker load` 并滚动更新，不依赖镜像仓库

编排文件与服务器管理脚本都在本仓库 `docker/`，说明见 [docker/README.md](../docker/README.md)。

## 发布链路

| 组件 | 仓库 | 镜像 | 服务器命令 |
| --- | --- | --- | --- |
| API | Jokester.Admin | `jokester-admin:<tag>` | `./jokester.sh deploy-api <tar> <image>` |
| Web | Jokester-Ai-Image | `jokester-web:<tag>` | `./jokester.sh deploy-web <tar> <image>` |

- `deploy-api`：`docker load` → 写入 `.env` 的 `API_IMAGE` → `docker compose up --wait mysql redis api`；
  5 分钟内未 healthy 则恢复上一个 `API_IMAGE`。
- `deploy-web`：检查证书 → 用新镜像离线执行 `nginx -t` → 写入 `WEB_IMAGE` → 启动 web → 通过 443
  冒烟测试首页和 `/api/sites/site_code`；失败则恢复上一个 `WEB_IMAGE`。
- 两个命令共用 `DEPLOY_ROOT/.deploy.lock`，不会并发执行；成功后会删除上传的镜像包。
- 首次部署必须先发 API 并上传部署包（`docker/` 生产文件 + `jokester.admin.sql`），它负责安装 `jokester.sh`。
- MySQL、Redis、`data/` 不参与镜像回滚。
- `.env`、`api.env`、`certs/`、`data/` 只存在于服务器，永远不要放进部署包。

## 更新命令

以下命令在开发机 PowerShell 执行。`<server-ip>` 换成服务器 IP 或专用 SSH 域名（不能用经 CDN 代理的
`johhai.com`），部署目录默认 `/opt/jokester-admin`。服务器为 `aarch64` 时把 `--platform` 改为 `linux/arm64`。

### 只更新前端

```powershell
cd E:\Project\Jokester-Ai-Image
$tag = "web-$(git rev-parse --short=12 HEAD)"
docker build --pull --platform linux/amd64 --provenance=false --build-arg VITE_API_BASE_URL=/api --build-arg VITE_CLIENT_BUILD=$tag -t jokester-web:$tag .
docker save -o jokester-web-$tag.tar jokester-web:$tag
scp jokester-web-$tag.tar root@<server-ip>:/opt/jokester-admin/
ssh root@<server-ip> "cd /opt/jokester-admin && ./jokester.sh deploy-web ./jokester-web-$tag.tar jokester-web:$tag"
```

### 只更新后端（无编排文件和数据库变更）

```powershell
cd E:\Project\Jokester.Admin
$tag = "api-$(git rev-parse --short=12 HEAD)"
docker build --pull --platform linux/amd64 --provenance=false -t jokester-admin:$tag .
docker save -o jokester-admin-$tag.tar jokester-admin:$tag
scp jokester-admin-$tag.tar root@<server-ip>:/opt/jokester-admin/
ssh root@<server-ip> "cd /opt/jokester-admin && ./jokester.sh deploy-api ./jokester-admin-$tag.tar jokester-admin:$tag"
```

### 后端有编排文件（`docker/`）或 `jokester.admin.sql` 变更

部署包包含空库初始化文件 `jokester.admin.sql`，以及 `deploy/api-sql-files.txt` 中列出的增量迁移和核验 SQL。先上传并解压：

```powershell
cd E:\Project\Jokester.Admin
$releaseSql = Get-Content -LiteralPath deploy/api-sql-files.txt
$env:COPYFILE_DISABLE = '1' # macOS tar 忽略扩展属性
tar -cf jokester-docker-bundle.tar -C docker compose.yaml compose.production.yaml jokester.sh .env.production.example api.env.production.example README.md mysql/initdb/10-jokester-init.sh mysql/import/README.md -C .. jokester.admin.sql @releaseSql
if ($LASTEXITCODE -ne 0) { throw "部署包生成失败" }
scp jokester-docker-bundle.tar root@<server-ip>:/opt/jokester-admin/
ssh root@<server-ip> "cd /opt/jokester-admin && tar -xf jokester-docker-bundle.tar --no-same-owner && chmod 755 jokester.sh && rm jokester-docker-bundle.tar"
```

核对目标库并完成该版本要求的迁移后，执行上面的“只更新后端”命令。使用 `deploy/teamcity-deploy.ps1` 时，该入口会解包后直接切换 API，须在触发部署前将所需 SQL 传到服务器并完成核验与迁移。

### 数据库增量迁移

已有数据卷按版本说明运行增量 SQL。`jokester.admin.sql` 包含 `DROP TABLE`，仅供空库初始化。`deploy-api` 切换镜像时不会执行 SQL。

```bash
cd /opt/jokester-admin
./jokester.sh backup
./jokester.sh sql /path/to/migration.sql
```

### 2026-11-25 AI 生图后端

本次升级以已经包含 11 月 22 日模型配置审计表和路由 `revision` 的 `main` 为基线（`12dd15b`）。解包后，在目标服务器运行只读核验。先确认 `target_database` 是业务库，再比较每项的 `actual` 和 `expected`。

```bash
cd /opt/jokester-admin
./jokester.sh sql docs/checks/20261125-ai-image-rollout.sql
```

| 核验项 | 对应增量（位于 `docs/migrations/`） | 生效内容 |
| --- | --- | --- |
| `vip_price_columns`、`vip_task_columns`、`vip_tables`、`vip_price_checks`、`vip_setting_row` | `20261124-add-ai-pricing-admin-and-vip-models.sql` | 保留标准价，首次初始化 VIP 折扣与审计。后续运营调价以库内记录为准。 |
| `old_gpt_seed_names` | `20261124-unify-ai-image-model-names.sql` | 将仍带 1K/2K/4K 后缀的 GPT Image 2 种子名改为统一展示名，并记录审计。 |
| `gif_task_columns`、`gif_route_column` | `20261125-add-ai-image-gif-output.sql` | 增加 GIF 任务、用量与路由别名字段。已有任务的输出格式默认为 `png`。 |

**已经更新的服务器**：各项均符合预期即可跳过本批 SQL。**等待升级的服务器**：先备份，只执行核验显示尚缺的对应行，再重复核验。若同一组列只出现一部分，先核对 `SHOW CREATE TABLE` 与对应迁移中的 `ALTER TABLE`；须补齐差异后再切换 API。

```bash
./jokester.sh backup
./jokester.sh sql docs/migrations/20261124-add-ai-pricing-admin-and-vip-models.sql
./jokester.sh sql docs/migrations/20261124-unify-ai-image-model-names.sql
./jokester.sh sql docs/migrations/20261125-add-ai-image-gif-output.sql
./jokester.sh sql docs/checks/20261125-ai-image-rollout.sql
```

首次上线时，在服务器 `api.env` 中设置 `AiImageGif__AcceptNewTasks=false`，再升级全部 API 和 Worker。确认消费者已升级、超管已在模型配置页填好支持 GIF 的供应商别名后，开放新任务并发布 Web。需要降版时，先关闭 GIF 新建，让新版 Worker 结清在途任务。具体设置和结清查询见 [运行手册](runbook.md#启用和停用首页-gif)。

### 验收与回滚

```bash
cd /opt/jokester-admin
./jokester.sh ps
./jokester.sh logs api
curl --fail https://johhai.com/api/sites/site_code
```

- 健康检查或冒烟测试失败时，脚本会自动恢复上一个镜像。
- 需要手动回滚：把 `.env` 中的 `API_IMAGE` / `WEB_IMAGE` 改回旧 tag（旧镜像仍在服务器上），再执行
  `docker compose up -d --no-deps api`（或 `web`）。
- 清理不再使用的旧镜像：`docker image prune -a`（先确认当前 `.env` 记录的镜像仍在运行）。

## 网络、DNS 与 HTTPS

1. `johhai.com`（以及 `www.johhai.com`）DNS 指向服务器，Cloudflare 代理开启。
2. 云安全组 / UFW 只放行 `22/tcp`、`80/tcp`、`443/tcp`；不要放行 MySQL `3306` 或 Redis `6379`
   （生产编排不会对宿主机开放这两个端口）。
3. Cloudflare SSL/TLS 模式使用 `Full (strict)`，不能用 `Flexible`（否则 HTTPS 重定向循环）。
4. HTTPS 由 nginx 终止。在 Cloudflare 控制台 **SSL/TLS → Origin Server → Create Certificate** 生成
   Origin Certificate（主机名填 `johhai.com, *.johhai.com`，有效期 15 年，无需续期），保存到服务器：

   ```bash
   install -d -m 700 /opt/jokester-admin/certs
   nano /opt/jokester-admin/certs/tls.crt   # 粘贴 Origin Certificate
   nano /opt/jokester-admin/certs/tls.key   # 粘贴 Private Key
   chmod 600 /opt/jokester-admin/certs/tls.key
   ```

   证书缺失时 `deploy-web` 会在切换流量前直接失败。Origin Certificate 只被 Cloudflare 信任；
   如果以后关闭 Cloudflare 代理，需要换成公开 CA 证书（同样放到 `certs/`）。
5. `REAL_IP_MODE=cloudflare` 时 nginx 只信任 Cloudflare 官方网段传来的 `CF-Connecting-IP`，再以
   `X-Forwarded-For` 交给 API；网段列表在前端仓库 `docker/nginx/snippets/realip-cloudflare.conf`。

## 从旧 Caddy 部署原地迁移

旧部署（`docker-compose.production.yml` + Caddyfile + `.env.production`）已在本地用 docker-in-docker
完整演练过：数据卷、`data/` 上传文件、Redis 中的 Refresh Token 均保留，过渡期 Caddy 仍可转发到新 API。

1. **备份**（服务器上）：

   ```bash
   cd /opt/jokester-admin
   docker exec jokester-mysql sh -c 'exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers --events --hex-blob --default-character-set=utf8mb4 --no-tablespaces "$MYSQL_DATABASE"' > ~/jokester-before-migration.sql
   chmod 600 ~/jokester-before-migration.sql
   tar -czf ~/jokester-data-before-migration.tgz data .env.production
   ```

2. 按上文把 Cloudflare Origin Certificate 放到 `/opt/jokester-admin/certs/`。
3. 按“后端有编排文件变更”上传部署包，再按“只更新后端”发布 API。`deploy-api` 发现只有
   `.env.production` 时会自动执行 `migrate-from-caddy`：
   - 生成 `.env`：沿用 `APP_DOMAIN`、MySQL/Redis 账号密码、当前 API 镜像，项目名 `jokester-admin`、
     子网 `172.29.0.0/24`、web 地址 `172.29.0.10` 与旧部署一致，因此原 `jokester-admin_mysql-data`、
     `jokester-admin_redis-data` 卷被直接复用；
   - 生成 `api.env`：旧文件中除连接串、域名、代理等编排管理的键以外的全部业务配置；
   - 旧 `.env.production` 保留不动。

   随后 MySQL / Redis / API 以新编排重建，`jokester-caddy` 继续运行并转发到新 API。
4. 按“只更新前端”发布 Web。`deploy-web` 停止 `jokester-caddy`，由 nginx 接管 80/443；冒烟测试通过后
   删除 Caddy 容器，失败则重新启动 Caddy。
5. 验收：

   ```bash
   cd /opt/jokester-admin
   ./jokester.sh ps
   curl --fail https://johhai.com/api/sites/site_code
   ```

   并验收登录 / 刷新令牌、注册邮件、私有媒体读写和一个后台任务。
6. 确认稳定后可手工清理旧文件：`docker-compose.production.yml`、`Caddyfile`、`server-*.sh`、
   `frontend/`、`frontend-releases/`，以及 Caddy 卷 `jokester-admin_caddy-data`、`jokester-admin_caddy-config`。
   `.env.production` 建议在确认新 `.env` / `api.env` 无误后移到备份位置。

## 全新服务器

```bash
mkdir -p /opt/jokester-admin
```

1. 在开发机按“后端有编排文件变更”上传并解压部署包（`jokester.sh` 由它安装）。
2. 在服务器上生成配置并填写：

   ```bash
   cd /opt/jokester-admin
   ./jokester.sh init production   # 生成 .env / api.env（600 权限），MySQL/Redis/JWT 密钥自动随机
   nano .env                       # 确认域名
   nano api.env                    # 填写 SMTP 等业务配置
   ```

3. 放置 HTTPS 证书（见上文）。需要迁入已有数据时，把 `mysqldump` 导出的 `*.sql` 放进
   `mysql/import/`，首次启动会导入它而不是执行 `jokester.admin.sql`。
4. 依次按“只更新后端”“只更新前端”发布 API 和 Web，然后创建管理员：

   ```bash
   ./jokester.sh seed-admin
   ```

## 日常运维

在 `/opt/jokester-admin` 下执行（`./jokester.sh help` 查看全部）：

| 命令 | 用途 |
| --- | --- |
| `./jokester.sh ps` / `logs api` | 状态、日志（容器日志按 `LOG_MAX_SIZE` × `LOG_MAX_FILE` 轮转） |
| `./jokester.sh seed-admin` | 创建或重置超级管理员（支持邮箱账号；会撤销该账号现有会话） |
| `./jokester.sh sql docs-migration.sql` | 执行增量迁移 |
| `./jokester.sh backup` | 导出数据库到 `backups/`（600 权限） |
| `./jokester.sh cli --migrate-ai-media --dry-run` | 执行 API 维护命令 |

也可以直接使用 `docker compose`：`.env` 中的 `COMPOSE_FILE` 已组合生产编排，`API_IMAGE` / `WEB_IMAGE`
记录当前发布版本。

## 持久化与备份

不在镜像内、需要异机备份的内容：

- MySQL 卷 `jokester-admin_mysql-data`、Redis 卷 `jokester-admin_redis-data`
- `/opt/jokester-admin/data/`（`private-media`、`prompt-images`、`blog`、`avatar`、`data-protection`）
- `/opt/jokester-admin/.env`、`api.env`、`certs/`

更新命令不替代数据库、私有媒体和 DataProtection Keys 的异机备份；正式开放前应配置定时备份并做恢复演练。
