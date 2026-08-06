FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/RegistryBridge.Api/RegistryBridge.Api.csproj src/RegistryBridge.Api/
RUN dotnet restore src/RegistryBridge.Api/RegistryBridge.Api.csproj

COPY . .
RUN dotnet publish src/RegistryBridge.Api/RegistryBridge.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV TRIVY_CACHE_DIR=/var/lib/registrybridge/trivy

COPY --from=build /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=5s --timeout=3s --start-period=20s --retries=3 \
    CMD ["curl", "--fail", "http://localhost:8080/ready"]

ENTRYPOINT ["dotnet", "RegistryBridge.Api.dll"]
