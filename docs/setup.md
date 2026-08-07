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

## Run locally

```sh
aspire run
```

Aspire starts PostgreSQL, the API, and the Next.js Control Plane. Open the Control Plane at `http://localhost:3000`; Aspire displays the service endpoints and logs in its dashboard.

## Deployment configuration

Development defaults to `example.azurecr.io` and the `target-acr` OCI credential handle. Override the non-secret settings with `Deployment__TargetRegistry`, `Deployment__CredentialHandles__0__Name`, and `Deployment__CredentialHandles__0__Type` as needed. Credential material is never stored in RegistryBridge configuration.
