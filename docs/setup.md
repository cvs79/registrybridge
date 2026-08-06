# RegistryBridge local setup

## Prerequisites

- Docker Compose v2

## Configure a local deployment

RegistryBridge reads local deployment configuration from an ignored `.env` file. Create it from the safe committed template:

```sh
cp .env.example .env
```

| Variable | Purpose |
|----------|---------|
| `POSTGRES_DB` | Local PostgreSQL database name. |
| `POSTGRES_USER` | Local PostgreSQL user. |
| `POSTGRES_PASSWORD` | Local PostgreSQL password. Replace the example value; never commit `.env`. |
| `REGISTRYBRIDGE_TARGET_REGISTRY` | The deployment's fixed target registry host, such as `myregistry.azurecr.io`. |
| `REGISTRYBRIDGE_CREDENTIAL_HANDLE_NAME` | Non-secret name shown in the Control Plane for the deployment's OCI-registry credential. |
| `REGISTRYBRIDGE_CREDENTIAL_HANDLE_TYPE` | Credential type; use `OciRegistry` for an OCI registry. |

The credential handle is an identifier only. Registry credential material is not stored in PostgreSQL and must not be committed to `.env.example`.

## Run locally

```sh
docker compose --env-file .env up --build
```

The Control Plane is available at [http://localhost:8080](http://localhost:8080). The application health endpoints are [http://localhost:8081/health](http://localhost:8081/health) and [http://localhost:8081/ready](http://localhost:8081/ready). PostgreSQL is exposed at `127.0.0.1:5432` for local database tools.

The `postgres-data` and `trivy-cache` named volumes persist across normal Compose restarts. To reset the local installation:

```sh
docker compose --env-file .env down --volumes
```
