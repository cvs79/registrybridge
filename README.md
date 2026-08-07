# RegistryBridge

RegistryBridge is a local Control Plane for operating a deployment-specific Catalog of registry artifacts and synchronizing them to its target registry.

## Repository layout

- `apphost/` is the .NET Aspire orchestrator, which starts the API, Next.js Control Plane, and PostgreSQL.
- `servicedefaults/` provides the Aspire OpenTelemetry, service discovery, resilience, and health-check defaults.
- `src/RegistryBridge.Api/` is the ASP.NET Core API and application process.
- `web/` is the Next.js App Router Control Plane, built with TypeScript and Tailwind CSS.
- `tests/RegistryBridge.Api.IntegrationTests/` contains the PostgreSQL-backed integration tests.
- `RegistryBridge.slnx` remains at the repository root, the conventional location for a .NET solution.

## Local runtime

Install the same local tooling used by Zep:

```sh
dotnet tool install -g aspire.cli
dotnet restore RegistryBridge.slnx
cd web && npm install && cd ..
```

Start the full local application:

```sh
aspire run
```

Aspire opens its dashboard automatically. The Control Plane is available at [http://localhost:3000](http://localhost:3000); it proxies catalog requests to the API through a server-side Next.js route. The API's development health endpoints are `/health` and `/alive`.

The development deployment configuration defaults to `example.azurecr.io` and the `target-acr` OCI credential handle. Override these non-secret values with `Deployment__TargetRegistry` and `Deployment__CredentialHandles__0__Name` / `Deployment__CredentialHandles__0__Type` when required.
