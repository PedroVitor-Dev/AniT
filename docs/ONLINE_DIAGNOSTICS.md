# Etapa 1 — medições do Online Anime Player

Esta etapa adiciona observabilidade; não troca motor, player, prioridades, timeouts, cache ou configurações de decodificação. Logs são locais, não há envio automático.

## Coleta

Cada tentativa de abertura Cloud cria uma sessão com relógio monotônico, desde o clique aceito: identificação do episódio, criação do serviço, restauração, seleção, início do motor, primeiro peer/dado observado, cabeçalho verificado, sondagem do MKV, primeira peça de mídia, HTTP pronto, processo/IPC, contêiner e primeiro sinal de frame.

Os sinais de rede são amostrados a cada 500 ms; primeiro peer e primeiro dado têm essa resolução aproximada. Marcos podem se sobrepor e não devem ser somados como se fossem etapas exclusivas. Cache existente pode permitir abrir sem peer/dado novo: n/d nesses campos não é falha.

No mpv, o primeiro sinal de frame exige saída de vídeo configurada, `video-frame-info/picture-type` disponível e `playback-restart`. É uma aproximação do primeiro frame, não uma medição física do monitor. Ausência desse sinal é registrada como indisponível, nunca substituída pelo horário de abertura da janela. Propriedades opcionais sem suporte não interrompem a reprodução. Referência: https://mpv.io/manual/stable/.

Durante o vídeo: cache em segundos, posição, pausa, buffering, busca, frames descartados na saída/decodificador e desvio A/V. Amostras limitadas a uma por segundo por origem. Buffer inicial, busca e pausas manuais não são contados como engasgos comuns; um engasgo aberto ao fechar entra no resumo. Reabertura do índice gera evento próprio. Os números de frames descartados são contadores do player e podem reiniciar em reaberturas do arquivo.

## Arquivos

`%LOCALAPPDATA%/AniT/Diagnostics/Online/online-trace-*.jsonl`

Fila de até 2048 eventos, escrita assíncrona e rotação de aproximadamente 8 MiB por arquivo; até dez arquivos. Falha no disco ou fila cheia não bloqueia o vídeo. Eventos podem ser perdidos em encerramento abrupto/falha de escrita; sessões sem resumo ficam identificadas como incompletas. `OnlineTraceJournal.DroppedEvents` e `WriteFailed` permitem inspecionar a saúde do coletor.

Somente identificador aleatório de sessão, sequência, horários, nomes fixos de etapas e medidas numéricas. Não inclui magnet, URL do streaming, hash do torrent, título, caminho de arquivo ou IP de peers.

Gerar relatório:

```powershell
python scripts/Report-OnlinePlayback.py CAMINHO_DO_LOG.jsonl --output artifacts/diagnostico-online.md
```

## Comparação reproduzível

1. Usar o mesmo arquivo autorizado, faixa de áudio/legenda, posição inicial e velocidade. Registrar versão do AniT, tipo de conexão, horário e demais clientes torrent em execução.
2. Separar ensaios por processo recém-aberto/reutilizado e por percentual já verificado. Processo frio não significa arquivo sem cache.
3. Executar ao menos três aberturas por condição; comparar mediana e pior resultado. Não apagar os downloads reais do usuário para criar uma condição fria. Usar biblioteca/perfil de teste quando precisar de cache vazio.
4. Reproduzir por alguns minutos, pausar manualmente, buscar uma região disponível e outra ainda não baixada. Conferir tipos de buffer no relatório.
5. Comparar primeiro peer, primeiro dado, HTTP e primeiro sinal de frame; depois tempo acumulado de rebuffer e contadores de decodificação. Guardar logs incompletos/cancelados para não esconder tentativas ruins.
6. Só comparar motores nas etapas seguintes quando os dois estiverem disponíveis sob essas condições. A etapa 1 não fornece benchmark do libtorrent.

## Validação controlada

O teste `ActualTorrentStreamReadsVerifiedBytesBeforeDownloadCompletes` usa arquivos sintéticos, um seed em loopback e taxa limitada. Com `ANIT_TRACE_TEST_OUTPUT` definido, exporta `controlled-cold.jsonl` e `controlled-warm.jsonl`. Verifica os marcos até HTTP, leitura correta antes do fim do download e reaproveitamento com 100% em cache. Não mede DHT pública nem primeiro frame, pois esse teste não executa um player.

