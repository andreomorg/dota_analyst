# Plano técnico — processamento e enriquecimento OpenDota

Status: implementação local concluída em 14/10/2026, com revisão após cada tarefa e suítes completas somente na etapa final. Base: código da release `v1.25.0` e [diagnóstico anterior](opendota-processing.md). Prioridade visual: desktop. [Configuração para habilitar](opendota-operations.md); deploy de produção ainda pendente.

Cards e quadro de execução local: [sprint 21](../work/sprint-21-opendota-processing/board.md). São 13 tarefas de implementação/validação focada e uma tarefa final de validação completa (F01).

Regra do usuário: executar um agente separado de code review após **cada tarefa**, corrigir os achados relevantes e obter revisão das correções antes de avançar. Rodar testes focados durante a execução; **suítes completas somente em F01, ao final de A e B**. A08 e B05 não antecipam testes completos. Registrar revisão e evidências por card; não marcar como concluído antes do aceite.

## Entregas e ordem

1. **Entrega A:** abrir uma partida não processada solicita automaticamente o parse no OpenDota; acompanhar e atualizar os dados sem recarregar a página.
2. **Entrega B:** aproveitar dados adicionais, começando por dano recebido por habilidade/item. Usos de habilidades/itens, ações/APM e origens de ouro/XP vêm em tarefas separadas.

O parser Go continua sendo a fonte das análises próprias, mapas, geometria, estatísticas por minuto e baselines. A nova integração complementa essas informações; não substitui o parser nem altera a autenticação Steam. Não criar estatísticas, séries temporais ou classificação de dano a partir de campos ausentes.

## Decisões de arquitetura

| Camada | Responsabilidade proposta |
|---|---|
| Application | Contratos `IOpenDotaParseCoordinator`, `IOpenDotaParseClient` e `IOpenDotaParseRequestRepository`; coordenação do caso de uso e DTOs públicos. |
| Infrastructure/OpenDota | POST de solicitação, GET de acompanhamento, tradução de erros, limite ponderado de chamadas e worker em segundo plano. |
| Infrastructure/Persistence | Registro durável da solicitação, exclusão mútua entre instâncias e atualização da partida. |
| Domain | Novos valores de análise somente quando existir uma regra de domínio; nenhum estado HTTP ou job externo. |
| Server | Manter rota atual de análise em `Controllers/V1/PlayersController.cs`, documentar campos adicionais no OpenAPI; sem lógica de parse no controller. |
| React | Apresentar estados e atualizar a análise; nunca chamar OpenDota diretamente. |

Nomes são propostas para implementação, não classes já existentes. Não é necessário criar uma rota pública de solicitação nesta primeira entrega: a abertura da análise já é o gatilho. Preservar o rate limit da nossa API e controlar o custo externo no backend.

### Estados e contrato público

Adicionar `openDotaProcessing` opcional ao DTO `MatchAnalysis`, sem mudar `isParsed` ou `replayStatus`:

```json
{
  "isParsed": false,
  "replayStatus": "ready",
  "openDotaProcessing": {
    "status": "queued",
    "reason": null,
    "retryAfterSeconds": 15,
    "requestedAt": "2026-10-14T16:00:00Z"
  }
}
```

Exemplo de formato, sem representar uma solicitação real. Estados públicos propostos: `queued` (fila local), `processing` (solicitação externa aceita/acompanhamento), `refreshing` (buscando resultado), `ready` (partida confirmada como processada), `deferred` (tentativa adiada), `failed` e `unavailable`. `reason` deve ser um código estável e sanitizado; não devolver stack traces, chaves ou mensagens externas cruas. Sem percentual ou previsão de conclusão inventados. Frontend anterior deve continuar funcionando sem ler o campo novo.

Separar estado interno de envio (`submitting`) dos estados públicos. Conclusão do job só leva a `ready` quando um GET atualizado confirmar `version` presente. Uma partida já processada retorna `ready` sem criar job.

### Persistência e concorrência

Criar tabela `opendota_parse_requests` com chave única `match_id`, `job_id` anulável, `state`, contadores separados de envio/consulta, `requested_at`, `updated_at`, `next_attempt_at`, `last_error_code`, `lease_until` e token de posse. Tempos UTC pelo `TimeProvider`. Criar índice para registros elegíveis por estado/data.

O cadastro e a aquisição da tarefa devem ser atômicos no PostgreSQL. Uma instância adquire uma concessão temporária; a conclusão usa o token para impedir que um worker antigo sobrescreva trabalho mais recente. Não manter transação aberta durante chamadas HTTP. Recuperar registros interrompidos após reinício; não depender apenas de `Channel` ou lock em memória.

**Limite importante:** banco e OpenDota não participam de uma mesma transação. Se o POST for aceito e o processo cair antes de guardar `job_id`, não podemos garantir envio externo exatamente uma vez. Marcar envio de resultado desconhecido, esperar um intervalo, consultar a partida e só então avaliar novo POST. Não repetir POST automaticamente no middleware HTTP. Registrar esse cenário em teste e documentação.

