# RegistryBridge local setup

## Prerequisites

- .NET SDK 10.x
- Aspire CLI 13.x or later
- Node.js 20.x or later
- Docker Desktop, used by Aspire to run PostgreSQL

## Install dependencies

```sh
dotnet tool install -g aspire.cli
dotnet restore RegistryBridge.slnx
cd web && npm install && cd ..
```

## Configure a local deployment

Create the ignored local settings file:

```sh
cp .env.example .env
```

| Variable | Purpose |
|----------|---------|
| `REGISTRYBRIDGE_TARGET_REGISTRY` | The deployment's fixed target registry host, such as `myregistry.azurecr.io`. |
| `REGISTRYBRIDGE_CREDENTIAL_HANDLE_NAME` | Non-secret name shown in the Control Plane for the deployment's OCI-registry credential. |
| `REGISTRYBRIDGE_CREDENTIAL_HANDLE_TYPE` | Credential type; use `OciRegistry` for an OCI registry. |

The credential handle is an identifier only. Registry credential material is not stored in PostgreSQL and must not be committed to `.env`.

## Run locally

```sh
aspire run
```

Aspire starts PostgreSQL, the API, and the Next.js Control Plane. Open the Control Plane at `http://localhost:3000`; Aspire displays the service endpoints and logs in its dashboard.

## Deployment configuration

Aspire loads `.env` from the repository root and passes those values to the API. PostgreSQL is provisioned and configured by Aspire; do not add a connection string or database credentials to `.env`.
