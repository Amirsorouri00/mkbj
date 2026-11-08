#!/usr/bin/env bash
#
# Brings up the production stack and proves the things a release has to get right.
#
# This is deliberately not a test suite. The suite runs against a test host with a Testcontainers
# database; this runs the real image, built by the real Dockerfile, wired by the real compose
# file, migrated by the real --migrate, against the real roles script. Everything it checks has
# been checked already somewhere else — what is new is that it is checked *here*, in the
# arrangement that actually ships.
#
# Usage:  tools/smoke/smoke.sh [--keep]
#   --keep   leave the stack running afterwards, for poking at a failure

set -Eeuo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly COMPOSE_DIR="${ROOT}/build/compose"
readonly ENV_FILE="${COMPOSE_DIR}/.env.smoke"

KEEP=0
[[ "${1:-}" == "--keep" ]] && KEEP=1

readonly API_PORT="${SMOKE_API_PORT:-18080}"
readonly API="http://127.0.0.1:${API_PORT}"

compose() {
  docker compose \
    -f "${COMPOSE_DIR}/docker-compose.yml" \
    -f "${COMPOSE_DIR}/docker-compose.smoke.yml" \
    --env-file "${ENV_FILE}" "$@"
}

PASSED=0
FAILED=0

check() {
  local what="$1"; shift

  if "$@" >/dev/null 2>&1; then
    printf '  ok    %s\n' "${what}"
    PASSED=$((PASSED + 1))
  else
    printf '  FAIL  %s\n' "${what}"
    FAILED=$((FAILED + 1))
  fi
}

# Every exit path tears the stack down, including a failure partway through: a smoke run that
# leaves containers and volumes behind makes the next one start from a state nobody chose.
cleanup() {
  local status=$?

  if [[ ${KEEP} -eq 1 ]]; then
    printf '\nLeaving the stack up (--keep). Tear down with:\n  %s\n' \
      "docker compose -f build/compose/docker-compose.yml -f build/compose/docker-compose.smoke.yml --env-file build/compose/.env.smoke down -v"
  else
    printf '\nTearing down...\n'
    compose down -v --remove-orphans >/dev/null 2>&1 || true
  fi

  exit "${status}"
}
trap cleanup EXIT

# --- Secrets, generated per run -------------------------------------------------------------
#
# Never committed and never reused. A smoke stack with a known password is a smoke stack
# somebody eventually leaves running.
generate() { head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32; }

# Every variable compose marks required, generated fresh. Derived from the compose file
# when this block was written; a newly required variable fails the run at `up`, loudly,
# which is the right way to find out.
printf 'Writing %s\n' "${ENV_FILE}"
cat > "${ENV_FILE}" <<EOF
ACME_EMAIL=smoke@example.invalid
API_KEYS_PEPPER=$(generate)
APP_DB_PASSWORD=$(generate)
AUTH_HOST=localhost
KC_ADMIN_WEB_ORIGIN=http://127.0.0.1:18080
KC_API_PUBLIC_URL=http://127.0.0.1:18081
KC_BOOTSTRAP_ADMIN_PASSWORD=$(generate)
KC_BOOTSTRAP_ADMIN_USERNAME=admin
KC_DB_PASSWORD=$(generate)
KC_SHOP_WEB_ORIGIN=http://127.0.0.1:18080
KC_SMTP_FROM=smoke@example.invalid
KC_SMTP_FROM_DISPLAY_NAME=Eways Smoke
KC_SMTP_HOST=mailpit
KC_SMTP_PASSWORD=
KC_SMTP_PORT=1025
KC_SMTP_STARTTLS=false
KC_SMTP_USER=
MIGRATOR_DB_PASSWORD=$(generate)
MINIO_APP_ACCESS_KEY=smokeapp
MINIO_APP_SECRET_KEY=$(generate)
MINIO_ROOT_PASSWORD=$(generate)
MINIO_ROOT_USER=smokeroot
POSTGRES_PASSWORD=$(generate)
PUBLIC_HOST=localhost
STORAGE_HOST=localhost
BRAVE_API_KEY=
ANTHROPIC_API_KEY=
EOF
chmod 600 "${ENV_FILE}"

