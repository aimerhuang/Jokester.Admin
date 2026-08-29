#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <deploy-root> <image-archive> <image-reference>" >&2
  exit 2
fi

deploy_root="$1"
image_archive="$2"
image_reference="$3"
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
compose_file="$deploy_root/docker-compose.production.yml"
environment_file="$deploy_root/.env.production"

if [[ ! "$deploy_root" =~ ^/[a-zA-Z0-9._/-]+$ ]]; then
  echo "Invalid deploy root: $deploy_root" >&2
  exit 2
fi
if [[ ! "$image_reference" =~ ^[a-z0-9._/-]+:[a-z0-9._-]+$ ]]; then
  echo "Invalid image reference: $image_reference" >&2
  exit 2
fi
if [[ ! -f "$image_archive" ]]; then
  echo "Image archive does not exist: $image_archive" >&2
  exit 2
fi

command -v docker >/dev/null
docker compose version >/dev/null
command -v flock >/dev/null

install -d -m 0755 "$deploy_root" "$deploy_root/releases" "$deploy_root/data" "$deploy_root/frontend"
exec 9>"$deploy_root/.deploy.lock"
if ! flock -n 9; then
  echo "Another deployment is already running." >&2
  exit 3
fi

install -m 0644 "$script_directory/docker-compose.production.yml" "$compose_file"
install -m 0644 "$script_directory/Caddyfile" "$deploy_root/Caddyfile"
install -m 0644 "$script_directory/.env.production.example" "$deploy_root/.env.production.example"
install -m 0755 "$script_directory/server-import-database.sh" "$deploy_root/server-import-database.sh"
install -m 0755 "$script_directory/server-prepare-environment.sh" "$deploy_root/server-prepare-environment.sh"

for data_directory in private-media prompt-images blog avatar data-protection; do
  install -d -m 0750 "$deploy_root/data/$data_directory"
done
chown -R 1654:1654 \
  "$deploy_root/data/private-media" \
  "$deploy_root/data/prompt-images" \
  "$deploy_root/data/blog" \
  "$deploy_root/data/avatar" \
  "$deploy_root/data/data-protection"

if [[ ! -f "$environment_file" ]]; then
  echo "Production environment file is missing." >&2
  echo "A template was installed at $deploy_root/.env.production.example." >&2
  echo "Create $environment_file, set chmod 600, import the database, and run the build again." >&2
  exit 10
fi
chmod 0600 "$environment_file"

echo "Loading image $image_reference"
docker load --input "$image_archive"

previous_image="$(docker inspect --format '{{.Config.Image}}' jokester-api 2>/dev/null || true)"
export JOKESTER_IMAGE="$image_reference"
compose=(
  docker compose
  --project-directory "$deploy_root"
  --env-file "$environment_file"
  --file "$compose_file"
)

"${compose[@]}" config --quiet

wait_for_api() {
  local status
  for _ in $(seq 1 60); do
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' jokester-api 2>/dev/null || true)"
    case "$status" in
      healthy)
        return 0
        ;;
      exited|dead)
        return 1
        ;;
    esac
    sleep 5
  done
  return 1
}

rollback() {
  if [[ -z "$previous_image" || "$previous_image" == "$image_reference" ]]; then
    echo "No previous API image is available for rollback." >&2
    return 1
  fi

  echo "Rolling back API to $previous_image" >&2
  export JOKESTER_IMAGE="$previous_image"
  "${compose[@]}" up --detach --no-deps api
  wait_for_api
  "${compose[@]}" up --detach caddy
}

if ! "${compose[@]}" up --detach --remove-orphans; then
  echo "Docker Compose failed to start the release." >&2
  "${compose[@]}" logs --tail 200 api mysql redis caddy || true
  rollback || true
  exit 1
fi

if ! wait_for_api; then
  echo "API did not become healthy within five minutes." >&2
  "${compose[@]}" logs --tail 200 api mysql redis caddy || true
  rollback || true
  exit 1
fi

printf '%s\n' "$image_reference" >"$deploy_root/.deployed-image"
rm -f -- "$image_archive"

echo "Deployment succeeded: $image_reference"
"${compose[@]}" ps
