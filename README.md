# DotaAnalyst

[![CI](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml)

Analisador de partidas de Dota 2. Você informa seu account ID do Dota (ou SteamID64) e vê suas partidas recentes — herói, K/D/A, duração e resultado — com dados do [OpenDota](https://www.opendota.com).

- **Back-end:** ASP.NET Core (.NET 10) em DDD — `Domain`, `Application`, `Infrastructure` e `Server` (API).
- **Front-end:** React 19 + Vite.

**No ar:** https://dotaanalyst.onrender.com (plano grátis do Render: o primeiro acesso depois de 15 min parado demora ~1 min).

## Rodando com Docker

Precisa só do [Docker](https://www.docker.com/products/docker-desktop/).

```sh
docker compose up --build
```

Abra http://localhost:8080. Para parar: `docker compose down`.

Para usar outra porta, copie `.env.example` para `.env` e mude `API_PORT`.

## Rodando para desenvolver

Precisa do .NET 10 SDK e do Node.js 24.

- **Visual Studio 2026:** abra `DotaAnalyst.slnx` e aperte F5 (sobe a API e o Vite juntos).
- **Linha de comando:** `dotnet run --project DotaAnalyst.Server` — o Vite é iniciado automaticamente.

A aplicação abre em https://localhost:59438 (API em https://localhost:7208). Na primeira vez, confie no certificado de desenvolvimento: `dotnet dev-certs https --trust`.

Testes: `dotnet test`.
