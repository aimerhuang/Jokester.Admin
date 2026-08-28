# TeamCity + Docker 单机生产部署

本文适用于以下部署拓扑：

- TeamCity Server/Agent：Windows
- 目标服务器：Ubuntu 26.04，SSH 端口 22
- API 域名：`johhai.com`
- API、MySQL、Redis、Caddy：同一台服务器上的 Docker 容器
- 镜像传输：TeamCity 通过 SSH/SCP 直接发送到服务器，不依赖镜像仓库

## 发布链路

TeamCity 每次构建会生成不可变镜像标签：

```text
jokester-admin:<build-number>-<commit-prefix>
```

随后通过 SCP 上传镜像和 Compose 文件。服务器加载镜像、更新容器并等待 API 健康检查；
新 API 五分钟内未变为 healthy 时，脚本恢复上一个 API 镜像。MySQL、Redis 和持久化目录
不参与应用镜像回滚。

## 网络和 DNS

1. 将 `johhai.com` 的 DNS A 记录指向服务器公网 IPv4。
2. 如果配置 AAAA 记录，必须确认服务器 IPv6 可以正常接收 80/443 流量。
3. 云安全组和 UFW 只需放行 `22/tcp`、`80/tcp`、`443/tcp` 和 `443/udp`。
4. 不要放行 MySQL `3306` 或 Redis `6379`。

服务器先确认架构：

```bash
uname -m
```

`x86_64` 使用默认的 `linux/amd64` 镜像。如果结果为 `aarch64`，在 TeamCity Parameters 中
增加 `env.DOCKER_PLATFORM=linux/arm64`。

Caddy 会自动申请和续期 HTTPS 证书，因此首次启动前必须保证 DNS 已生效，并且 80/443
没有被宿主机其他 Web 服务占用。

当前 `johhai.com` 使用 Cloudflare 代理，公共 DNS 返回的是 Cloudflare 地址而不是源站地址。
Cloudflare SSL/TLS 模式必须使用 `Full (strict)`，不能使用 `Flexible`，否则 HTTPS 重定向可能
循环。首次签发证书失败时，先确认 Cloudflare 能把 80 端口的 ACME 请求转发到源站；必要时可
临时切换为 DNS only，证书签发成功后再恢复代理。Caddyfile 已限定只信任 Cloudflare 官方网段
提供的 `CF-Connecting-IP`，并把真实客户端 IP 传给 API。

## TeamCity SSH 身份

建议为 TeamCity 单独生成 Ed25519 密钥。下面的私钥路径只保存在 TeamCity 主机，不进入仓库：

```powershell
New-Item -ItemType Directory -Force D:\TeamCityKeys
ssh-keygen -t ed25519 -f D:\TeamCityKeys\jokester-deploy -C teamcity-jokester
Get-Content D:\TeamCityKeys\jokester-deploy.pub |
  ssh root@<server-ip> "umask 077; mkdir -p ~/.ssh; cat >> ~/.ssh/authorized_keys"
```

在 TeamCity 项目的 `SSH Keys` 页面上传 `D:\TeamCityKeys\jokester-deploy` 私钥，然后在
Build 配置的 `Build Features` 中添加 `SSH Agent` 并选择该密钥。不要把 SSH 私钥保存为
普通文本参数。

获取服务器 SSH 公钥行：

```powershell
ssh-keyscan -p 22 -t ed25519 <server-ip>
```

应通过云控制台登录服务器，并用下面的命令核对指纹，避免直接信任未经核验的扫描结果：

```bash
ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
```

## TeamCity 参数

在 Build 配置的 `Parameters` 中添加：

| 参数 | 示例 | 类型 |
| --- | --- | --- |
| `env.DEPLOY_HOST` | `<server-ip>` | Text |
| `env.DEPLOY_PORT` | `22` | Text |
| `env.DEPLOY_USER` | `root` | Text |
| `env.DEPLOY_ROOT` | `/opt/jokester-admin` | Text |
| `env.DEPLOY_HOST_KEY` | `ssh-keyscan` 输出的完整一行 | Text |

