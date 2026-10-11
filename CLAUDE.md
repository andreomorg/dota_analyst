# DotaAnalyst

Analisador de partidas de Dota 2: o usuário vê suas partidas e análises de replay num front-end React, servido por uma API em C#.

O dono do projeto está começando em C# — explique as decisões e os conceitos novos em português, de forma didática, em vez de só entregar código.

## Convenções

- **Código sempre em inglês:** classes, interfaces, métodos, propriedades, variáveis, rotas e pastas (ex.: `PlayersController`, `GET /api/players/{accountId}/matches`). Explicações e conversas continuam em português.

## Git

- Remoto: https://github.com/andreomorg/dota_analyst (branch `main`).
- O `git` não está no PATH; o executável que funciona é o do Visual Studio: `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.
- **Tags de versão:** versionamento semântico `vMAJOR.MINOR.PATCH` (ex.: `v1.0.0`), com tag anotada (`git tag -a v1.1.0 -m "..."`) enviada com `git push origin <tag>`. O MAJOR acompanha a versão da API na URL (`/api/v1` → `v1.x.y`; uma `v2.0.0` só existe junto com `Controllers/V2/`). MINOR = funcionalidade nova compatível (ex.: endpoint novo); PATCH = correção. Tag ao fechar uma sprint ou correção, nunca sem pedido do dono do projeto. Enviar uma tag `v*` dispara o `.github/workflows/release.yml`: roda os mesmos testes do CI e, se passarem, publica a imagem `ghcr.io/andreomorg/dota_analyst:<versão>` (sem o `v`, ex.: `1.2.0`) e `:latest`.

## Estrutura

Back-end em **.NET 10** organizado em **DDD**, um projeto por camada:

- `DotaAnalyst.Domain/` — regras do Dota: entidades e value objects (`Matches/`: `PlayerMatch`, `MatchDetails` (agregado), `MatchPlayer`, `MatchId`, `Benchmark`, `Kda`, `Team`; `Players/AccountId`; `Heroes/Hero`; `Items/Item`) e domain services (`Analysis/PerformanceEvaluator`). Não referencia nenhum outro projeto nem pacote.
- `DotaAnalyst.Application/` — casos de uso (`Matches/GetRecentMatchesHandler`, `Matches/GetMatchAnalysisHandler`, `Players/GetUserProfileHandler`), DTOs de saída (`MatchSummary`, `MatchAnalysis`, `UserProfile`) e as interfaces do que vem de fora (`Abstractions/`: `IMatchProvider`, `IReplayAnalyzer`, `IHeroCatalog`, `IItemCatalog`, `IMatchRepository`, `IPlayerSearchRepository`, `ISteamProfileProvider`; exceções `DataSourceUnavailableException` (OpenDota) e `StorageUnavailableException` (banco)). Referencia só o Domain.
- `DotaAnalyst.Infrastructure/` — implementações das interfaces: OpenDota (`OpenDota/`), perfis da Steam Web API (`Steam/`), cache, catálogos em camadas (`Catalogs/LayeredCatalog`: memória → banco → OpenDota) e PostgreSQL com EF Core (`Persistence/`: `AppDbContext`, repositórios, `Migrations/`). Referencia Application. O `IMemoryCache` tem `SizeLimit`: toda entrada usa `Caching/CacheEntries` (catálogos com prioridade alta, partidas normal); "partida não encontrada" fica em cache por 1 minuto, com prioridade baixa (expulsa primeiro).
- `DotaAnalyst.Server/` — a camada de API: controllers por versão (`Controllers/V1/`), login com a Steam (`Authentication/`), `Program.cs` e OpenAPI (`/openapi/v1.json` em Development). Referencia Application e Infrastructure; não tem regra de negócio.
- `DotaAnalyst.Tests/` — testes xUnit, com uma pasta por camada (`dotnet test`). Os do banco (`Infrastructure/Persistence/`) usam PostgreSQL real via Testcontainers (`PostgresFixture`, coleção `Postgres`): precisam do Docker rodando.
- `dotaanalyst.client/` — front-end **React 19 + Vite**, em JavaScript (`.jsx`). Chamadas à API ficam em `src/api/`.
- `DotaAnalyst.slnx` — solução do Visual Studio 2026 que junta tudo.

## Regras do DDD

- Dependências só apontam para dentro: `Server → Application → Domain` e `Infrastructure → Application`. O Domain nunca conhece OpenDota, HTTP, banco ou ASP.NET.
- Regra de negócio fica no Domain (ex.: vitória = time do jogador igual ao time vencedor), nunca em controller, Infrastructure ou React.
- Formatos externos (JSON do OpenDota, tabelas do banco) ficam na Infrastructure e são convertidos para o domínio lá dentro (ex.: `OpenDotaMappings`).
- Cada camada registra seus serviços num `DependencyInjection.cs` próprio (`AddApplication()`, `AddInfrastructure(...)`), chamado pelo `Program.cs`.
- Tabelas guardam "records" da Infrastructure (`MatchRecord`, `PlayerSearchRecord`, `CatalogEntryRecord`), nunca entidades do Domain; o mapeamento EF fica em `IEntityTypeConfiguration` ao lado de cada record. Nomes no banco em `snake_case`.
- Controllers só validam a entrada, chamam um caso de uso e traduzem o resultado para HTTP.

## Como rodar

- **Banco local:** `docker compose up -d db` e a connection string no user-secrets do Server (`ConnectionStrings:Database`, já configurado nesta máquina). As migrations rodam sozinhas na subida da API. Para ver o banco: pgAdmin (instalado), `localhost:5432`, banco/usuário `dotaanalyst`, senha do `.env`.
- Pelo Visual Studio: F5 sobe a API e o Vite juntos.
- Pela linha de comando: `dotnet run --project DotaAnalyst.Server` (o SPA proxy inicia o Vite) ou, separadamente, `npm run dev` em `dotaanalyst.client/`.
- API em `https://localhost:7208`; Vite em `https://localhost:59438`.
- Pelo Docker: `docker compose up --build` na raiz → `http://localhost:8080` (build de produção, só HTTP; API + PostgreSQL). Precisa de um `.env` com `POSTGRES_PASSWORD` (veja `.env.example`); porta por `API_PORT`. `docker compose down` para parar (`-v` apaga os dados do banco).