Os testes de relógio controlado verificam a separação de startup/busca/pausa/rebuffer, tempo antes da coleta, concorrência, cancelamento e falha do logger. Resultados sintéticos não demonstram a velocidade do magnet real na conexão do usuário.

Validação do gerador de relatório: `python tests/OnlineDiagnosticsReportTests.py` (biblioteca padrão, sem dependências externas).

## Etapa 2 — abertura sob demanda

A restauração agora lê somente `downloads.json` e normaliza os itens para aguardarem seleção. `SnapshotAsync` mostra o catálogo do banco sem criar managers para esses itens. Um magnet antigo indisponível não dispara busca de metadados no monitor nem bloqueia o play de outro torrent.

Somente `OpenStreamAsync` carrega o manager escolhido. Metadados locais previamente salvos são reutilizados. Os marcos `selected_manager_load_begin`/`selected_manager_loaded` separam essa etapa da leitura do catálogo no relatório. A verificação e recuperação do cache de vídeo continuam sob responsabilidade do motor ao abrir o episódio; a listagem inativa não revalida os arquivos parciais.

Serviço é obtido fora da thread da interface durante o preparo. O popup começa com uma etapa e relógio antes de haver conexões, sem apresentar ausência de peers como diagnóstico enquanto ainda está carregando o catálogo. As prioridades são alteradas apenas quando necessário e itens inativos não passam novamente por seleção de arquivos.

Cancelamento durante ativação limpa as prioridades, interrompe o manager (inclusive em inicialização) e volta ao estado de espera sem apagar arquivos. Falha/cancelamento de leitura do catálogo não deixa a restauração marcada como concluída. A listagem aguarda a operação de metadados apenas se ela pertencer ao torrent efetivamente escolhido; seu cancelamento segue o token da preparação.

Regressões: catálogo com 80 magnets sem peers não cria nenhum manager; abrir um arquivo de teste já em cache cria apenas o manager escolhido, ignorando outro magnet indisponível; cancelar na seleção e repetir preserva o arquivo e mantém um único manager; importar novamente um torrent listado é rejeitado sem iniciar download.

## Etapa 3: reconexão e descoberta

O marcador `recent_peers_loaded` indica a leitura das dicas locais antes da descoberta adicional. O cache usa apenas conexões de torrents públicos, separadas pelo conjunto de infohashes, com expiração de 24 h e limites de 64 torrents / 32 peers. Não cria managers ao listar a biblioteca e só injeta fontes ao abrir o episódio.

Até 16 trackers são consultados independentemente, com prazo de 45 s para permitir retransmissões UDP. A consulta roda em segundo plano: uma fonte disponível pode entregar o vídeo sem esperar pelos trackers lentos. As rodadas extras de anúncio foram removidas; o motor mantém os intervalos e tentativas automáticas. Cancelar a preparação ou fechar o player cancela as consultas suplementares.

O DHT mantém o cache nativo habilitado, mas não é inicializado antecipadamente: na API pública do MonoTorrent 3.0.2, `ClientEngine.Dht` só expõe estado/monitoramento. Antecipar seu início exigiria mudar a integração do motor. Não usamos reflexão nem iniciamos torrents fictícios para contornar isso.

Validação controlada: o teste de streaming HTTP usa um seed local autorizado e uma dica persistida, sem `AddPeerAsync` manual no manager. Verifica primeira transferência, cabeçalho verificado, abertura antes do download completo e integridade dos ranges. Isso comprova o caminho de reconexão; não mede disponibilidade ou tempo de primeiro quadro de um swarm público.

## Etapa 4 — comparação dos motores

A avaliação e a decisão estão em [ONLINE_ENGINE_DECISION.md](ONLINE_ENGINE_DECISION.md). A bancada é independente do aplicativo, usa rede em loopback e gera relatórios de abertura e busca de intervalos verificados. O motor de produção permanece MonoTorrent 3.0.2; nenhum resultado desse ensaio mede primeiro quadro ou descoberta em rede pública.

