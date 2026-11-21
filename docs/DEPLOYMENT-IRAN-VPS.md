# Iranian infrastructure shortlist for the MKBJ demo

Checked on 2026-11-21. Provider prices and inventory can change; verify the checkout total before
paying. The application needs one Ubuntu VM with a public IPv4 address, root/sudo SSH access,
Docker support, at least 4 vCPU, 8 GB RAM and 80 GB SSD. The expected architecture is `x86_64`.

## Recommendation

The budget pick is **Felexco's 4-core / 8-GB Iran VPS**: 4 vCPU, 8 GB RAM, 100 GB SSD, a dedicated
IPv4 address and 300 GB download traffic for 3,478,000 toman per month including VAT. Its product
page explicitly promises Ubuntu 24.04, Linux root access and positions the size for multiple
services and a database. This is the closest low-cost published configuration to what MKBJ needs.

Use **ArvanCloud** when provider maturity, APIs, snapshots and cloud controls matter more than the
lowest demo price. Its closest published ready-made size is 4 CPU, 12 GB RAM and 75 GB disk at
4,946,400 toman per month, plus the first IPv4 at 234,000 toman. ArvanCloud also exposes API, CLI,
SDK and Terraform interfaces. Add at least 5 GB of disk if the selected package permits it.

**Liara** is also technically suitable and developer-friendly, with hourly billing. Its current
IaaS Uranus plan is 4 CPU, 8 GB RAM and 80 GB SSD at 4,600,000 toman per month, plus 300,000 toman
for IPv4; international download is listed at 2,500 toman per GB. It is therefore not the cheapest
current option, but the hourly model is useful for a short acceptance test.

ParsPack is a viable third quote, but its public page uses a live configurator and does not expose
a stable 4 CPU / 8 GB / 80 GB checkout price in the static page. Ask sales for that exact Linux
configuration and compare the final monthly price with Felexco before buying.

Do not choose a shared web-hosting or a small PaaS plan. MKBJ runs PostgreSQL, Keycloak, MinIO,
the API, a worker, two web applications and Caddy together; it needs a real VM and persistent
Docker volumes.

### Constrained 2 GB demonstration tier

The repository also contains a `low-memory` tier for an exceptionally cheap 1-vCPU/2-GB/25-GB
VM. It creates 4 GB of swap, applies strict container limits and runs background jobs inside the
API instead of a separate worker. This is acceptable for one-person feature demonstrations only:
startup and seeding are slower, concurrent use may swap heavily, and it is unsuitable for load,
reliability or performance testing. Set GitHub variable `DEMO_TIER=low-memory` to select it.

## Questions to send the provider before purchase

Ask for written confirmation of all of these:

1. Ubuntu 24.04 LTS `x86_64`, with root or passwordless-sudo SSH access.
2. A dedicated, static, public IPv4 address (not CGNAT) and inbound TCP 22, 80 and 443.
3. Outbound HTTPS access to `github.com`, `api.github.com`, `ghcr.io`,
   `pkg-containers.githubusercontent.com`, `registry-1.docker.io`, `auth.docker.io`,
   `download.docker.com`, `quay.io` and `mcr.microsoft.com`.
4. Docker Engine and Docker Compose are permitted; no nested virtualization is required.
5. At least 80 GB SSD and a snapshot/backup option for the VM or its disk.
6. The traffic allowance and the charge for international download traffic. Initial container
   pulls can consume several gigabytes.
7. A refund or trial window if GitHub Actions cannot reach SSH or the VM cannot pull from GHCR.

## Purchase and acceptance procedure

1. Buy only one month or use hourly billing for the first test.
2. Create an Ubuntu 24.04 VM, add an SSH public key, and allow 22/80/443 in the provider firewall.
3. From your own machine, verify that the public IP accepts SSH.
4. Copy `tools/deploy/preflight-ubuntu-vps.sh` to the VM and run it. Do not continue if any registry
   check fails; request a refund or ask the provider to fix international egress.
5. Set GitHub environment variables and secrets described in `docs/DEPLOYMENT-DEMO.md`.
   Iranian VMs normally use `DEMO_DOCKER_PLATFORMS=linux/amd64`.
6. Run **Actions -> Deploy demo -> Run workflow**. The workflow builds in GitHub, pushes images to
   GHCR, connects to the VM over SSH, migrates and seeds the database, and starts the complete
   stack. Subsequent successful CI runs on `main` or `master` deploy automatically.

The GitHub deployment preserves the server-side `.env` and Docker volumes. Re-deploying code does
not reset the Backoffice or shop credentials. A completely destroyed VM is initialized and seeded
again using the stable credentials stored as GitHub secrets.

## GitHub reachability caveat

No provider marketing page can guarantee that GitHub can reach a newly allocated Iranian IP or
that the VM can reach every foreign container registry at the moment you buy it. Those paths can
change independently of the provider. The repository is ready for an ordinary SSH/GHCR flow, but
the preflight and the first GitHub deployment are the actual acceptance test; run both inside the
refund window.

## Provider pages used for the comparison

- Felexco Iran VPS: <https://felexco.com/iran>
- Liara pricing: <https://liara.ir/pricing/>
- Liara cloud VM: <https://liara.ir/products/cloud-server/>
- ArvanCloud server pricing: <https://www.arvancloud.ir/fa/pricing/cloud-server>
- ArvanCloud cloud server: <https://www.arvancloud.ir/fa/products/cloud-server>
- ParsPack cloud server: <https://parspack.com/cloud-server>
