# RegistryBridge

RegistryBridge is a local Control Plane for operating a deployment-specific Catalog of registry artifacts and synchronizing them to its target registry.

## Repository layout

- `src/RegistryBridge.Api/` is the ASP.NET Core API and application process.
- `src/RegistryBridge.ControlPlane/` is the Vite/React Control Plane. Its nested `src/` is the conventional Vite source directory.
- `tests/RegistryBridge.Api.IntegrationTests/` contains the PostgreSQL-backed integration tests.
- `RegistryBridge.slnx` remains at the repository root, the conventional location for a .NET solution.

## Local runtime

Create your local configuration from the committed safe template, then set a non-production database password and the target registry for this deployment:

```sh
cp .env.example .env
```

Start the Control Plane, application, and PostgreSQL with the explicitly selected environment file:

```sh
docker compose --env-file .env up --build
```

The Control Plane is available at [http://localhost:8080](http://localhost:8080), the application health endpoints at [http://localhost:8081/health](http://localhost:8081/health) and [http://localhost:8081/ready](http://localhost:8081/ready), and PostgreSQL at `localhost:5432` for DBeaver. See [the setup guide](docs/setup.md) for the complete variable reference and reset procedure.
