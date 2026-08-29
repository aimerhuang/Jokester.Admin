#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <source-env> <production-env>" >&2
  exit 2
fi

source_env="$1"
production_env="$2"

if [[ ! -f "$source_env" ]]; then
  echo "Source environment file does not exist: $source_env" >&2
  exit 2
fi
if [[ -e "$production_env" ]]; then
  echo "Refusing to overwrite existing production environment: $production_env" >&2
  exit 3
fi

command -v openssl >/dev/null

production_directory="$(dirname -- "$production_env")"
install -d -m 0755 "$production_directory"
temporary_env="$(mktemp "$production_directory/.env.production.tmp.XXXXXX")"
trap 'rm -f -- "$temporary_env"' EXIT
umask 077

excluded_keys='^(APP_DOMAIN|ACME_EMAIL|API_PORT|JOKESTER_IMAGE|MYSQL_DATABASE|MYSQL_USER|MYSQL_PASSWORD|MYSQL_ROOT_PASSWORD|REDIS_PASSWORD|ASPNETCORE_ENVIRONMENT|ASPNETCORE_URLS|Swagger__Enabled|AllowedHosts|Database__.*|Redis__ConnectionString|Security__AllowedOrigins__.*|Security__KnownProxies__.*|AiMediaStorage__RootPath|PROMPT_IMAGE_ROOT|PROMPT_SYNC_HTTP_PROXY|PromptLibrary__HttpProxy|HTTP_PROXY|HTTPS_PROXY|ALL_PROXY|NO_PROXY|Jwt__Issuer)='

sed 's/\r$//' "$source_env" | grep -Ev "$excluded_keys" >"$temporary_env"

acme_email="$(sed 's/\r$//' "$source_env" | sed -n 's/^Mail__FromAddress=//p' | tail -n 1)"
if [[ "$acme_email" != *@* ]]; then
  acme_email='admin@johhai.com'
fi

mysql_password="$(openssl rand -hex 32)"
mysql_root_password="$(openssl rand -hex 32)"
redis_password="$(openssl rand -hex 32)"

{
  printf '\n# Cloud production overrides\n'
  printf 'APP_DOMAIN=johhai.com\n'
  printf 'ACME_EMAIL=%s\n' "$acme_email"
  printf 'API_PORT=5049\n'
  printf 'JOKESTER_IMAGE=jokester-admin:latest\n'
  printf 'MYSQL_DATABASE=jokester_admin\n'
  printf 'MYSQL_USER=jokester\n'
  printf 'MYSQL_PASSWORD=%s\n' "$mysql_password"
  printf 'MYSQL_ROOT_PASSWORD=%s\n' "$mysql_root_password"
  printf 'REDIS_PASSWORD=%s\n' "$redis_password"
  printf 'ASPNETCORE_ENVIRONMENT=Production\n'
  printf 'ASPNETCORE_URLS=http://+:8080\n'
  printf 'Swagger__Enabled=false\n'
  printf 'AllowedHosts=johhai.com\n'
  printf 'Jwt__Issuer=https://johhai.com\n'
  printf 'Security__AllowedOrigins__0=https://johhai.com\n'
  printf 'AiMediaStorage__RootPath=/data/private-media/ai\n'
  printf 'PROMPT_IMAGE_ROOT=/data/prompt-images\n'
  printf 'PROMPT_SYNC_HTTP_PROXY=\n'
} >>"$temporary_env"

if ! grep -Eq '^Jwt__SecretKey=.{32,}$' "$temporary_env"; then
  echo "Jwt__SecretKey is missing or shorter than 32 characters in the source environment." >&2
  exit 4
fi
if ! grep -Eq '^Jwt__Audience=.+$' "$temporary_env"; then
  echo "Jwt__Audience is missing in the source environment." >&2
  exit 4
fi

chmod 0600 "$temporary_env"
mv -- "$temporary_env" "$production_env"
trap - EXIT
rm -f -- "$source_env"

echo "Production environment created at $production_env"
