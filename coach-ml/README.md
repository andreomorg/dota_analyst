# DotaAnalyst coach-ml

Projeto Python para datasets, curadoria, auditoria, treino LoRA, avaliação e serviço interno de revisão. Python **3.11+**. C# mantém regras/cálculos/evidências e a API; Go mantém o parser de replay. O navegador continua falando somente com a API C#.

Os comandos táticos `tactical-infer`, `tactical-refine`, `tactical-review` e `tactical-curate` aceitam `--actions <diretório>` para consumir, explicitamente, um recibo de `CoachingActionEvidenceBuilder`. A opção exige índice de desenvolvimento coberto pelo recibo, hashes do manifesto/índice/dados e vínculo por caso, partida, herói e âncora. Preserva tempos fracionários e fonte nula versus vazia; rejeita campos extras, atores incorretos e divergências entre roster e posições. Controles e validação diagnóstica exigiriam exportações próprias e são recusados nesse modo.

O novo fato `recorded-actions` contém somente identidade técnica/time e usos registrados nos 30s anteriores. Não representa inventário atual, cooldown, mana, alvo ou sucesso. Funções inferidas retrospectivamente são ocultadas (`role.position=null`); as entradas anteriores permanecem iguais quando a opção é omitida. `tactical-refine --actions` permite apenas perguntas condicionais sem preferência e não combina com `--bounded-facts`, cuja política atual se refere a posicionamento. Isso amplia a evidência de entrada, sem modificar pesos ou conceder aprovação tática.

Nomes técnicos são identificadores opacos: o refinamento pede recuperar modalidade/efeito quando faltam metadados. Alertas de referência de uso e condições idênticas auxiliam a curadoria; não validam mecânicas, alvo ou efeito. Definições do jogo instalado exigem procedência e compatibilidade com o patch da partida antes de serem usadas como evidência histórica.

Os mesmos comandos aceitam `--patches <diretório>` para enriquecer uma seleção de desenvolvimento com recibo de metadados de patch. O leitor verifica hashes de manifesto/índice/dados, partida, fonte e conflitos com o contexto existente; não modifica os documentos originais. `matchPatch` recebe o identificador informado pela fonte, mantendo `mechanicsVerified=false` e a lacuna `patch-mechanics`. Um ID OpenDota, um campo de cabeçalho do replay e um número de build do cliente pertencem a fontes distintas; não autorizam associar automaticamente o catálogo instalado à partida.

`tactical-reference-audit --history <diretório> --patch-map <patch.json> --patch-id <id> --output <arquivo-novo>` recompõe propriedades de itens nos snapshots de histórico publicados. Confere hashes, origem/ordem das revisões, baseline, intervalo do patch, campos e alegações de estabilidade. Propriedades que mudaram são explicitamente separadas das estáveis; metadado de alvo ausente permanece desconhecido. O pacote resultante é uma referência diagnóstica: `allowedForTraining=false` e `mechanicsVerifiedForReplay=false`. Histórico publicado completo não demonstra que todas as atualizações do jogo foram capturadas. Essa operação CPU não carrega modelos nem altera contextos ou pesos.

## Arquitetura interna

Um pacote modular com portas e adaptadores, executado separadamente como serviço HTTP CPU ou processo de treino GPU:

| Módulo | Responsabilidade |
|---|---|
| `core` | Erros comuns, JSON estrito/canônico, metadados streaming e leitura limitada de artefatos |
| `reviews.contracts` | Settings e validação dos contratos internos |
| `reviews.application` | Casos de uso de revisão e portas `ReviewEngine`/`JsonFetcher` |
| `reviews.engines`, `reviews.upstream` | Política de prévia, integração Ollama e cliente HTTP limitado sem redirects |
| `experiments.preparation` | Configuração, arquivos e isolamento dos conjuntos antes de carregar ML |
| `experiments.application` | Coordenação do experimento por `TrainingBackend` |
| `experiments.backend`, `lora` | Adaptador com import tardio e algoritmo LoRA existente |
| `runtime` | Fases independentes de preparação, otimização, checkpoint e geração; dependências tensoriais recebidas do runtime |
| `experiments.runs`, `experiments.control` | Tentativas persistidas em SQLite, posse exclusiva de recurso e pausa cooperativa |
| `experiments.capabilities` | Identidade de tarefa/dataset/modelo, candidato treinado e registro explícito de decisão de promoção |
| `service`, `training` | Composição, transporte HTTP/CLI e imports compatíveis com consumidores anteriores |

Os casos de uso aceitam implementações falsas para testar sem rede/modelos. O núcleo não depende da auditoria nem dos adaptadores. Os testes de arquitetura conferem essas fronteiras. Validações e formato público do C# permanecem preservados; nenhuma migração de framework HTTP ou hiperparâmetro é necessária para essa separação.

