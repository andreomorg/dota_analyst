# Builds the whole app into one image: the C# API serving the compiled React client.
# Usage: docker compose up --build   (or: docker build -t dotaanalyst . && docker run -p 8080:8080 dotaanalyst)

# 1) React client: produces dist/
FROM node:24-alpine AS client-build
WORKDIR /src/dotaanalyst.client
# Dependencies first, so this layer is cached until package*.json changes.
COPY dotaanalyst.client/package.json dotaanalyst.client/package-lock.json ./
RUN npm ci
COPY dotaanalyst.client/ ./
RUN npm run build

# 2) .NET API: restore, then publish with the React files in wwwroot
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first, so `dotnet restore` is cached until a .csproj changes.
COPY DotaAnalyst.Domain/DotaAnalyst.Domain.csproj DotaAnalyst.Domain/
COPY DotaAnalyst.Application/DotaAnalyst.Application.csproj DotaAnalyst.Application/
COPY DotaAnalyst.Infrastructure/DotaAnalyst.Infrastructure.csproj DotaAnalyst.Infrastructure/
COPY DotaAnalyst.Server/DotaAnalyst.Server.csproj DotaAnalyst.Server/
RUN dotnet restore DotaAnalyst.Server/DotaAnalyst.Server.csproj -p:ExcludeSpaClient=true

COPY DotaAnalyst.Domain/ DotaAnalyst.Domain/
COPY DotaAnalyst.Application/ DotaAnalyst.Application/
COPY DotaAnalyst.Infrastructure/ DotaAnalyst.Infrastructure/
COPY DotaAnalyst.Server/ DotaAnalyst.Server/
# Must happen before publish: MapStaticAssets only serves files known at publish time.
COPY --from=client-build /src/dotaanalyst.client/dist/ DotaAnalyst.Server/wwwroot/
RUN dotnet publish DotaAnalyst.Server/DotaAnalyst.Server.csproj \
    -c Release -o /app/publish --no-restore -p:ExcludeSpaClient=true

# 3) Runtime: only the ASP.NET runtime and the published files
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl is only used by the docker-compose health check (the base image has no HTTP client).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# Non-root user that ships with the official .NET images.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DotaAnalyst.Server.dll"]
