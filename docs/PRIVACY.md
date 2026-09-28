# Privacidade e dados locais

## Resumo

O AniT é local-first. Não existe conta AniT, servidor próprio, telemetria obrigatória ou sincronização automática em nuvem. Biblioteca, perfis, histórico e preferências permanecem no computador.

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

## O que não é enviado ao AniT

O projeto não possui backend e não recebe intencionalmente:

- vídeos ou legendas;
- biblioteca, banco ou backup;
- progresso, avaliações, PIN ou lista de perfis;
- caminhos locais e logs;
- notificações do Windows.

Issues no GitHub são públicas. Revise anexos: screenshots, logs, banco, `profiles.json` e backups podem conter dados pessoais.

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
