#!/usr/bin/env bash
# Jokester Docker management for Linux servers (and any bash + Docker host).
# Lives next to compose.yaml; every command runs from this directory so that
# docker compose picks up .env (COMPOSE_FILE / COMPOSE_PROJECT_NAME) automatically.
set -Eeuo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"

readonly app_uid=1654
readonly legacy_caddy_container=jokester-caddy
readonly image_pattern='^[a-z0-9._/-]+:[a-z0-9._-]+$'

die() {
  echo "error: $*" >&2
  exit 1
}

compose() {
  docker compose "$@"
}

random_hex() {
  od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
}

env_get() {
  local key="$1" file="${2:-.env}"
  [[ -f "$file" ]] || return 0
  sed 's/\r$//' "$file" | sed -n "s/^${key}=//p" | tail -n 1
}

# Replace or append KEY=VALUE without changing the file mode.
env_set() {
  local key="$1" value="$2" file="${3:-.env}" temporary
  temporary="$(mktemp "${file}.tmp.XXXXXX")"
  awk -v key="$key" -v value="$value" '
    BEGIN { done = 0 }
    index($0, key "=") == 1 { if (!done) { print key "=" value; done = 1 } next }
    { print }
    END { if (!done) print key "=" value }
  ' "$file" >"$temporary"
  chmod --reference="$file" "$temporary" 2>/dev/null || chmod 600 "$temporary"
  mv -- "$temporary" "$file"
}

copy_template() {
  local template="$1" target="$2" line
  if [[ -e "$target" ]]; then
    echo "Keep existing $target"
    return
  fi
  [[ -f "$template" ]] || die "template $template is missing"
  (
    umask 077
    while IFS= read -r line || [[ -n "$line" ]]; do
      while [[ "$line" == *__RANDOM__* ]]; do
        line="${line/__RANDOM__/$(random_hex)}"
      done
      printf '%s\n' "$line"
    done <"$template" >"$target"
  )
  echo "Created $target"
}

is_production() {
  [[ "$(env_get COMPOSE_FILE)" == *compose.production.yaml* ]]
}

require_env_files() {
  [[ -f .env && -f api.env ]] && return
  if [[ ! -f .env && ! -f api.env && -f .env.production ]]; then
    echo "Found legacy .env.production; migrating it to .env + api.env."
    migrate_from_caddy
    return
  fi
  die ".env / api.env are missing. Run './jokester.sh init production' (new server) or './jokester.sh migrate-from-caddy'."
}

prepare_data_directories() {
  is_production || return 0
  local directory cert_dir
  install -d -m 0755 data
  for directory in private-media/ai prompt-images blog avatar data-protection; do
    install -d -m 0750 "data/$directory"
  done
  chown -R "$app_uid:$app_uid" data 2>/dev/null || echo "warning: could not chown ./data to $app_uid (run as root)." >&2
  cert_dir="$(env_get TLS_CERT_DIR)"
  install -d -m 0700 "${cert_dir:-./certs}"
}

require_tls_certificate() {
  [[ "$(env_get TLS_MODE)" == "on" ]] || return 0
  local directory
  directory="$(env_get TLS_CERT_DIR)"
  directory="${directory:-./certs}"
  [[ -s "$directory/tls.crt" && -s "$directory/tls.key" ]] \
    || die "TLS_MODE=on but $directory/tls.crt and $directory/tls.key are missing. See README.md (HTTPS 证书)."
}

acquire_lock() {
  command -v flock >/dev/null || die "flock is required"
  exec 9>"$root/.deploy.lock"
  flock -n 9 || die "another deployment is already running"
}

container_exists() {
  docker container inspect "$1" >/dev/null 2>&1
}

container_running() {
  [[ "$(docker container inspect --format '{{.State.Running}}' "$1" 2>/dev/null || true)" == "true" ]]
}