O fingerprint dos **novos** experimentos usa caminhos relativos e hashes de todas as subpastas Python. Alterar um engine ou caso de uso também altera a identidade do runtime. Um freeze anterior não deve ser atualizado para forçar retomada; preserve sua imagem original. Execuções previamente congeladas permanecem separadas. Novas execuções gerenciadas têm controle durável e pausa cooperativa; não existe agendamento distribuído nem promoção automática.

## Execução gerenciada e pausa persistente

Use um único arquivo SQLite em **volume Linux local compartilhado** por todos os workers da mesma GPU. Não compartilhe esse banco por SMB, pelo PostgreSQL da aplicação ou com o serviço HTTP. A imagem `training` define `COACH_ML_RUN_STORE=/run-state/runs.sqlite3`; monte o mesmo volume em `/run-state` em todas as execuções. Um banco isolado por container não coordena a GPU entre containers.

```powershell
docker volume create coach-ml-run-state
```

Ao executar um novo experimento no container de treino, acrescente `--mount type=volume,source=coach-ml-run-state,target=/run-state` aos mounts do experimento/modelo. Fora do Docker, use `coach-ml train ... --run-store <arquivo-local>`. Sem configuração do store, a CLI do host mantém o modo anterior com trava apenas por experimento. O treino já congelado não recebe essa migração.

O início imprime `runId` e `attemptId`. A pausa cooperativa é suportada em `stage run`; `prepare` tem estado próprio e não é tratado como treino com checkpoint de otimizador. Para solicitar pausa, use o mesmo store e ID em outro processo:

```powershell
coach-ml run-control --store <arquivo-local> --run-id <runId> --action pause
coach-ml run-control --store <arquivo-local> --run-id <runId> --action status
```

No Docker, esses comandos podem usar a imagem `training` e o mesmo volume, **sem** `--gpus`: consultar ou solicitar pausa não carrega pesos. A solicitação muda o estado para `pause-requested`; não suspenda o container com `docker pause`. Durante otimização, o worker conclui o passo, sela o checkpoint, libera seus tensores e encerra. Durante geração, pausa entre batches, preservando o prefixo das previsões. O caso de uso confere o checkpoint antes de registrar `paused` e liberar a posse no store. A CLI deve encerrar normalmente para liberar também o contexto CUDA do processo.

Retome executando `train --stage run` no mesmo experimento, imagem e store. Uma nova tentativa restaura o checkpoint e mantém a identidade congelada. Treino, geração, avaliação e conclusão têm estados separados. Tentativa antiga não altera a atual. Falha abrupta/SIGKILL mantém a posse bloqueada para investigação; não há expiração automática nem comando que roube o recurso de um processo pausado. O store coordena apenas workers configurados nele; não detecta nem substitui reservas de outros processos, incluindo o treino legado pausado.

## Candidatos e promoção

`prepare` registra `model-capability.json`: tarefa, esquemas de entrada/saída, políticas observadas, modelo/tokenizer fixados, template, avaliador e hashes dos conjuntos. O freeze vincula esse manifesto. Ao concluir a execução, `model-candidate.json` vincula a capacidade aos hashes do adaptador e freeze.

O contrato `competency-review-v1` não é o contrato HTTP `assessments`. Não converter campos ou promover esse adaptador diretamente ao serviço. `coach-ml promotion-check --capability <model-candidate.json> --evaluation <avaliacao.json> --decision <decisao.json> --output <registro-novo.json>` verifica compatibilidade, hashes da decisão, aprovação tática explícita e modelo de rollback. Ele registra a decisão e retorna `installed: false`; não instala nem aprova modelo. As avaliações automáticas atuais não concedem aprovação tática, portanto não passam esse gate sozinhas.

## Dependências e verificação GPU

`constraints-training.txt` fixa 43 versões diretas/transitivas para a base Docker atual, Python 3.11/Linux x86_64/CUDA 12.8. A instalação Docker aplica essas restrições; `pyproject.toml` e `constraints-build.txt` fixam setuptools/wheel. Esse lock tem escopo de plataforma e versões, não é um inventário de hashes de wheels para todas as plataformas. Preserve a imagem por digest e o fingerprint de ambiente por experimento. Não aplique o lock Linux ao Python Windows nem atualize um runtime congelado.

O tracking operacional usa os eventos duráveis do store e os manifestos/relatórios existentes. MLflow permanece opcional, sem dependência adicionada ou novo serviço obrigatório.

O ensaio GPU reproduzível fica em `tests/gpu/verify_resume.py` e exige `COACH_ML_GPU_TEST=1`, `CUBLAS_WORKSPACE_CONFIG=:4096:8` e `COACH_ML_GPU_EVIDENCE=<arquivo-novo>`. Com o extra `train`, execute `python -m gpu.verify_resume` incluindo `coach-ml/tests` no `PYTHONPATH`. Usa um modelo Llama sintético mínimo em BF16, teto de alocação de 256 MiB e compara o loop anterior contínuo com o loop extraído interrompido/retomado. Os quatro tensores LoRA ficaram exatamente iguais em CUDA; isso valida a mecânica do ensaio, sem medir qualidade tática ou capacidade do corpus completo.

