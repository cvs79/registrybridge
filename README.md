# RegistryBridge

RegistryBridge is a localhost-only Control Plane for operating a deployment-specific Catalog of registry artifacts and synchronizing them to a fixed target registry.

## Run locally

Docker Compose is the supported local deployment:

```sh
cp .env.example .env
docker compose --env-file .env up --build
```

The Control Plane is available at [http://127.0.0.1:8080](http://127.0.0.1:8080). PostgreSQL is published only on `127.0.0.1:5432` for tools such as DBeaver. The application is intentionally unauthenticated, so do not expose it beyond localhost.

Catalog history and operational records persist in the `registrybridge-postgres` volume; Trivy's cache persists in `registrybridge-trivy-cache`. To reset local data completely:

```sh
docker compose --env-file .env down --volumes
```

`.env` is ignored. It declares PostgreSQL access, the target registry, and non-secret Credential Handle names and types. Do not put registry credentials in the Catalog or commit them.

## Development

```sh
dotnet test RegistryBridge.slnx
npm ci --prefix src/RegistryBridge.ControlPlane
npm run build --prefix src/RegistryBridge.ControlPlane
```

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

Create your local deployment configuration from the safe template:

```sh
cp .env.example .env
```

Start the full local application:

```sh
aspire run
```

Aspire opens its dashboard automatically. The Control Plane is available at [http://localhost:3000](http://localhost:3000); it proxies catalog requests to the API through a server-side Next.js route. The API's development health endpoints are `/health` and `/alive`.

Aspire loads the non-secret deployment settings in `.env` and passes them to the API. PostgreSQL is still managed by Aspire, so no database credentials belong in `.env`.