smoke_test_web() {
  local domain https_port http_port
  domain="$(env_get APP_DOMAIN)"
  if [[ "$(env_get TLS_MODE)" == "on" ]]; then
    https_port="$(env_get WEB_HTTPS_PORT)"
    https_port="${https_port:-443}"
    curl --fail --silent --show-error --insecure --max-time 20 \
      --resolve "$domain:$https_port:127.0.0.1" \
      "https://$domain:$https_port/api/sites/site_code" >/dev/null || return 1
    curl --fail --silent --show-error --insecure --max-time 20 \
      --resolve "$domain:$https_port:127.0.0.1" \
      "https://$domain:$https_port/" | grep -qi '<div id="app"' || return 1
  else
    http_port="$(env_get WEB_PORT)"
    http_port="${http_port:-80}"
    curl --fail --silent --show-error --max-time 20 --header "Host: $domain" \
      "http://127.0.0.1:$http_port/api/sites/site_code" >/dev/null || return 1
  fi
}

migrate_from_caddy() {
  local legacy=.env.production key value line api_image domain origin
  [[ -f "$legacy" ]] || die "$legacy not found in $root"
  [[ ! -e .env && ! -e api.env ]] || die ".env or api.env already exists; refusing to overwrite."

  copy_template .env.production.example .env
  for key in APP_DOMAIN MYSQL_DATABASE MYSQL_USER MYSQL_PASSWORD MYSQL_ROOT_PASSWORD REDIS_PASSWORD; do
    value="$(env_get "$key" "$legacy")"
    [[ -n "$value" ]] || die "$key is missing in $legacy"
    env_set "$key" "$value"
  done
  domain="$(env_get APP_DOMAIN)"
  origin="$(env_get Security__AllowedOrigins__0 "$legacy")"
  env_set PUBLIC_ORIGIN "${origin:-https://$domain}"

  api_image="$(docker container inspect --format '{{.Config.Image}}' jokester-api 2>/dev/null || true)"
  [[ -n "$api_image" ]] || api_image="$(cat .deployed-image 2>/dev/null || true)"
  env_set API_IMAGE "$api_image"

  # Everything else in the legacy file is application configuration.
  local excluded='^(APP_DOMAIN|ACME_EMAIL|API_PORT|JOKESTER_IMAGE|MYSQL_[A-Z_]+|REDIS_PASSWORD|COMPOSE_[A-Z_]+|TZ|ASPNETCORE_URLS|AllowedHosts|Database__.*|Redis__ConnectionString|Security__AllowedOrigins__0|Security__KnownProxies__.*|AiMediaStorage__RootPath|PROMPT_IMAGE_ROOT)='
  (
    umask 077
    {
      echo "# Migrated from .env.production on $(date -u +%Y-%m-%dT%H:%M:%SZ)."
      echo "# Compose-owned keys (connection strings, hosts, proxies, paths) live in .env / compose.yaml."
      sed 's/\r$//' "$legacy" | grep -Ev "$excluded" | grep -Ev '^[[:space:]]*$'
    } >api.env
  )
  echo "Created .env and api.env from $legacy (the legacy file is kept)."
}

deploy_api() {
  local archive="${1:-}" image="${2:-}" previous
  [[ -f "$archive" ]] || die "usage: deploy-api <image-archive.tar> <image-reference>"
  [[ "$image" =~ $image_pattern ]] || die "invalid image reference: $image"
  acquire_lock
  require_env_files
  prepare_data_directories

  echo "Loading $image"
  docker load --input "$archive"
  previous="$(env_get API_IMAGE)"
  env_set API_IMAGE "$image"
  compose config --quiet

  if ! compose up --detach --wait --wait-timeout 300 mysql redis api; then
    echo "API $image did not become healthy." >&2
    compose logs --tail 200 api mysql redis || true
    env_set API_IMAGE "$previous"
    if [[ -n "$previous" && "$previous" != "$image" ]]; then
      echo "Rolling back API to $previous" >&2
      compose up --detach --wait --wait-timeout 300 api || true
    fi
    exit 1
  fi

  rm -f -- "$archive"
  echo "API deployed: $image"
  compose ps
}

