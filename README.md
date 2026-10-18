# DotaAnalyst

[![CI](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/andreomorg/dota_analyst/actions/workflows/ci.yml)
[![Coach ML](https://github.com/andreomorg/dota_analyst/actions/workflows/coach-ml.yml/badge.svg?branch=main)](https://github.com/andreomorg/dota_analyst/actions/workflows/coach-ml.yml)

Analisador de partidas de Dota 2. Entre com a sua conta Steam (senha ou QR code no app Steam) — ou informe qualquer account ID do Dota/SteamID64 — e veja as partidas recentes e uma análise de cada uma, com dados do [OpenDota](https://www.opendota.com) e com o que o próprio projeto lê do **replay** da partida.

**Demonstração:** https://dotaanalyst.onrender.com. A hospedagem está configurada no plano gratuito do Render; o primeiro acesso após inatividade pode demorar.

## O que a análise mostra

A página de uma partida abre com o placar dos dois times e é dividida em **abas** (cada uma com o próprio endereço, `?tab=`, para compartilhar ou abrir em outra aba do navegador):

| Aba | O que tem |
|---|---|
| **Resumo** | Destaques, desempenho, gráfico de vantagem dos times e atalhos para os assuntos da partida. |
| **Como melhorar** | Dicas das regras atuais, com links para os números que as sustentam, e percentis do OpenDota. O coach com LLM continua experimental. |
| **Fase de rotas** | Resultado da rota, métricas, gráfico por estatística até 15 minutos e mapa de calor de 0–10 minutos. |
| **Posicionamento** | Mapa de calor após a fase de rotas, mortes, wards e indicadores de posicionamento e visão. |
| **Lutas e objetivos** | Lutas detectadas, participação, dano causado/recebido e eventos de objetivos. |
| **Build** | Itens iniciais e compras, evolução econômica e ordem observada de habilidades. |
| **Jogadores** | Os dez jogadores, itens finais e comparação de estatísticas minuto a minuto. |

As comparações com profissionais e jogadores de alto nível do mesmo herói e posição ficam na aba do respectivo assunto, com seleção de momento quando disponível. Abas dependem dos dados existentes e do estado de processamento do replay.

O visual segue o design "Obsidian Telemetry" feito no Google Stitch (tema escuro, números em fonte mono, verde/vermelho para bom/ruim, violeta para "você"), com as fontes servidas pelo próprio site.

## Coach com modelo de linguagem — experimental

O backend inclui um fluxo experimental para a aba **Como melhorar**, integrado por rotas internas entre a API C# e o serviço Python. O frontend público continua usando as dicas das regras existentes.

- **Fatos verificáveis:** episódios de mortes e lutas, cobertura das posições e distinção entre situação confirmada e desconhecida. Horários e distância ao aliado observado são calculados pelo código, com os instantes das amostras preservados.
- **Dois caminhos experimentais:** seleção restrita de achados/exercícios por IDs, com texto factual montado pela aplicação; e geração de hipóteses condicionais, alternativas e exercícios a partir de um contexto anterior ao evento. Referências válidas não garantem que o texto livre esteja correto.
- **Observação separada da hipótese:** o contrato experimental de reflexão v2 mantém números e classificação na observação determinística. Posições oniscientes não representam visão, intenção ou recursos disponíveis ao jogador; o resultado posterior fica separado dos fatos na âncora de revisão.
- **Aprendizado avaliado, sem promoção:** o experimento factual LoRA no Qwen3-0.6B concluiu uma época com 159.335 exemplos de 968 partidas. Em 288 casos de 96 partidas separadas, o adaptador reproduziu os alvos provisórios em 288/288; o modelo base acertou os fatos em 27/288 após normalização. A revisão mostrou somente dois exercícios fixos: o ganho demonstra extração e reprodução da política ensinada, sem comprovar coaching tático individualizado. Os casos incluem uma entrada repetida dentro da validação (287 inputs únicos), sem sobreposição exata com treino.

Os contratos e testes ficam nas pastas `Coaching` de Domain, Application e Infrastructure. O projeto [coach-ml](coach-ml/README.md) concentra curadoria, auditoria, treino, avaliação e serviço HTTP Python. A API C# entrega contextos de replays arquivados e consome revisões por rotas internas autenticadas; o Python pode buscar esses contextos por callback. O perfil Docker `coach-ml` inicia o serviço, com prévia por política em CPU por padrão e Ollama configurável. As dicas públicas continuam nas regras existentes. Dados, conjuntos de avaliação e pesos locais não são distribuídos pelo repositório; `docker compose up` não inicia treino nem instala modelos.

A [documentação do coach-ml](coach-ml/README.md) descreve a separação entre dados, curadoria, treino e avaliação. O projeto Python audita treino/validação juntos, produz conjuntos sem entradas repetidas, confere orçamento de tokens e avalia previsões por competência. Esses controles verificam integridade e limites dos exemplos, sem conceder aprovação tática.

O experimento contextual usa seis competências e uma política versionada: cada exercício aponta para instantes/registros concretos e pede uma anotação verificável no replay. O conjunto preparado tem 34.827 exemplos de treino e 3.442 de validação nas mesmas 968/96 partidas. O progresso das execuções é registrado em artefatos locais, fora do Git. Não há modelo treinado promovido ao serviço nem aprendizado automático a cada replay.

```mermaid
flowchart LR
    Browser[React / Steam] --> API[API C#]
    API --> Domain[Domain / evidências de replay]
    API -->|Revisão interna autenticada| Python[Python coach-ml]
    Python -->|Callback de contexto arquivado| API
    Python --> Preview[Prévia CPU por política]
    Python --> Ollama[Ollama opcional]
    Corpus[Corpus local separado por partida] --> Audit[Curadoria / auditoria / tokenização]
    Audit --> Train[Treino LoRA no container com GPU]
    Train --> Checkpoints[Adaptador separado / checkpoints selados]
    Checkpoints --> Evaluation[Avaliação base versus adaptado / review]
    Evaluation --> Decision[Decisão explícita de promoção]
```

O treinamento é independente do serviço HTTP. A promoção exige decisão explícita; o fluxo acima não instala o adaptador automaticamente. Código é versionado, enquanto replays, datasets, pesos, credenciais e relatórios permanecem locais.

## Como funciona

O [diagrama de arquitetura](#arquitetura) mostra os processos locais, os serviços em produção e as integrações externas.

- **O processamento pesado fica na máquina do dono do projeto:** ler replays (dezenas de MB cada), coletar partidas, calcular distribuições e guardar os documentos brutos. **O Neon guarda os dados refinados para consulta** — distribuições de comparação e análises, incluindo grades compactas de mapa de calor, sem trilhas de posições cruas. O site local e a produção consultam a base de comparação publicada.
- **A base de comparação cresce onde faz falta:** o coletor olha os heróis das últimas 20 partidas da conta foco e, enquanto algum tiver menos de 10 partidas de alto nível na base, busca primeiro partidas com esses heróis (o de menos dados antes); depois volta às profissionais.
- Com essa máquina desligada o site continua no ar: o que já foi analisado aparece normalmente; replays novos ficam "indisponíveis" até ela voltar.
- Dados: [OpenDota](https://www.opendota.com) (API pública) e replays dos servidores da Valve. O minimapa e os retratos/ícones de heróis e itens são arte do próprio Dota 2, servida localmente; projeto de fã, não comercial. Rever os direitos antes de monetizar.

## Tecnologias

- **Back-end:** ASP.NET Core (.NET 10) em DDD — `Domain` (regras do Dota), `Application` (casos de uso), `Infrastructure` (OpenDota, Steam, banco, parser, coletor) e `Server` (API).
- **Banco:** PostgreSQL com EF Core — local no Docker; [Neon](https://neon.tech) em produção.
- **Front-end:** React 19 + Vite, CSS próprio (sem framework), gráficos e mapas em SVG/canvas feitos à mão.
- **Leitura de replays:** serviço próprio em Go (`replay-parser/`).
- **Coach experimental:** Python 3.11+, serviço HTTP interno, auditoria e curadoria em CPU; PyTorch/Transformers/PEFT opcionais para LoRA offline no container com GPU.
- **Login:** Steam OpenID — o site nunca vê senha nem token; recebe só o SteamID.
- **Testes e entrega:** xUnit (com PostgreSQL real via [Testcontainers](https://dotnet.testcontainers.org)), testes Go e unittest Python; GitHub Actions (CI, workflow Coach ML e publicação da imagem `ghcr.io/andreomorg/dota_analyst`).

## Rodando com Docker

Precisa só do [Docker](https://www.docker.com/products/docker-desktop/).

```sh
cp .env.example .env   # preencha POSTGRES_PASSWORD e REPLAY_PARSER_API_KEY (instruções no arquivo)
docker compose up --build
```

Abra http://localhost:8080. Sobem a API (com o React embutido), o PostgreSQL e o parser de replays. Para parar: `docker compose down` (os dados do banco ficam no volume `db-data`; `docker compose down -v` apaga tudo).

O `.env` define a porta da aplicação (`API_PORT`), a senha do PostgreSQL local (`POSTGRES_PASSWORD`, obrigatória), o token entre a API e o parser (`REPLAY_PARSER_API_KEY`, obrigatório), a porta do banco na sua máquina (`POSTGRES_PORT`, só em 127.0.0.1) e, opcionalmente, a [Steam Web API key](https://steamcommunity.com/dev/apikey) (`STEAM_API_KEY`) para mostrar nome e avatar de quem está logado. O login com a Steam funciona também em `http://localhost:8080`.

Serviços opcionais, ligados por perfil (`COMPOSE_PROFILES` no `.env`, ex. `tunnel,collector`, ou `docker compose --profile <perfil> up -d`):

- **`tunnel`** — túnel ngrok para a produção usar o parser desta máquina (`NGROK_AUTHTOKEN`, `NGROK_URL`).
- **`collector`** — coletor da base de comparação: parseia partidas profissionais e de alto nível, guarda o bruto no PostgreSQL local e publica o resultado (`COLLECTOR_FOCUS_ACCOUNT_ID`; `COLLECTOR_TARGET_PER_HERO`, padrão 10, e `COLLECTOR_DAILY_LIMIT`, padrão 600; `MIRROR_DATABASE` para publicar também no Neon).
- **`coach-ml`** — serviço Python interno, habilitado com `COACH_ML_ENABLED=true` e chave compartilhada `COACH_ML_API_KEY`. O padrão é prévia CPU por política; esse perfil não inicia treinamento. Configuração e comandos em [coach-ml/README.md](coach-ml/README.md).

## Rodando para desenvolver

Para executar API e React no host, precisa do .NET 10 SDK, Node.js 24 e Docker para o PostgreSQL. Go é necessário para desenvolver ou executar o parser no host; Python 3.11+ é necessário para executar o coach fora do container.

1. Suba o banco: `docker compose up -d db`.
2. Uma vez, diga à API onde está o banco (fica no user-secrets, fora do Git):
   ```sh
   dotnet user-secrets set "ConnectionStrings:Database" "Host=localhost;Port=5432;Database=dotaanalyst;Username=dotaanalyst;Password=<POSTGRES_PASSWORD do .env>" --project DotaAnalyst.Server
   ```
3. **VS Code:** F5 → **"API + React"** (configuração em `.vscode/`). **Visual Studio 2026:** abra `DotaAnalyst.slnx` e aperte F5. **Linha de comando:** `dotnet run --project DotaAnalyst.Server --launch-profile https` — o Vite é iniciado pelo SPA proxy.

A aplicação abre em https://localhost:59438 (API em https://localhost:7208). Na primeira vez, confie no certificado de desenvolvimento: `dotnet dev-certs https --trust`. As tabelas são criadas sozinhas quando a API sobe (migrations). Documentação da API (só em Development): https://localhost:59438/api/docs.

Para olhar o banco, use o pgAdmin (ou outro cliente): servidor `localhost`, porta `5432`, banco/usuário `dotaanalyst`, senha do `.env`.

**Replay com a API executada no host:** o serviço `replay-parser` do Compose não publica uma porta no host. Para esse modo de desenvolvimento, execute o parser Go em outro terminal, dentro de `replay-parser/`:

```powershell
$env:REPLAY_PARSER_API_KEY = '<token compartilhado com a API>'
go run ./cmd/replay-parser
```

Na raiz do repositório, configure a API para acessá-lo:

```sh
dotnet user-secrets set "ReplayParser:BaseUrl" "http://localhost:8081/" --project DotaAnalyst.Server
dotnet user-secrets set "ReplayParser:ApiKey" "<mesmo token do parser>" --project DotaAnalyst.Server
```

Sem parser configurado e acessível, o site continua oferecendo os dados disponíveis, mas o processamento de novos replays não fica habilitado. O arquivo `.env` é usado pelo Compose; a API executada diretamente recebe configuração por user-secrets, variáveis de ambiente ou arquivos de configuração.

### Verificações de desenvolvimento

| Parte | Comando | Diretório |
|---|---|---|
| .NET | `dotnet test DotaAnalyst.Tests -p:ExcludeSpaClient=true` | Raiz |
| React | `npm ci`, `npm run lint` e `npm run build` | `dotaanalyst.client/` |
| Parser Go | `go test ./...` | `replay-parser/` |
| Python | `python -m pip install ./coach-ml` e `python -B -m unittest discover -s coach-ml/tests -v` | Raiz |

Os testes .NET de persistência usam PostgreSQL descartável via Testcontainers e precisam do Docker. Os testes Python de contrato não exigem GPU nem pesos. Para alterações pequenas, use filtros .NET, pacotes Go ou módulos unittest correspondentes à mudança; não é necessário repetir todas as suítes a cada tarefa.

**Mudou uma entidade ou tabela?** Crie uma migration:

```sh
dotnet ef migrations add <Nome> --project DotaAnalyst.Infrastructure --startup-project DotaAnalyst.Infrastructure --output-dir Persistence/Migrations
```

(`dotnet tool restore` instala o `dotnet-ef` na primeira vez.)

## Estrutura

| Pasta | O que tem |
|---|---|
| `DotaAnalyst.Domain/` | Regras do Dota: partidas, métricas, regiões do mapa, mapa de calor, lutas, build, comparações |
| `DotaAnalyst.Application/` | Casos de uso e interfaces do que vem de fora |
| `DotaAnalyst.Infrastructure/` | OpenDota, Steam, PostgreSQL, cliente do parser, coletor |
| `DotaAnalyst.Server/` | API (`/api/v1/...`), login com a Steam, `Program.cs` |
| `DotaAnalyst.Tests/` | Testes .NET, uma pasta por camada |
| `dotaanalyst.client/` | Front-end React (`src/analysis/` as abas da análise, `src/ui/` peças comuns, `src/index.css` cores e fontes do design) |
| `replay-parser/` | Serviço Go que lê os replays (`POST /v1/match`) |
| `coach-ml/` | Serviço HTTP Python, curadoria, auditoria, treino LoRA e avaliação |
| `docs/diagrams/` | Índice e convenções dos diagramas Mermaid do README |

Regras de arquitetura, decisões e detalhes de cada parte: [`CLAUDE.md`](CLAUDE.md) e [`replay-parser/CLAUDE.md`](replay-parser/CLAUDE.md).

### Módulos internos do Go e Python

Os dois serviços usam módulos com responsabilidades separadas e interfaces nas fronteiras externas. No Go, `parsing` coordena download, timeout, capacidade compartilhada e consumo do resultado; `httpapi` mantém autenticação e contrato HTTP. Os coletores Manta e o documento `/v1/match` permanecem compatíveis. O comando `inspect` pode gerar um manifesto local separado com hashes e identificação do build, sem modificar o documento v1.

No Python, `core` reúne JSON estrito, validação e leitura limitada de artefatos. `reviews` separa contratos, casos de uso, engines e cliente de rede; `experiments` separa preparação e coordenação do backend LoRA. A CLI e o HTTP compõem as implementações, e os comandos CPU continuam sem inicializar PyTorch. Novos fingerprints de treino incluem os arquivos das subpastas; experimentos congelados mantêm seu runtime original.

```mermaid
flowchart LR
    CSharp[API C#] --> GoHTTP[Go: httpapi]
    GoHTTP --> Parsing[parsing: coordenação e capacidade]
    Parsing --> Source[Porta de aquisição]
    Valve[Adaptador Valve] --> Source
    Parsing --> Decoder[Porta de análise]
    Manta[Manta e coletores] --> Decoder
    CSharp --> PythonHTTP[Python: transporte HTTP]
    PythonHTTP --> Reviews[reviews: casos de uso]
    Reviews --> Engines[Porta de engine e contexto]
    Adapters[Política, Ollama e cliente JSON] --> Engines
    CLI[CLI Python] --> Experiments[experiments: preparação e execução]
    Experiments --> Backend[Porta TrainingBackend]
    LoRA[Runtime LoRA com import tardio] --> Backend
```

As setas para portas mostram dependências e implementações. Testes de arquitetura verificam as fronteiras; testes de ciclo de vida verificam limites e liberação de recursos. Essa separação facilita manutenção e avaliação, mas não comprova melhoria tática do modelo.

Novos treinos Python podem usar um store SQLite local compartilhado para controlar tentativas, posse de GPU e pausa cooperativa após checkpoint selado. O runtime foi dividido em preparação, otimização, checkpoints e geração; um ensaio CUDA/BF16 mínimo confirmou tensores idênticos entre execução contínua e interrompida/retomada. O contrato de promoção vincula tarefa, dados, base e adaptador, exigindo decisão explícita; não instala modelos. Dependências de treino estão fixadas para a plataforma Docker atual. [Operação e limites](coach-ml/README.md#execução-gerenciada-e-pausa-persistente).

```mermaid
flowchart LR
    CLI[CLI de novo experimento] --> Store[Store SQLite local compartilhado]
    Store --> Attempt[Tentativa com posse de GPU]
    Attempt --> Optimize[Otimização]
    Pause[Solicitação de pausa] --> Store
    Optimize --> Checkpoint[Checkpoint selado e verificado]
    Checkpoint --> Paused[Pausa persistente e saída do worker]
    Paused --> Resume[Nova tentativa com mesmo freeze]
    Resume --> Optimize
    Optimize --> Generate[Geração e avaliação]
    Generate --> Candidate[Candidato com hashes e contrato]
    Candidate --> Decision[Decisão explícita com rollback]
```

O store coordena somente processos que usam o mesmo banco; não assume que um processo morreu por falta de atualização. Execuções legadas mantêm sua imagem original. No Go, três replays reais de diferentes durações produziram documentos idênticos aos anteriores; o cliente C# leu os três. Um teste de dois parses simultâneos com detector de corrida passou sob limite de 1 GiB. São evidências locais, sem garantia de capacidade de produção.

## Diagramas

Todos os diagramas são blocos Mermaid editáveis neste README, no mesmo estilo da visão geral do coach. O tema e os controles de navegação dependem do visualizador Markdown. Veja também o [índice dos diagramas](docs/diagrams/README.md).

### Arquitetura

```mermaid
flowchart TD
    Browser["Jogador / navegador"] --> Render["Render<br/>API C# + React"]
    subgraph Cloud["Produção na nuvem"]
        Render --> Neon["Neon / PostgreSQL<br/>Dados refinados"]
    end
    Render --> Steam["Steam OpenID"]
    Render --> OpenDota["OpenDota API"]
    Render -. "Replay via túnel ngrok" .-> Parser
    subgraph Local["Máquina local / Docker"]
        API["API C# + React local"] --> Parser["Parser Go<br/>Replay .dem"]
        API --> DB["PostgreSQL local<br/>Dados brutos e refinados"]
        Collector["Coletor de baselines"] --> Parser
        Collector --> DB
        API <-->|"HTTP autenticado"| Python["Python coach-ml<br/>Serviço de revisão"]
        Trainer["Python / treino GPU separado<br/>Pesos e checkpoints locais"]
    end
    Parser --> Valve["Valve / arquivos de replay"]
    Collector --> OpenDota
    Collector -->|"Publica distribuições"| Neon
```

### Camadas DDD

```mermaid
flowchart TD
    React["React client"] -->|HTTP| Server["DotaAnalyst.Server<br/>Controllers / Steam / DI"]
    Server --> Application["DotaAnalyst.Application<br/>Casos de uso / DTOs / interfaces"]
    Application --> Domain["DotaAnalyst.Domain<br/>Regras / métricas / fatos de replay"]
    Server -. "Registra implementações" .-> Infrastructure["DotaAnalyst.Infrastructure<br/>Persistência / clientes externos"]
    Infrastructure -->|"Depende das interfaces"| Application
    Infrastructure -->|HTTP| Python["Python coach-ml<br/>Processo separado"]
    Infrastructure -->|HTTP| Go["Parser Go<br/>Processo separado"]
```

As dependências .NET apontam para dentro. Domain não conhece HTTP, PostgreSQL, Python ou ML; as setas para os processos externos representam comunicação em execução.

### Coach experimental

O fluxo abaixo usa integração interna autenticada entre C# e Python. O serviço de revisão é separado do treinador; as dicas públicas seguem as regras existentes. Validação estrutural e avaliação de conteúdo são etapas distintas, sem adaptador promovido ao produto.

A observação determinística e o contrato de reflexão v2 permanecem preservados. A projeção qualitativa v3 (`PrepareBoundedCoaching`) distingue disponibilidade de distância na âncora e cobertura do episódio posterior; `PrepareAliasedCoaching` substitui IDs por aliases e restaura as referências no backend. A projeção temporal v4 (`PrepareTemporalCoaching`) compara a distância registrada ao mesmo aliado entre a âncora e dez segundos antes, informa a cobertura intermediária e mantém valores e tempos de proveniência no backend. Essas amostras não reconstroem trajeto, visão, intenção ou causa do resultado. Validação de formato e referências não aprova o conteúdo tático.

```mermaid
flowchart LR
    Replay["Replay arquivado"] --> Facts["C# / episódios e fatos<br/>Âncora separada do resultado"]
    Facts --> Context["C# / contexto limitado<br/>CoachMlClient"]
    Context -->|"HTTP autenticado"| Python["Python / revisão provisória<br/>Prévia CPU ou Ollama"]
    Python --> Validate["C# / valida contrato<br/>IDs e evidências"]
    Validate --> Review["Review de conteúdo<br/>Utilidade e limitações"]
    Review -. "Decisão explícita" .-> Product["Produto público<br/>Dicas atuais por regras"]
```

### Curadoria, treinamento e avaliação do modelo

O Python separa partidas, prepara os exemplos, audita dados e orçamento de tokens, confere o pior microbatch na GPU e congela o experimento. O treino salva checkpoints selados; a comparação base versus adaptado e o review vêm antes de qualquer decisão de promoção. O teste reservado permanece separado.

```mermaid
flowchart LR
    subgraph Data["Dados / CPU"]
        direction TB
        Corpus["Corpus / manifest<br/>Separação por partida"] --> Curation["Curadoria Python<br/>Seis competências / política v2"]
        Curation --> Audit["Auditoria<br/>Hashes / duplicatas / cobertura / tokens"]
    end
    subgraph Training["Treino / container GPU"]
        direction TB
        Prepare["Prepare<br/>Pior microbatch / gradientes / memória"] --> Freeze["Congela experimento<br/>Dados / código / base / ambiente"]
        Freeze --> Train["Run / uma época<br/>Base intacta / novo LoRA"]
        Train --> Checkpoint["Checkpoint selado<br/>Adaptador / otimizador / RNG / SHA-256"]
        Checkpoint -. "Verifica antes de retomar" .-> Train
        Train --> Final["Adaptador final separado"]
    end
    subgraph Comparison["Comparação / review"]
        direction TB
        Predictions["Mesmos casos de validação<br/>Respostas base e adaptado"] --> Evaluation["Avaliação pareada<br/>Fatos / foco / ganhos / regressões"]
        Evaluation --> Review["Review de conteúdo<br/>Decisão explícita sobre promoção"]
    end
    Data --> Training --> Comparison
```

Checkpoints permitem retomada; a avaliação usa o adaptador final. Testes reservados permanecem separados e não são abertos pela preparação. Loss baixa e reprodução de templates não comprovam domínio tático.

### Integração interna C# e Python

Há dois caminhos: C# envia as evidências preparadas ao Python, ou Python recebe partida/herói e busca o contexto arquivado por callback autenticado. Nenhum deles oferece acesso direto do Python ao banco ou inicia treinamento.

```mermaid
flowchart LR
    subgraph Direct["C# envia as evidências"]
        Request["POST /api/internal/coach/<br/>reviews/{matchId}/{heroId}"] --> Context["C# prepara contexto<br/>Replay arquivado / até três cenas"]
        Context --> Python["Python<br/>POST /v1/reviews"]
        Python --> Validate["C# valida resposta<br/>Engine / IDs / referências"]
    end
    subgraph Callback["Python busca as evidências"]
        ByMatch["Python<br/>POST /v1/reviews/by-match"] --> Get["C# / GET /api/internal/coach/<br/>contexts/{matchId}/{heroId}"]
        Get --> Bounded["Contexto limitado<br/>Sem acesso direto ao banco"]
        Bounded --> Response["Python revisa e responde<br/>Engine e referências explícitas"]
    end
```

As duas entradas exigem chave de máquina, independente da sessão Steam. O serviço HTTP não inicia treinamento nem publica adaptadores.

### Fluxo da análise

```mermaid
sequenceDiagram
    participant React
    participant API as API C#
    participant DB as PostgreSQL
    participant OD as OpenDota
    participant Worker as Fila / Worker
    participant Parser as Parser Go
    React->>API: Solicita análise da partida
    API->>DB: Consulta partida e análise salvas
    DB-->>API: Dados disponíveis
    opt Partida ausente ou desatualizada
        API->>OD: Consulta partida e catálogos
        OD-->>API: Dados da partida
        API->>DB: Salva dados atualizados
    end
    alt Análise pronta
        API-->>React: Análise / ready
    else Replay precisa ser processado
        API->>Worker: Enfileira processamento
        API-->>React: Dados existentes / processing
        Worker->>Parser: POST /v1/match
        Parser-->>Worker: Documento do replay
        Worker->>DB: Salva métricas, heatmap e estado ready
        React->>API: Consulta novamente
        API->>DB: Busca análise concluída
        DB-->>API: Análise salva
        API-->>React: Análise / ready
    end
```

O fluxo mostra o caminho de sucesso. Estados de replay indisponível e falhas continuam sendo tratados pelos contratos da API.

### Base de comparação

```mermaid
flowchart LR
    OD["OpenDota<br/>Partidas e heróis recentes da conta foco"] --> Priority["Prioriza heróis com menos dados<br/>Depois partidas profissionais / alto nível"]
    Priority --> Collect["CollectBaselineHandler<br/>Uma partida por vez / limite configurado"]
    Collect --> Parser["Parser Go<br/>Inferência de função quando necessária"]
    Parser --> Raw["PostgreSQL local<br/>baseline_matches / baseline_metrics"]
    Raw --> Publish["PublishBaselineHandler<br/>Percentis por herói / posição / métrica"]
    Publish --> Local["baseline_distributions<br/>Banco local"]
    Publish --> Neon["baseline_distributions<br/>Neon / dados refinados"]
    Local --> Site["Site local"]
    Neon --> Production["API em produção"]
```

### Login com a Steam

```mermaid
sequenceDiagram
    participant Browser as Navegador
    participant API as API C# / AuthController
    participant Steam as Steam OpenID
    Browser->>API: GET /api/v1/auth/login?returnUrl=...
    API-->>Browser: Redireciona para Steam
    Browser->>Steam: Autentica na página da Steam
    Note over Browser,Steam: Senha ou QR code ficam na Steam
    Steam-->>Browser: Retorna resposta OpenID
    Browser->>API: /api/auth/steam/callback
    API->>Steam: check_authentication
    Steam-->>API: Confirma identidade / SteamID
    API-->>Browser: Cookie HttpOnly / volta à página de origem
    Browser->>API: GET /api/v1/me com cookie
    API-->>Browser: Perfil autenticado
```

### Banco de dados

```mermaid
flowchart TD
    subgraph Shared["Local e Neon / dados refinados"]
        Matches["matches<br/>match_id / details / replay_url"]
        Analyses["replay_analyses<br/>match_id / status / data"]
        Catalogs["catalog_entries<br/>catalog + id / name / details"]
        Distributions["baseline_distributions<br/>hero_id + role + source / distributions"]
        Searches["player_searches<br/>account_id / contagem"]
        Keys["data_protection_keys<br/>Proteção do cookie de sessão"]
    end
    subgraph Raw["Somente local / dados brutos"]
        Documents["replay_documents<br/>match_id / documento arquivado"]
        BaselineMatches["baseline_matches<br/>match_id / source / status"]
        Metrics["baseline_metrics<br/>match_id + hero_id / role / metrics"]
    end
    Metrics -->|"Agrega e publica percentis"| Distributions
```

Os grupos mostram onde os dados ficam armazenados; as caixas não declaram relacionamentos de chave estrangeira. A seta representa a publicação dos agregados, sem enviar os documentos brutos ao Neon.

## Versões e publicação

Versionamento semântico (`vMAJOR.MINOR.PATCH`; o MAJOR acompanha a versão da API, `/api/v1`). O workflow de release é disparado por uma tag de versão e reutiliza o CI antes de publicar a imagem no GHCR. Crie releases a partir do `main`, usado pelo build do Render. O deploy é solicitado somente se o secret `RENDER_DEPLOY_HOOK_URL` estiver configurado; sem ele, essa etapa é pulada. O workflow Coach ML executa as verificações Python separadamente quando seus arquivos mudam.