## Banco (PostgreSQL + EF Core)

- **O banco é uma camada a mais, nunca ponto único de falha:** erro de banco vira `StorageUnavailableException`, que os casos de uso registram como aviso e seguem pelo OpenDota. O `StorageGuard` (singleton) traduz os erros e funciona como *circuit breaker*: depois de uma falha de **conexão** o banco é pulado por 30 s (erro respondido pelo próprio PostgreSQL, como chave duplicada, ou dado ilegível não abre o disjuntor).
- **Sem connection string a API sobe mesmo assim**, só com o OpenDota (`NoDatabase` no lugar dos repositórios, aviso no log). Com o banco fora na subida, a API leva ~30 s para abrir a porta (timeouts) e depois funciona normalmente.
- **Repositórios usam `IDbContextFactory`** (um `DbContext` curto por operação), porque o caso de uso de análise consulta partida, heróis e itens em paralelo e um `DbContext` não aceita operações simultâneas.
- **O que guarda:** `matches` (partida com resultado conhecido, agregado inteiro em `jsonb` no formato `StoredMatchDetails`), `player_searches` (account ID pesquisado com sucesso, sem dados do visitante), `catalog_entries` (heróis/itens) e `data_protection_keys`.
- **Partidas:** salva toda partida com resultado conhecido. Parseada salva é definitiva (nunca volta ao OpenDota; só outra parseada a substitui, o que recupera uma linha ilegível); não parseada salva é usada por 15 min (`UnparsedRefreshInterval`), depois o OpenDota é consultado de novo, e continua sendo usada se o OpenDota cair. `saved_at` vem do `TimeProvider` da aplicação (o mesmo relógio que mede a idade).
- **Catálogos:** memória → banco (cópia com menos de 24 h) → OpenDota (salva no banco) → cópia antiga do banco se o OpenDota falhar ou responder lista vazia → vazio. Uma carga por vez (`CatalogLoadLocks`).
- **Gravações concorrentes** usam upsert atômico (`INSERT ... ON CONFLICT`) via `ExecuteSqlAsync` com interpolação (vira parâmetro, sem SQL injection). `timestamptz` só aceita UTC: use `ToUniversalTime()`.
- **Migration nova** ao mudar record/configuração: `dotnet ef migrations add <Nome> --project DotaAnalyst.Infrastructure --startup-project DotaAnalyst.Infrastructure --output-dir Persistence/Migrations` (o `DesignTimeDbContextFactory` dispensa connection string). O teste `Model_HasNoChangesMissingAMigration` falha se faltar migration. Migrations aplicadas pelo `DatabaseMigrator` (hosted service): primeira tentativa antes da API abrir a porta e antes do Data Protection ler a tabela de chaves (por isso é registrado antes do `AddDataProtection`); se falhar, tenta de novo a cada 30 s em segundo plano. `Database:MigrateOnStartup=false` desliga.
- **Connection string** `ConnectionStrings:Database`, sempre segredo. Use `Timeout=5` para falhar rápido com o banco fora. Produção: Neon (`SSL Mode=Require`), cadastrada no painel do Render.

