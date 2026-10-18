# Diagramas do DotaAnalyst

A fonte atual dos diagramas são os blocos `mermaid` no [README principal](../../README.md#diagramas). Todos usam o mesmo estilo nativo da visão geral do coach, sem cores ou tema fixados. O visualizador Markdown determina a aparência clara/escura e os controles de zoom, cópia e navegação disponíveis.

| Diagrama | Escopo |
|---|---|
| [Arquitetura](../../README.md#arquitetura) | Processamento local, nuvem, Python HTTP e treino separado |
| [Camadas DDD](../../README.md#camadas-ddd) | Dependências .NET e processos externos |
| [Coach experimental](../../README.md#coach-experimental) | Evidências C#, revisão Python e validação |
| [Curadoria, treinamento e avaliação](../../README.md#curadoria-treinamento-e-avaliação-do-modelo) | Dados, congelamento, GPU, checkpoints e comparação |
| [Integração interna](../../README.md#integração-interna-c-e-python) | Rotas e callbacks autenticados |
| [Fluxo da análise](../../README.md#fluxo-da-análise) | Consulta pública e processamento de replay |
| [Base de comparação](../../README.md#base-de-comparação) | Coleta, agregação e publicação |
| [Login Steam](../../README.md#login-com-a-steam) | Autenticação pública |
| [Banco de dados](../../README.md#banco-de-dados) | Dados locais e refinados |

## Editar e conferir

Edite diretamente o bloco Mermaid correspondente no README. Prefira rótulos curtos, aspas nos textos e quebras `<br/>` nos fluxogramas. Use `flowchart` para arquitetura/processos e `sequenceDiagram` para chamadas ordenadas. Não adicione uma imagem PNG ao README para substituir o diagrama nativo.

Confira a renderização, a legibilidade e os contratos envolvidos. Uma mudança documental não exige rodar a suíte da aplicação. Cores e controles podem variar entre GitHub, IDE e outros visualizadores; fazem parte do renderizador, não da aplicação DotaAnalyst.

## Arquivos anteriores

[dotaanalyst.drawio](dotaanalyst.drawio) e a pasta `images/` preservam a versão anterior editável e suas exportações. Não são mais a fonte de verdade dos diagramas exibidos no README. Novas alterações devem ser feitas nos blocos Mermaid.

O serviço HTTP Python e o treinamento são fluxos separados. Veja [a documentação do coach-ml](../../coach-ml/README.md) para configuração, execução e limites dos experimentos.
