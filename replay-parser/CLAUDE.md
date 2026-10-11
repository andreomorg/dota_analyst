# replay-parser (serviço Go)

Serviço HTTP interno que recebe a URL de um replay do Dota 2 (CDN da Valve), lê com o [`manta`](https://github.com/dotabuff/manta) e devolve a **fase de rotas** (até 10:00) em JSON. Só a API em C# o chama; o React nunca fala com ele. Vale também o `CLAUDE.md` da raiz (convenções, Git, tags).

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
| `cmd/inspect/` | Ferramenta de desenvolvimento: imprime propriedades das entidades de um replay | — |
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
- Erros: 400 URL inválida/fora de `*.valve.net`; 404 replay não existe mais na Valve; 422 replay ilegível; 502 falha ao baixar. `GET /health` → 200.

## Segurança

- **SSRF:** só `http(s)://*.valve.net/570/<arquivo>.dem.bz2`; redirecionamento para outro host é recusado.
- Limites: download (ex.: 200 MB), descompactado (ex.: 1 GB), tempo total (ex.: 3 min). `context.Context` em toda operação demorada (a API desistiu → download e parse param).
- Imagem final mínima, usuário não-root, sem porta publicada no compose.

## Como rodar

- Go 1.27 instalado (`C:\Program Files\Go\bin`); ferramentas em `%USERPROFILE%\go\bin` (`gopls`, `dlv`, `staticcheck`). Editor: VS Code + extensão Go (o Visual Studio não abre Go; este projeto não entra no `.slnx`).
- `go run ./cmd/replay-parser` (porta 8081, variável `PORT`); `go test ./...`.
- Inspecionar um replay: `go run ./cmd/inspect -replay ../work/replays/9034320024.dem.bz2 -tick 18000 -keys "<regexp>"` (30 ticks por segundo).
- Replays de teste ficam em `../work/replays/` (fora do Git).
