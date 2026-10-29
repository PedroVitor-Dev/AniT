# Online Anime Player · Beta

O menu do AniT abre uma janela dedicada para magnet links, URLs HTTP/HTTPS de arquivos `.torrent` e arquivos `.torrent` locais. Não há catálogo, busca ou fonte de conteúdo fornecida pelo AniT.

## Uso

1. Leia o aviso e confirme que possui autorização para baixar e compartilhar o material.
2. Escolha **Arquivo .torrent** (arquivo local ou URL HTTP/HTTPS) ou **Link magnético**. Selecionar um arquivo inicia a importação; para links, cole e clique em **Adicionar à biblioteca**.
3. O destino é a primeira pasta habilitada e disponível da biblioteca, usada como pasta base. O seletor aparece somente quando não existe pasta disponível; a escolha é salva para as próximas importações.
4. Um popup mostra leitura da fonte, organização e identificação via integrações existentes. O nome vem do parser e do AniList, sem campo manual; a capa e os metadados são salvos antes da conclusão quando o serviço responde. Depois do cadastro, **Concluir agora** permite encerrar a busca de arte sem perder os episódios.
5. Ao exibir **Carregado!**, o importador fecha e retorna ao Início atualizado. Abra os episódios pela Home ou Biblioteca; somente o episódio aberto inicia o download.

**Cloud** indica um arquivo ainda em download, não armazenamento remoto. A tag aparece nos cartões de início, na biblioteca, nos episódios e nos downloads. Ao concluir e verificar o arquivo, ele passa a ser reproduzido do disco, preservando o mesmo ID de episódio e seu progresso.

Adicionar a lista obtém apenas metadados, sem selecionar vídeos. Abrir um episódio inicia somente esse vídeo e suas legendas correspondentes; trocar de episódio pausa o anterior. A preparação mostra velocidade, fontes, dados recebidos e tempo decorrido. Fechar o player pausa o download sem apagar dados. Reabrir o AniT aguarda nova escolha, inclusive para listas antigas. Não há download automático do próximo episódio.

Fechar apenas a janela de gerenciamento mantém o episódio ativo. A tela de importação não exibe uma segunda lista de reprodução nem controles de transferência. A troca de perfil encerra o motor anterior.

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

O ensaio nativo agora compara a porcentagem do arquivo selecionado, não a do torrent inteiro, e limita inclusive a primeira rajada por relay TCP. `ANIT_TEST_STARTUP_ONLY=1` limita o ensaio ao início/retomada parcial. Busca durante buffering passou após a correção assíncrona do envio ao MPC-HC. Peças de fronteira podem incluir bytes de arquivos vizinhos sem selecioná-los para download.

Resultado do ensaio sob demanda: MPC-HC retomou em 69,435 segundos e avançou até 70,688 segundos com apenas 4,2% do episódio selecionado verificado; os demais arquivos permaneceram sem seleção. Mídia sintética HEVC/Opus, rede limitada no relay.

## Busca durante buffering corrigida (28/10/2026)

O comando de busca do MPC-HC pode aguardar leituras de rede dentro do processamento de `WM_COPYDATA`. O prazo antigo de 1,5 segundo gerava falha mesmo quando o player ainda estava buscando dados. O AniT agora envia buscas fora da thread da janela de notificações, serializa os envios e aguarda até 45 segundos. A interface continua processando mensagens e o cancelamento encerra a espera do chamador; um comando já enviado não é repetido. A memória de `COPYDATA` permanece pertencendo ao worker até o retorno nativo.

Ensaio nativo com MKV HEVC/Opus sintético de 210 segundos: início com 5,0% do episódio verificado; pausa, avanço para 150 segundos com rede limitada e lentidão adicional, retorno a 12 segundos ainda com apenas 7,5% do arquivo, conclusão do download e nova busca para 20 segundos. O avanço demorou 4,6 segundos e a UI processou 71 intervalos de verificação enquanto o envio aguardava. Esse tempo é resultado local de teste, não previsão para fontes da internet.

