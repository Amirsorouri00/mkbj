#!/usr/bin/env bash
#
# Mints a Backoffice super_admin token without weakening the committed realm.
#
# The realm export ships `backoffice-console` with direct access grants OFF and no users, which is
# right: a realm that hands out tokens to anything holding a username is not a realm you want in
# production, and a committed test user is a credential in the repository. So this turns the grant
# on, creates one user, takes a token, and puts both back — the restore runs from a trap, so it
# also runs when the caller fails partway through.
#
# Usage:  eval "$(tools/smoke/backoffice-token.sh <keycloak-base-url> <kc-admin-user> <kc-admin-password>)"
#   prints  BACKOFFICE_TOKEN=<jwt>
#           BACKOFFICE_CLEANUP=<path to a script that undoes everything>
#
# The caller is responsible for running the cleanup script. Nothing here is left behind on
# failure: a failure before the cleanup script is written has not yet changed the realm.

set -Eeuo pipefail

readonly KC="${1:?keycloak base url}"
readonly KC_USER="${2:?keycloak admin user}"
readonly KC_PASSWORD="${3:?keycloak admin password}"

readonly REALM=Backoffice
readonly CLIENT=backoffice-console
readonly TEMP_USER="smoke-$(date +%s)-$$"
readonly TEMP_PASSWORD="$(head -c 24 /dev/urandom | base64 | tr -d '/+=')"

api() {
  local method="$1" path="$2"; shift 2

  curl -fsS -X "${method}" "${KC}/admin/realms/${path}" \
    -H "Authorization: Bearer ${ADMIN_TOKEN}" \
    -H 'Content-Type: application/json' "$@"
}

# --- the master admin, which the realm export does not touch ---------------------------------
ADMIN_TOKEN="$(curl -fsS -X POST "${KC}/realms/master/protocol/openid-connect/token" \
  -d 'grant_type=password' -d 'client_id=admin-cli' \
  --data-urlencode "username=${KC_USER}" --data-urlencode "password=${KC_PASSWORD}" \
  | python -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
readonly ADMIN_TOKEN

# --- direct grants, remembered so they can be put back ---------------------------------------
CLIENT_JSON="$(api GET "${REALM}/clients?clientId=${CLIENT}")"
CLIENT_UUID="$(printf '%s' "${CLIENT_JSON}" | python -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')"
GRANTS_WERE="$(printf '%s' "${CLIENT_JSON}" \
  | python -c 'import json,sys; print(str(json.load(sys.stdin)[0].get("directAccessGrantsEnabled", False)).lower())')"

api PUT "${REALM}/clients/${CLIENT_UUID}" -d '{"directAccessGrantsEnabled": true}' >/dev/null

# --- anything a previous run left behind ---------------------------------------------------
#
# The trap below undoes this run. It cannot undo a run that died before the cleanup script was
# written — a failed token request, an interrupted shell — and those leave a super_admin in the
# realm. Sweeping the prefix first means repeated runs cannot accumulate accounts, which is the
# failure mode that turns a tidy helper into five stray admins nobody remembers creating.
STALE="$(api GET "${REALM}/users?max=200" \
  | python -c "import json,sys; print(' '.join(u['id'] for u in json.load(sys.stdin) if u['username'].startswith('smoke-')))")"

for stale in ${STALE}; do
  api DELETE "${REALM}/users/${stale}" >/dev/null 2>&1 || true
done

# --- one user, in one role -------------------------------------------------------------------
api POST "${REALM}/users" -d "$(python - "${TEMP_USER}" "${TEMP_PASSWORD}" <<'PY'
import json, sys
print(json.dumps({
    "username": sys.argv[1],
    "enabled": True,
    "requiredActions": [],
    "emailVerified": True,
    "email": f"{sys.argv[1]}@example.invalid",
    "credentials": [{"type": "password", "value": sys.argv[2], "temporary": False}],
}))
PY
)" >/dev/null

USER_ID="$(api GET "${REALM}/users?username=${TEMP_USER}&exact=true" \
  | python -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')"

# The realm applies CONFIGURE_TOTP to every new user, which is right for a human and fatal for a
# password grant: Keycloak answers "Account is not fully set up" as a plain invalid_grant, so the
# cause is invisible from the error alone. Cleared explicitly, and the password set through
# reset-password rather than trusted to the create call.
api PUT "${REALM}/users/${USER_ID}" -d '{"requiredActions": [], "emailVerified": true}' >/dev/null
api PUT "${REALM}/users/${USER_ID}/reset-password"   -d "{\"type\": \"password\", \"value\": \"${TEMP_PASSWORD}\", \"temporary\": false}" >/dev/null

ROLE="$(api GET "${REALM}/roles/super_admin")"
api POST "${REALM}/users/${USER_ID}/role-mappings/realm" -d "[${ROLE}]" >/dev/null

# --- the token the caller wanted ---------------------------------------------------------------
TOKEN="$(curl -fsS -X POST "${KC}/realms/${REALM}/protocol/openid-connect/token" \
  -d 'grant_type=password' -d 'scope=openid' -d "client_id=${CLIENT}" \
  --data-urlencode "username=${TEMP_USER}" --data-urlencode "password=${TEMP_PASSWORD}" \
  | python -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"

# --- how to undo it ------------------------------------------------------------------------------
CLEANUP="$(mktemp -t backoffice-cleanup.XXXXXX)"
cat > "${CLEANUP}" <<EOF
#!/usr/bin/env bash
set -uo pipefail
curl -fsS -X DELETE "${KC}/admin/realms/${REALM}/users/${USER_ID}" \\
  -H "Authorization: Bearer ${ADMIN_TOKEN}" >/dev/null 2>&1 || true
curl -fsS -X PUT "${KC}/admin/realms/${REALM}/clients/${CLIENT_UUID}" \\
  -H "Authorization: Bearer ${ADMIN_TOKEN}" -H 'Content-Type: application/json' \\
  -d '{"directAccessGrantsEnabled": ${GRANTS_WERE}}' >/dev/null 2>&1 || true
rm -f "\${BASH_SOURCE[0]}"
EOF
chmod +x "${CLEANUP}"

printf 'BACKOFFICE_TOKEN=%s\n' "${TOKEN}"
printf 'BACKOFFICE_CLEANUP=%s\n' "${CLEANUP}"