## Serviço no Docker

No arquivo local .env, configure COACH_ML_ENABLED=true e COACH_ML_API_KEY com uma chave aleatória de pelo menos dezesseis caracteres. Use a mesma chave entre os dois serviços. O padrão COACH_ML_ENGINE=policy-preview executa prévias por política, somente em CPU, identificadas como policy-preview-v1. Não é inferência do modelo treinado.

```powershell
docker compose --profile coach-ml up -d --build api coach-ml
```

O serviço Python fica em http://coach-ml:8090 na rede interna, sem porta publicada. As rotas protegidas recebem Authorization: Bearer e oferecem:

| Rota | Operação |
|---|---|
| GET /health | Liveness do processo, sem afirmar que um modelo está pronto |
| POST /v1/reviews | Recebe contexto preparado pelo C# e devolve revisão com engine/origem |
| POST /v1/reviews/by-match | Recebe matchId como string e heroId inteiro, busca contexto no C# e revisa |

O callback configurado em COACH_ML_CALLBACK_URL chama GET /api/internal/coach/contexts/{matchId}/{heroId}, usando COACH_ML_CALLBACK_KEY. C# também oferece POST /api/internal/coach/reviews/{matchId}/{heroId} para solicitar a revisão ao Python. Essas rotas ficam fora do OpenAPI público e exigem CoachMl habilitado e a chave de máquina; autenticação Steam permanece separada.

A API lê somente replay já arquivado para esse fluxo, entrega no máximo três cenas e valida IDs/evidências da resposta. O Python não recebe acesso direto ao PostgreSQL nem às credenciais da aplicação. Os dois lados preservam o outcome retrospectivo separado das evidências anteriores à âncora.

Para inferência via Ollama, configure COACH_ML_ENGINE=ollama, COACH_ML_OLLAMA_URL e COACH_ML_MODEL. O modelo deve existir nesse runtime; o serviço não baixa pesos e não promove adaptadores treinados automaticamente. Prompts e transporte de hipóteses Ollama agora ficam no Python. Não habilite essa inferência na GPU ocupada por outro treino. Payloads são limitados, redirects de upstream não são seguidos e a revisão é serializada para retornar busy em vez de sobrecarregar o backend.

O Dockerfile tem o alvo service, sem dependências de ML e com usuário não root, e o alvo training, com dependências opcionais/CUDA para novos experimentos. O perfil Compose constrói somente service. Uma execução nova exige preflight do próprio conjunto; o ensaio sintético de retomada não autoriza substituir um runtime congelado.

As duas bases estão fixadas por digest. Para construir uma imagem de treino candidata separada: `docker build --target training -t dotaanalyst-coach-ml-training:next coach-ml`. Não substitua a imagem de um treino congelado. Registre seu ID e `python -m pip freeze`: a construção aplica `constraints-training.txt`, enquanto o fingerprint registra os pacotes efetivamente instalados. O ensaio GPU mínimo descrito acima não substitui preflight do corpus real.

Erros do serviço: 401 sem autenticação, 404 para contexto arquivado ausente, 413 para tamanho inválido, 415 para conteúdo diferente de JSON, 422 para contrato enviado inválido, 502 para falha/resposta inválida do upstream e 504 para timeout de upstream. Revisão ocupada retorna 503 com `Retry-After: 1`; `/health` continua disponível. Nenhuma dessas respostas inclui payload privado ou chave. Erros no JSON do modelo ou do callback são falhas do upstream, não erros de contrato do cliente.

## Instalar e usar

