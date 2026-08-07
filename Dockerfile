FROM node:26-alpine AS control-plane-build

WORKDIR /src/RegistryBridge.ControlPlane
COPY src/RegistryBridge.ControlPlane/package.json src/RegistryBridge.ControlPlane/package-lock.json ./
RUN npm ci
COPY src/RegistryBridge.ControlPlane/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build

WORKDIR /src
COPY src/RegistryBridge.Api/RegistryBridge.Api.csproj src/RegistryBridge.Api/
COPY servicedefaults/RegistryBridge.ServiceDefaults.csproj servicedefaults/
RUN dotnet restore src/RegistryBridge.Api/RegistryBridge.Api.csproj
COPY . .
COPY --from=control-plane-build /src/RegistryBridge.Api/wwwroot/ src/RegistryBridge.Api/wwwroot/
RUN dotnet restore src/RegistryBridge.Api/RegistryBridge.Api.csproj \
    && dotnet publish src/RegistryBridge.Api/RegistryBridge.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=api-build /app/publish/ ./
ENTRYPOINT ["dotnet", "RegistryBridge.Api.dll"]
CMD ["serve"]
