# Online Anime Player · Beta

O menu do AniT abre uma janela dedicada para magnet links, URLs HTTP/HTTPS de arquivos `.torrent` e arquivos `.torrent` locais. Não há catálogo, busca ou fonte de conteúdo fornecida pelo AniT.

## Uso

1. Leia o aviso e confirme que tem autorização para baixar e compartilhar o material. A tela explica que BitTorrent também envia partes do conteúdo para outros participantes.
2. Informe a fonte, opcionalmente o nome do anime e a pasta de destino.
3. Clique em **Adicionar lista à biblioteca**. Magnet links precisam obter metadados dos participantes; a preparação pode ser cancelada e tem limite de dois minutos.
4. Confira os episódios e clique em **Assistir em streaming**. Uma janela de preparação permite cancelar a espera pelos trechos iniciais e pelo índice do vídeo.
5. A reprodução usa o MPC-HC integrado e o fluxo normal de histórico, progresso, Baki-Pi e conquistas. O aplicativo padrão do Windows continua disponível para arquivos completos; arquivos incompletos exigem o MPC-HC integrado.

**Cloud** indica um arquivo ainda em download, não armazenamento remoto. A tag aparece nos cartões de início, na biblioteca, nos episódios e nos downloads. Ao concluir e verificar o arquivo, ele passa a ser reproduzido do disco, preservando o mesmo ID de episódio e seu progresso.

Adicionar a lista obtém apenas metadados, sem selecionar vídeos. Abrir um episódio inicia somente esse vídeo e suas legendas correspondentes; trocar de episódio pausa o anterior. A preparação mostra velocidade, fontes, dados recebidos e tempo decorrido. Fechar o player pausa o download sem apagar dados. Reabrir o AniT aguarda nova escolha, inclusive para listas antigas. Não há download automático do próximo episódio.

Fechar apenas a janela de gerenciamento mantém o episódio ativo. O botão de pausa interrompe o download selecionado. A troca de perfil encerra o motor anterior.

## Implementação

- MonoTorrent 3.0.2, versão estável, com notificações de licença distribuídas em `Licenses/Online`. Documentação e código: https://github.com/alanmcgovern/monotorrent.
- Manifestos por perfil em `Online/<profile-id>/downloads.json`, gravados por substituição atômica. Metadados e fast resume ficam no cache do motor.
- Cada torrent possui uma pasta exclusiva em `<destino>/AniT Online/<job-id>`. Caminhos são validados; executáveis e outros arquivos sem relação com vídeo/legendas não são selecionados.
- O motor valida as peças do torrent. A fonte de reprodução é um stream seekable que prioriza a posição solicitada. O endpoint HTTP de vídeo escuta apenas em `127.0.0.1`, com porta dinâmica, rota aleatória e suporte a HEAD e ranges simples. Não depende de ACL de HttpListener, servidor central ou privilégios de administrador.
- Até oito conexões HTTP locais; leitura em blocos de 64 KiB; as solicitações de vídeo e índice mantêm respostas independentes. Uma leitura ainda sem peças verificadas cede a vez após 300 ms; conexões abandonadas pelo player são encerradas sem cancelar as demais.
- Raízes gerenciadas ficam fora da varredura e reorganização comuns, evitando duplicar ou mover arquivos vinculados ao motor. Os IDs de episódios continuam sendo os mesmos usados pelo restante do AniT.
- O motor pode compartilhar peças enquanto baixa. A verificação periódica interrompe o torrent quando todos os vídeos e legendas selecionados foram concluídos; não há seeding contínuo automático.

## Limites do beta

A velocidade depende da conexão e de participantes com os arquivos disponíveis. Sem fontes, o AniT informa que está aguardando participantes. Buscar uma região ainda não baixada pode exigir novo buffering. Não há promessa de início imediato.