336 testes automatizados, compilação Release sem avisos/erros e ensaios nativos do Movie Theater (P2P e streaming, incluindo recuperação de mídia interrompida) passaram. O ensaio ampliado usa os mesmos comandos `--check-online-player --mkv --resume` com um MKV sintético de pelo menos 90 segundos; a posição de avanço se adapta à duração. Não defina `ANIT_TEST_STARTUP_ONLY` ao validar buscas.

## Prioridade no começo do episódio

A preparação padrão do MonoTorrent 3.0.2 lia primeiro uma posição próxima ao final e só depois o começo. O AniT agora cria o stream sem esse prebuffer, posiciona explicitamente o seletor no começo e aguarda apenas a primeira leitura verificada antes de liberar o endpoint ao player. Índices e outras regiões são pedidos pelo contêiner via HTTP Range. Isso remove uma dependência desnecessária da preparação, sem ignorar hashes ou garantir início imediato quando faltam fontes.

Regressão com peças de 4 MiB: o peer sintético possui as primeiras peças, mas não as duas últimas. A preparação e a resposta HTTP do começo passam mesmo com o final indisponível; o fluxo antigo ficaria esperando por ele. 337 testes e 28 cenários de UI passaram; o ensaio nativo de MKV também passou, incluindo buscas sob rede limitada. O DHT preserva seu cache entre sessões. O magnet real foi inspecionado apenas nos metadados (peças de 4 MiB); a reprodução dessa fonte depende do novo teste do usuário.

## Retomada no fim e tela preta

O diagnóstico do teste real encontrou posição salva em 1421,056 segundos para um episódio com duração de 1421,056 segundos. A preparação levou 39 segundos para verificar o primeiro bloco, e o player informou o contêiner carregado 21 segundos depois, mas recebeu o comando para ir ao fim. Portanto abrir o player não validava reprodução. O início do arquivo já em cache decodificou 24 quadros em diagnóstico local com FFmpeg, sem baixar mais conteúdo ou alterar o arquivo.

O app descarta para retomada posições nos últimos dois segundos (com limite proporcional para clipes curtos), sem apagar o histórico. O bridge confere novamente a posição após NOWPLAYING, quando conhece a duração, e aplica somente uma busca inicial. O ensaio de MKV usa `ANIT_TEST_RESUME_AT_END=1` e `ANIT_TEST_END_POSITION_SECONDS=210` para conferir que um ponto salvo exatamente no fim do arquivo sintético de 210 segundos volta ao começo e avança de verdade. A retomada intermediária em 69,435 segundos tem ensaio separado. A velocidade de um torrent real ainda depende da rede, das fontes e das regiões exigidas pelo contêiner; esse resultado não comprova início instantâneo da fonte testada pelo usuário.

## Motor de streaming e MKV com anexos grandes (28/10/2026)

O caso real não estava corrigido apenas com a validação do checkpoint. O segundo episódio também saltava para o final sem retomada. A inspeção do primeiro arquivo encontrou **42.473.057 bytes de anexos antes do primeiro cluster de vídeo**, que começa no offset 42.563.264. Os 4 MiB iniciais não continham esse primeiro trecho de imagem. O MPC-HC ainda aguardava consultas ao final do contêiner durante a abertura.

O Beta agora usa um processo mpv dedicado com JSON IPC, cache limitado, espera de rede e controles nativos. Arquivos locais completos e Movie Theater continuam no MPC-HC. O fluxo preserva os eventos e IDs usados pelo histórico, Baki-Pi e conquistas. Fechar o player cancela esperas de índice e pausa a transferência.

Para MKV/WebM em download, uma vista HTTP transforma cabeçalhos de anexos e atalhos para metadados opcionais em elementos EBML Void **do mesmo comprimento**. O arquivo salvo e suas peças verificadas não são modificados. Offsets, vídeo, áudio, legendas, duração e capítulos são preservados; fontes incorporadas ficam temporariamente ocultas, então as legendas usam fontes do sistema. O arquivo completo é reproduzido no formato original, com suas fontes. A preparação verifica o cabeçalho e o primeiro trecho de imagem, sem baixar dezenas de MB de fontes.

