#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 3 || "$3" != "--confirm-empty" ]]; then
  echo "Usage: $0 <deploy-root> <sql-dump> --confirm-empty" >&2
  exit 2
fi

deploy_root="$1"
sql_dump="$2"
environment_file="$deploy_root/.env.production"
compose_file="$deploy_root/docker-compose.production.yml"

if [[ ! "$deploy_root" =~ ^/[a-zA-Z0-9._/-]+$ ]]; then
  echo "Invalid deploy root: $deploy_root" >&2
  exit 2
fi
if [[ ! -f "$sql_dump" ]]; then
  echo "SQL dump does not exist: $sql_dump" >&2
  exit 2
fi
if [[ ! -f "$environment_file" || ! -f "$compose_file" ]]; then
  echo "The production environment and Compose files must be installed first." >&2
  exit 2
fi

compose=(
  docker compose
  --project-directory "$deploy_root"
  --env-file "$environment_file"
  --file "$compose_file"
)

"${compose[@]}" up --detach mysql redis

for _ in $(seq 1 60); do
  mysql_status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' jokester-mysql 2>/dev/null || true)"
  [[ "$mysql_status" == "healthy" ]] && break
  sleep 5
done
if [[ "${mysql_status:-}" != "healthy" ]]; then
  echo "MySQL did not become healthy." >&2
  exit 1
fi

table_count="$("${compose[@]}" exec -T mysql sh -ec \
  'exec mysql --batch --skip-column-names --user="$MYSQL_USER" --password="$MYSQL_PASSWORD" "$MYSQL_DATABASE" --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE();"')"
table_count="$(echo "$table_count" | tr -d '[:space:]')"
if [[ ! "$table_count" =~ ^[0-9]+$ ]]; then
  echo "Could not determine the target database table count." >&2
  exit 1
fi
if (( table_count != 0 )); then
  echo "Refusing to import into a non-empty database ($table_count tables)." >&2
  exit 4
fi

echo "Importing $sql_dump into the empty production database."
"${compose[@]}" exec -T mysql sh -ec \
  'exec mysql --user="$MYSQL_USER" --password="$MYSQL_PASSWORD" "$MYSQL_DATABASE"' <"$sql_dump"

table_count="$("${compose[@]}" exec -T mysql sh -ec \
  'exec mysql --batch --skip-column-names --user="$MYSQL_USER" --password="$MYSQL_PASSWORD" "$MYSQL_DATABASE" --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE();"')"
echo "Database import completed. Table count: $(echo "$table_count" | tr -d '[:space:]')"
echo "Keep the dump protected until production verification is complete, then remove it manually."