## Login (Steam OpenID)

- **Fluxo:** o React navega (link, não `fetch`) para `GET /api/v1/auth/login?returnUrl=<caminho local>` → página oficial da Steam (senha **ou QR code pelo app Steam**) → `GET /api/auth/steam/callback` (técnico, fora do versionamento) → volta ao `returnUrl` com o cookie. Falha/cancelamento → `/?login=failed`. Pacote `AspNet.Security.OpenId.Steam`, que confirma a resposta com a própria Steam (`check_authentication`). O site nunca vê senha nem token; recebe só o SteamID64.
- **Não fazer QR code própria** (API interna `BeginAuthSessionViaQR`): entregaria ao servidor um token da conta Steam inteira, é o padrão de golpes de roubo de conta e não é pública. A página OpenID da Steam já tem QR code.
- **Sessão:** cookie `dotaanalyst.session`, `HttpOnly`, `SameSite=Lax`, `Secure` quando a requisição é HTTPS (`SameAsRequest`, para o compose em `http://localhost` funcionar), 30 dias renovados com o uso. Guarda só a claim `account_id` (`Authentication/CurrentUser` é o único lugar que lê/escreve claims e interpreta o identificador da Steam). O cookie de correlação do login também é `SameSite=Lax` (o padrão `None` é recusado em HTTP).
- **API nunca redireciona para a Steam:** sem sessão, `[Authorize]` responde 401 `ProblemDetails` (403 para acesso negado); só o endpoint de login desafia a Steam. `returnUrl` só local (`Url.IsLocalUrl`, contra *open redirect*). Logout é `POST /api/v1/auth/logout` (204, idempotente).
- **`GET /api/v1/me`** → `{ accountId, steamId64, personaName, avatarUrl }`. `steamId64` é **texto** (maior que 2^53, o JavaScript arredondaria). Nome e avatar vêm do `ISteamProfileProvider` (Steam Web API `GetPlayerSummaries`, cache de 1 h, só avatar `https`); sem a chave `Steam:ApiKey` (segredo; `STEAM_API_KEY` no `.env`, `Steam__ApiKey` no Render) ficam `null`, e falha da Steam nunca derruba o `/me`. O `HttpClient` da Steam não registra log (`RemoveAllLoggers`): a chave vai na URL.
- **Testes:** `ApiFactory` troca o *backchannel* da Steam por uma Steam falsa (`FakeSteamOpenId`), então o fluxo completo de login é testado offline (`AuthApiTests`).
- **Front-end:** sessão em `src/auth/` (`CurrentUserProvider`, `useCurrentUser()` → `{ status, user, logout }`), cabeçalho em `src/layout/SiteHeader.jsx`. Logado, a página de partidas carrega as do próprio usuário; a busca de outros jogadores continua sem login.

## Produção (Render)

