#!/usr/bin/env bash
set -euo pipefail

# Idempotent bootstrap for an Ubuntu VM (OCI, Liara, ArvanCloud, or a conventional VPS). The workflow passes
# the demo identities as base64 environment values, so they never appear in a process argument.

if [[ -n ${BOOTSTRAP_ENV_FILE:-} ]]; then
  set -a
  # The workflow writes only base64 values and simple host metadata to this root-readable file.
  # shellcheck disable=SC1090
  source "$BOOTSTRAP_ENV_FILE"
  set +a
fi

if [[ ${EUID} -ne 0 ]]; then
  exec sudo -E bash "$0" "$@"
fi

: "${DEMO_PUBLIC_IP:?DEMO_PUBLIC_IP is required}"
: "${MKBJ_DEMO_ADMIN_USERNAME_B64:?admin username is required}"
: "${MKBJ_DEMO_ADMIN_EMAIL_B64:?admin email is required}"
: "${MKBJ_DEMO_ADMIN_PASSWORD_B64:?admin password is required}"
: "${MKBJ_DEMO_SHOP_USERNAME_B64:?shop username is required}"
: "${MKBJ_DEMO_SHOP_EMAIL_B64:?shop email is required}"
: "${MKBJ_DEMO_SHOP_PASSWORD_B64:?shop password is required}"

decode() { printf '%s' "$1" | base64 --decode; }
admin_username=$(decode "$MKBJ_DEMO_ADMIN_USERNAME_B64")
admin_email=$(decode "$MKBJ_DEMO_ADMIN_EMAIL_B64")
admin_password=$(decode "$MKBJ_DEMO_ADMIN_PASSWORD_B64")
shop_username=$(decode "$MKBJ_DEMO_SHOP_USERNAME_B64")
shop_email=$(decode "$MKBJ_DEMO_SHOP_EMAIL_B64")
shop_password=$(decode "$MKBJ_DEMO_SHOP_PASSWORD_B64")

enable_https=${MKBJ_DEMO_ENABLE_HTTPS:-false}
case "$enable_https" in
  true)
    : "${ACME_EMAIL:?ACME_EMAIL is required when HTTPS is enabled}"
    public_scheme=https
    ;;
  false)
    public_scheme=http
    ;;
  *)
    echo 'MKBJ_DEMO_ENABLE_HTTPS must be true or false.' >&2
    exit 64
    ;;
esac

safe_value='^[A-Za-z0-9@._+!-]+$'
for value in "$admin_username" "$admin_email" "$admin_password" "$shop_username" "$shop_email" "$shop_password"; do
  if [[ ! $value =~ $safe_value ]]; then
    echo 'Demo identity values may contain only letters, numbers, @ . _ + ! and -.' >&2
    exit 64
  fi