A importação considera todos os vídeos do torrent como parte da temporada informada. O parser existente identifica temporadas e números de episódio; arquivos sem numeração recebem a ordem dos nomes. Confira os nomes e evite adicionar coleções mistas de séries e extras sem revisar. A seleção acompanha o episódio aberto. Não há transcodificação, navegador web ou integração com Movie Theater nesta versão.

Os downloads são locais e ocupam espaço no destino escolhido. Os arquivos e metadados do motor precisam permanecer nessas pastas para a retomada funcionar. O backup do catálogo não é um backup dos vídeos baixados.

## Verificação

- `dotnet test tests/AniT.Tests`: importação em lote, identidade de episódio, preservação de avaliações/progresso, retomada, validação de caminhos, ranges e transferência real entre dois motores locais com dados sintéticos.
- `dotnet run --project tests/AniT.UiChecks -- --check-online`: claro/escuro, texto 100%/140%, três tamanhos de janela, aviso inicial e lista de episódios.
- `dotnet run --project tests/AniT.UiChecks -- --check-online-player`: AVI gerado pelo próprio teste, dois clientes locais e MPC-HC real para reprodução durante download, pausa, busca e retomada.

Os testes não utilizam mídias pessoais nem torrents de terceiros.

Validação de 28/10/2026: 326 testes passaram; 24 cenários de layout passaram. O MPC-HC reproduziu o AVI sintético com aproximadamente 24% baixado, pausou, buscou 12 segundos e continuou em 20 segundos após a conclusão do download. Isso verifica o fluxo local, não estima desempenho de torrents na internet.

## Regressão de MKV e progresso (28/10/2026)

A leitura do índice de um contêiner pode ocorrer enquanto a resposta principal aguarda dados. Um teste reproduz a falha antiga: a segunda solicitação cortava o corpo da primeira antes do `Content-Length` anunciado. A resposta principal agora permanece aberta e as leituras compartilham o stream verificado em turnos limitados.

O AniT salva apenas posições confirmadas pelo MPC-HC, inclusive o zero real ao voltar ao início. Uma consulta sem resposta não vira tempo assistido. No streaming, EOF só conclui automaticamente o episódio se houver posição confirmada próxima do final; uma posição desconhecida ao fechar não apaga a retomada.

Para repetir a validação ampliada, gere um MKV de teste com HEVC e Opus (por exemplo, com `testsrc2` e `sine` do FFmpeg). O arquivo é copiado para três episódios sintéticos; nenhum vídeo pessoal ou fonte externa é necessário:

```powershell
$env:ANIT_TEST_MKV = 'C:\testes\video-sintetico-hevc-opus.mkv'
dotnet run --project tests/AniT.UiChecks -- --check-online-player --mkv --resume
dotnet run --project tests/AniT.UiChecks -- --check-playback-evidence
```

O MKV deve ter ao menos 90 segundos para o cenário de retomada em 69,435 s. O FFmpeg e a mídia de teste não são distribuídos no AniT. O teste falha se `--mkv` for solicitado sem um arquivo de teste válido.

## Validação do download sob demanda

336 testes automatizados passaram; a UI foi verificada em 28 cenários. Um diagnóstico do magnet informado consultou apenas metadados: 12 episódios, nenhum arquivo selecionado e zero bytes de vídeo recebidos. Isso não valida a reprodução daquela fonte.

O ensaio nativo agora compara a porcentagem do arquivo selecionado, não a do torrent inteiro, e limita inclusive a primeira rajada por relay TCP. `ANIT_TEST_STARTUP_ONLY=1` limita o ensaio ao início/retomada parcial. Busca durante buffering apresentou timeout no MPC-HC e permanece uma limitação conhecida. Peças de fronteira podem incluir bytes de arquivos vizinhos sem selecioná-los para download.

Resultado do ensaio sob demanda: MPC-HC retomou em 69,435 segundos e avançou até 70,688 segundos com apenas 4,2% do episódio selecionado verificado; os demais arquivos permaneceram sem seleção. Mídia sintética HEVC/Opus, rede limitada no relay.
