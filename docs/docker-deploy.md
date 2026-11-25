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

先打包并上传，服务器解压覆盖后再发布；数据库增量迁移要在 `deploy-api` 之前执行（见下）：

```powershell
cd E:\Project\Jokester.Admin
tar -cf jokester-docker-bundle.tar -C docker compose.yaml compose.production.yaml jokester.sh .env.production.example api.env.production.example README.md mysql/initdb/10-jokester-init.sh mysql/import/README.md -C .. jokester.admin.sql
scp jokester-docker-bundle.tar root@<server-ip>:/opt/jokester-admin/
ssh root@<server-ip> "cd /opt/jokester-admin && tar -xf jokester-docker-bundle.tar --no-same-owner && chmod 755 jokester.sh && rm jokester-docker-bundle.tar"
```

随后执行上面的“只更新后端”命令。

### 数据库增量迁移

API 会读取新表和新列，必须先迁移、再发布。已有库不会重新执行 `jokester.admin.sql`：

```bash
cd /opt/jokester-admin
./jokester.sh backup
./jokester.sh sql /path/to/migration.sql
```

迁移文件在 `docs/migrations/`，先用 `scp` 传到服务器。不要重复执行已经跑过的迁移。

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