done
if (( ${#admin_password} < 14 || ${#shop_password} < 10 )); then
  echo 'Admin password must be at least 14 characters and shop password at least 10.' >&2
  exit 64
fi

if ! command -v docker >/dev/null 2>&1; then
  apt-get update
  apt-get install -y ca-certificates curl gnupg openssl
  . /etc/os-release
  if curl -fsSL --max-time 20 "https://download.docker.com/linux/ubuntu/dists/$VERSION_CODENAME/Release" >/dev/null; then
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    printf 'Types: deb\nURIs: https://download.docker.com/linux/ubuntu\nSuites: %s\nComponents: stable\nSigned-By: /etc/apt/keyrings/docker.asc\n' "$VERSION_CODENAME" > /etc/apt/sources.list.d/docker.sources
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  else
    echo "Docker has no repository for Ubuntu $VERSION_CODENAME; using Ubuntu's packages."
    apt-get install -y docker.io docker-compose-v2
  fi
  systemctl enable --now docker
fi

demo_tier=${MKBJ_DEMO_TIER:-standard}
if [[ $demo_tier == low-memory && ! -f /swapfile ]]; then
  echo 'Creating a 4 GB swap file for the low-memory demo tier.'
  fallocate -l 4G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=4096 status=progress
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  printf '/swapfile none swap sw 0 0\n' >> /etc/fstab
  printf 'vm.swappiness=20\n' > /etc/sysctl.d/99-mkbj-demo.conf
  sysctl --system >/dev/null
fi

install -d -m 0750 /opt/mkbj/build/compose
env_file=/opt/mkbj/build/compose/.env
state_dir=/var/lib/mkbj
state_env_file=$state_dir/demo.env
install -d -m 0700 "$state_dir"

host_ip=${DEMO_PUBLIC_IP//./-}
base_domain=${DEMO_BASE_DOMAIN:-$host_ip.sslip.io}

upsert_env() {
  local file="$1" key="$2" value="$3"
  if grep -q "^${key}=" "$file"; then
    sed -i "s|^${key}=.*$|${key}=${value}|" "$file"
  else
    printf '%s=%s\n' "$key" "$value" >> "$file"
  fi
}

configure_public_transport() {
  local file="$1"
  upsert_env "$file" PUBLIC_SCHEME "$public_scheme"
  upsert_env "$file" MKBJ_DEMO_ENABLE_HTTPS "$enable_https"
  upsert_env "$file" ACME_EMAIL "${ACME_EMAIL:-}"
  upsert_env "$file" KC_SHOP_WEB_ORIGIN "$public_scheme://shop.$base_domain"
  upsert_env "$file" KC_ADMIN_WEB_ORIGIN "$public_scheme://admin.$base_domain"
  upsert_env "$file" KC_API_PUBLIC_URL "$public_scheme://api.$base_domain"
  upsert_env "$file" SHOP_REDIRECT_URI "$public_scheme://shop.$base_domain/*"
  upsert_env "$file" BACKOFFICE_REDIRECT_URI "$public_scheme://admin.$base_domain/*"
}

# Releases replace /opt/mkbj as one directory. Keep generated secrets outside that release tree
# so a failed or interrupted swap cannot rotate database passwords while retaining their volumes.
if [[ ! -f $env_file && -f $state_env_file ]]; then
  install -m 0600 "$state_env_file" "$env_file"
fi

if [[ -n ${GHCR_USERNAME_B64:-} && -n ${GHCR_TOKEN_B64:-} ]]; then
  ghcr_username=$(decode "$GHCR_USERNAME_B64")
  decode "$GHCR_TOKEN_B64" | docker login ghcr.io --username "$ghcr_username" --password-stdin
fi

# Preserve generated credentials for persistent volumes and existing demo-user passwords. Only the
# explicitly selected public transport and its derived browser URLs are reconciled on later releases.
if [[ -f $env_file ]]; then
  configure_public_transport "$env_file"
  install -m 0600 "$env_file" "$state_env_file"
  echo "Preserving existing $env_file and using $public_scheme for public endpoints"
  exit 0
fi

random_hex() { openssl rand -hex "$1"; }

umask 077
cat > "$env_file" <<EOF
PUBLIC_SCHEME=$public_scheme
PUBLIC_HOST=api.$base_domain
ADMIN_HOST=admin.$base_domain
SHOP_HOST=shop.$base_domain
AUTH_HOST=auth.$base_domain
STORAGE_HOST=storage.$base_domain
ACME_EMAIL=${ACME_EMAIL:-}
APP_ENVIRONMENT=Staging
MKBJ_DEMO_TIER=$demo_tier
MKBJ_DEMO_ENABLE_HTTPS=$enable_https

POSTGRES_PASSWORD=$(random_hex 24)
APP_DB_PASSWORD=$(random_hex 24)
MIGRATOR_DB_PASSWORD=$(random_hex 24)

MINIO_ROOT_USER=mkbjroot
MINIO_ROOT_PASSWORD=$(random_hex 24)
MINIO_APP_ACCESS_KEY=mkbjapp
MINIO_APP_SECRET_KEY=$(random_hex 24)

KC_DB_PASSWORD=$(random_hex 24)
KC_BOOTSTRAP_ADMIN_USERNAME=kcadmin
KC_BOOTSTRAP_ADMIN_PASSWORD=$(random_hex 24)
KC_SMTP_HOST=localhost
KC_SMTP_PORT=1025
KC_SMTP_FROM=no-reply@$base_domain
KC_SMTP_USER=
KC_SMTP_PASSWORD=
KC_SMTP_FROM_DISPLAY_NAME=MKBJ-Demo
KC_SMTP_STARTTLS=false
KC_SMTP_AUTH=false
KC_SHOP_WEB_ORIGIN=$public_scheme://shop.$base_domain
KC_ADMIN_WEB_ORIGIN=$public_scheme://admin.$base_domain
KC_API_PUBLIC_URL=$public_scheme://api.$base_domain
SHOP_REDIRECT_URI=$public_scheme://shop.$base_domain/*
BACKOFFICE_REDIRECT_URI=$public_scheme://admin.$base_domain/*

MKBJ_DEMO_ADMIN_USERNAME=$admin_username
MKBJ_DEMO_ADMIN_EMAIL=$admin_email
MKBJ_DEMO_ADMIN_PASSWORD=$admin_password
MKBJ_DEMO_SHOP_USERNAME=$shop_username
MKBJ_DEMO_SHOP_EMAIL=$shop_email
MKBJ_DEMO_SHOP_PASSWORD=$shop_password

API_KEYS_PEPPER=$(random_hex 32)
BRAVE_API_KEY=
ANTHROPIC_API_KEY=
EOF

chmod 0600 "$env_file"
install -m 0600 "$env_file" "$state_env_file"
echo "Created $env_file for $base_domain"

