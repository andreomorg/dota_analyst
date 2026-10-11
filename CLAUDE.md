# DotaAnalyst

Analisador de partidas de Dota 2: o usuário vê suas partidas e análises de replay num front-end React, servido por uma API em C#.

O dono do projeto está começando em C# — explique as decisões e os conceitos novos em português, de forma didática, em vez de só entregar código.

## Convenções

- **Código sempre em inglês:** classes, interfaces, métodos, propriedades, variáveis, rotas e pastas (ex.: `PlayersController`, `GET /api/players/{accountId}/matches`). Explicações e conversas continuam em português.

## Git

- Remoto: https://github.com/andreomorg/dota_analyst (branch `main`).
- O `git` não está no PATH; o executável que funciona é o do Visual Studio: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.

## Estrutura

- `DotaAnalyst.Server/` — API ASP.NET Core em **.NET 10**, com controllers (`Controllers/`) e OpenAPI (`/openapi/v1.json` em Development).
- `dotaanalyst.client/` — front-end **React 19 + Vite**, em JavaScript (`.jsx`).
- `DotaAnalyst.slnx` — solução do Visual Studio 2026 que junta os dois.

Os arquivos `WeatherForecast*` são exemplo do template, não código do projeto.

## Como rodar

- Pelo Visual Studio: F5 sobe a API e o Vite juntos.
- Pela linha de comando: `dotnet run --project DotaAnalyst.Server` (o SPA proxy inicia o Vite) ou, separadamente, `npm run dev` em `dotaanalyst.client/`.
- API em `https://localhost:7208`; Vite em `https://localhost:59438`.

## Ao adicionar endpoints

O Vite só repassa para a API as rotas listadas em `server.proxy` no `dotaanalyst.client/vite.config.js` (hoje só `^/weatherforecast`). Todo controller novo precisa de uma entrada ali — prefira prefixar as rotas com `/api` e usar uma única regra `'^/api'`.

## Regras de negócio

- **Identidade do usuário:** o login é só pela Steam (OpenID); não existe senha própria. O SteamID64 do login é convertido no account ID do Dota: `accountId = steamId64 - 76561197960265728`.
- **Sessão:** cookie de autenticação `HttpOnly`.
- **Porta de entrada única:** o React fala apenas com a API em C#. Serviços externos (OpenDota, futuro serviço Go) são chamados pela API, nunca direto pelo front-end.
- **Fonte dos dados:** partidas e análises vêm da API pública do OpenDota (https://docs.opendota.com), atrás de uma interface (ex.: `IReplayAnalyzer`) para poder trocar a implementação.
- **Segredos:** nunca no `appsettings.json` nem no Git — user-secrets ao rodar localmente e variável de ambiente (via `.env` fora do Git) no Docker.
- **Execução:** `docker compose up` na raiz deve subir o projeto inteiro (API com o React embutido, PostgreSQL e, no futuro, o serviço Go).

## Plano de desenvolvimento

O roteiro do que construir e em que ordem fica em `work/plan.md`. A pasta `work/` é local e está no `.gitignore` — leia esse arquivo antes de começar uma etapa nova e atualize-o quando uma etapa terminar ou a ordem mudar.