deploy_web() {
  local archive="${1:-}" image="${2:-}" previous cert_dir legacy_stopped=0 tls_arguments=()
  [[ -f "$archive" ]] || die "usage: deploy-web <image-archive.tar> <image-reference>"
  [[ "$image" =~ $image_pattern ]] || die "invalid image reference: $image"
  command -v curl >/dev/null || die "curl is required"
  acquire_lock
  require_env_files
  [[ -n "$(env_get API_IMAGE)" ]] || die "API_IMAGE is empty; deploy the API first."
  require_tls_certificate

  echo "Loading $image"
  docker load --input "$archive"

  # Validate the rendered nginx configuration (including certificates) before touching traffic.
  if [[ "$(env_get TLS_MODE)" == "on" ]]; then
    cert_dir="$(env_get TLS_CERT_DIR)"
    cert_dir="$(cd "${cert_dir:-./certs}" && pwd)"
    tls_arguments=(--volume "$cert_dir:/etc/nginx/certs:ro")
  fi
  docker run --rm --network none --add-host api:127.0.0.1 \
    --env APP_DOMAIN="$(env_get APP_DOMAIN)" \
    --env TLS_MODE="$(env_get TLS_MODE)" \
    --env REAL_IP_MODE="$(env_get REAL_IP_MODE)" \
    "${tls_arguments[@]}" \
    "$image" nginx -t

  previous="$(env_get WEB_IMAGE)"
  env_set WEB_IMAGE "$image"
  compose config --quiet

  # The legacy Caddy container owns ports 80/443 and the web IP until we replace it.
  if container_running "$legacy_caddy_container"; then
    echo "Stopping legacy $legacy_caddy_container"
    docker stop "$legacy_caddy_container" >/dev/null
    legacy_stopped=1
  fi

  if ! compose up --detach --wait --wait-timeout 180 --no-deps web || ! smoke_test_web; then
    echo "Web $image failed to start or pass the smoke test." >&2
    compose logs --tail 200 web || true
    env_set WEB_IMAGE "$previous"
    if [[ -n "$previous" && "$previous" != "$image" ]]; then
      echo "Rolling back web to $previous" >&2
      compose up --detach --wait --wait-timeout 180 --no-deps web || true
    else
      compose rm --stop --force web || true
    fi
    if (( legacy_stopped )); then
      echo "Restarting legacy $legacy_caddy_container" >&2
      docker start "$legacy_caddy_container" >/dev/null || true
    fi
    exit 1
  fi

  if container_exists "$legacy_caddy_container"; then
    docker rm --force "$legacy_caddy_container" >/dev/null
    echo "Removed legacy $legacy_caddy_container"
  fi
  rm -f -- "$archive"
  echo "Web deployed: $image"
  compose ps
}

seed_admin() {
  local user_name="${BootstrapAdmin__UserName:-}" password="${BootstrapAdmin__Password:-}"
  if [[ -z "$user_name" ]]; then
    read -r -p 'Super admin account (user name or email): ' user_name
  fi
  if [[ -z "$password" ]]; then
    read -r -s -p 'Password: ' password
    echo
  fi
  [[ -n "$user_name" && -n "$password" ]] || die "account and password are required"
  # Credentials travel through the environment, never through process arguments.
  BootstrapAdmin__UserName="$user_name" BootstrapAdmin__Password="$password" \
    compose run --rm --no-TTY --env BootstrapAdmin__UserName --env BootstrapAdmin__Password api --seed-admin
}

configure_legal() {
  local arguments=() line platform
  [[ -f legal.env ]] || die "legal.env is missing. Copy legal.env.example to legal.env and fill in the approved values."
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" == \#* || "$line" != *=* ]] && continue
    arguments+=(--env "$line")
  done <legal.env
  for platform in web all; do
    compose run --rm --no-TTY "${arguments[@]}" --env "LegalDocuments__Platform=$platform" api --configure-legal-documents
  done
}

