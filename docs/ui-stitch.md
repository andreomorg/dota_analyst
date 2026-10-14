# Referências visuais da aplicação

Fonte oficial: [projeto DotaAnalyst no Stitch](https://stitch.google.com/projects/13241258925802792561).

O objetivo é uma aplicação profissional para apresentação do trabalho do autor. A implementação deve preservar a composição das telas escolhidas, fontes, espaçamentos, cores e assets, conectando os dados reais sem inventar indicadores de serviço ou navegação sem destino.

## Referências selecionadas

| Tela | ID no Stitch | Viewport CSS |
|---|---|---|
| Início e partidas — produção desktop | `9532957234220353483` | 1280px |
| Início e partidas — produção mobile | `9532957234220354159` | 390px |
| Análise — original desktop | `c4d830735f5541f39e1c0cadfef105ec` | 1280px |
| Análise — original mobile | `5011471ef3ba4fa0a160f6f5ed241808` | 390px |
| Como melhorar — original desktop | `5632a4722a0d45dca0ca1405afbe857a` | 1280px |

As versões de produção preservam os originais e retiram controles de simulação, status/latência fictícios e links sem funcionalidade. As screenshots do Stitch podem estar em escala 2x: a resolução do PNG não é a largura CSS do viewport.

## Contrato de implementação

- Fontes Inter e JetBrains Mono servidas localmente; base 16px para manter unidades `rem` do export.
- Retratos, ícones e minimapa vêm do catálogo real do jogo. Imagens geradas no protótipo não substituem a identidade de um herói ou a geometria do replay.
- Dados de teste ficam no navegador de validação, por interceptação de requisições; nenhum fallback com estatísticas fictícias é incluído no produto.
- Preservar `player`, `tab`, autenticação Steam, estados de replay, estatísticas por minuto e comparações existentes.
- Comparar desktop/mobile no mesmo viewport e com conteúdo equivalente. Um percentual de diff com dados/assets distintos não mede a fidelidade do layout.
- Verificar estados vazios, erro, carregamento e acesso por teclado. Tabelas e linhas de itens com scroll precisam receber foco e ter um nome acessível.

## Plano e evidências locais

Conforme convenção do projeto, `work/` não é versionado. O plano completo solicitado pelo autor está em `work/sprint-20-stitch-ui/board.md`, acompanhado de 15 cards, inventário, exports e diário em `evidence.md`.

As verificações locais usam Edge headless/Playwright, fixtures interceptadas, capturas e auditoria axe. Lint e build continuam sendo os checks de CI. Capturas de navegador são verificações adicionais; a auditoria automática não substitui revisão manual de acessibilidade.

As sete abas desktop e 18 estados auxiliares já têm referências próprias exportadas e revisadas. IDs e regiões estão na matriz local `work/sprint-20-stitch-ui/desktop-reference-matrix.md`; desenhos na galeria `desktop-gallery.html`. A implementação React segue `react-implementation-plan.md` e os cards 06–15, concluídos para desktop com validação visual e dados determinísticos. A integração com a API real permanece pendente de verificação em produção. Mobile foi adiado pelo usuário. O shell das abas segue o Resumo; conteúdo, dados e assets seguem o contrato de vinculação documentado no plano.

## Referências finais de análise desktop

| Aba | ID Stitch | Viewport CSS |
|---|---|---|
| Resumo | `9532957234220352597` | 1280px |
| Como melhorar | `9532957234220353145` | 1280px |
| Fase de rotas | `9532957234220353652` | 1280px |
| Posicionamento | `9532957234220354497` | 1280px |
| Lutas e objetivos | `9532957234220354328` | 1280px |
| Build | `9532957234220353821` | 1280px |
| Jogadores | `9532957234220353314` | 1280px |

Estados de busca/sessão e análise/replay estão em duas bibliotecas, com 18 regiões identificadas. Os atlas são documentação e não serão renderizados integralmente no produto.