Na raiz do repositório, com Python disponível:

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -e ./coach-ml
.venv/Scripts/python -m coach_ml --help
```

Linux: use `.venv/bin/python`. A instalação fornece também o comando `coach-ml`. Auditoria, consulta de progresso e curadoria usam somente a biblioteca padrão, sem carregar PyTorch ou inicializar GPU.

```powershell
coach-ml status --experiment work/coach-learning/grounded-training
coach-ml audit-sft --dataset work/coach-learning/grounded-training/validation.jsonl --metadata work/coach-learning/grounded-training/validation-metadata.json --manifest work/coach-learning/all-database/manifest.json --partition validation
```

O status descreve arquivos e seu horário de atualização; não confirma processo vivo. Consulte Docker separadamente para isso. Código zero na auditoria indica integridade aceita, código dois indica erro. Avisos de pouca diversidade ou origem provisória não conferem aprovação tática.

O status usa schemaVersion 2: arquivos de progresso vazios, truncados ou preenchidos por bytes nulos geram `warnings` e código dois, com diagnóstico JSON. `checkpoint` mostra separadamente a última referência salva, seu hash de freeze e a presença dos arquivos esperados. Isso não valida o conteúdo dos tensores nem transforma a posição do checkpoint em progresso atual. O comando não corrige arquivos, desserializa checkpoints ou retoma processos automaticamente. Artefatos de resultado/avaliação corrompidos não são apresentados como disponíveis.

## Preparar outro currículo

```powershell
coach-ml build-curriculum --source work/coach-learning/all-database --output work/coach-learning/new-curriculum
```

A saída deve ser um diretório novo. O builder confere hashes da fonte e usa somente train/train-known e validation-new. Produz development.jsonl, validation.jsonl, metadados alinhados, instructions.txt e build-audit.json. Não abre testes reservados, não chama modelo e não executa treino.

O perfil experimental `competency-review-v1` cobre cobertura de posições, separação ao aliado, economia amostrada, compras, upgrades e objetivos. Seleciona até seis casos por competência/partida, equilibrando estados, funções e fases. Usar a mesma cena em tarefas diferentes não aumenta o número de partidas. Os alvos são política provisória do assistente, sem revisão humana fictícia; variedade de exercícios não prova qualidade tática.

`build-curriculum --policy-version 2` ativa exercícios contextuais: o request recebe `reviewPolicyVersion: 2`, e o alvo liga a conferência à âncora, aos registros e a uma anotação verificável no replay. O padrão permanece 1, com as respostas anteriores preservadas. O perfil de auditoria continua `competency-review-v1` porque a estrutura das seis competências permanece; a versão da política é explícita no request e no build-audit. Ambos são alvos provisórios: variação de timestamps/contagens não equivale a diversidade de conceitos táticos. Preserve datasets e runtimes congelados; gere outra pasta ao mudar de política.

Use `audit-sft --profile competency-review-v1` para esse contrato. O perfil padrão factual-review mantém o contrato antigo do treino em andamento. A auditoria confere identidade/hash declarados, partições, JSON estrito, fidelidade do alvo ao input e diversidade; não reprocessa replay bruto nem certifica toda prosa livre. Os metadados JSON são lidos incrementalmente, com memória adicional proporcional aos IDs/hashes únicos.

Para conferir treino e validação juntos, inclusive entradas idênticas provenientes de partidas diferentes:

```powershell
coach-ml audit-experiment --experiment work/coach-learning/new-curriculum --manifest work/coach-learning/all-database/manifest.json --profile competency-review-v1
```

O comando apenas lê os dados e retorna código dois se houver entrada repetida dentro de um conjunto ou compartilhada entre treino/validação. Não remove dados automaticamente. Partidas diferentes podem produzir entradas idênticas depois de uma projeção limitada; por isso conferir somente matchId não basta. A inspeção de novos treinos também exige esse isolamento.

Para medir a cobertura do conjunto já validado:

```powershell
coach-ml coverage --experiment work/coach-learning/new-curriculum-clean --manifest work/coach-learning/all-database/manifest.json --profile competency-review-v1 --output work/coach-learning/coverage-report.json
```

O relatório separa exemplos, perspectivas de jogador/episódio e partidas, por herói, posição, fase e competência. Mostra heróis presentes só no treino ou só na validação e combinações da validação ausentes no treino. Essas combinações podem representar generalização deliberada, não um erro a corrigir misturando as partições. As fases usam a âncora: antes de 10 min, 10–25 min e a partir de 25 min. Não atribui nomes externos aos heróis nem transforma papel declarado em revisão humana. O contrato atual não projeta patch; sua cobertura não é inferida. Nenhum peso ou replay bruto é aberto, e cobertura não concede aprovação tática.

Com `--output`, o relatório completo vai para um arquivo novo e o terminal recebe só um resumo. O diretório de saída precisa existir e arquivos anteriores não são sobrescritos. Sem essa opção, o comando imprime o JSON completo para permitir redirecionamento.

Para produzir outro conjunto sem entradas repetidas:

```powershell
coach-ml deduplicate --source work/coach-learning/new-curriculum --output work/coach-learning/new-curriculum-clean --manifest work/coach-learning/all-database/manifest.json --profile competency-review-v1
```

O destino precisa ser novo. O comando valida a origem, mantém a primeira ocorrência no treino e remove da validação entradas já vistas. Preserva mensagens/alvos e renumera somente as linhas dos metadados. Se a limpeza eliminaria todas as amostras de uma partida, falha para permitir outra seleção sem esconder perda de cobertura. Confere hashes da origem antes/depois e audita o resultado. `curation-audit.json` registra sucesso e hashes; uma pasta parcial sem esse arquivo não é um conjunto aprovado. Arquivos anteriores permanecem intactos.

## Orçamento de tokens em CPU

Antes de congelar outro treino, confira o tamanho real dos exemplos com o tokenizer da revisão local do modelo. Use um ambiente com o extra `tokenizer` (`python -m pip install -e "./coach-ml[tokenizer]"`) ou o ambiente de treino já existente. A imagem leve do serviço continua sem essas dependências opcionais.

```powershell
coach-ml token-budget --dataset work/coach-learning/new-run/development.jsonl --dataset work/coach-learning/new-run/validation.jsonl --snapshot work/coach-learning/training-smoke/cache/hub/models--Qwen--Qwen3-0.6B/snapshots/c1899de289a04d12100db370d81485cdf75e47ca --max-training-tokens 2048 --max-new-tokens 384
```

O comando aplica o mesmo template sem thinking do treinador, inclui EOS na resposta e relata máximos, percentil 95 e excessos separados de entrada/saída. Não carrega pesos, não treina nem trunca exemplos. Código dois indica orçamento excedido ou erro; aumentar o limite exige revisar a configuração do próximo experimento. Monte o snapshot somente leitura e desative rede/GPU no container usado para essa checagem.

## Novos treinos

O treinador foi extraído para novos experimentos. **O treino que já estava rodando continua no seu runtime congelado em work.** Este pacote recusa executar experimentos congelados pelo runtime antigo.

Instale o extra `train` somente em ambiente de ML compatível:

```powershell
python -m pip install -e "./coach-ml[train]"
```

O extra fixa as versões diretas usadas no ambiente local: PyTorch 2.8.0, Transformers 4.56.2, PEFT 0.17.1, Safetensors 0.8.0 e Accelerate 1.10.1. A instalação do PyTorch com CUDA depende da plataforma; o extra não configura driver nem garante aceleração. O ambiente Docker já existente evita alterar o host e o treino ativo. Versões transitivas e imagem também devem ser registradas/congeladas em cada experimento.

Em um diretório novo dentro de work, prepare os JSONL/metadados, instructions.txt, config.json e experimental-authorization.json. O último registra um `scope` textual do experimento autorizado, sem representar aprovação tática humana. A [configuração de exemplo](configs/lora.example.json) tem contagens ilustrativas: substitua samples e validationSamples pelos números auditados. Use o perfil correto e o cache local da revisão fixada do modelo.

```powershell
coach-ml train --stage inspect --experiment work/coach-learning/new-run --model-cache work/coach-learning/training-smoke/cache --manifest work/coach-learning/all-database/manifest.json
```

`inspect` valida configuração, fontes/partições e contagens sem importar ML. `prepare` exige GPU CUDA compatível com BF16, tokeniza sem truncamento, verifica gradientes/memória e congela fontes/código/pesos-base. `run` executa uma época, salva checkpoints retomáveis e um adaptador separado e compara base/adaptado automaticamente. Para essas duas etapas, troque somente `--stage` no comando anterior. Não execute enquanto outro treino ocupa a GPU.

Use cache montado somente leitura e rede desligada no Docker. O runtime exige arquivos locais, trust_remote_code=False e local_files_only=True. Não baixa modelos. O congelamento inclui todos os módulos Python do pacote; mudanças no pacote após prepare exigem um novo experimento. Não alterar código congelado para retomar uma execução.

O fingerprint inclui também Python, sistema/arquitetura e versões das distribuições instaladas, incluindo dependências transitivas. Uma retomada com bibliotecas diferentes é recusada mesmo se os JSONL e o código do projeto não mudaram. Preserve a imagem original do experimento.

O arquivo `.coach-ml.lock` impede processos concorrentes no mesmo diretório. Após desligamento abrupto, confirme que o processo terminou antes de remover uma trava residual. Retome com o mesmo estágio run e os mesmos arquivos; os checkpoints incluem otimizador e RNG. Falhas ao salvar um checkpoint parcial podem exigir investigação antes de retomar.

Nos novos experimentos, cada checkpoint recebe `checkpoint-manifest.json` com tamanho e SHA-256 do adaptador, configuração e estado do otimizador. O runtime confere o selo antes de desserializar e publica o ponteiro somente após conferir o diretório final. Checkpoints antigos sem selo são recusados por esse runtime; mantenha seu runtime original, sem criar selos retroativos. Diretórios parciais são preservados para investigação. Essa verificação detecta alteração acidental, não autentica a origem dos arquivos nem torna um `state.pt` externo confiável. Não há garantia contra toda falha de disco ou perda de energia. O comando `status` continua uma inspeção leve de referências, sem verificar esses hashes nem carregar tensores.

Antes de iniciar inferência, o runtime futuro valida os prefixos de previsões base/adaptado em CPU: sequência, identidade, tipos e linhas completas. Prefixos válidos são continuados; arquivos completos dispensam geração. Linhas interrompidas, inclusive JSON válido sem newline final, são recusadas sem reparo ou descarte automático. Preserve o arquivo para investigação. Essa checagem não atesta qualidade do texto nem autentica sua procedência; a avaliação completa continua obrigatória.

Execute o treinador sem `-O` ou `PYTHONOPTIMIZE`: o runtime contém verificações obrigatórias que não podem ser desativadas. Dependências opcionais ausentes retornam erro controlado e liberam a trava; o serviço leve não instala PyTorch automaticamente.

Resultados: output/training-result.json, output/adapter-final, previsões base/adaptado e evaluation-audit.json. A avaliação distingue JSON estrito/normalizado, observação factual, foco e reprodução da política. Review de conteúdo continua pendente; nenhuma promoção automática ou aprendizagem geral de Dota é inferida pela loss.

Para avaliar previsões já salvas, sem importar ML:

```powershell
coach-ml evaluate --experiment work/coach-learning/new-run
```

A avaliação exige os dois arquivos de previsões completos, alinha cada exemplo e produz métricas por competência. Predições de competências devem carregar `heroId`, `competencyId` e `exampleId`, além de linha/partida/episódio/variante; `hitTokenLimit` deve ser booleano. Saída truncada não recebe crédito. JSON com cercas Markdown conta somente nas métricas normalizadas. O comando preserva relatórios existentes, usa trava exclusiva e não altera o progresso do treino. Não acrescente identidades fictícias a previsões antigas para forçar compatibilidade: preserve seu avaliador original.

O relatório schemaVersion 2 inclui comparação pareada: casos corretos nos dois modelos, ganhos, regressões e erros comuns, por competência e partida. `evaluation-cases.jsonl` referencia cada exemplo e seus motivos de falha, sem duplicar textos/contextos. Uma diferença de redação da política não é automaticamente erro factual. Os hashes dos inputs e do JSONL acompanham o agregado; este é publicado por último. Artefatos finais ou parciais existentes não são sobrescritos. Se houver falha durante a publicação, preserve a pasta para investigação e use outra pasta para nova comparação. A comparação mantém os escores base em memória, proporcionalmente ao número de exemplos. Casos da mesma partida não são partidas independentes, e ganho nessas métricas não aprova qualidade tática.

Para avaliar adicionalmente um treino concluído preservando seu pipeline original:

```powershell
coach-ml snapshot-evaluation --source work/coach-learning/grounded-training --output work/coach-learning/grounded-training-comparison
```

O destino deve ser novo e ficar fora da origem. O comando exige `output/training-result.json` com contagem válida, confere a configuração quando presente, copia metadados e previsões e verifica hashes antes/depois. A avaliação ocorre na cópia, somente em CPU. `snapshot-audit.json` é publicado por último; sem ele, não considere o snapshot concluído. Resultado ausente, previsões incompletas ou contrato incompatível são recusados sem reescrever rótulos. Isso não detecta processo vivo nem prova qualidade do adaptador. Não execute no treino ainda em andamento; arquivos originais, pesos e código congelado permanecem separados.

## Curadoria tática experimental

Os comandos `tactical-infer`, `tactical-review` e `tactical-curate` são separados do contrato HTTP e dos perfis LoRA existentes. A projeção usa listas explícitas de campos, exclui outcomes/IDs de episódios do prompt, rejeita fontes futuras e preserva empates de eventos. Com `--include-support`, confere o hash da proveniência temporal arquivada e acrescenta a transição de separação calculada pelo C#, sem inferir trajeto, visão ou capacidade de intervenção.

```powershell
coach-ml tactical-infer --source work/coach-learning/all-database --index work/coach-learning/tactical-selection-v1/capacity-comparison.jsonl --output work/coach-learning/tactical-policy-example --engine policy --include-support
```

`--engine policy` produz somente uma referência genérica de formato. `--engine ollama` usa um modelo já instalado e registra seu digest, prompt, parâmetros, tokens e tempo; não baixa modelos. `--engine huggingface --snapshot <snapshot-local>` usa o extra `train`/CUDA; `--adapter <diretorio-local>` permite comparar um adaptador existente, sem atualizá-lo. Esse backend agrupa até quatro entradas e registra tempo por batch e pico de alocação PyTorch; a primeira chamada pode preencher um cache de batch, portanto seu tempo por linha não deve ser apresentado como latência individual de serviço. Execute backends GPU sequencialmente.

Por padrão, os índices aceitos são de desenvolvimento (`train`/`train-known`), com verificação contra o manifesto, hashes dos documentos e isolamento de caminhos. O modo explícito `--partition validation-diagnostic` aceita validações já inspecionadas, sem tratá-las como avaliação cega. Reservas de teste não entram nesses modos. `--bundle` aceita controles auditados em âncoras independentes, como alternativa a `--index`. Previsões têm trava exclusiva, identidade de entrada/runtime e retomada por prefixo; arquivo interrompido é recusado. Após uma interrupção abrupta, confira processos antes de tratar uma trava residual. Uma resposta truncada não recebe crédito estrutural mesmo se o JSON estiver fechado.

```powershell
coach-ml tactical-review --source work/coach-learning/all-database --index work/coach-learning/tactical-selection-v1/capacity-comparison.jsonl --predictions work/coach-learning/tactical-candidate/predictions.jsonl --output work/coach-learning/tactical-critique --include-support
coach-ml tactical-curate --source work/coach-learning/all-database --index work/coach-learning/tactical-selection-v1/capacity-comparison.jsonl --generation work/coach-learning/tactical-candidate --review work/coach-learning/tactical-critique --output work/coach-learning/tactical-curated --minimum-accepted 96 --include-support
```

A crítica verifica contrato, citações e consistência entre notas/veredito; registra a possibilidade de gerador e crítico compartilharem o modelo. A curadoria exige alinhamento completo, vincula a crítica às previsões avaliadas e revalida os candidatos. Verificações lexicais adicionais sinalizam referências explícitas a aliados tratados como inimigos e contagens contraditórias na prosa; não certificam qualquer frase livre. Casos sinalizados aguardam revisão em vez de serem corrigidos automaticamente.

A saída `provisional-sft.jsonl` contém somente candidatos aceitos provisoriamente, com rejeições e proveniência preservadas. `experimentalDataReady` indica apenas o mínimo solicitado de exemplos; `humanReviewed`, `trainingApproved` e `tacticalQualityApproved` continuam falsos. Críticas inválidas ou rejeições não viram alvos aprovados.

`tactical-curate --content-review <recibo.json>` permite aplicar vetos por caso de uma revisão provisória do assistente. O recibo exige todos os casos, hashes das previsões e de cada input, origem explícita e motivo. Pode somente remover candidatos; `keep-provisional` não promove uma rejeição do crítico. O hash do recibo acompanha metadados e auditoria. Exporte para um diretório novo para preservar a curadoria anterior.

`tactical-refine` permite calibrar o professor com exemplos provisórios conferidos contra o índice de desenvolvimento. Registra separadamente o prompt do professor e o contrato do aluno; explicita a relação aliado/inimigo e o significado dos objetivos registrados. `tactical-review --calibrated` usa a rubrica correspondente. Essas chamadas usam saída estruturada e ainda passam por validação semântica local; gerar JSON válido não aprova a tática.

Ambos aceitam `--think --max-tokens 4096` para um ensaio separado com raciocínio habilitado no modelo local. O padrão continua sem raciocínio e com 1024 tokens. A opção e o orçamento fazem parte da identidade de retomada; a resposta final é preservada, mas o campo de raciocínio interno não é exportado para treino. Truncamentos continuam inválidos; compare qualidade, tempo e orçamento separadamente antes de escalar.

O modo opcional `tactical-refine --bounded-facts` restringe a oportunidade factual a um texto canônico e as limitações a valores derivados da projeção. Ações, condições e exercício continuam gerados pelo modelo e precisam de revisão. O esquema e a pós-validação local exigem essas restrições, inclusive na retomada e curadoria. A identidade declara `conditional-replay-review-only`: preferência indeterminada e revisão condicional/insuficiência. Reduzir fatos inventados por construção não comprova aprendizado tático ou capacidade de escolher a melhor jogada; mantenha esse ensaio separado da geração livre.

A política `canonical-position-review-v2` também fixa o exercício quando é preciso recuperar uma posição ausente: identifica a lacuna atual ou anterior pelo contrato de suporte e exige a janela temporal correta. Demais exercícios continuam livres. A política v1 arquivada permanece reavaliável sem impor essa regra retroativamente; a versão é registrada em cada previsão.

`tactical-assemble` monta um experimento novo do perfil `tactical-review-v1`, com treino e validação diagnóstica separados por partida, mínimos explícitos e hashes dos arquivos preparados. Recusa veto de conteúdo e diretório incompleto ou alterado. O perfil está integrado à auditoria, preparação e avaliação; a integração foi validada em CPU. O treino exige corpus e orçamento próprios. Sua avaliação mede contrato e alertas lexicais, sem apresentar esses números como precisão tática. Esses comandos não iniciam treino, não tornam o adaptador compatível com `assessments` e não promovem modelo.

Testes direcionados, sem pesos ou GPU:

```powershell
python -B -m unittest discover -s coach-ml/tests -p 'test_tactical*.py' -v
```

## Modelos de previsão (risco de morte e próxima região)

O primeiro modelo estima a chance de morrer nos próximos **15 s**; o segundo prevê a região **30 s depois** (ou `dead`, morto naquele instante). Os rótulos são acontecimentos registrados no próprio replay, sem precisar de revisão humana para treinar. Isso não aprova automaticamente as dicas: imitar um destino frequente não prova que ele era a melhor jogada.

As colunas visíveis representam o que o jogador podia saber naquele momento; as `hidden*` incluem informação oculta pela névoa. O risco compara uma versão visível com uma onisciente para diagnóstico. Explicações e dicas usam somente o modelo visível, para não cobrar do jogador algo que ele não podia ver.

As amostras são calculadas pelo C# a partir do documento guardado, nas rotas internas `/api/internal/death-risk/samples/{id}` e `/api/internal/next-region/samples/{id}`. O exportador usa a chave de máquina já configurada em `$env:COACH_ML_API_KEY`, enviada como Bearer; Python não consulta o banco. `featureVersion` identifica o contrato: listas por versão (`SITUATION_BY_VERSION`, `VISIBLE_BY_VERSION`, `HIDDEN_BY_VERSION`) definem as colunas. Cada modelo guarda suas próprias colunas em `training.json`, permitindo ler exportações antigas com seus modelos correspondentes.

Os exemplos abaixo partem da raiz do repositório e usam pastas novas, ilustrativas, em `work/death-risk/`. Prepare um manifesto com `matchId` e `split`; para próxima região, mantenha as partidas do dono em `owner-application`, fora de `train`/`train-known`. O raciocínio e as decisões estão no [plano local](../work/death-risk/plan.md), ignorado pelo Git e disponível apenas no checkout local.

Para treinar e analisar em CPU, instale o extra `[risk]` (scikit-learn), com as dependências transitivas fixadas pelas constraints. Como alternativa, construa a imagem CPU `risk`; os comandos seguintes pressupõem o pacote instalado no ambiente local.

```powershell
python -m pip install -c coach-ml/constraints-risk.txt -e "./coach-ml[risk]"
docker build --target risk -t dotaanalyst-coach-ml-risk:local coach-ml
```

Primeiro, exporte um CSV gzip por partição, com manifesto e hashes. Repita com `--kind next-region` para obter os rótulos do segundo modelo.

```powershell
coach-ml risk-export --manifest work/coach-learning/prediction-run/manifest.json --api-url http://localhost:8080/ --output work/death-risk/export-example
coach-ml risk-export --manifest work/coach-learning/prediction-run/manifest.json --api-url http://localhost:8080/ --kind next-region --output work/death-risk/export-region-example
```

Depois, treine o risco: taxa constante, regressão simples e boosting visível/onisciente. `--iterations` oferece tamanhos para escolher dentro do desenvolvimento; partidas inteiras ficam separadas entre ajuste, escolha e calibração, sem usar validação ou teste para treinar.

```powershell
coach-ml risk-train --export work/death-risk/export-example --output work/death-risk/models-example --iterations 400 800 1600 3200
```

Compare os modelos em `validation-new`, conferindo PR-AUC e calibração (se o risco previsto combina com a frequência real). Só depois de congelar modelo e protocolo, acrescente `--final` **uma única vez** para avaliar exclusivamente `test-new`, em outra pasta. O código exige a opção, mas não impede repetições em pastas novas.

```powershell
coach-ml risk-evaluate --models work/death-risk/models-example --export work/death-risk/export-example --output work/death-risk/evaluation-example
```

Explique o modelo visível: importância geral e destaques antes das mortes selecionadas. `advice` indica uma mudança na direção permitida como conselho; `context` apenas descreve o momento, sem recomendar mudar aquele número. O arquivo de foco contém pares como `[{"matchId":"123","heroId":1}]`, substituídos por identidades reais.

```powershell
coach-ml risk-explain --models work/death-risk/models-example --export work/death-risk/export-example --partition train --focus work/death-risk/focus.json --output work/death-risk/explain-example
```

Treine a próxima região com as colunas visíveis, comparando boosting com frequências por região atual e por papel/faixa de cinco minutos.

```powershell
coach-ml region-train --export work/death-risk/export-region-example --output work/death-risk/models-region-example --iterations 100 200 400
```

Avalie em `validation-new`: acerto entre os primeiros um/três destinos, log loss e calibração por classe. A mesma regra de `--final`, somente em `test-new` e uma vez após congelar, vale aqui.

```powershell
coach-ml region-evaluate --models work/death-risk/models-region-example --export work/death-risk/export-region-example --output work/death-risk/evaluation-region-example
```

Por fim, gere candidatas para revisão: as três condições precisam ocorrer juntas — destino observado com probabilidade **< 15%**, outro destino com **> 50%**, e consequência ruim: morto em t+30 s ou ganho de patrimônio nos próximos 60 s menor que metade da mediana positiva do mesmo papel/faixa de cinco minutos. Isso ainda não publica dicas no site.

```powershell
coach-ml region-hints --models work/death-risk/models-region-example --export work/death-risk/export-region-example --partition owner-application --focus work/death-risk/focus.json --output work/death-risk/hints-example
```

As partições `reserved-*` nunca são exportadas nem avaliadas; explicações/dicas também recusam `test-new`. Cada saída exige pasta nova: `export-manifest.json`, `training.json`, `report.json`, `explanations.json` ou `hints.json` é escrito por último como registro de conclusão. Preserve pastas parciais. A leitura confere hashes dos CSVs; avaliação, explicação e dicas conferem também os pesos e a identidade da exportação usada no treino.

## Testar e empacotar

Para alterações de arquitetura/serviço, rode primeiro os testes diretamente afetados:

```powershell
python -B -m unittest discover -s coach-ml/tests -p test_architecture.py -v
python -B -m unittest discover -s coach-ml/tests -p test_service.py -v
```

A suíte abaixo continua disponível para validação ampla quando necessária:

```powershell
python -B -m unittest discover -s coach-ml/tests -v
python -m pip wheel ./coach-ml --no-deps --wheel-dir work/coach-learning/package-wheels
```

Os testes não precisam de GPU, internet ou dados privados. Dados, pesos, cache de modelos, relatórios e ambientes virtuais ficam locais e ignorados. O pacote é privado e não será publicado automaticamente. Código mantido e testes ficam neste projeto; use `coach-ml` ou `python -m coach_ml` para executar os comandos.
