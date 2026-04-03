# RegistryBridge

![Sync status](../../actions/workflows/sync.yml/badge.svg)
![Pages deploy](../../actions/workflows/pages.yml/badge.svg)

RegistryBridge automates mirroring of curated public Docker images and Helm charts into a private Azure Container Registry (ACR). It supports plain mirror copies (via `crane`), custom wrapper builds (via Docker multi-stage Dockerfiles), and Helm OCI pushes — all driven by a single `catalog.yaml` source of truth. A nightly GitHub Actions workflow syncs every entry, Trivy scans the resulting images for CVEs, and a GitHub Pages dashboard gives you a real-time view of what's in your registry and whether anything is vulnerable.

## Badges

| Workflow | Status |
|----------|--------|
| Nightly sync | ![Sync](../../actions/workflows/sync.yml/badge.svg) |
| CVE scan | ![Scan](../../actions/workflows/scan.yml/badge.svg) |
| Pages deploy | ![Pages](../../actions/workflows/pages.yml/badge.svg) |

## Quick start

**1. Fork or use this template**

Click **Use this template** on GitHub to create your own copy of this repository.

**2. Set three GitHub Secrets**

In your repo go to **Settings → Secrets and variables → Actions** and add:

| Secret | Value |
|--------|-------|
| `ACR_LOGIN_SERVER` | e.g. `myacr.azurecr.io` |
| `ACR_CLIENT_ID` | Service principal app ID |
| `ACR_CLIENT_SECRET` | Service principal password |

See the [setup guide](docs/setup.md#2-required-github-secrets) for how to create the service principal with the `acrpush` role.

**3. Edit `catalog.yaml`**

Replace `myacr.azurecr.io` with your ACR login server, then add, remove, or modify image and chart entries. Push to `main` — or go to **Actions → Sync → Run workflow** to trigger a sync immediately.

## Documentation

Full documentation is available on the [GitHub Pages site](../../pages) and in [docs/setup.md](docs/setup.md), covering:

- How to add mirror, build, and chart entries
- ACR service principal setup
- Interpreting the sync dashboard and `sync-report.json`
- Onboarding a new customer environment
