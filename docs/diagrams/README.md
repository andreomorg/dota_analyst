# Diagramas do DotaAnalyst

A fonte dos diagramas são os blocos `mermaid` do [README principal](../../README.md#como-funciona): uma visão geral e um diagrama por serviço, num estilo simples, sem cores ou tema fixados. O visualizador Markdown decide a aparência clara/escura e os controles de zoom.

| Diagrama | O que mostra |
|---|---|
| [Visão geral](../../README.md#como-funciona) | Nuvem, máquina local e serviços externos |
| [Site](../../README.md#site-react) | Páginas e a conversa com a API |
| [API](../../README.md#api-c) | Camadas e o fluxo de uma análise com replay em processamento |
| [Leitor de replays](../../README.md#leitor-de-replays-go) | Do link do replay ao documento da partida |
| [Banco de dados](../../README.md#banco-de-dados-postgresql) | O que fica no banco local e o que vai à nuvem |
| [Base de comparação](../../README.md#base-de-comparação-coletor) | Coleta, percentis e publicação |
| [Modelos de previsão e coach](../../README.md#modelos-de-previsão-e-coach-python) | Momentos do replay, exemplos e avaliação |
| [Login com a Steam](../../README.md#login-com-a-steam) | Autenticação pela página oficial |

## Editar e conferir

Edite o bloco Mermaid no README. Prefira rótulos curtos, aspas nos textos e quebras `<br/>`; `flowchart` para estrutura e processos, `sequenceDiagram` para chamadas em ordem. O README explica **como funciona**, para quem chega ao repositório; decisões e detalhes técnicos ficam no `CLAUDE.md`, no `replay-parser/CLAUDE.md` e no [README do coach-ml](../../coach-ml/README.md). Não use imagens PNG no lugar dos diagramas.
