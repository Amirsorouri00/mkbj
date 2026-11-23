#!/usr/bin/env bash
set -euo pipefail

root=/opt/mkbj
compose_file=$root/build/compose/docker-compose.yml
low_memory_compose_file=$root/build/compose/docker-compose.low-memory.yml
env_file=$root/build/compose/.env
release_file=$root/build/compose/.release.env

[[ -f $env_file ]] || { echo "Missing $env_file; run the Ubuntu VM bootstrap first." >&2; exit 1; }
[[ -f $release_file ]] || { echo "Missing $release_file from the deployment workflow." >&2; exit 1; }

demo_tier=$(sed -n 's/^MKBJ_DEMO_TIER=//p' "$env_file" | tail -1)
compose_files=(-f "$compose_file")
runtime_services=(api worker admin-web shop-web caddy)
if [[ $demo_tier == low-memory ]]; then
  compose_files+=(-f "$low_memory_compose_file")
  runtime_services=(api admin-web shop-web caddy)
  echo 'Using the constrained 2 GB demo profile; background jobs run inside the API.'
fi
compose=(docker compose "${compose_files[@]}" --env-file "$env_file" --env-file "$release_file")

# Phase 1 intentionally accepts a short outage. Stop callers before applying expand-only schema
# changes, then seed the demo catalogue idempotently before either web application is exposed.
"${compose[@]}" stop caddy api worker admin-web shop-web 2>/dev/null || true
"${compose[@]}" pull
# A fresh Keycloak database on the constrained 1-vCPU demo VM imports two realms and builds its
# Quarkus configuration. It can legitimately take longer than three minutes before the first
# readiness response, even though the steady-state service starts much faster.
# The mirrored infrastructure images carry the release SHA, so Compose recreates their database
# containers on every release. Recreate their dependants in the same operation: leaving an old
# Keycloak process running while keycloak-db is replaced strands it in an unhealthy connection
# state even after the database is back. Named volumes remain intact.
"${compose[@]}" up -d --wait --wait-timeout 360 --no-build --force-recreate \
  postgres keycloak-db keycloak minio
"${compose[@]}" run --rm --no-deps minio-init
"${compose[@]}" --profile demo-seed run --rm --no-deps keycloak-demo-users
"${compose[@]}" run --rm --no-deps migrations
"${compose[@]}" --profile demo-seed run --rm --no-deps seed
"${compose[@]}" up -d --no-build --remove-orphans "${runtime_services[@]}"
"${compose[@]}" up -d --wait --wait-timeout 360 --no-build "${runtime_services[@]}"

# Keep two recent image generations locally for a quick manual rollback; discard older layers.
docker image prune -f --filter 'until=168h'

set -a
# shellcheck disable=SC1090
source "$env_file"
set +a
curl --fail --silent --show-error --retry 12 --retry-delay 5 "https://$PUBLIC_HOST/health/ready" >/dev/null
curl --fail --silent --show-error --retry 12 --retry-delay 5 "https://$ADMIN_HOST/" >/dev/null
curl --fail --silent --show-error --retry 12 --retry-delay 5 "https://$SHOP_HOST/" >/dev/null
echo "Demo deployment is healthy: https://$SHOP_HOST and https://$ADMIN_HOST"

