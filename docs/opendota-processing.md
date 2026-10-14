# Processamento automático de partidas no OpenDota

Diagnóstico original em 14/10/2026, após a release `v1.25.0`. A implementação descrita no plano foi concluída localmente nesta mesma data, com reviews e validação final. Para habilitar o processamento automático, configurar `OpenDota__Parsing__Enabled=true` após aplicar migrations. Veja [operação e configuração](opendota-operations.md).

Plano para execução: [implementação técnica, contratos e tarefas](opendota-implementation-plan.md).

## Fluxo anterior — release v1.25.0

**Na release v1.25.0, a aplicação não pedia ao OpenDota para processar uma partida.** Consultava os dados disponíveis e, quando `isParsed` era falso, mostrava o aviso de que os percentis de desempenho poderiam faltar. O processamento automático existente pertencia ao **nosso parser Go**, não ao OpenDota. O detalhamento abaixo preserva o diagnóstico que motivou a mudança.

São dois estados independentes:

| Campo da análise | O que significa |
|---|---|
| `isParsed` | O OpenDota já processou a partida. É determinado pela presença de `version` na resposta externa. |
| `replayStatus` | Estado do processamento pelo nosso parser: `processing`, `ready`, `failed`, `expired` ou `unavailable`. |

Portanto, uma partida pode ter nossos mapas e estatísticas por minuto prontos (`replayStatus = ready`), mas continuar sem os percentis do OpenDota (`isParsed = false`).

### Fluxo atual ao abrir a análise

1. O React busca a análise na nossa API.
2. `GetMatchAnalysisHandler.LoadMatchAsync` tenta carregar a partida do banco. Uma cópia processada pelo OpenDota é considerada definitiva; uma cópia não processada é reutilizada durante **15 minutos** desde sua gravação.
3. Quando precisa consultar o OpenDota, `OpenDotaReplayAnalyzer` usa `GET /matches/{match_id}`. Seu cache em memória dura **5 minutos** para partidas não processadas, **1 hora** para processadas e **1 minuto** para uma resposta de partida inexistente.
4. Separadamente, `GetReplayAsync` consulta o resultado do nosso parser. Se não existe resultado válido e há `ReplayUrl`, tenta colocar o replay na nossa fila. Resultados antigos podem ser reprocessados; falhas recentes aguardam **1 hora** antes de nova tentativa. A fila precisa estar habilitada, ter espaço e acesso ao banco para guardar o resultado.
5. O React consulta novamente a análise a cada **10 segundos**, por até aproximadamente **5 minutos**, somente quando `replayStatus = processing`. **`isParsed = false` sozinho não inicia consultas periódicas.**
6. Na aba Resumo, `isParsed = false` apenas mostra um aviso. Nenhuma solicitação ao OpenDota é enviada.

Os tempos dos caches não devem ser somados como uma espera fixa: são camadas distintas. Entretanto, atualizar a página pode devolver a mesma cópia não processada mesmo depois de o OpenDota terminar.

## Melhoria desejada

**Ao abrir uma partida existente, finalizada e ainda não processada pelo OpenDota, solicitar automaticamente seu processamento, sem exigir um clique adicional.** “Na hora” significa enviar a solicitação ao abrir a análise; o processamento continuará dependendo da fila e da disponibilidade do OpenDota.

A API oficial oferece:

- `POST /request/{match_id}` para solicitar processamento;
- `GET /request/{jobId}` para consultar a solicitação;
- `GET /matches/{match_id}` para obter os dados atualizados da partida.