Sem banco disponível: entregar análise básica e indicar processamento indisponível; não realizar envios sem coordenação durável. Não alterar o fallback de leitura de partidas existente.

### Cache e atualização

- O gatilho roda depois de carregar uma partida válida e finalizada, inclusive quando ela vem do cache/banco com `IsParsed = false`. Solicitar apenas na análise visitada, não em toda a lista de partidas nem no coletor de baselines.
- Criar operação explícita de atualização no contrato de leitura OpenDota. O worker deve conseguir ignorar a cópia não processada de memória, sem desabilitar o cache de todas as consultas.
- Na conclusão, buscar dados, persistir a cópia processada e atualizar/invalidar o cache correspondente. Se a gravação falhar, manter a tarefa em atualização pendente e reutilizar o job; não enviar novo POST.
- Durante o acompanhamento, consultar estado durável/cache de estado. Não fazer um GET externo de partida a cada polling do navegador.
- Os 15 minutos do banco e 5 minutos da memória continuam úteis para consultas normais; o fluxo de conclusão precisa contorná-los especificamente.

### Limites e tentativas

Reutilizar `OpenDotaRateLimiter` e `OpenDotaRateLimitHandler`. Hoje o handler reserva uma chamada por request; estender a reserva para custo **10 no POST de parse**, mantendo custo 1 nos GETs. A [API oficial](https://docs.opendota.com/#tag/request) documenta esse peso para rate limit. Respeitar `Retry-After`, pausa compartilhada e evitar consumir o orçamento de buscas normais com jobs.

Valores iniciais propostos, configuráveis em `OpenDota:Parsing`: `Enabled`, intervalo de consulta 15 s crescendo até 60 s, janela de acompanhamento ativo 10 min, espera de 15 min para retomar uma tarefa lenta, no máximo 3 tentativas de envio em 24 h por partida e uma chamada de worker por vez. Não encerrar como falha definitiva apenas porque a fila externa demorou. Consultas sucessivas ao mesmo job não contam como novos envios.

Classificar timeout de POST como resultado desconhecido; timeout/5xx de GET como transitório; 429 como adiado; erro definitivo apenas conforme o contrato externo verificado. Job não encontrado não prova replay expirado: consultar a partida e aplicar a política de recuperação. A regra atual de expiração do nosso parser não deve ser transferida automaticamente para respostas do OpenDota.

## Tarefas executáveis

| ID | Trabalho e arquivos principais | Depende de | Aceite |
|---|---|---|---|
| A01 | Confirmar JSON e semântica de POST/GET `/request`, erros e `jobId`; criar fixtures mínimas em `DotaAnalyst.Tests/Infrastructure`. Consultar contrato/código oficial; teste externo opcional com uma partida real, sem POST repetido. | — | Campos e estados confirmados, respostas ausentes/erro cobertas, sem suposições sobre um campo `status` externo. |
| A02 | Definir abstrações, opções e DTO `openDotaProcessing` em Application; atualizar OpenAPI. | A01 | Contrato aditivo, estados independentes e exemplos de ready/deferred/error. |
| A03 | Criar record/configuração/repositório e migration EF; implementar aquisição e retomada com concessão temporária. | A02 | Duas instâncias não enviam normalmente a mesma tarefa; reinício e resultado desconhecido têm recuperação. |
| A04 | Implementar métodos de solicitação/consulta no cliente e custo ponderado no limiter/handler; registrar em `DependencyInjection.cs`. | A01–A02 | Peso 10, GET 1, 429/timeout/JSON inválido traduzidos; nenhum retry cego de POST. |
| A05 | Implementar coordinator e worker; integrar `GetMatchAnalysisHandler` após carregar partida; não bloquear a resposta em chamada externa. | A03–A04 | Abre partida não processada e registra tarefa imediatamente, inclusive com banco recente; não solicita inexistentes/processadas/não finalizadas; falha não apaga análise. |
| A06 | Atualização explícita no `IReplayAnalyzer`/`OpenDotaReplayAnalyzer`, persistência e invalidação de cache após conclusão. | A05 | Dados novos vencem caches antigos; erro ao salvar retoma atualização sem novo parse. |
| A07 | Criar referência de estados no Stitch e adaptar `useMatchAnalysis.js`, aviso em `MatchAnalysisPage.jsx` e CSS. | A02, A05–A06 | Polling continua por qualquer processo ativo, termina/cancela corretamente, preserva dados, estados com foco/acessibilidade e fidelidade desktop. |
| A08 | Validar ponta a ponta com fixtures e uma partida real; registrar evidências e instruções operacionais em `work/`. | A01–A07 | Todos os cenários abaixo passam; separar simulação de prova real e registrar limites de produção. |
| B01 | Inventário campo a campo: schema OpenDota × parser bruto Go × Domain × DTO × UI. | A01 | Identifica informações já coletadas, disponíveis externamente e realmente ausentes; evita duplicações. |
| B02 | Integrar `damage_inflictor_received`, `ability_uses` e `item_uses` em DTOs externos nullable, mapeamentos internos e persistência versionada. | A06, B01 | Não perde dados antigos; ausência é null, zero real permanece zero; chaves externas resolvidas por catálogos. |
| B03 | Adicionar origem e escopo aos dados adicionais; referência Stitch e integração em Lutas/Build. | B02 | Sem somar fontes sobrepostas ou alinhar linhas por índice; reconciliação por chave validada; dano recebido não inventa tipo físico/mágico. |
| B04 | Integrar ações/APM e origens de ouro/XP após validar definição e unidades; ampliar catálogo só quando houver semântica válida. | B01 | Totais externos não viram série por minuto; novas métricas não entram em baselines incompatíveis. |
| B05 | Validação e documentação da entrega B. | B03–B04 | Sem regressões nas abas e sem duplicação de estatística; dados extras ausentes mantêm a UI completa. |

A01–A08 formam a primeira entrega. B01–B05 são uma segunda entrega; não atrasam o processamento automático. Chat, pings, desconexões e outros campos ficam fora desta implementação inicial.

## Regras do enriquecimento

O [modelo oficial do OpenDota](https://github.com/odota/core/blob/master/sql/create_tables.sql) contém os campos extras, mas não garante presença em toda resposta. A01/B01 precisam conferir valores reais e documentação antes do mapeamento.

- Usar nomes de habilidades/itens e identificadores do catálogo, sem confundir habilidade evoluída com habilidade utilizada.
- Agregados do OpenDota podem usar recortes diferentes dos nossos eventos de dano. Mostrar a origem e validar comparabilidade antes de combinar valores numa mesma linha.
- Não substituir a análise própria por dados incompletos. Não recalcular nosso mapa com posições externas.
- Partidas já processadas e persistidas são definitivas hoje: **adicionar campos no deserializador não atualiza registros antigos automaticamente**. Versionar o enriquecimento, refrescar dados externos de forma controlada quando faltam campos novos e guardar marcador da versão. Não enfileirar novo parse para uma partida já processada só para enriquecer.
- Não guardar o JSON externo inteiro por conveniência; persistir campos selecionados no formato interno, atualizar `StoredMatchMappings` e manter compatibilidade com registros antigos.

## Verificação objetiva

Testes .NET focados nos componentes alterados; PostgreSQL/Testcontainers para aquisição concorrente e migrations. Usar relógio falso e HttpMessageHandler fake, sem sleeps reais ou dependência da fila pública. Durante os cards, testes Go focados somente se houver mudança no parser. Concentrar as suítes completas .NET, frontend e Go, além de lint/build do cliente, em F01 ao final de toda a implementação. Repetir checks apenas quando falhas ou novas correções justificarem.

Casos essenciais: cache recente não impede envio; processada/inexistente não envia; concorrência por match e não por jogador; reinício retoma job; POST de resultado desconhecido não duplica imediatamente; peso 10 e 429; conclusão com `version` ainda ausente; gravação falha; atualização apesar do cache; banco indisponível; parser Go pronto com OpenDota pendente e vice-versa; campos extras ausentes e zero real; registro antigo sem enriquecimento.

React: fixtures para estados independentes, polling limitado, falha transitória preservando análise e cancelamento ao navegar. Capturas no viewport da referência desktop, auditoria de acessibilidade e uma inspeção visual por estado novo. Preservar mobile existente, sem expandir seu escopo.

## Publicação e operação

- Feature flag inicialmente desligada; migration compatível e habilitação gradual depois dos testes. Credenciais apenas no servidor, sem documentos locais ou headers MCP no commit.
- Logs estruturados por `matchId`, transição, duração e código sanitizado; medir solicitações, deduplicações, erros, 429 e tarefas antigas. Não adicionar status/latência fictícios ao produto.
- Desabilitar a feature interrompe novos envios e consultas; preservar registros para retomada. Reversão de código não exige apagar dados nem reverter migration automaticamente.
- Commit/push/release quando solicitados. Após habilitar, verificar uma partida real não processada até aparecerem os dados, e uma já processada sem novo envio. Reportar separadamente CI, deploy e sucesso funcional.

## Critério de conclusão

Entrega A concluída somente quando uma análise visitada solicita processamento automaticamente, acompanha sem duplicação normal, atualiza os dados apesar dos caches e preserva os dois estados independentes. Entrega B concluída somente quando os novos campos chegam de respostas reais, respeitam escopo/origem e aparecem nas referências aprovadas. Uma release com CI verde, sozinha, não comprova esses comportamentos.
