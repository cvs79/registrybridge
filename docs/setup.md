# RegistryBridge — Setup Guide

## 1. Prerequisites

| Tool | Minimum version | Purpose |
|------|----------------|---------|
| GitHub account | — | Hosting the repo and running Actions |
| Azure Container Registry | — | Private registry target |
| [crane](https://github.com/google/go-containerregistry/releases) | v0.20+ | Mirror-mode image copy (installed automatically in CI) |
| [Helm](https://helm.sh/docs/intro/install/) | v3.14+ | Chart pull/push (installed automatically in CI) |
| [yq](https://github.com/mikefarah/yq/releases) | v4+ | YAML parsing in scripts (installed automatically in CI) |
| [jq](https://stedolan.github.io/jq/) | v1.6+ | JSON manipulation in scripts |
| Docker | 24+ | Build-mode wrapper images |

For local development you only need crane, yq, jq, and docker. In GitHub Actions all tools are installed by the workflow automatically.

---

## 2. Required GitHub Secrets

RegistryBridge uses an ACR service principal with the `acrpush` role. Create one with:

```bash
ACR_NAME="myacr"                # your ACR name (without .azurecr.io)
SUBSCRIPTION=$(az account show --query id -o tsv)
ACR_ID=$(az acr show --name "${ACR_NAME}" --query id -o tsv)

# Create the service principal
SP=$(az ad sp create-for-rbac \
  --name "sp-registrybridge" \
  --role acrpush \
  --scopes "${ACR_ID}" \
  --output json)

echo "ACR_LOGIN_SERVER: ${ACR_NAME}.azurecr.io"
echo "ACR_CLIENT_ID:    $(echo $SP | jq -r .appId)"
echo "ACR_CLIENT_SECRET:$(echo $SP | jq -r .password)"
```

Add all three values to your GitHub repository under **Settings → Secrets and variables → Actions**:

| Secret name | Value |
|-------------|-------|
| `ACR_LOGIN_SERVER` | `myacr.azurecr.io` |
| `ACR_CLIENT_ID` | Service principal app ID (UUID) |
| `ACR_CLIENT_SECRET` | Service principal password |

> **Tip:** Scope the `acrpush` role to a specific repository within ACR if you need tighter permissions, using `--scopes "${ACR_ID}/repositories/<name>"`.

---

## 3. Fork / Template the Repo for a New Customer

1. Click **Use this template** (or fork) on the GitHub repository page.
2. Enable GitHub Pages: **Settings → Pages → Source → GitHub Actions**.
3. Add the three secrets from section 2.
4. Edit `catalog.yaml` and replace every occurrence of `myacr.azurecr.io` with the customer's ACR login server.
5. Trigger a manual sync from **Actions → Sync → Run workflow**.

---

## 4. Add a New Image (Mirror Mode)

Open `catalog.yaml` and append an entry to the `images` list:

```yaml
images:
  - source: index.docker.io/library/nginx
    tag: "1.27.0"
    target: myacr.azurecr.io/library/nginx
    mode: mirror
```

- `source` — fully-qualified Docker image reference (use `index.docker.io/` prefix for Docker Hub).
- `tag` — exact version tag to pin. Mutable tags like `latest` are intentionally unsupported.
- `target` — full path in your ACR (no tag suffix; the `tag` field is appended automatically).
- `mode: mirror` — uses `crane copy` for an efficient layer-copy without extracting images locally.

Commit and push, or trigger a manual sync.

---

## 5. Add a Custom Wrapper Build (Build Mode)

1. Create a new directory under `builds/`:

   ```
   builds/
   └── myapp/
       ├── Dockerfile
       └── content/
           └── config.yaml   # any files COPY'd by the Dockerfile
   ```

2. Write a `Dockerfile` that **must not** provide a default for `ARG VERSION`:

   ```dockerfile
   ARG VERSION
   FROM index.docker.io/myvendor/myapp:${VERSION}

   COPY ./content/config.yaml /etc/myapp/config.yaml
   ```

   The build step always passes `--build-arg VERSION=<tag>` from `catalog.yaml`. If `ARG VERSION` has a default, the build would succeed even without the catalog tag — omitting the default is a deliberate guardrail.

3. Add an entry to `catalog.yaml`:

   ```yaml
   images:
     - source: index.docker.io/myvendor/myapp
       tag: "2.0.0"
       target: myacr.azurecr.io/myvendor/myapp-custom
       mode: build
       context: ./builds/myapp
       dockerfile: Dockerfile
   ```

4. Commit. The next sync will build the wrapper image, push it, and record a SHA-256 hash of the build context. Subsequent syncs skip the rebuild if neither the `tag` nor any file in `context` has changed.

---

## 6. Add a Helm Chart

Append an entry to the `charts` list in `catalog.yaml`:

```yaml
charts:
  - repo: https://charts.bitnami.com/bitnami
    chart: postgresql
    version: "15.5.20"
    target: myacr.azurecr.io/helm/postgresql
```

- `repo` — the Helm repository URL (HTTP or HTTPS).
- `chart` — chart name within the repository.
- `version` — exact chart version to mirror.
- `target` — full OCI path **including the chart name**. RegistryBridge strips the last path segment (`/postgresql`) to derive the `helm push` registry argument (`oci://myacr.azurecr.io/helm`), then pushes the chart tgz there.

Helm charts are fetched with `helm pull --untar=false` and pushed with `helm push` to ACR's OCI endpoint.

---

## 7. Trigger a Manual Sync

1. Go to **Actions → Sync** in the GitHub repository.
2. Click **Run workflow**, choose the branch (`main`), and click the green button.
3. Monitor progress in the workflow run log.

After the sync completes the **Scan** workflow starts automatically, followed by the **Deploy Pages** workflow updating the dashboard.

To trigger just a scan without a sync: **Actions → Scan → Run workflow**.

---

## 8. Interpreting sync-report.json and the Dashboard

`docs/sync-report.json` is the machine-readable record of every sync and scan run. It is committed to the repository by GitHub Actions and served as part of the GitHub Pages site.

### Top-level fields

| Field | Description |
|-------|-------------|
| `last_sync` | ISO 8601 timestamp of the most recent successful sync run |
| `images[]` | One entry per image synced |
| `charts[]` | One entry per chart synced |
| `scans[]` | One CVE summary entry per image scanned |

### `images[]` entry

| Field | Description |
|-------|-------------|
| `source` | Upstream image name |
| `target` | ACR image path |
| `tag` | Pinned version tag |
| `mode` | `mirror` or `build` |
| `status` | `success` or `failure` |
| `context_hash` | SHA-256 of build context (build mode only); used to skip unchanged rebuilds |
| `synced_at` | ISO 8601 timestamp |

### `charts[]` entry

| Field | Description |
|-------|-------------|
| `chart` | Chart name |
| `version` | Chart version |
| `target` | OCI target in ACR |
| `status` | `success` or `failure` |
| `synced_at` | ISO 8601 timestamp |

### `scans[]` entry

| Field | Description |
|-------|-------------|
| `target` | ACR image path |
| `tag` | Scanned tag |
| `critical` / `high` / `medium` / `low` | CVE counts by severity |
| `scanned_at` | ISO 8601 timestamp |

### Dashboard

The GitHub Pages site at your repo's Pages URL loads `sync-report.json` at runtime and renders:

- A **Last synced** timestamp in the header.
- A filterable **Mirrored Images** table.
- A filterable **Helm Charts** table.
- A **CVE Summary** card grid per image.

If `sync-report.json` contains a `failure` entry for any image or chart, the status cell renders in red. Investigate by opening the corresponding workflow run in GitHub Actions.