# --- The stack ------------------------------------------------------------------------------

printf '\nBuilding and starting the stack...\n'
compose up -d --build

printf '\nWaiting for the API to report ready'
ready=0
for _ in $(seq 1 60); do
  if curl -fsS "${API}/health/ready" >/dev/null 2>&1; then ready=1; break; fi
  printf '.'
  sleep 5
done
printf '\n'

if [[ ${ready} -ne 1 ]]; then
  printf '\nThe API never became ready. Last 50 lines:\n\n'
  compose logs --tail 50 api
  exit 1
fi

printf '\nSeeding the demo catalogue...\n'
compose run --rm --entrypoint dotnet api EwaysVNext.Host.dll --seed

# --- What a release has to get right ---------------------------------------------------------

printf '\nChecks:\n'

check "health/live answers" \
  curl -fsS "${API}/health/live"

check "health/ready answers" \
  curl -fsS "${API}/health/ready"

# The partner contract is public, and this is the one environment that proves it is served
# outside Development.
check "the OpenAPI contract is published" \
  curl -fsS "${API}/openapi/v1.json"

check "the contract names the partner key scheme" \
  bash -c "curl -fsS '${API}/openapi/v1.json' | grep -q PartnerApiKey"

# The storefront is anonymous on purpose: a shop that needs a token to show a product page is a
# shop nobody indexes and nobody links to.
check "the storefront answers without a token" \
  curl -fsS "${API}/api/v1/shop/categories"

check "the storefront has products after seeding" \
  bash -c "curl -fsS '${API}/api/v1/shop/products?currency=USD' | grep -q productId"

check "GraphQL answers an anonymous storefront query" \
  bash -c "curl -fsS -X POST '${API}/graphql' -H 'content-type: application/json' \
    -d '{\"query\":\"query { shopCategories { id name } }\"}' | grep -q shopCategories"

# The other half of the same rule: everything that is not the storefront must refuse.
check "an admin route refuses an anonymous caller" \
  bash -c "[[ \$(curl -s -o /dev/null -w '%{http_code}' '${API}/api/v1/admin/dashboard') == 401 ]]"

check "a partner route refuses an anonymous caller" \
  bash -c "[[ \$(curl -s -o /dev/null -w '%{http_code}' '${API}/api/v1/orders') == 401 ]]"

check "a partner route refuses a bogus key" \
  bash -c "[[ \$(curl -s -o /dev/null -w '%{http_code}' -H 'Authorization: Bearer pk_nope_nope' \
    '${API}/api/v1/orders') == 401 ]]"

# Scalar is a tool for this team, not part of the contract; it must not ship.
check "the browsable API UI is not exposed" \
  bash -c "[[ \$(curl -s -o /dev/null -w '%{http_code}' '${API}/scalar/v1') != 200 ]]"

check "every response carries a correlation id" \
  bash -c "curl -fsS -D - -o /dev/null '${API}/health/live' | grep -qi 'x-correlation-id'"

# The application role owns nothing: it can read and write rows and cannot change the schema.
# This is the check that proves the roles script ran in the arrangement that ships.
check "the application role cannot create tables" \
  bash -c "compose exec -T -e PGPASSWORD=\$(grep '^APP_DB_PASSWORD=' '${ENV_FILE}' | cut -d= -f2) \
    postgres psql -U eways_app -d eways -c 'CREATE TABLE smoke_should_fail (id int);' 2>&1 \
    | grep -qi 'permission denied'"

check "the worker container is running" \
  bash -c "[[ \$(compose ps -q worker | wc -l) -gt 0 ]]"

printf '\n%d passed, %d failed\n' "${PASSED}" "${FAILED}"

[[ ${FAILED} -eq 0 ]]
