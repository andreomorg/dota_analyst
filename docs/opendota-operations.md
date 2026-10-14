# OpenDota — configuração e verificação

## Habilitar

A solicitação automática foi implementada atrás de uma flag inicialmente desligada. Configurar no servidor:

```text
OpenDota__Parsing__Enabled=true
```

Exige `ConnectionStrings:Database` já configurada e migrations aplicadas. A API continua funcional sem o banco, mas não envia solicitações sem coordenação persistente. Credenciais permanecem no servidor.

Ao abrir uma partida finalizada não processada, a API registra uma tarefa e retorna os dados disponíveis. O worker envia ao OpenDota, acompanha o job, busca a partida atualizada e grava o resultado. `openDotaProcessing` é independente de `replayStatus`. Partidas processadas com versão antiga dos dados extras recebem atualização por GET, sem POST de parse.

## Opções

| Opção em OpenDota:Parsing | Padrão | Significado |
|---|---|---|
| Enabled | false | Habilita novos registros e trabalho externo |
| PollSeconds | 15 | Intervalo inicial de acompanhamento do job |
| MaxPollSeconds | 60 | Intervalo máximo na janela ativa |
| ActiveMinutes | 10 | Janela ativa desde o registro da solicitação |
| ResumeMinutes | 15 | Intervalo para retomadas e tentativas adiadas |
| MaxSubmitAttempts | 3 | Limite de envios por partida na janela |
| RetryHours | 24 | Janela para renovar o limite de tentativas |

No navegador, nosso replay mantém limite aproximado de 5 minutos de acompanhamento; OpenDota usa até 10 minutos. Sair da página cancela as consultas do navegador, mas a tarefa durável do servidor continua.

## Dados complementares

`openDotaExtras` expõe dano recebido por habilidade/item, usos, APM, contagens de ações e origens de ouro/XP. Campos ausentes são null, zero real permanece zero. O desktop mostra dano em Lutas e usos/APM/economia em Build, inclusive quando nosso parser está indisponível. Mobile mantém seu escopo anterior.

Os agregados não são somados aos do parser Go; seus recortes podem diferir. Não são séries por minuto nem entram nas baselines. Identificadores sem catálogo permanecem identificadores: nomes e tipos de dano não são inventados. As origens de ouro/XP ainda usam os códigos do serviço. Registros persistidos antigos são legíveis e recebem enriquecimento controlado por versão.

## Diagnóstico e recuperação

- `queued`: tarefa registrada; `processing`: solicitação externa em andamento; `refreshing`: confirmação/atualização dos dados.
- `deferred`: serviço recusou/adiou; respeitar o limiter compartilhado e intervalo de retomada.
- `ready`: dados de partida processada confirmados e salvos. Job removido da fila não é prova suficiente de sucesso.
- `failed`/`unavailable`: dados existentes continuam visíveis. Não apagar o registro para provocar envios repetidos.
- POST de resultado desconhecido ou reinício durante envio: intenção persistida permite reconciliação antes de reenviar. Não há garantia de exactly-once na fronteira com o serviço externo.
- Desabilitar a flag e reiniciar interrompe novos envios e consultas do worker; registros ficam disponíveis para retomada. Não remover dados nem reverter migrations automaticamente.

## Evidências locais

Quadro e reviews: `work/sprint-21-opendota-processing/board.md`. Teste real da entrega A: partida 9044145299, job 584510181, um POST e confirmação version 22 com resultado salvo no banco de teste isolado. Isso comprova o fluxo local com OpenDota real; não equivale a deploy da feature em produção.

Validação da entrega B usa campos reais dessa partida vinculados a um shell determinístico de teste: não confundir o placar do shell com os números do replay real. Fixtures permanecem fora do produto. Suplemento visual: [Stitch](https://stitch.google.com/projects/13241258925802792561), tela `10491768443470618148`.