Cada solicitação de processamento conta como **10 chamadas para o limite de requisições**, segundo a [documentação oficial](https://docs.opendota.com/#tag/request). Não devemos enviar uma nova solicitação a cada atualização da página ou a cada consulta periódica.

## Plano de implementação

1. **Criar uma integração própria com a fila do OpenDota.** Definir uma abstração na Application e implementar envio e consulta em Infrastructure/OpenDota. Guardar o identificador retornado e o estado da solicitação; validar o formato real da resposta antes de definir o DTO.
2. **Disparar automaticamente pelo backend.** Integrar ao caso de uso de análise quando a partida existe, terminou e `IsParsed` é falso. Usar coordenação em segundo plano para não bloquear a entrega dos dados básicos durante o processamento. A solicitação deve acontecer também quando os dados vierem do banco ou do cache.
3. **Evitar duplicatas.** Coordenar por `matchId`, independentemente do jogador selecionado. Reutilizar uma solicitação em andamento, aplicar intervalo entre tentativas e tratar concorrência. Preferir estado persistido para sobreviver a reinícios e múltiplas instâncias; definir retenção e comportamento sem banco.
4. **Acompanhar e atualizar os dados.** Consultar o job com intervalos progressivos e limites. Quando ele concluir, buscar a partida novamente, ignorando ou invalidando especificamente os caches da cópia não processada. Confirmar `IsParsed` na resposta e atualizar o banco; conclusão do job, sozinha, não deve ser apresentada como dados já disponíveis.
5. **Expor um estado separado na nossa API.** Propor um campo como `openDotaProcessing`, sem alterar o significado de `replayStatus` nem de `isParsed`. Diferenciar solicitação em andamento, dados disponíveis, falha e impossibilidade de processar conforme as respostas reais do serviço.
6. **Atualizar o React.** Acompanhar a análise enquanto nosso parser ou a solicitação ao OpenDota estiver em andamento, com tempo máximo e cancelamento ao sair da página. Exibir o estado real sem prometer um prazo. Criar primeiro a referência correspondente no Stitch para qualquer novo controle ou apresentação de estado, conforme o contrato visual do projeto.
7. **Tratar falhas sem perder a análise básica.** Timeout, limite de chamadas, indisponibilidade e replay inacessível não devem impedir a exibição dos dados existentes. Não classificar um replay como expirado apenas por estar indisponível logo após o fim da partida. Preservar o parser Go e suas regras atuais.

## Como saber que ficou pronto

- Abrir uma partida não processada provoca uma solicitação automática, inclusive com cópia recente no banco.
- Duas pessoas abrindo a mesma partida, ou consultas periódicas, não criam solicitações duplicadas.
- Partida já processada não provoca solicitação; partida inexistente também não.
- Depois da conclusão, os dados novos aparecem sem aguardar o cache antigo expirar e sem recarregar manualmente a página.
- Erros do OpenDota preservam os dados básicos e têm tentativas limitadas.
- Nosso parser continua funcionando e seu estado permanece independente.
- Testes focados cobrem disparo, concorrência, atualização do cache e falhas; a apresentação dos estados é verificada visualmente no desktop.

## Arquivos que sustentam a análise

- [Caso de uso e leitura do banco](../DotaAnalyst.Application/Matches/GetMatchAnalysisHandler.cs): `LoadMatchAsync`, `GetReplayAsync` e intervalos de atualização/tentativa.
- [Cliente OpenDota](../DotaAnalyst.Infrastructure/OpenDota/OpenDotaClient.cs): apenas consultas GET; não possui solicitação de parse.
- [Cache de partidas OpenDota](../DotaAnalyst.Infrastructure/OpenDota/OpenDotaReplayAnalyzer.cs).
- [Mapeamento de `IsParsed`](../DotaAnalyst.Infrastructure/OpenDota/OpenDotaMappings.cs).
- [Configuração do parser próprio](../DotaAnalyst.Infrastructure/DependencyInjection.cs): `AddReplayParsing` exige endereço válido e banco configurado.
- [Fila do parser próprio](../DotaAnalyst.Infrastructure/ReplayParser/ReplayParseQueue.cs).
- [Consultas periódicas no React](../dotaanalyst.client/src/analysis/useMatchAnalysis.js).
- [Aviso de partida não processada](../dotaanalyst.client/src/analysis/MatchAnalysisPage.jsx).
- [Regra de disponibilidade do replay](../DotaAnalyst.Domain/Replays/ReplayAvailability.cs).

O diagnóstico acima descreve o comportamento anterior. **A solicitação automática e o enriquecimento agora estão implementados localmente**, atrás de flag inicialmente desligada. A prova real processou a partida 9044145299 com um POST e confirmou dados salvos. Publicação em produção ainda pendente; evidências e reviews estão em `work/sprint-21-opendota-processing/`.
