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

Os arquivos `WeatherForecast*` são exemplo do template e podem ser removidos quando o primeiro endpoint real existir.

## Como rodar

- Pelo Visual Studio: F5 sobe a API e o Vite juntos.
- Pela linha de comando: `dotnet run --project DotaAnalyst.Server` (o SPA proxy inicia o Vite) ou, separadamente, `npm run dev` em `dotaanalyst.client/`.
- API em `https://localhost:7208`; Vite em `https://localhost:59438`.

## Ao adicionar endpoints

O Vite só repassa para a API as rotas listadas em `server.proxy` no `dotaanalyst.client/vite.config.js` (hoje só `^/weatherforecast`). Todo controller novo precisa de uma entrada ali — prefira prefixar as rotas com `/api` e usar uma única regra `'^/api'`.

## Direção do projeto

1. **Agora:** buscar dados de partidas na API pública do OpenDota (https://docs.opendota.com) a partir do C#, usando `HttpClient` registrado via `AddHttpClient`, e exibir no React.
2. Manter a análise atrás de uma interface (ex.: `IReplayAnalyzer`), com a primeira implementação usando o OpenDota.
3. **Futuro:** um serviço separado em Go usando o parser `manta` para ler os `.dem` diretamente. A API em C# continua sendo a única porta de entrada do React e chama o serviço Go (HTTP/JSON primeiro, fila depois se o parsing ficar lento).
