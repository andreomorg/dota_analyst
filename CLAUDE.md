# DotaAnalyst

Analisador de partidas de Dota 2: o usuário vê suas partidas e análises de replay num front-end React, servido por uma API em C#.

O dono do projeto está começando em C# — explique as decisões e os conceitos novos em português, de forma didática, em vez de só entregar código.

## Convenções

- **Código sempre em inglês:** classes, interfaces, métodos, propriedades, variáveis, rotas e pastas (ex.: `PlayersController`, `GET /api/players/{accountId}/matches`). Explicações e conversas continuam em português.

## Git

- Remoto: https://github.com/andreomorg/dota_analyst (branch `main`).
- O `git` não está no PATH; o executável que funciona é o do Visual Studio: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.
- **Tags de versão:** versionamento semântico `vMAJOR.MINOR.PATCH` (ex.: `v1.0.0`), com tag anotada (`git tag -a v1.1.0 -m "..."`) enviada com `git push origin <tag>`. O MAJOR acompanha a versão da API na URL (`/api/v1` → `v1.x.y`; uma `v2.0.0` só existe junto com `Controllers/V2/`). MINOR = funcionalidade nova compatível (ex.: endpoint novo); PATCH = correção. Tag ao fechar uma sprint ou correção, nunca sem pedido do dono do projeto.

## Estrutura

Back-end em **.NET 10** organizado em **DDD**, um projeto por camada:

- `DotaAnalyst.Domain/` — regras do Dota: entidades e value objects (`PlayerMatch`, `Kda`, `AccountId`, `Team`, `Hero`). Não referencia nenhum outro projeto nem pacote.
- `DotaAnalyst.Application/` — casos de uso (ex.: `Matches/GetRecentMatchesHandler`), DTOs de saída (`MatchSummary`) e as interfaces do que vem de fora (`Abstractions/`: `IMatchProvider`, `IHeroCatalog`). Referencia só o Domain.
- `DotaAnalyst.Infrastructure/` — implementações das interfaces: OpenDota (`OpenDota/`), cache e, no futuro, EF Core/PostgreSQL. Referencia Application.
- `DotaAnalyst.Server/` — a camada de API: controllers por versão (`Controllers/V1/`), `Program.cs` e OpenAPI (`/openapi/v1.json` em Development). Referencia Application e Infrastructure; não tem regra de negócio.
- `DotaAnalyst.Tests/` — testes xUnit, com uma pasta por camada (`dotnet test`).
- `dotaanalyst.client/` — front-end **React 19 + Vite**, em JavaScript (`.jsx`). Chamadas à API ficam em `src/api/`.
- `DotaAnalyst.slnx` — solução do Visual Studio 2026 que junta tudo.

## Regras do DDD

- Dependências só apontam para dentro: `Server → Application → Domain` e `Infrastructure → Application`. O Domain nunca conhece OpenDota, HTTP, banco ou ASP.NET.
- Regra de negócio fica no Domain (ex.: vitória = time do jogador igual ao time vencedor), nunca em controller, Infrastructure ou React.
- Formatos externos (JSON do OpenDota, tabelas do banco) ficam na Infrastructure e são convertidos para o domínio lá dentro (ex.: `OpenDotaMappings`).
- Cada camada registra seus serviços num `DependencyInjection.cs` próprio (`AddApplication()`, `AddInfrastructure(...)`), chamado pelo `Program.cs`.
- Controllers só validam a entrada, chamam um caso de uso e traduzem o resultado para HTTP.

## Como rodar

- Pelo Visual Studio: F5 sobe a API e o Vite juntos.
- Pela linha de comando: `dotnet run --project DotaAnalyst.Server` (o SPA proxy inicia o Vite) ou, separadamente, `npm run dev` em `dotaanalyst.client/`.
- API em `https://localhost:7208`; Vite em `https://localhost:59438`.
- Pelo Docker: `docker compose up --build` na raiz → `http://localhost:8080` (build de produção, só HTTP; porta configurável por `API_PORT` no `.env`, veja `.env.example`). `docker compose down` para parar.

## Docker