- No ar em **https://dotaanalyst.onrender.com** — serviço `dotaanalyst` (workspace "DotaAnalyst"), plano Free, região Virginia. Configuração descrita no `render.yaml`; o Render compila o `Dockerfile` do `main`.
- **Publicar uma versão:** criar e enviar uma tag `v*` a partir do `main`. O `release.yml` roda os testes, publica a imagem no ghcr.io e chama o deploy hook do Render (segredo `RENDER_DEPLOY_HOOK_URL` no GitHub). Auto-deploy está desligado: push no `main` sozinho não muda o site.
- **Logs e deploys:** painel do Render ou o MCP do Render (`list_logs`, `list_deploys`). Health check: `/api/health`.
- Plano grátis: dorme após 15 min sem acesso (primeiro acesso ~1 min), 512 MB. Não usar "pingers" para mantê-lo acordado.

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
- JSON em camelCase; enums saem como texto camelCase (`"goldPerMinute"`, `"strong"`), configurado no `Program.cs`. Erros sempre como `ProblemDetails` (400 entrada inválida, 404 não encontrado, 429 limite excedido, 502 OpenDota indisponível, 500 inesperado).
- **Limite de requisições:** todo controller (`MapControllers().RequireRateLimiting(...)`) tem limite por IP, janela fixa, configurado em `RateLimiting` no `appsettings.json` (padrão 60/minuto) — `DotaAnalyst.Server/RateLimiting/`. `/api/health` fica fora. Excedeu → 429 com `Retry-After`.
- **Forwarded headers:** `ForwardedHeaders:Enabled` (desligado por padrão e no `docker compose` local) só deve ser ligado atrás do proxy da hospedagem (variável `ForwardedHeaders__Enabled=true`, já no `render.yaml`). Ligado sem proxy, qualquer um falsifica o IP pelo `X-Forwarded-For` e burla o limite. `ForwardedHeaders:ForwardedForHeaderName` diz de qual cabeçalho vem o IP do cliente (padrão `X-Forwarded-For`) e `ForwardedHeaders:ForwardLimit` (padrão 1) quantos proxies são confiáveis: **depende da hospedagem**. No **Render** usamos `CF-Connecting-IP` (verificado em 11/10/2026: o caminho é Cloudflare → balanceador do Render → proxy local, o `X-Forwarded-For` tem vários saltos internos e mantém o que o cliente manda; o Cloudflare sobrescreve o `CF-Connecting-IP` com o IP real). Ao trocar de hospedagem, ligar `Diagnostics__LogForwardedHeaders=true` temporariamente para ver os cabeçalhos reais e desligar depois (registra IPs).
- O limite conta IPv6 pela rede /64 (um usuário costuma controlar uma /64 inteira) e IPv4-mapeado como IPv4 (`ApiRateLimiting.ClientKey`).
- **Pacotes vulneráveis:** o CI falha se `dotnet list package --vulnerable --include-transitive` acusar algo. Dependência indireta vulnerável se corrige com referência direta a uma versão corrigida, com comentário no `.csproj` (ex.: `AngleSharp` no Server).
- **Testes de integração** da API ficam em `DotaAnalyst.Tests/Server/` com `ApiFactory` (OpenDota, repositórios, Steam OpenID e perfis da Steam trocados por fakes; connection string e `Steam:ApiKey` vazias de propósito para não usar as do user-secrets; nenhum teste acessa a internet — só o Testcontainers baixa a imagem do PostgreSQL na primeira vez). Endpoint novo ganha teste lá.
- O Vite só repassa para a API as rotas listadas em `server.proxy` no `dotaanalyst.client/vite.config.js`, e a única regra é `'^/api'`. Toda rota deve começar com `/api`; senão o React não a alcança em desenvolvimento.

## Regras de negócio

- **Identidade do usuário:** o login é só pela Steam (OpenID; veja "Login"); não existe senha própria. O SteamID64 do login é convertido no account ID do Dota: `accountId = steamId64 - 76561197960265728`.
- **Sessão:** cookie de autenticação `HttpOnly`.
- **Porta de entrada única:** o React fala apenas com a API em C#. Serviços externos (OpenDota, futuro serviço Go) são chamados pela API, nunca direto pelo front-end.
- **Fonte dos dados:** partidas e análises vêm da API pública do OpenDota (https://docs.opendota.com), sempre atrás de interfaces da Application (`IMatchProvider`, `IReplayAnalyzer`, `IHeroCatalog`, `IItemCatalog`) para poder trocar a implementação. O `IReplayAnalyzer` só entrega os dados da partida; quem interpreta é o Domain.
- **Avaliação de desempenho:** usa os benchmarks da partida (percentil de 0 a 1 comparado a outros jogadores **do mesmo herói**; maior é sempre melhor, inclusive em mortes). Percentil ≥ 0,70 é ponto forte, ≤ 0,30 é ponto a melhorar, o resto é médio (`PerformanceEvaluator`). Cura por minuto igual a zero é ignorada (herói sem cura não é "ponto fraco"). Pontos fortes vêm do melhor para o pior; pontos a melhorar, do pior para o melhor.
- **Nomes desconhecidos:** herói ou item sem nome no catálogo aparece como `Unknown hero` / `Unknown item`; catálogo indisponível nunca derruba a resposta.
- **Regra de vitória:** `player_slot` de 0 a 127 é Radiant e de 128 a 255 é Dire; o jogador venceu se o time dele é o vencedor. Sem resultado conhecido, a vitória fica indefinida (`null`), nunca "derrota".
- **Account ID:** aceita o account ID do Dota (32 bits, maior que zero) ou o SteamID64, que é convertido.
- **Segredos:** nunca no `appsettings.json` nem no Git — user-secrets ao rodar localmente e variável de ambiente (via `.env` fora do Git) no Docker.
- **Execução:** `docker compose up` na raiz deve subir o projeto inteiro (API com o React embutido, PostgreSQL e, no futuro, o serviço Go).

## Plano de desenvolvimento

O roteiro do que construir e em que ordem fica em `work/plan.md`. A pasta `work/` é local e está no `.gitignore` — leia esse arquivo antes de começar uma etapa nova e atualize-o quando uma etapa terminar ou a ordem mudar.
