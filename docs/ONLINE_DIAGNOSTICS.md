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