- `Dockerfile` multi-stage: estágio Node compila o React; estágio SDK copia o `dist/` para `DotaAnalyst.Server/wwwroot/` **antes** do `dotnet publish` (o `MapStaticAssets` só serve arquivos que existem no publish); estágio final só com o runtime do ASP.NET, usuário não-root, porta 8080.
- O build do container passa `-p:ExcludeSpaClient=true`, que desliga a referência ao `.esproj` no `DotaAnalyst.Server.csproj` (o React já é compilado no estágio Node; o estágio do SDK não tem Node nem a pasta do client). Nenhum outro projeto deve referenciar o `.esproj` — o Visual Studio às vezes adiciona essa referência sozinho; se aparecer, remova.
- Ao criar um projeto .NET novo que o Server use, adicione o `COPY` do `.csproj` e da pasta no `Dockerfile`.
- O `vite.config.js` só gera/lê o certificado HTTPS no `npm run dev`; o `npm run build` não pode depender do `dotnet`.
- `/api/health` é só liveness (não chama o OpenDota) e é usado pelo health check do compose. O compose **não** reinicia container `unhealthy` (o `restart` só age quando o processo morre); o status serve para `depends_on: condition: service_healthy`.
- Serviços novos (PostgreSQL, serviço Go) entram no `docker-compose.yml`; segredos vêm do `.env` (fora do Git e do `.dockerignore`), nunca escritos no YAML. O `.env` só alimenta a interpolação `${VAR}` do YAML — para chegar no container, cada variável precisa estar em `environment:`.

## Ao adicionar endpoints

- **Versionamento:** a API é versionada pela URL. Controllers da versão 1 ficam em `DotaAnalyst.Server/Controllers/V1/` (namespace `DotaAnalyst.Server.Controllers.V1`) com rota `api/v1/...` (ex.: `[Route("api/v1/players")]`). Mudança que quebra o contrato (remover/renomear campo, mudar significado ou código HTTP) não entra na v1: cria `Controllers/V2/` com rota `api/v2/...` e a v1 continua funcionando. Acrescentar endpoint ou campo novo não quebra o contrato e fica na versão atual.
- Endpoints técnicos ficam fora do versionamento: `/api/health`.
- O Vite só repassa para a API as rotas listadas em `server.proxy` no `dotaanalyst.client/vite.config.js`, e a única regra é `'^/api'`. Toda rota deve começar com `/api`; senão o React não a alcança em desenvolvimento.

## Regras de negócio

- **Identidade do usuário:** o login é só pela Steam (OpenID); não existe senha própria. O SteamID64 do login é convertido no account ID do Dota: `accountId = steamId64 - 76561197960265728`.
- **Sessão:** cookie de autenticação `HttpOnly`.
- **Porta de entrada única:** o React fala apenas com a API em C#. Serviços externos (OpenDota, futuro serviço Go) são chamados pela API, nunca direto pelo front-end.
- **Fonte dos dados:** partidas e análises vêm da API pública do OpenDota (https://docs.opendota.com), sempre atrás de interfaces da Application (`IMatchProvider`, `IHeroCatalog` e, para análise de replay, `IReplayAnalyzer`) para poder trocar a implementação.
- **Regra de vitória:** `player_slot` de 0 a 127 é Radiant e de 128 a 255 é Dire; o jogador venceu se o time dele é o vencedor. Sem resultado conhecido, a vitória fica indefinida (`null`), nunca "derrota".
- **Account ID:** aceita o account ID do Dota (32 bits, maior que zero) ou o SteamID64, que é convertido.
- **Segredos:** nunca no `appsettings.json` nem no Git — user-secrets ao rodar localmente e variável de ambiente (via `.env` fora do Git) no Docker.
- **Execução:** `docker compose up` na raiz deve subir o projeto inteiro (API com o React embutido, PostgreSQL e, no futuro, o serviço Go).

## Plano de desenvolvimento

O roteiro do que construir e em que ordem fica em `work/plan.md`. A pasta `work/` é local e está no `.gitignore` — leia esse arquivo antes de começar uma etapa nova e atualize-o quando uma etapa terminar ou a ordem mudar.
