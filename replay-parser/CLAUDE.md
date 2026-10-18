# replay-parser (serviço Go)

Serviço HTTP interno que recebe a URL de um replay do Dota 2 (CDN da Valve), lê com o [`manta`](https://github.com/dotabuff/manta) e devolve em JSON a **fase de rotas** (até 10:00, `/v1/laning`) ou **a partida inteira** (`/v1/match`, sprint 10). Só a API em C# o chama; o React nunca fala com ele. **Em produção** ele roda na máquina do dono do projeto (docker compose) e a API no Render o alcança por um túnel **ngrok** (serviço `tunnel` do mesmo compose, perfil `tunnel`; veja "Replay (serviço Go)" no `CLAUDE.md` da raiz); por isso o token é obrigatório no compose. Vale também o `CLAUDE.md` da raiz (convenções, Git, tags).

O dono do projeto está aprendendo Go (e C#): explique as decisões e os conceitos novos do Go em português, de forma didática.

## Regras (as mesmas do projeto C#, no jeito do Go)

- **Código em inglês** (pacotes, tipos, funções, rotas); conversa em português.
- **Dependências só apontam para dentro:** `cmd` compõe `httpapi`, `replay` e `valve`; `httpapi → parsing → laning`. `valve → parsing` usa erros estáveis; `replay → (laning, matchdata)` implementa análise. `httpapi` não importa adaptadores Valve/Manta. `laning` e `matchdata` só usam a biblioteca padrão. `internal/architecture` verifica essas fronteiras.
- **Interfaces no lugar de quem usa** (idioma do Go): o pacote que precisa de algo declara a interface; o `main.go` entrega a implementação; testes entregam falsos.
- **Contrato versionado na URL** (`/v1/laning`, `/v1/match`); mudança que quebra → `/v2/`. Contrato abaixo, testado aqui e no cliente C#.
- **Erros como `application/problem+json`** (mesmo formato da API).
- **Testes** `*_test.go` ao lado do código, em tabela (*table-driven*, como o `[Theory]` do xUnit). Nenhum teste acessa a internet; os de replay real só rodam se o arquivo existir: `REPLAY_TEST_FILE` (padrão `../work/replays/9034320024.dem.bz2`, All Pick) e `REPLAY_TEST_FILE_TURBO` (padrão `../work/replays/9041133074.dem`, Turbo parseada pelo OpenDota).
- **Uma tag de versão para o repositório inteiro** (`vX.Y.Z`, veja o `CLAUDE.md` da raiz).
- `gofmt` (formatação automática ao salvar no VS Code), `go vet` e `staticcheck` sem avisos.

## Estrutura

| Pasta | Papel | Equivale no C# a |
|-------|-------|-------------------|
| `cmd/replay-parser/` | `main.go`: configuração (variáveis de ambiente), monta as peças, encerra com SIGTERM | `Program.cs` |
| `cmd/inspect/` | Ferramenta de desenvolvimento: imprime propriedades das entidades de um replay; `-laning` (resumo aos 10:00), `-full` (mede o parse inteiro), `-match` (JSON do `/v1/match`), `-catalog`/`-measure`/`-download` (spike da sprint 10) | — |
| `internal/laning/` | Regra: `Lane`, `PlayerLaning`, ponto do mapa → rota, agregação das amostras, valores aos 10:00 | Domain |
| `internal/matchdata/` | Contrato do `/v1/match`: só tipos com tags JSON e comentários (unidades); `List[T]` nunca vira `null`; tuplas `[t, x, y]`/`[t, "nome"]` | DTOs |
| `internal/replay/` | Adaptador do `manta`. `analyzer.go`: `/v1/laning`. `clock.go`: relógio do jogo e pausas (dos dois). `match.go`: `MatchAnalyzer` + interface `Collector`. `session.go`/`players.go`: a `Session` (ganchos e serviços comuns). `collect_*.go`: um coletor por seção (`match` = partida + draft; `players`, `combat`, `items`, `vision` = cards 03–06; `laning` = a rota de cada jogador pela mesma regra do `/v1/laning`, sprint 11 — os números aos 10:00 são o minuto 10 de `timelines.perMinute`) | Infrastructure |
| `internal/valve/` | Baixa o `.dem.bz2` (só `*.valve.net`), limites de tamanho/tempo, descompacta em streaming | Infrastructure |
| `internal/httpapi/` | Rotas, autenticação, JSON, gzip/teto de 4 MB e tradução de erros para ProblemDetails; delega coordenação a `parsing` | Controllers |
| `internal/parsing/` | Caso de uso compartilhado: stream, timeout, capacidade, precedência de erros de leitura e consumo do resultado; sem HTTP/Manta/Valve | Application |
| `internal/provenance/` | Manifesto local opcional com hashes, build e limitações; não muda o contrato v1 | Exportação local |
| `internal/architecture/` | Testes das dependências permitidas entre pacotes | Testes |

`internal/` é especial no Go: nada fora deste módulo pode importar o que está lá dentro.

O slot permanece ocupado até o resultado ser consumido, incluindo escrita HTTP; o stream fecha antes dessa escrita. Não criar nova fila ou retry no Go: a coordenação de solicitações permanece no C#. Os logs `parse resources` medem abertura, análise, leitura e duração sem anexar a URL do replay; a fase de abertura não inclui todo download, pois a leitura é streaming durante o parse.

### Manifesto local de procedência

```powershell
go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -match -out ../work/match-new.json -manifest ../work/match-new.manifest.json
```

Com `-manifest`, ambos os destinos devem ser novos. O manifesto é publicado sem sobrescrita e contém hashes dos bytes locais do documento, replay e binário, versão do contrato, informações disponíveis do build/Manta e data UTC. Não inclui caminhos locais ou URL. Patch desconhecido fica `null`; lista vazia não certifica cobertura observada. Os arquivos de entrada devem permanecer imutáveis durante a exportação. Manifesto ausente após falha significa exportação incompleta; preserve os arquivos para investigação e use outro destino. Nada é baixado nem enviado ao treinamento por esse comando.

Verificações direcionadas: `go test ./internal/parsing ./internal/architecture ./internal/provenance ./internal/httpapi`. `go test -race ./internal/parsing ./internal/httpapi` requer CGO e compilador C compatível; o CI Linux executa essa checagem e fuzzing limitado de URL. Fuzzing de URLs não acessa a rede nem alimenta arquivos arbitrários ao Manta.

`inspect -match -metrics <arquivo-novo.json>` mede tempo e picos amostrados de heap/runtime Go; em Linux com cgroup v2, também registra `cgroupPeakBytes`. O arquivo não é sobrescrito. Amostragem tem custo e não deve ser usada para comparar latência de produção; o tempo sem profiling deve ser medido separadamente. Dados de cgroup não equivalem a RSS de um processo e dependem dos limites/mounts do container. A aceitação local incluiu três replays (cerca de 17, 29 e 56 minutos), igualdade com o código anterior e dois parses simultâneos da partida longa com `-race` dentro de 1 GiB; não elevar concorrência ou memória a partir dessa amostra.

## Contrato

`POST /v1/laning` — corpo `{ "replayUrl": "http://replay202.valve.net/570/<match>_<salt>.dem.bz2" }`

200:
```json
{
  "laningEndsAtSeconds": 600,
  "players": [
    { "heroId": 31, "steamId": "76561198059783443", "isRadiant": true, "lane": "bottom",
      "lastHits": 12, "denies": 3, "netWorth": 2900, "xp": 3100, "deaths": 1 }
  ]
}
```
- `lane`: `top` | `middle` | `bottom` | `jungle` | `roaming` (posição do herói entre 1:00 e 10:00). `steamId` como texto (64 bits). Valores aos 10:00 do relógio do jogo (ou no fim, se a partida acabar antes).

`POST /v1/match` — mesmo corpo, token, limites (semáforo **dividido** com o `/v1/laning`, tempo, tamanho do replay) e erros. 200 = a partida inteira; **a fonte da verdade é `internal/matchdata`** (nomes, unidades e o que pode ser `null` estão nos comentários). Resumo: `schemaVersion` (1), `match` (id, modo, vencedor, duração, início/fim Unix, pausas), `draft`, `players`, `timelines` (por minuto em colunas; posições `[t, x, y]` a cada 2 s), `kills`, `buybacks`, `items`, `abilities`, `combat` (dano/cura agregados), `vision`, `objectives`. Sem chat. Tempo = segundos do relógio do jogo (pausas fora, negativo antes da buzina, 1 casa); posição = coordenadas do mapa; jogador = índice 0–9; itens/habilidades pelo nome interno; listas vazias são `[]`, nunca `null`. Resposta com **gzip** se o cliente mandar `Accept-Encoding: gzip`; acima de **4 MB** (sem compressão) é bug → 500 com log de erro.

- Erros (os dois endpoints): 400 URL inválida/fora de `*.valve.net`; 401 sem token/token errado; 404 replay não existe mais na Valve; 422 replay ilegível; 502 falha ao baixar; 503 ocupado (`Retry-After`). `GET /health` → 200 (sem token).
- **Token:** `Authorization: Bearer <REPLAY_PARSER_API_KEY>`, comparado com `crypto/subtle`; nunca no log. Variável vazia → sem autenticação (só `go run` local; o compose exige a variável).

## Coletores do `/v1/match`

- **Um coletor por arquivo** (`internal/replay/collect_<seção>.go`), implementando `Collector`: `Register(*Session)` só se inscreve nos ganchos (nada do replay foi lido ainda); `Fill(*matchdata.Match) error` escreve **só as próprias seções**, depois do replay inteiro. Estado só nos campos do próprio coletor (dois parses podem rodar ao mesmo tempo). A lista fica em `newCollectors()` (`match.go`).
- **Ganchos da `Session`:** `OnSecond(func(second int))` (uma vez por segundo do relógio, de 0:00 até o fim, jogadores já conhecidos), `OnEnd(func(second int))` (uma vez, no tick em que a partida acaba: itens e habilidades ainda existem), `OnCombatLog(func(CombatEntry), tipos...)` (nomes já resolvidos pela `CombatLogNames`, jogador atacante/alvo 0–9 ou −1), `OnEntity(prefixoDaClasse, func(*manta.Entity, manta.EntityOp))`, `OnChatEvent(func(*dota.CDOTAUserMsg_ChatEvent, ServerTime))`, `OnFileInfo(func(*dota.CDemoFileInfo))`. Mensagens raras: direto em `session.Parser.Callbacks` (nunca em `CNETMsg_Tick`/`PacketEntities`, que desligam o caminho rápido do `manta`).
- **Serviços:** `Clock` (`Now()`/`At(segundosDoServidor)` guardam `ServerTime`; `Seconds(ServerTime)` converte **só no `Fill`**, porque `m_flGameStartTime` é 0 até a buzina; `GameEnd()`, `PausedSeconds()`), `Players` (`All()`, `Get(i)`, `ByHeroName`, `BySteamID`, `ByHeroEntity`, `Hero(i)`, `Resource(i)`, `TeamData(i)`; os 10 ficam conhecidos ~1:25 antes da buzina; antes disso `ByHeroName` olha os heróis que já existem), `Name(índice)` (CombatLogNames), `EntityName(entidade)` (EntityNames), `Entity(classe)` (entidades únicas: game rules, PlayerResource, DataRadiant/Dire).
- Erro no `Fill` ou pânico = replay ilegível (422); dado opcional ausente = seção vazia. Pegadinhas do replay: seção "Pegadinhas para os coletores" do `../work/sprint-10-replay-data/card-01-spike.md`.

## O que o `/v1/match` entrega e quão confiável é (conferido em 12/10/2026)

Partida de 28 min: ~2 s, pico ~100 MB, **329 KB (82 KB gzip)** — maiores: `timelines` 135 KB (posições a cada 2 s), `combat` 85 KB, `abilities` 41 KB, `items` 33 KB. Conferência contra o OpenDota (Turbo 9041133074) e contra os contadores do próprio jogo:

- **Iguais:** números finais dos 10 jogadores (K/D/A, nível, LH/DN, patrimônio, dano em heróis/torres, cura); K/D dos kills (com as regras do placar: morte com Aegis não conta; morte para creep/torre com um só inimigo na assistência é kill dele); wards (57/57, horário ≤ 1 s, posição ≤ 64), runas (28/28), torres/barracks (ordem e autor), Roshan; compras (≤ 2 s, inclusive as de antes de −0:59 tiradas das entidades), usos de item, itens finais, quantidade de upgrades; draft de Captains Mode (24/24, partida 9040944381).
- **Quase:** por minuto, ouro +2 a +8 (lemos um tick depois), XP/LH iguais em ≥ 94%; dano do Undying +3% (zumbis da Tombstone); cura do Tidehunter abaixo do contador do jogo (curas que faltam no combat log); assistências do combat log ⊂ placar.
- **Não confiável / ausente:** `draft.announcedBans` não é só a fase de bans (inclui heróis indisponíveis e escolhas abandonadas — use o `picks_bans` do OpenDota); TPs comprados (contador do jogo zerado); posição inicial; pegar Cheese/fragmento do Refresher (sem evento); Tormentor e pausas sem replay de teste.

## Segurança

- **SSRF:** só `http(s)://*.valve.net/570/<arquivo>.dem.bz2`; redirecionamento para outro host é recusado.
- **A URL do ngrok é pública:** token obrigatório no compose, só `*.valve.net`, limite de parses simultâneos (`MAX_CONCURRENT_PARSES`, padrão 1, **2 no compose** porque a API local e a de produção dividem o serviço; acima disso → 503).
- **Memória:** o `manta` confia em tamanhos escritos no arquivo e falta de memória no Go é fatal (o `recover` não segura): o compose limita o container (`mem_limit: 1g`, `pids_limit`), então um replay malicioso derruba só ele, que reinicia.
- Limites: download (ex.: 200 MB), descompactado (ex.: 1 GB), tempo total (ex.: 3 min). `context.Context` em toda operação demorada (a API desistiu → download e parse param).
- Imagem final mínima, usuário não-root, sem porta publicada no compose (a API e o ngrok o alcançam pela rede do compose). Health check pelo próprio binário (`replay-parser -healthcheck`), já que a imagem não tem shell.

## Como rodar

- Go 1.27 instalado (`C:\Program Files\Go\bin`). O `go.mod` exige **1.27.2** (correções de segurança da biblioteca padrão, achadas pelo `govulncheck` em 12/10/2026): o comando `go` baixa esse toolchain sozinho. Versão do Go = `go` do `go.mod` + imagem `golang:` do `Dockerfile`, sempre juntas; o CI roda `govulncheck` e falha quando sai um patch de segurança. ferramentas em `%USERPROFILE%\go\bin` (`gopls`, `dlv`, `staticcheck`). Editor: VS Code + extensão Go (o Visual Studio não abre Go; este projeto não entra no `.slnx`).
- `go run ./cmd/replay-parser` (porta 8081, variável `PORT`); `go test ./...`.
- Inspecionar um replay: `go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -tick 18000 -keys "<regexp>"` (30 ticks por segundo); `-laning` imprime o resumo aos 10:00 e `-full` mede o parse inteiro; `-match -out ../work/9034320024.json` grava o JSON do `/v1/match` (tempo e tamanho, com e sem gzip, no stderr). Nomes das propriedades e medidas: `../work/sprint-09-replay-parser/card-01-spike.md`.
- **Replays `.dem.bz2` da Valve vêm em zstd** (`28 B5 2F FD`): o descompactador é escolhido pelos primeiros bytes (zstd, bzip2 ou `.dem` puro), nunca pela extensão.
- Replays de teste ficam em `../work/replays/` (fora do Git).
