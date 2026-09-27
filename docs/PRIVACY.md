# Privacidade e dados locais

## Resumo

O AniT é local-first. Não existe conta AniT, servidor próprio ou sincronização obrigatória. Sua biblioteca é mantida no computador onde o aplicativo é executado.

## Dados armazenados

O banco SQLite pode conter:

- títulos e aliases;
- pastas configuradas e caminhos de arquivos;
- temporadas, episódios, versões e disponibilidade;
- posição e status de reprodução;
- avaliações e comentários pessoais;
- referências a capas e metadados públicos;
- itens pendentes de revisão.

Arquivos padrão:

```text
%LOCALAPPDATA%\AniT\Data\anit.db
%LOCALAPPDATA%\AniT\Data\*.log
%LOCALAPPDATA%\AniT\Covers\*.jpg
```

## Acesso à rede

O AniT acessa a internet somente para recursos habilitados pelo usuário. Dependendo das configurações, um termo derivado do título do anime pode ser enviado diretamente aos seguintes provedores públicos:

- AniList, para títulos, aliases, sinopse, gêneros, estúdio, notas e artes;
- Jikan/MyAnimeList e Kitsu, para metadados e artes oficiais;
- Wallhaven, Danbooru, Safebooru e Gelbooru, para pesquisa de artes;
- MyMemory, para tradução opcional de sinopses;
- fontes HTTPS personalizadas cadastradas pelo próprio usuário.

Essas requisições revelam ao provedor o endereço IP normal da conexão, o termo pesquisado e informações técnicas usuais de uma requisição HTTP. O AniT não controla a infraestrutura nem as políticas desses serviços. Fontes personalizadas são limitadas a HTTPS público; endereços locais, loopback e redes reservadas são bloqueados.

O modo totalmente offline impede essas consultas. Capas e metadados já presentes em cache continuam disponíveis.

## O que não é enviado pelo AniT

O aplicativo não possui backend próprio e não envia intencionalmente:

- arquivos de vídeo;
- progresso ou avaliações pessoais;
- banco SQLite;
- lista completa de caminhos locais;
- logs de diagnóstico.

Notificações do Windows são produzidas localmente. O AniT não utiliza um servidor de push próprio.

Issues no GitHub são públicas. Revise logs e screenshots antes de anexar; eles podem conter nome de usuário, caminhos ou nomes de arquivos.

## Controle e exclusão

Você pode operar offline depois que os dados necessários estiverem em cache. Para excluir todos os dados do AniT, feche o aplicativo e remova `%LOCALAPPDATA%\AniT`. Isso não remove sua mídia original.

## Futuras integrações

Qualquer proposta de conta, nuvem ou telemetria deve ser opcional, documentada e discutida publicamente antes da implementação. Esta página deverá ser atualizada no mesmo pull request.
