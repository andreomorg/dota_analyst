# replay-parser (serviço Go)

Serviço HTTP interno que recebe a URL de um replay do Dota 2 (CDN da Valve), lê com o [`manta`](https://github.com/dotabuff/manta) e devolve a **fase de rotas** (até 10:00) em JSON. Só a API em C# o chama; o React nunca fala com ele. **Em produção** ele roda na máquina do dono do projeto (docker compose) e a API no Render o alcança por um túnel **ngrok** (serviço `tunnel` do mesmo compose, perfil `tunnel`; veja "Replay (serviço Go)" no `CLAUDE.md` da raiz); por isso o token é obrigatório no compose. Vale também o `CLAUDE.md` da raiz (convenções, Git, tags).

O dono do projeto está aprendendo Go (e C#): explique as decisões e os conceitos novos do Go em português, de forma didática.

## Regras (as mesmas do projeto C#, no jeito do Go)

- **Código em inglês** (pacotes, tipos, funções, rotas); conversa em português.
- **Dependências só apontam para dentro:** `cmd → httpapi → (replay, valve) → laning`. O `laning` (a regra) só usa a biblioteca padrão; não conhece HTTP, `manta` nem a Valve.
- **Interfaces no lugar de quem usa** (idioma do Go): o pacote que precisa de algo declara a interface; o `main.go` entrega a implementação; testes entregam falsos.
- **Contrato versionado na URL** (`/v1/laning`); mudança que quebra → `/v2/`. Contrato abaixo, testado aqui e no cliente C#.
- **Erros como `application/problem+json`** (mesmo formato da API).
- **Testes** `*_test.go` ao lado do código, em tabela (*table-driven*, como o `[Theory]` do xUnit). Nenhum teste acessa a internet; o do replay real só roda se o arquivo existir em `../work/replays/`.
- **Uma tag de versão para o repositório inteiro** (`vX.Y.Z`, veja o `CLAUDE.md` da raiz).
- `gofmt` (formatação automática ao salvar no VS Code), `go vet` e `staticcheck` sem avisos.

## Estrutura

| Pasta | Papel | Equivale no C# a |
|-------|-------|-------------------|
| `cmd/replay-parser/` | `main.go`: configuração (variáveis de ambiente), monta as peças, encerra com SIGTERM | `Program.cs` |
| `cmd/inspect/` | Ferramenta de desenvolvimento: imprime propriedades das entidades de um replay; `-laning` (resumo aos 10:00) e `-full` (mede o parse inteiro) | — |
| `internal/laning/` | Regra: `Lane`, `PlayerLaning`, ponto do mapa → rota, agregação das amostras, valores aos 10:00 | Domain |
| `internal/replay/` | Adaptador do `manta`: lê o replay e entrega amostras ao `laning` | Infrastructure |
| `internal/valve/` | Baixa o `.dem.bz2` (só `*.valve.net`), limites de tamanho/tempo, descompacta em streaming | Infrastructure |
| `internal/httpapi/` | `POST /v1/laning`, `GET /health`, JSON, erros → ProblemDetails | Controllers |

`internal/` é especial no Go: nada fora deste módulo pode importar o que está lá dentro.

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
- Erros: 400 URL inválida/fora de `*.valve.net`; 401 sem token/token errado; 404 replay não existe mais na Valve; 422 replay ilegível; 502 falha ao baixar; 503 ocupado (`Retry-After`). `GET /health` → 200 (sem token).
- **Token:** `Authorization: Bearer <REPLAY_PARSER_API_KEY>`, comparado com `crypto/subtle`; nunca no log. Variável vazia → sem autenticação (só `go run` local; o compose exige a variável).

## Segurança

- **SSRF:** só `http(s)://*.valve.net/570/<arquivo>.dem.bz2`; redirecionamento para outro host é recusado.
- **A URL do ngrok é pública:** token obrigatório no compose, só `*.valve.net`, limite de parses simultâneos (`MAX_CONCURRENT_PARSES`, padrão 1, **2 no compose** porque a API local e a de produção dividem o serviço; acima disso → 503).
- **Memória:** o `manta` confia em tamanhos escritos no arquivo e falta de memória no Go é fatal (o `recover` não segura): o compose limita o container (`mem_limit: 1g`, `pids_limit`), então um replay malicioso derruba só ele, que reinicia.
- Limites: download (ex.: 200 MB), descompactado (ex.: 1 GB), tempo total (ex.: 3 min). `context.Context` em toda operação demorada (a API desistiu → download e parse param).
- Imagem final mínima, usuário não-root, sem porta publicada no compose (a API e o ngrok o alcançam pela rede do compose). Health check pelo próprio binário (`replay-parser -healthcheck`), já que a imagem não tem shell.

## Como rodar

- Go 1.27 instalado (`C:\Program Files\Go\bin`). O `go.mod` exige **1.27.2** (correções de segurança da biblioteca padrão, achadas pelo `govulncheck` em 12/10/2026): o comando `go` baixa esse toolchain sozinho. Versão do Go = `go` do `go.mod` + imagem `golang:` do `Dockerfile`, sempre juntas; o CI roda `govulncheck` e falha quando sai um patch de segurança. ferramentas em `%USERPROFILE%\go\bin` (`gopls`, `dlv`, `staticcheck`). Editor: VS Code + extensão Go (o Visual Studio não abre Go; este projeto não entra no `.slnx`).
- `go run ./cmd/replay-parser` (porta 8081, variável `PORT`); `go test ./...`.
- Inspecionar um replay: `go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -tick 18000 -keys "<regexp>"` (30 ticks por segundo); `-laning` imprime o resumo aos 10:00 e `-full` mede o parse inteiro. Nomes das propriedades e medidas: `../work/sprint-09-replay-parser/card-01-spike.md`.
- **Replays `.dem.bz2` da Valve vêm em zstd** (`28 B5 2F FD`): o descompactador é escolhido pelos primeiros bytes (zstd, bzip2 ou `.dem` puro), nunca pela extensão.
- Replays de teste ficam em `../work/replays/` (fora do Git).
