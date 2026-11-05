#!/usr/bin/env bash
# MySQL 官方镜像只在数据卷为空时执行本脚本（可能被 source，也可能被直接执行）。
# 1. /jokester/import 下存在 *.sql：按文件名顺序导入到 $MYSQL_DATABASE（用于迁移现有数据）。
# 2. 否则执行 /jokester/schema/jokester.admin.sql 建表和种子数据；脚本里写死的
#    CREATE DATABASE / USE `jokester.admin` 会被去掉，改为写入 $MYSQL_DATABASE。
#
# 失败时写入 $JOKESTER_INIT_FAILED_MARKER：容器自动重启后数据目录已非空、不会再次初始化，
# 健康检查据此保持 unhealthy，避免 API 连到半初始化的库。需要 reset 数据卷后重新启动。

JOKESTER_INIT_FAILED_MARKER=/var/lib/mysql/.jokester-init-failed

jokester_mysql() {
  mysql --protocol=socket --user=root --password="${MYSQL_ROOT_PASSWORD}" \
    --default-character-set=utf8mb4 "$@"
}

jokester_init_database() {
  local import_dir=/jokester/import
  local schema_file=/jokester/schema/jokester.admin.sql
  local import_files=()
  local file table_count

  if [ -d "$import_dir" ]; then
    while IFS= read -r -d '' file; do
      import_files+=("$file")
    done < <(find "$import_dir" -maxdepth 1 -type f -name '*.sql' -print0 | sort -z)
  fi

  if [ "${#import_files[@]}" -gt 0 ]; then
    for file in "${import_files[@]}"; do
      echo "[jokester-init] Importing ${file} into ${MYSQL_DATABASE}"
      jokester_mysql "$MYSQL_DATABASE" <"$file" || return 1
    done
  else
    if [ ! -f "$schema_file" ]; then
      echo "[jokester-init] Schema file is missing: ${schema_file}" >&2
      return 1
    fi
    echo "[jokester-init] Applying jokester.admin.sql to ${MYSQL_DATABASE}"
    sed \
      -e '/^CREATE DATABASE IF NOT EXISTS `jokester\.admin`/,/;[[:space:]]*$/d' \
      -e '/^USE `jokester\.admin`;/d' \
      "$schema_file" | jokester_mysql "$MYSQL_DATABASE" || return 1
  fi

  table_count="$(jokester_mysql --batch --skip-column-names \
    --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '${MYSQL_DATABASE}';")" || return 1
  if [ "${table_count:-0}" -eq 0 ]; then
    echo "[jokester-init] No tables were created in ${MYSQL_DATABASE}" >&2
    return 1
  fi
  echo "[jokester-init] Done. Tables in ${MYSQL_DATABASE}: ${table_count}"
}

if ! (set -o pipefail; jokester_init_database); then
  echo "[jokester-init] Initialization FAILED. Fix the SQL, then remove the data volumes (.\\jokester.ps1 reset) and start again." >&2
  touch "$JOKESTER_INIT_FAILED_MARKER"
  if [ "${BASH_SOURCE[0]}" = "$0" ]; then exit 1; else return 1; fi
fi
