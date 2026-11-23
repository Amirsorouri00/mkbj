# Ubuntu VM demo deployment and GitHub CI/CD

This runbook deploys the complete MKBJ demo to one Ubuntu VM from OCI, Liara, ArvanCloud or another
VPS provider. The VM runs PostgreSQL, Keycloak, MinIO, the .NET API and worker, the Backoffice SPA,
the Next.js shop and Caddy. GitHub Actions builds architecture-appropriate images, stores them in
GHCR and deploys a tested commit over SSH. For the current Iranian-provider comparison and purchase
checklist, see `docs/DEPLOYMENT-IRAN-VPS.md`.

This is a **demo topology**, not a production recommendation. It has one machine, brief release
downtime and no off-machine backup unless you add one.

## Required VM size

Use at least 4 vCPU, 8 GB RAM and 80 GB SSD for a low-traffic demo. Twelve GB RAM is preferable.
The workflow supports common `linux/amd64` Iranian VMs and OCI's `linux/arm64` Ampere VM. Smaller
application tiers cannot reliably hold Keycloak, two PostgreSQL databases and MinIO together.

- [OCI Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)
- [GitHub Packages billing](https://docs.github.com/en/billing/concepts/product-billing/github-packages)
- [GitHub Actions secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets)

OCI-specific free-tier limits still apply when OCI is selected. Paid Iranian VMs instead have
provider-specific traffic, snapshot and international-connectivity limits.

## 1. Create the VM

Create one Ubuntu 24.04 instance:

- Shape: 4 vCPU, 8 GB RAM and 80 GB SSD minimum. OCI users may instead use
  `VM.Standard.A1.Flex`, 2 OCPUs and 12 GB RAM.
- Assign a public IPv4 address and keep it reserved for this VM.
- Add ingress rules for TCP 22 and 80. Add 443 only when HTTPS is enabled. Restrict port 22 to
  your own IP when practical.
- Keep the generated SSH private key. Do not commit it.

The workflow installs Docker on the first deployment. No manual application setup is needed on
the VM.

## 2. Choose hostnames

The zero-cost default uses `sslip.io` over HTTP so certificate issuance cannot block a disposable
demo. For public IP `203.0.113.10`, the default deployment creates:

- `http://shop.203-0-113-10.sslip.io`
- `http://admin.203-0-113-10.sslip.io`
- `http://api.203-0-113-10.sslip.io`
- `http://auth.203-0-113-10.sslip.io`
- `http://storage.203-0-113-10.sslip.io`

Set `DEMO_ENABLE_HTTPS=true` to make Caddy obtain and renew public certificates. HTTPS mode requires
ports 80 and 443 to be reachable from public certificate-authority validation servers. For a
durable demo, use HTTPS, set `DEMO_BASE_DOMAIN` to a domain you control, and point `api`, `admin`,
`shop`, `auth` and `storage` beneath it to the VM. HTTP mode sends credentials without transport
encryption and is only suitable for a temporary evaluation environment on a trusted network.

## 3. Configure GitHub

Create a GitHub environment named `demo`. On GitHub Free with a private repository, use repository
variables and repository secrets with the same names if environment secrets are unavailable.

Variables:

| Name | Value |
|---|---|
| `DEMO_PUBLIC_IP` | VM public IPv4 address |
| `DEMO_SSH_USER` | `ubuntu` |
| `DEMO_ENABLE_HTTPS` | `false` by default; set `true` only when ports 80/443 are internationally reachable |
| `DEMO_ACME_EMAIL` | Required only when `DEMO_ENABLE_HTTPS=true`; email used for certificate registration |
| `DEMO_BASE_DOMAIN` | Optional custom base domain; blank uses `sslip.io` |
| `DEMO_DOCKER_PLATFORMS` | `linux/amd64` for Iranian VPS (default); `linux/arm64` for OCI Ampere |
| `DEMO_TIER` | `standard` normally; `low-memory` only for a 1-vCPU/2-GB disposable demo VM |

Secrets:

| Name | Purpose |
|---|---|
| `DEMO_SSH_PRIVATE_KEY` | Private key for the VM's `ubuntu` account |
| `DEMO_SSH_KNOWN_HOSTS` | Pinned SSH host-key line for the VM |
| `DEMO_ADMIN_USERNAME` | Stable Backoffice login name |
| `DEMO_ADMIN_EMAIL` | Backoffice email |
| `DEMO_ADMIN_PASSWORD` | At least 14 characters |
| `DEMO_SHOP_USERNAME` | Stable shop login name; normally the email |
| `DEMO_SHOP_EMAIL` | Shop email |
| `DEMO_SHOP_PASSWORD` | At least 10 characters |
| `GHCR_DEPLOY_USERNAME` | GitHub account allowed to pull the images |
| `GHCR_DEPLOY_TOKEN` | Fine-grained or classic token with `read:packages` access |

Obtain and verify the VM host key from a trusted machine, then save the complete output as
`DEMO_SSH_KNOWN_HOSTS`:

```bash
ssh-keyscan -H YOUR_VM_IP
```

Do not copy a host key from an untrusted network without verifying its fingerprint in the
provider panel or console.

The repository's `GITHUB_TOKEN` publishes images. The separate read-only deploy token lets the VM
pull private GHCR images and is stored only in Docker's root credential store on that VM.

## 4. First deployment

Open **Actions → Deploy demo → Run workflow**. The workflow:

1. builds API, Backoffice and shop images for `DEMO_DOCKER_PLATFORMS`;
2. copies only the deployment manifests and scripts to `/opt/mkbj`;
3. installs Docker if this is the first deployment;
4. creates `/opt/mkbj/build/compose/.env` once with generated infrastructure secrets and reconciles
   the selected HTTP/HTTPS transport on later deployments;
5. starts PostgreSQL, Keycloak and MinIO;
6. applies migrations explicitly;
7. runs the idempotent demo seed in `Staging`;
8. starts the API, worker, Backoffice, shop and Caddy;
9. waits for health checks and verifies all three public surfaces.

The API intentionally refuses demo seeding in `Production`. This topology uses `Staging` only so
the clearly marked demo catalogue can be populated; it retains non-development exception handling
and authentication behavior.

## 5. Credential and data persistence

The deployment never rewrites the server's `.env` after it exists. Docker named volumes preserve
both application data and the Keycloak database. Keycloak's startup import skips existing realms,
so a code deployment does not recreate users or change passwords.

If the VM and all volumes are intentionally destroyed, running the workflow with the same six demo
identity secrets recreates the same login credentials. Infrastructure passwords and the API-key
pepper are regenerated because the corresponding databases are also new.

The demo seed runs after every migration because it is idempotent. This ensures a genuinely clean
database is usable on its first deployment while existing demo data is not duplicated.

## 6. Continuous delivery

For the temporary demo phase, a push to `main` or `master` starts
`.github/workflows/deploy-demo.yml` directly. The deployment's container builds are the only gate;
the automated test workflow is intentionally disabled. The workflow can also be run manually.
Deployments are serialized, and images are addressed by commit SHA rather than a mutable tag.

The test projects and local test commands remain in the repository. Restore an automated CI gate
before treating this demo topology as a production release process.

To roll back, rerun **Deploy demo** from the desired earlier commit. Schema migrations are designed
to be expand-only; do not automatically roll a database migration backward.

## 7. Operating the demo

On the VM:

```bash
cd /opt/mkbj/build/compose
sudo docker compose --env-file .env --env-file .release.env ps
sudo docker compose --env-file .env --env-file .release.env logs --tail=200 api worker
```

Back up the named PostgreSQL, Keycloak and MinIO volumes before any destructive maintenance.
Provider snapshots are useful but backup scheduling is not created by this repository.