## Etapa 5 — leitura sob demanda, início e índices

O servidor HTTP mantém um cursor e uma demanda por resposta/preparo de índice. A demanda mais recente tem preferência para dados ausentes; um pedido novo interrompe a espera anterior e orienta o streaming picker. Leitores antigos podem entregar bytes disponíveis, restaurando depois a posição preferida. Ao terminar ou desconectar, a demanda libera a prioridade. Não há mais reinício periódico da leitura a cada 300 ms. O polling de disponibilidade não reposiciona leitores antigos. A desconexão cancela a espera e libera o pedido.

O índice MKV usa a mesma arbitragem, em blocos de até 64 KiB, sem segurar a leitura durante o índice inteiro nem o bloqueio de troca/fechamento do serviço. O player cancela o índice automático antes de uma busca explícita. Fechar a reprodução cancela e drena esses pedidos antes de descartar o stream. Índices fora do arquivo ou acima de 16 MiB são rejeitados.

O parser lê os cabeçalhos EBML necessários e um SeekHead limitado a 256 KiB, sem exigir antecipadamente um bloco inicial fixo desse tamanho. No streaming, os anexos de fontes já ocultados recebem payload virtual zerado, evitando solicitar seu conteúdo nessas leituras. Tamanho, offsets e pacotes de mídia são preservados; o arquivo salvo não muda. O motor ainda pode receber peças compartilhadas ou antecipadas no mesmo episódio. As fontes incorporadas permanecem disponíveis no arquivo completo.

A disponibilidade usa o último byte incluído no intervalo (`offset + count - 1`), evitando esperar a peça seguinte no limite exato. Os trechos virtuais não exigem suas peças físicas.

Sinais locais novos: `http_demand_started`/`index_demand_started` (ID e offset), `read_wait_started` (primeiro intervalo ausente da demanda), `read_preempted`/`read_resumed` (ID e espera em ms) e `index_demand_ready`. Não incluem nomes, magnet, URL, hash ou IP. Primeiro quadro e rebuffer continuam medidos separadamente no player.

### Validação controlada

```powershell
$env:ANIT_STREAMING_STEP5_OUTPUT = 'C:/caminho/para/saida'
dotnet test tests/AniT.Tests -c Release --filter FullyQualifiedName~RealTorrentNewestSeek
```

A fixture usa dois arquivos sintéticos de 8 MiB, peças de 2 MiB e relay TCP limitado a 1 MiB/s. Abre o segundo arquivo, mantém a resposta inicial ativa, busca uma região ausente em 75% e solicita 25% enquanto a busca anterior espera. Confere 64 KiB byte a byte contra a origem e verifica que o outro episódio continua desmarcado e sem peças verificadas. Exporta `seek-during-buffering.jsonl` e `seek-result.json` quando a variável está definida.

No ensaio local de 29/10/2026, a nova busca recebeu 64 KiB corretos em aproximadamente 2,48 s, com 50% do arquivo verificado, uma preempção e zero peças verificadas do outro episódio. É uma fixture com fonte conhecida, sem player/decodificação: não mede primeiro quadro nem comprova ganho no magnet real.

As regressões também cobrem leitura longa sem reinícios, desconexão durante espera, vídeo pedido durante preparo do índice, fechamento com índice travado, anexos que não podem ser lidos e limites exatos de peças. O teste IPC verifica que a busca explícita cancela o índice em segundo plano sem esperar seus dados.


## Etapa 6 — buffer adaptativo em segundos

O Online Anime Player agora estima a reserva contínua na posição atual do player. Usa `demuxer-cache-state`, os intervalos conectados de `seekable-ranges`, os bytes de pacotes à frente e a taxa de entrada do demuxer. Trechos separados já baixados não contam como reserva imediatamente reproduzível. A duração estimada limita o intervalo quando ambos estão disponíveis; dados inválidos ou antigos são descartados. A duração escalar recente serve de alternativa quando o estado detalhado não existe.