O índice de busca é obtido e verificado quando há retomada ou avanço. A referência original ao índice é então reativada na vista HTTP, e o player recarrega a posição com o índice correto. Isso evita tanto a leitura de metadados no fim durante toda abertura quanto buscas que varriam o vídeo inteiro por falta de índice.

A entrega HTTP mantém respostas independentes, mas uma resposta antiga sem dados não disputa mais a prioridade com uma solicitação nova do contêiner. Esperas por peças não encerram mais a resposta após 90 segundos como se o vídeo tivesse acabado. Trackers declarados pelo próprio torrent público são consultados com concorrência limitada e respeitando os intervalos; torrents privados mantêm sua política original.

**Evidência:** nos mesmos trechos reais já em cache, o controle integrado mpv chegou a 1,21 segundo de reprodução em 1,65 segundo e informou saída de vídeo HEVC de 1920×1080 em 10 bits. O teste completo pelo serviço MonoTorrent também avançou o vídeo real. O MPC-HC demorava cerca de 23 segundos nesse ensaio. Isso isola o custo da abertura, **não promete esse tempo para um download frio**: uma medição separada da fonte real recebeu aproximadamente 3 MB em 50 segundos. A disponibilidade da fonte continua sendo uma limitação mensurável.

Validação nativa com mídia sintética: início parcial, retomada em 69,435 segundos, pausa, avanço para 150 segundos sob rede limitada, retorno a 12 segundos, conclusão do arquivo, fechamento com checkpoint confirmado e reabertura do player. Para repetir:

```powershell
./scripts/Get-Mpv.ps1 -Destination Player/MPV
$env:ANIT_TEST_MKV = 'C:\testes\video-sintetico-hevc-opus.mkv'
$env:ANIT_TEST_USE_MPV = '1'
dotnet run --project tests/AniT.UiChecks -- --check-online-player --mkv --resume
```

Os binários do mpv ficam fora do Git e são incluídos na publicação pelo projeto do app. O instalador verifica a presença do motor e das licenças; nenhuma associação de arquivos ou configuração global do Windows é modificada.

O índice também é preparado em segundo plano quando há ao menos cinco segundos de mídia em cache. O cursor e as faixas escolhidas são preservados ao ativá-lo, permitindo que os controles nativos do mpv usem as buscas precisas. Trocar de episódio cancela essa preparação, e eventos antigos de reprodução não são aplicados ao novo episódio. O ensaio nativo final validou a busca enviada diretamente ao mpv, sem passar pelo método de busca do AniT.

Verificação final: 348 testes automatizados; cobertura de linhas de 72,09% (acima do piso de 70%); 28 cenários visuais; reprodução real em cache pelo serviço; fluxo de player nativo com abertura parcial, checkpoint no fim, controles nativos, fechamento e reabertura.

## Fluxo de importação simplificado

O consentimento usa o estilo de checkbox do AniT, com fundo e texto vinculados ao tema. O ajuste automático de contraste não reinterpreta seu texto contra um fundo branco inexistente. A interface não solicita nome, pasta ou gestão de episódios em cada uso.

A importação limpa nomes técnicos, ordena temporada e número, conserva nomes alternativos ao adotar o título identificado e reutiliza animes existentes. Episódios sem título descritivo recebem “Episódio 01”, preservando o nome físico do arquivo. A integração de metadados registra aliases explicitamente como novos objetos, corrigindo uma falha de gravação encontrada no ensaio.

Verificação: 349 testes automatizados, 48 layouts em claro/escuro e fonte 100%/140%, e `--check-online-import` com HTTP simulado para AniList/capa. O ensaio confere nome canônico, capa salva, cadastro sem vídeo baixando, conclusão sem arte após cancelamento e importações posteriores sem duplicar o anime. O popup respeita redução de movimento. Falhas de rede mantêm o nome reconhecido e a lista utilizável; capas podem ser completadas depois.
