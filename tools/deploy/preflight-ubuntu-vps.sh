#!/usr/bin/env bash
set -euo pipefail

# Run during the provider's refund/trial window. This checks the properties MKBJ's SSH/GHCR
# deployment needs without installing or changing anything on the server.

failures=0

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1" >&2; failures=$((failures + 1)); }

arch=$(uname -m)
case "$arch" in
  x86_64|aarch64|arm64) pass "supported CPU architecture: $arch" ;;
  *) fail "unsupported CPU architecture: $arch" ;;
esac

demo_tier=${MKBJ_DEMO_TIER:-standard}
memory_kib=$(awk '/MemTotal:/ { print $2 }' /proc/meminfo)
minimum_memory_kib=7500000
minimum_disk_kib=60000000
if [[ $demo_tier == low-memory ]]; then
  minimum_memory_kib=1800000
  # A 4 GB swap file is mandatory on the 25 GB shape and leaves roughly 16-17 GB free.
  minimum_disk_kib=14000000
  printf 'WARN  validating the constrained demo-only tier; this is not production capacity\n'
fi
if (( memory_kib >= minimum_memory_kib )); then
  pass "RAM threshold for $demo_tier tier"
else
  fail "RAM below the $demo_tier tier threshold"
fi

disk_kib=$(df -Pk / | awk 'NR == 2 { print $4 }')
if (( disk_kib >= minimum_disk_kib )); then
  pass "disk threshold for $demo_tier tier"
else
  fail "free disk below the $demo_tier tier threshold"
fi

if command -v curl >/dev/null 2>&1; then
  pass "curl is available"
else
  fail "curl is missing"
fi

check_https() {
  local label=$1
  local url=$2
  local status
  status=$(curl --connect-timeout 10 --max-time 20 -sS -o /dev/null -w '%{http_code}' "$url" || true)
  if [[ $status =~ ^(200|301|302|401|403|404)$ ]]; then
    pass "$label reachable over HTTPS ($status)"
  else
    fail "$label not reachable over HTTPS (status ${status:-none})"
  fi
}

check_https GitHub https://github.com/
check_https "GitHub API" https://api.github.com/
check_https GHCR https://ghcr.io/v2/
check_https "Docker Hub registry" https://registry-1.docker.io/v2/
check_https "Docker package repository" https://download.docker.com/linux/ubuntu/dists/
check_https Quay https://quay.io/v2/
check_https "Microsoft container registry" https://mcr.microsoft.com/v2/

if (( failures > 0 )); then
  printf '\n%d preflight check(s) failed. Use the provider refund window and do not deploy yet.\n' "$failures" >&2
  exit 1
fi

printf '\nThe VM passed the MKBJ deployment preflight.\n'
