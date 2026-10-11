# DotaAnalyst

[![CI](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml)

Analisador de partidas de Dota 2. Entre com a sua conta Steam (senha ou QR code no app Steam) — ou informe qualquer account ID do Dota/SteamID64 — e veja as partidas recentes e a análise de cada uma (herói, K/D/A, pontos fortes e a melhorar), com dados do [OpenDota](https://www.opendota.com).

- **Back-end:** ASP.NET Core (.NET 10) em DDD — `Domain`, `Application`, `Infrastructure` e `Server` (API).
- **Banco:** PostgreSQL com EF Core — guarda partidas já analisadas, nomes de heróis/itens, jogadores pesquisados e as chaves do Data Protection.
- **Front-end:** React 19 + Vite.

**No ar:** https://dotaanalyst.onrender.com (plano grátis do Render: o primeiro acesso depois de 15 min parado demora ~1 min).

## Rodando com Docker

Precisa só do [Docker](https://www.docker.com/products/docker-desktop/).

```sh
cp .env.example .env   # e troque POSTGRES_PASSWORD
docker compose up --build
```

Abra http://localhost:8080. Para parar: `docker compose down` (os dados do banco ficam no volume `db-data`; `docker compose down -v` apaga tudo).

O `.env` define a porta da aplicação (`API_PORT`), a senha do PostgreSQL local (`POSTGRES_PASSWORD`, obrigatória), a porta do banco na sua máquina (`POSTGRES_PORT`, só em 127.0.0.1) e, opcionalmente, a [Steam Web API key](https://steamcommunity.com/dev/apikey) (`STEAM_API_KEY`) para mostrar nome e avatar de quem está logado.

O login com a Steam funciona também em `http://localhost:8080`.

## Rodando para desenvolver

Precisa do .NET 10 SDK, do Node.js 24 e do Docker (para o PostgreSQL).

1. Suba só o banco: `docker compose up -d db`.
2. Uma vez, diga à API onde está o banco (fica no user-secrets, fora do Git):
   ```sh
   dotnet user-secrets set "ConnectionStrings:Database" "Host=localhost;Port=5432;Database=dotaanalyst;Username=dotaanalyst;Password=<POSTGRES_PASSWORD do .env>" --project DotaAnalyst.Server
   ```
3. **Visual Studio 2026:** abra `DotaAnalyst.slnx` e aperte F5 (sobe a API e o Vite juntos). **Linha de comando:** `dotnet run --project DotaAnalyst.Server` — o Vite é iniciado automaticamente.

A aplicação abre em https://localhost:59438 (API em https://localhost:7208). Na primeira vez, confie no certificado de desenvolvimento: `dotnet dev-certs https --trust`. As tabelas são criadas sozinhas quando a API sobe (migrations).

Para olhar o banco, use o pgAdmin (ou outro cliente): servidor `localhost`, porta `5432`, banco/usuário `dotaanalyst`, senha do `.env`.

**Testes:** `dotnet test`. Os testes do banco sobem um PostgreSQL descartável no Docker ([Testcontainers](https://dotnet.testcontainers.org)), então o Docker precisa estar rodando.

**Mudou uma entidade ou tabela?** Crie uma migration:

```sh
dotnet ef migrations add <Nome> --project DotaAnalyst.Infrastructure --startup-project DotaAnalyst.Infrastructure --output-dir Persistence/Migrations
```

(`dotnet tool restore` instala o `dotnet-ef` na primeira vez.)