`DEPLOY_HOST` 推荐使用服务器 IP 或专用 SSH 域名。如果 `johhai.com` 使用 CDN 代理，不能把
它作为 SSH 地址；这不影响生产配置中的 `APP_DOMAIN=johhai.com`。

## TeamCity Build Steps

保留已有的 Docker 检查步骤，然后按顺序增加两个 PowerShell 步骤。

构建镜像：

```powershell
.\deploy\teamcity-build.ps1
```

上传并部署：

```powershell
.\deploy\teamcity-deploy.ps1
```

两个步骤的 Working directory 都使用 `%teamcity.build.checkoutDir%`。部署步骤依赖构建步骤
在同一构建工作目录生成的 `artifacts/image-reference.txt`、`image-archive.txt` 和镜像 tar。

需要自动发布时，在 `Triggers` 中增加 VCS Trigger。建议先连续手动运行并完成回滚验收，
再启用 main 分支自动部署。

## 首次服务器配置

第一次运行 TeamCity 时，发布会有意停止并提示缺少：

```text
/opt/jokester-admin/.env.production
```

此时 Compose、Caddy、环境模板和数据库导入脚本已经放到服务器。登录服务器执行：

```bash
cd /opt/jokester-admin
cp .env.production.example .env.production
chmod 600 .env.production
nano .env.production
```

至少替换 MySQL、Redis、JWT、Bootstrap 和 SMTP 密钥。可以使用下面的命令生成适合连接串的
十六进制随机值：

```bash
openssl rand -hex 32
```

不要在密码中使用分号或逗号；它们会改变 MySQL/Redis 连接串的字段边界。生产 `.env.production`
只保存在服务器，并由 `root` 以 `0600` 权限持有。

## 首次迁移本地 MySQL

数据库迁移是一次性操作，不属于每次 TeamCity 发布。导出期间应停止本地写入。Windows 上使用
`--result-file`，避免旧版 PowerShell 的文本重定向改变 SQL 文件编码：

```powershell
& 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe' `
  --host=127.0.0.1 `
  --port=3306 `
  --user=<local-user> `
  --password `
  --single-transaction `
  --routines `
  --triggers `
  --events `
  --hex-blob `
  --default-character-set=utf8mb4 `
  --no-tablespaces `
  --result-file=jokester-production.sql `
  jokester.admin
```

`--password` 会交互式询问密码，不要把密码写进命令历史。通过 SSH 上传：

```powershell
scp -P 22 .\jokester-production.sql root@<server-ip>:/opt/jokester-admin/
```

在服务器上只向空数据库导入：

```bash
cd /opt/jokester-admin
./server-import-database.sh \
  /opt/jokester-admin \
  /opt/jokester-admin/jokester-production.sql \
  --confirm-empty
```

脚本会启动 MySQL/Redis、等待 MySQL 健康、确认目标库没有表，再导入并输出表数量。目标库非空时
会拒绝导入。SQL 备份可能包含用户信息和 Provider 密钥，验收完成后应人工安全移除。

## 正式发布和验收

数据库导入完成后重新点击 TeamCity `Run`。成功后检查：

```bash
docker compose \
  --project-directory /opt/jokester-admin \
  --env-file /opt/jokester-admin/.env.production \
  --file /opt/jokester-admin/docker-compose.production.yml \
  ps

curl --fail https://johhai.com/api/sites/site_code
```

容器 `jokester-mysql`、`jokester-redis`、`jokester-api` 应为 healthy，`jokester-caddy` 应为
running。还应验收登录/刷新令牌、邮件验证码、私有媒体读写和至少一个后台任务。生产 Swagger
默认关闭。

## 持久化与备份

以下内容不在 API 镜像内：

- MySQL volume：`jokester-admin_mysql-data`
- Redis volume：`jokester-admin_redis-data`
- Caddy 证书 volume：`jokester-admin_caddy-data`
- `/opt/jokester-admin/data/private-media`
- `/opt/jokester-admin/data/blog`
- `/opt/jokester-admin/data/avatar`
- `/opt/jokester-admin/data/prompt-images`
- `/opt/jokester-admin/data/data-protection`

一键发布不会替代数据库、私有媒体和 DataProtection Keys 的异机备份。正式开放前应单独配置定时
备份和恢复演练。
