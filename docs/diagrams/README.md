# Diagramas do DotaAnalyst

Fonte editável: [dotaanalyst.drawio](dotaanalyst.drawio). As imagens PNG do README são exportadas dessa fonte, sem redesenho separado. Os diagramas descrevem responsabilidades e contratos; não são indicadores de disponibilidade ou progresso de serviços.

| Página | Índice CLI | Imagem | Escopo |
|---|---:|---|---|
| 1. Arquitetura | 1 | [architecture.png](images/architecture.png) | Processamento local, nuvem, Python HTTP e treino separado |
| 2. Camadas DDD | 2 | [ddd-layers.png](images/ddd-layers.png) | Dependências .NET e limite com Python |
| 3. Fluxo da análise | 3 | [match-analysis.png](images/match-analysis.png) | Análise pública e processamento de replay |
| 4. Base de comparação | 4 | [comparison-baseline.png](images/comparison-baseline.png) | Coleta, agregação e publicação |
| 5. Login Steam | 5 | [steam-login.png](images/steam-login.png) | Autenticação pública |
| 6. Banco de dados | 6 | [database.png](images/database.png) | Dados locais e refinados |
| 7. Coach experimental | 7 | [coaching.png](images/coaching.png) | Evidências C#, revisão Python e validação |
| 8. Curadoria, treino e avaliação | 8 | [coach-training.png](images/coach-training.png) | Dados, congelamento, GPU, checkpoints e comparação |
| 9. Integração interna | 9 | [coach-integration.png](images/coach-integration.png) | Rotas e callbacks autenticados |

## Exportar

No draw.io Desktop, exporte a página desejada como PNG com escala 150% e margem 20. Pela CLI, na raiz do repositório, o equivalente para a página 8 é:

```powershell
& 'C:/Program Files/draw.io/draw.io.exe' --disable-gpu --export --format png --page-index 8 --scale 1.5 --border 20 --output docs/diagrams/images/coach-training.png docs/diagrams/dotaanalyst.drawio
```

A tabela usa a numeração a partir de 1, exigida pelo draw.io Desktop desde a versão 27.0.2; versões anteriores usavam índices a partir de 0. Ajuste o executável ao seu sistema. No Windows, automações podem iniciar esse comando com janela oculta. Depois de exportar, confira a imagem, os textos, os conectores e os links do README. Mudanças em login, banco ou análise pública só precisam alterar essas páginas se os respectivos contratos mudarem.

As chamadas C# ↔ Python usam chave de máquina, independente da sessão Steam. O serviço HTTP não treina nem instala adaptadores automaticamente. O treinamento usa outra execução/container, com cache offline e pesos separados; formatos corretos e loss baixa não aprovam qualidade tática. Veja [a documentação do coach-ml](../../coach-ml/README.md) para os limites de cada etapa.