usage() {
  cat <<'EOF'
Usage: ./jokester.sh <command> [arguments]

  init [local|production]       Create .env / api.env from templates with random secrets (default: production)
  migrate-from-caddy            Convert the legacy Caddy deployment's .env.production into .env + api.env
  up [service...]               Start services and wait until healthy (add --build for local source builds)
  down                          Stop and remove containers (volumes and ./data are kept)
  restart [service...]          Restart services
  ps                            Show container status
  logs [service...]             Follow logs (default: api)
  seed-admin                    Create or reset a super admin (prompts; email accounts supported)
  legal                         Configure legal documents from legal.env (web, then all)
  sql <file>                    Run a SQL file against the database, e.g. a docs/migrations script
  mysql                         Open a MySQL client
  backup [file]                 Dump the database (default: backups/jokester-<time>.sql, mode 600)
  cli <args...>                 Run a one-off API command, e.g. cli --migrate-ai-media --dry-run
  deploy-api <tar> <image>      Load an API image, switch to it and roll back if it is unhealthy
  deploy-web <tar> <image>      Load a web image, switch to it (replacing legacy Caddy) and roll back on failure
  reset                         Remove containers AND named volumes (asks for confirmation)
EOF
}

command="${1:-help}"
shift || true

case "$command" in
  init)
    case "${1:-production}" in
      local)
        copy_template .env.example .env
        copy_template api.env.example api.env
        ;;
      production)
        copy_template .env.production.example .env
        copy_template api.env.production.example api.env
        ;;
      *) die "usage: init [local|production]" ;;
    esac
    echo "Review .env and api.env before starting."
    ;;
  migrate-from-caddy) migrate_from_caddy ;;
  up)
    require_env_files
    prepare_data_directories
    compose up --detach --wait "$@"
    ;;
  down) compose down ;;
  restart) compose restart "$@" ;;
  ps) compose ps ;;
  logs)
    if (( $# == 0 )); then set -- api; fi
    compose logs --follow --tail 200 "$@"
    ;;
  seed-admin) require_env_files; seed_admin ;;
  legal) require_env_files; configure_legal ;;
  sql)
    [[ $# -eq 1 && -f "$1" ]] || die "usage: sql <file.sql>"
    compose exec -T mysql sh -c 'exec mysql --default-character-set=utf8mb4 --user=root --password="$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"' <"$1"
    echo "Applied $1"
    ;;
  mysql)
    compose exec mysql sh -c 'exec mysql --default-character-set=utf8mb4 --user=root --password="$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"'
    ;;
  backup)
    target="${1:-backups/jokester-$(date +%Y%m%d-%H%M%S).sql}"
    install -d -m 0700 "$(dirname -- "$target")"
    (
      umask 077
      compose exec -T mysql sh -c 'exec mysqldump --user=root --password="$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers --events --hex-blob --default-character-set=utf8mb4 --no-tablespaces "$MYSQL_DATABASE"' >"$target"
    )
    echo "Database exported to $target"
    ;;
  cli)
    (( $# > 0 )) || die "usage: cli <api arguments>"
    require_env_files
    compose run --rm --no-TTY api "$@"
    ;;
  deploy-api) deploy_api "$@" ;;
  deploy-web) deploy_web "$@" ;;
  reset)
    project="$(env_get COMPOSE_PROJECT_NAME)"
    read -r -p "This deletes MySQL and Redis volumes of '$project'. Type the project name to continue: " answer
    [[ "$answer" == "$project" && -n "$project" ]] || { echo "Cancelled."; exit 0; }
    compose down --volumes --remove-orphans
    ;;
  help|-h|--help) usage ;;
  *) usage; exit 2 ;;
esac