A política compara a entrada do player ao consumo estimado e suaviza as oscilações. Falta de dados e engasgos aumentam a proteção; recuperação sustentada a reduz aos poucos. Um cache cheio/ocioso com taxa zero não é tratado como falha de conexão. Contadores de transições registram também engasgos menores que o intervalo de medição, sem contar uma espera longa repetidamente.

- Abertura: preserva `cache-pause-initial=no`, com parâmetros iniciais pequenos; não exige baixar uma quantidade nova fixa antes do primeiro quadro.
- Retomada: reserva baseada em 1–8 segundos de reprodução, ajustada à velocidade, limitada a 0,25–16 segundos de mídia.
- Antecipação: 10–30 segundos de reprodução, ajustados à velocidade e limitados a 8–60 segundos de mídia. Define `cache-secs` e `demuxer-readahead-secs` juntos.
- Memória: cache à frente entre 32 e 96 MiB, em passos de 8 MiB, conforme bitrate estimado. Cache passado limitado a 8 MiB. São limites do cache de pacotes; decodificador e outras estruturas têm consumo próprio.
- Busca/troca de arquivo: limpa observações anteriores e reinicia a proteção; o processo reutilizado recebe novamente os valores iniciais. Fim do arquivo não precisa atingir uma reserva maior do que o restante disponível.
- Pausa e velocidade: o controlador adapta somente opções de cache; não envia comandos de pausa, retomada ou mudança de velocidade. O buffering automático permanece sob responsabilidade do mpv.
- Índice automático: só começa com reserva suficiente. Se essa reserva cai ou há buffering, cancela a leitura especulativa antes de uma recarga do vídeo; uma nova tentativa pode ocorrer após estabilizar. A busca explícita continua tendo prioridade.

As observações essenciais são ativadas mesmo sem sessão de diagnóstico. O controlador roda fora do leitor de respostas IPC, com mudanças limitadas por diferenças mínimas; uma opção recusada deixa de ser tentada naquele arquivo. Ausência de métricas mantém uma configuração conservadora, sem apresentar valores desconhecidos como zero medido.

O [manual do mpv](https://mpv.io/manual/stable/) descreve as estimativas de cache, os limites de memória e a relação entre `cache-secs` e `demuxer-readahead-secs`: ativar cache pode fazer o primeiro prevalecer. As opções foram conferidas no binário distribuído e verificadas por leitura após alteração via IPC.

### Métricas e testes

O relatório local inclui as amostras `buffer_policy`: reserva contínua observada, reserva para retomar, antecipação desejada, limite de memória, relação entrada/consumo, contagem de engasgos e permissão para índice. `buffer_option_applied` registra opção numérica/valor; `buffer_option_unavailable` informa falha opcional, sem dados da mídia ou da conexão.

A política possui testes com relógio controlado para entrada lenta, oscilações, recuperação gradual, pausa manual, buffering prolongado, pulsos curtos, busca, fim do arquivo, velocidade, dados ausentes e limites de memória. Os testes IPC verificam as opções enviadas ao player sem diagnósticos, fallback de métricas, cancelamento de índice especulativo, isolamento após busca e interrupção das tarefas.

A ferramenta [OnlineBufferProbe](../tools/OnlineBufferProbe/README.md) reproduz uma fixture audiovisual própria no mpv headless e no servidor HTTP real. Aplica lentidão/interrupção/recuperação na fonte, coleta 50 amostras e verifica que o controlador não desfaz uma pausa manual. Não usa a biblioteca nem um torrent público. `vo=null`/`ao=null` evitam abrir janela ou tocar áudio; o sinal de primeiro frame continua sendo a aproximação IPC, não medição da tela.

Esse ensaio não compara versões nem garante fluidez no magnet real. Validação de rede pública e da interface final permanece parte das próximas etapas.

Na rodada controlada de 29/10/2026 foram coletadas 50 amostras: a reserva de retomada variou entre 1 e 8 s, houve uma espera por dados de aproximadamente 3,39 s seguida de recuperação e a pausa manual foi preservada. O [resumo numérico](benchmarks/online-buffer-step-6/native-result.json) e a [verificação das opções nativas](benchmarks/online-buffer-step-6/runtime-options.json) estão registrados no repositório. A fixture e a saída nula não substituem um teste com torrent público e renderização normal.
