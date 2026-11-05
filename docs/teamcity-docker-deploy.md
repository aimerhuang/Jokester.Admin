# TeamCity + Docker 单机生产部署

部署拓扑：

- TeamCity Server/Agent：Windows（需要 Docker、OpenSSH 客户端、`tar`）
- 目标服务器：Ubuntu，SSH 端口 22，需要 Docker Engine + Compose 插件、`bash`、`curl`、`flock`
- 域名：`johhai.com`，经 Cloudflare 代理
- 服务器上全部以 Docker 运行：`web`（nginx + 前端）、`api`、`mysql`、`redis`；不使用 Caddy
- 镜像传输：TeamCity 通过 SSH/SCP 直接发送 `docker save` 镜像包，不依赖镜像仓库

编排文件与服务器管理脚本都在本仓库 `docker/`，说明见 [docker/README.md](../docker/README.md)。

## 发布链路

| 构建 | 仓库 | 构建脚本 | 发布脚本 | 服务器命令 |
| --- | --- | --- | --- | --- |
| API | Jokester.Admin | `deploy/teamcity-build.ps1` → `jokester-admin:<build>-<commit>` | `deploy/teamcity-deploy.ps1` | `jokester.sh deploy-api` |
| Web | Jokester-Ai-Image | `deploy/teamcity-build.ps1` → `jokester-web:<build>-<commit>` | `deploy/teamcity-deploy.ps1` | `jokester.sh deploy-web` |

- API 发布同时上传部署包（`docker/` 生产文件 + `jokester.admin.sql`），解压到 `DEPLOY_ROOT` 覆盖
  `compose.yaml`、`compose.production.yaml`、`jokester.sh` 等；`.env`、`api.env`、`certs/`、`data/` 永远不在包里。
- `deploy-api`：`docker load` → 写入 `.env` 的 `API_IMAGE` → `docker compose up --wait mysql redis api`；
  5 分钟内未 healthy 则恢复上一个 `API_IMAGE`。
- `deploy-web`：检查证书 → 用新镜像离线执行 `nginx -t` → 写入 `WEB_IMAGE` → 启动 web → 通过 443
  冒烟测试首页和 `/api/sites/site_code`；失败则恢复上一个 `WEB_IMAGE`。
- 两个发布共用 `DEPLOY_ROOT/.deploy.lock`，不会并发执行。首次部署必须先发 API（它负责安装 `jokester.sh`）。
- MySQL、Redis、`data/` 不参与镜像回滚。

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

服务器架构为 `aarch64` 时，在两个 TeamCity 构建的 Parameters 中增加 `env.DOCKER_PLATFORM=linux/arm64`。

## TeamCity 配置

两个构建配置都使用 PowerShell 步骤，Working directory 为 `%teamcity.build.checkoutDir%`：

```powershell
.\deploy\teamcity-build.ps1
.\deploy\teamcity-deploy.ps1
```

Parameters（两个构建相同）：

| 参数 | 示例 | 类型 |
| --- | --- | --- |
| `env.DEPLOY_HOST` | `<server-ip>` | Text |
| `env.DEPLOY_PORT` | `22` | Text |
| `env.DEPLOY_USER` | `root` | Text |
| `env.DEPLOY_ROOT` | `/opt/jokester-admin` | Text |
| `env.DEPLOY_HOST_KEY` | `ssh-keyscan` 输出的完整一行 | Text |
| `env.NODE_IMAGE` / `env.NGINX_IMAGE` / `env.NPM_REGISTRY` | 镜像加速地址（仅 Web，可选） | Text |

`DEPLOY_HOST` 使用服务器 IP 或专用 SSH 域名，不能用经 CDN 代理的 `johhai.com`。

SSH 身份：为 TeamCity 单独生成 Ed25519 密钥，私钥只保存在 TeamCity 主机，在项目 `SSH Keys` 上传，
并在构建的 `Build Features` 中添加 `SSH Agent`：

```powershell
ssh-keygen -t ed25519 -f D:\TeamCityKeys\jokester-deploy -C teamcity-jokester
Get-Content D:\TeamCityKeys\jokester-deploy.pub |
  ssh root@<server-ip> "umask 077; mkdir -p ~/.ssh; cat >> ~/.ssh/authorized_keys"
ssh-keyscan -p 22 -t ed25519 <server-ip>
```

通过云控制台登录服务器，用 `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` 核对指纹后再填入
`DEPLOY_HOST_KEY`。

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
3. 运行 **Jokester.Admin** 的 TeamCity 构建。`deploy-api` 发现只有 `.env.production` 时会自动执行
   `migrate-from-caddy`：
   - 生成 `.env`：沿用 `APP_DOMAIN`、MySQL/Redis 账号密码、当前 API 镜像，项目名 `jokester-admin`、
     子网 `172.29.0.0/24`、web 地址 `172.29.0.10` 与旧部署一致，因此原 `jokester-admin_mysql-data`、
     `jokester-admin_redis-data` 卷被直接复用；
   - 生成 `api.env`：旧文件中除连接串、域名、代理等编排管理的键以外的全部业务配置；
   - 旧 `.env.production` 保留不动。

   随后 MySQL / Redis / API 以新编排重建，`jokester-caddy` 继续运行并转发到新 API。
4. 运行 **Jokester-Ai-Image** 的 TeamCity 构建。`deploy-web` 停止 `jokester-caddy`，由 nginx 接管
   80/443；冒烟测试通过后删除 Caddy 容器，失败则重新启动 Caddy。
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

1. 先运行一次 Jokester.Admin 构建：部署包会被解压，随后因缺少 `.env` 失败并提示。
2. 在服务器上生成配置并填写：

   ```bash
   cd /opt/jokester-admin
   ./jokester.sh init production   # 生成 .env / api.env（600 权限），MySQL/Redis/JWT 密钥自动随机
   nano .env                       # 确认域名
   nano api.env                    # 填写 SMTP 等业务配置
   ```

3. 放置 HTTPS 证书（见上文）。需要迁入已有数据时，把 `mysqldump` 导出的 `*.sql` 放进
   `mysql/import/`，首次启动会导入它而不是执行 `jokester.admin.sql`。
4. 依次运行 Jokester.Admin、Jokester-Ai-Image 构建，然后创建管理员：

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
| `./jokester.sh legal` | 按 `legal.env` 配置法律文档 |
| `./jokester.sh cli --migrate-ai-media --dry-run` | 执行 API 维护命令 |

也可以直接使用 `docker compose`：`.env` 中的 `COMPOSE_FILE` 已组合生产编排，`API_IMAGE` / `WEB_IMAGE`
记录当前发布版本。

## 持久化与备份

不在镜像内、需要异机备份的内容：

- MySQL 卷 `jokester-admin_mysql-data`、Redis 卷 `jokester-admin_redis-data`
- `/opt/jokester-admin/data/`（`private-media`、`prompt-images`、`blog`、`avatar`、`data-protection`）
- `/opt/jokester-admin/.env`、`api.env`、`certs/`

一键发布不替代数据库、私有媒体和 DataProtection Keys 的异机备份；正式开放前应配置定时备份并做恢复演练。
