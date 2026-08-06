# RegistryBridge

RegistryBridge is a local Control Plane for operating a deployment-specific Catalog of registry artifacts and synchronizing them to its target registry.

## Local runtime

Start the Control Plane, application, and PostgreSQL:

```sh
docker compose up --build
```

The Control Plane is available at [http://localhost:8080](http://localhost:8080), the application health endpoints at [http://localhost:8081/health](http://localhost:8081/health) and [http://localhost:8081/ready](http://localhost:8081/ready), and PostgreSQL at `localhost:5432` for DBeaver.

The `postgres-data` and `trivy-cache` named volumes persist across normal Compose restarts. To reset a local installation, remove those volumes:

```sh
docker compose down --volumes
```

The application runs `serve`, takes a PostgreSQL advisory lock, and applies migrations before accepting traffic. A new installation has no Catalog Entries.
