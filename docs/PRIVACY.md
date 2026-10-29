# Privacidade e dados locais

## Resumo

O AniT é local-first. Não existe conta AniT, servidor próprio, envio automático de telemetria ao projeto ou sincronização automática em nuvem. Biblioteca, perfis, histórico e preferências permanecem no computador.

## Dados armazenados

O SQLite e os arquivos de configuração podem conter:

- títulos, aliases, gêneros, estúdio e metadados públicos;
- pastas, caminhos de arquivos, temporadas, episódios, versões e disponibilidade;
- posição, sessões, status e conclusão de reprodução;
- favoritos, tags, coleções, avaliações e comentários;
- conquistas, AniPoints, notificações e preferências;
- perfis locais, avatar, capa, nome, frase, título e opções de privacidade;
- itens pendentes de revisão e vínculos manuais.

Locais principais:

```text
%LOCALAPPDATA%\AniT\Data\anit.db
%LOCALAPPDATA%\AniT\Data\Profiles\<id>\anit.db
%LOCALAPPDATA%\AniT\Data\profiles.json
%LOCALAPPDATA%\AniT\Data\system-settings.json
%LOCALAPPDATA%\AniT\Data\Profile\
%LOCALAPPDATA%\AniT\Covers\
%LOCALAPPDATA%\AniT\Cache\
```

Arquivos `.bak`, logs e arquivos `-wal`/`-shm` podem existir durante recuperação e uso normal do SQLite.

Os diagnósticos estruturados do Online Anime Player ficam em `%LOCALAPPDATA%/AniT/Diagnostics/Online/`. Registram horários, etapas, contagem de conexões, velocidade, cache e sinais do player, com identificador aleatório por tentativa. Esses registros não incluem magnet, URL, título, caminho do vídeo, hash do torrent ou endereços dos peers. A retenção é limitada a dez arquivos de aproximadamente 8 MiB; não há envio automático. Os logs tradicionais do aplicativo podem conter outras informações técnicas: revise qualquer arquivo antes de compartilhá-lo. Consulte [Diagnóstico do streaming](ONLINE_DIAGNOSTICS.md).

## PIN dos perfis

O PIN opcional é armazenado como derivação PBKDF2 com salt, não como texto puro. Ele limita o acesso casual pela interface, mas não criptografa o banco, imagens ou backups. Uma pessoa com acesso ao usuário do Windows e aos arquivos locais pode copiá-los; use os controles de sessão e disco do Windows para proteção forte.

## Acesso à rede

Conforme as opções habilitadas, termos derivados do título podem ser enviados diretamente a:

- AniList;
- Jikan/MyAnimeList e Kitsu;
- Wallhaven, Danbooru, Safebooru e Gelbooru;
- MyMemory para tradução opcional;
- fontes HTTPS personalizadas cadastradas pelo usuário.

O provedor recebe o IP normal da conexão, o termo e dados técnicos usuais de HTTP. O AniT não controla suas políticas. Fontes personalizadas aceitam somente HTTPS público; loopback, redes locais e endereços reservados são bloqueados. O modo totalmente offline impede consultas integradas.

## Movie Theater e voz opcionais

Ao entrar em uma sala, os participantes compartilham endereço de rede, nome de exibição do perfil, identificador temporário, estados de preparação e reprodução. O arquivo selecionado só é enviado aos convidados quando o host autoriza o compartilhamento. No P2P, a cópia completa permanece no destino escolhido pelo convidado; no streaming, os trechos são mantidos em cache temporário.

O chat de voz exige entrar na voz e ativar o microfone separadamente. Ao ativar, áudio do microfone é enviado ao PC do host, que o retransmite aos demais participantes conectados à voz, usando TLS. Não é criptografia ponta a ponta entre convidados: o host intermedeia o áudio. O AniT não grava a conversa nem envia áudio a servidores do projeto. Silenciar, desativar áudio, sair da voz ou sair da sala interrompe a transmissão; o host pode bloquear o microfone ou expulsar participantes. A captura utiliza o dispositivo padrão do Windows e não captura o som do episódio. Compartilhe convites apenas com pessoas de confiança.

## O que não é enviado ao AniT

O projeto não possui backend e não recebe intencionalmente:

- vídeos ou legendas;
- biblioteca, banco ou backup;
- progresso, avaliações, PIN ou lista de perfis;
- caminhos locais e logs;
- notificações do Windows.

Issues no GitHub são públicas. Revise anexos: screenshots, logs, banco, `profiles.json` e backups podem conter dados pessoais.

## Online Anime Player · Beta

Quando você adiciona uma fonte torrent autorizada, o AniT entra na rede BitTorrent. Seu endereço IP e identificadores do torrent podem ser vistos pelos participantes, trackers e serviços DHT usados pela fonte. Durante o download, partes do conteúdo podem ser enviadas a outros participantes. Não há anonimização automática nem servidor AniT intermediando o tráfego.

Fontes, metadados e informações de retomada ficam em `%LOCALAPPDATA%\AniT\Online\<perfil>`. Os episódios ficam na pasta escolhida pelo usuário, em `AniT Online/<id>`. A tag Cloud significa download em andamento, não armazenamento em nuvem. Pausar ou encerrar o download interrompe esse tráfego; arquivos completos permanecem no computador.

## Backups e exportações

O `.anitbackup` pode conter praticamente todos os dados pessoais acima e caches de perfil, mas não vídeos. Ele é criado localmente; salvar em pendrive ou nuvem é uma escolha do usuário. Trate-o como arquivo privado.

JSON e CSV são exportações legíveis e podem expor histórico e avaliações. CSVs exportados são protegidos contra interpretação de fórmulas, mas ainda devem ser armazenados com cuidado.

## Controle e exclusão

- Oculte histórico, notas ou favoritos nas preferências do perfil.
- Limpe o histórico sem remover a Biblioteca pela interface.
- Exporte seus dados pessoais ou um backup completo.
- Exclua um perfil local pela tela de perfis.
- Para remover tudo, desinstale o app, feche-o e apague `%LOCALAPPDATA%\AniT`.

Apagar dados do AniT não remove seus vídeos originais.

## Futuras integrações

Conta, sincronização, atualização automática ou telemetria deverão ser opcionais, documentadas e discutidas publicamente antes de qualquer implementação.

### Cache local de fontes recentes

O Online Anime Player guarda, na pasta do perfil, `Online/<identificador-do-perfil>/recent-peers.json` com endereços IP, portas, identificadores de torrents públicos e a data da conexão. Esses dados servem exclusivamente para tentar reconectar fontes já utilizadas. O cache permanece neste computador, expira em 24 horas e limita-se a 64 torrents e 32 fontes por torrent. Torrents privados não são registrados nem recebem fontes desse cache. Apagar esse arquivo remove as dicas salvas; conexões futuras podem gerar novas entradas. Esse cache é separado dos logs de diagnóstico, que continuam sem endereços de peers ou hashes de torrents.
