# Etapa 4 — avaliação do motor do Online Anime Player

Status: manter MonoTorrent 3.0.2 no aplicativo. Libtorrent 2.1.1 permanece uma alternativa experimental, sem dependência nova no instalador.

## Decisão

Não há evidência suficiente de ganho consistente para justificar uma migração agora. A abertura sem cache com peças pequenas ficou próxima nos dois adaptadores sob o mesmo limite externo de rede. O protótipo nativo reaproveitou arquivo completo mais rapidamente, mas essa diferença é muito menor do que os minutos de espera relatados e não resolve descoberta de fontes. Também houve timeouts ao buscar trechos ainda ausentes no adaptador libtorrent com peças grandes.

Isso não prova uma deficiência geral do libtorrent: medimos um protótipo específico contra um seed MonoTorrent, com prioridades, filas e políticas distintas. Os timeouts podem envolver o adaptador ou interoperabilidade. Um motor alternativo ainda precisa passar pelos mesmos contratos do AniT, com cancelamento, persistência, seleção de arquivos, ranges HTTP, MKV, áudio, legendas e progresso da biblioteca.

## O que foi entregue

A bancada reproduzível está em `tools/TorrentEngineBench`. Compara versões fixadas dos dois motores fora do aplicativo, sem acessar a biblioteca do usuário. Inclui seed e cliente novos por ensaio, troca da ordem de execução, verificação byte a byte dos intervalos, detecção de download fora do episódio escolhido, cache vazio/completo, peças de 64 KiB/4 MiB e buscas para 75%/25% do arquivo. Limita o tráfego por relay TCP externo inclusive na primeira rajada; nenhum motor usa seu limitador interno.

São três repetições por motor, condição e tamanho de peça: 24 tentativas. Abertura com cache exige zero bytes de conteúdo recebidos. Os testes do relatório rejeitam leituras parciais/inválidas, mantêm falhas na contagem e impedem usar um binário antigo sem relay na comparação.

Os ensaios exploratórios anteriores ao relay não entram na decisão nem na tabela principal. Eles demonstraram que configurar o mesmo limite interno não controla igualmente as rajadas iniciais dos motores. Não comparamos números dessas condições como se fossem equivalentes.

## Resultados controlados

Veja [relatório da rodada com relay](benchmarks/online-engine-step-4/report.md) e [dados brutos](benchmarks/online-engine-step-4/results.json). Valores ausentes são informados como n/d; medianas dos ensaios concluídos devem ser lidas junto com a quantidade de falhas.

O cenário usa dados sintéticos e metadados locais. Não inclui resolução de magnet, DHT pública, HTTP, parser MKV ou renderização do mpv. Portanto, não mede primeiro quadro, fluidez de um episódio inteiro nem tempo de abertura do magnet real. As três repetições por cenário são suficientes para revelar regressões óbvias na bancada, não para estimar percentis de produção.

## Consequência para as próximas etapas

1. O passo 5 continua com o motor atual e melhora a política de leitura/prioridade para o começo do vídeo, índices e buscas. O que o player pede deve orientar a fila, sem uma leitura antiga disputar indefinidamente com uma busca nova. A seleção continua restrita ao episódio aberto.
2. O passo 6 usa buffer em segundos, demanda do player e taxa observada. Trocar de motor não elimina a necessidade de aguardar bytes e verificar a integridade de peças grandes.
3. A inicialização antecipada do DHT, pendente do passo 3, continua uma investigação separada. Esta bancada desabilita DHT; não contém evidência para afirmar que uma troca acelera a primeira descoberta pública.
4. Se voltarmos a testar libtorrent, usar primeiro um adaptador isolado com API restrita e cancelamento explícito. O Python/wheel usado aqui é ferramenta de laboratório; não será uma dependência do usuário. Uma integração de produção exigiria bridge nativa ou processo auxiliar versionado, empacotamento x64/Store, controle de portas, encerramento e persistência próprios.

Reabrir a decisão quando uma integração alternativa passar sem timeouts e demonstrar ganho repetível em descoberta, primeiro quadro e buscas, com conteúdo autorizado, seeds de implementações diferentes, rede pública e reprodução longa. Incluir ausência de seeds, mudanças de rede, torrents privados, arquivos retomados e peças grandes. Preservar a biblioteca existente e o comportamento de conquistas/progresso.

## Fontes primárias

- [Algoritmo de streaming do libtorrent](https://www.libtorrent.org/streaming.html): deadlines coordenam trechos urgentes; modo sequencial sozinho não equivale a streaming adaptado à reprodução.
- [API de deadlines, prioridades e leitura de peças](https://libtorrent.org/reference-Torrent_Handle.html).
- [Código e integração do MonoTorrent](https://github.com/alanmcgovern/monotorrent).
- [Distribuição libtorrent 2.1.1](https://pypi.org/project/libtorrent/2.1.1/), instalada somente na bancada com SHA-256 fixado.
