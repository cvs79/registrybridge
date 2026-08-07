# Local setup

RegistryBridge is deployed locally with Docker Compose. Copy the safe template, set a local PostgreSQL password and target registry, then start the stack:

```sh
cp .env.example .env
docker compose --env-file .env up --build
```

The Control Plane is at `http://127.0.0.1:8080`, with `/healthz` and `/readyz` available for local diagnostics. PostgreSQL is available to DBeaver at `127.0.0.1:5432`; use `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` from `.env`.

`registrybridge-postgres` retains Catalog Revisions, Synchronization Runs, Artifact Outcomes, Vulnerability Findings, and Run Logs. `registrybridge-trivy-cache` retains scanner downloads. Reset both with:

```sh
docker compose --env-file .env down --volumes
```

The Control Plane remains unauthenticated for this localhost-only MVP. Never bind it to a LAN interface or commit `.env`.
