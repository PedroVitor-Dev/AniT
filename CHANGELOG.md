# Changelog

Todas as mudanças relevantes serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto pretende adotar [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Documentação

- Central de documentação revisada para instalação, perfis/PIN, configurações, backup, migração, arquitetura, privacidade, suporte e processo de release.

## [1.0.1] - 2026-09-27

### Alterado

- O histórico agora usa o Baki-Pi `ask` para episódios que ainda não receberam avaliação.

### Corrigido

- Remover o PIN de um perfil agora persiste corretamente após salvar configurações e reiniciar o AniT.
- Alterações comuns do perfil não podem mais restaurar um PIN removido nem apagar um PIN recém-criado por meio de um rascunho antigo.

## 1.0.0 - 2026-09-27

### Adicionado

- Home premium responsiva com destaques da biblioteca.
- Biblioteca inteligente com múltiplas pastas, revisão, duplicatas e indisponíveis.
- Pesquisa instantânea por título principal ou em inglês, tolerante a acentos e pontuação.
- Metadados e capas via AniList com cache e fallback local.
- Página do anime com sinopse, nota pública e avaliações por episódio.
- Organizador opcional com prévia e suporte a legendas sidecar.
- Documentação e infraestrutura inicial para colaboração open source.
- Escaneamento automático na inicialização e em segundo plano, com regras configuráveis.
- Busca global por títulos, aliases, episódios e gêneros localizados.
- Favoritos, calendário, histórico, perfil, múltiplos perfis e 100 conquistas.
- Backup portátil, importação com pré-visualização e fluxo de religação da biblioteca.
- Configurações separadas para aparência, página inicial, biblioteca, imagens, reprodução, organização, notificações, perfil, backup e recursos avançados.
- Galeria de artes, cache offline e múltiplas fontes oficiais ou personalizadas.
- Modos claro, escuro e automático com preferências persistentes.
- Tags e coleções atribuíveis por anime, pesquisáveis e filtráveis na Biblioteca.

### Alterado

- Navegação principal usa o nome Biblioteca.
- Layouts e molduras foram refinados para diferentes resoluções e escalas.
- A identificação de episódios reconhece nomes ruidosos e títulos embutidos em arquivos fora de pastas específicas.
- Downloads online agora respeitam timeout, limite de conexões, modo offline e bloqueio de redes privadas.
- Arquivos de perfil e configurações passam a usar gravação atômica e cópia de recuperação.

### Corrigido

- Estado concluído quando todos os episódios locais foram assistidos.
- Falhas de carregamento e bindings da Biblioteca.
- Persistência e retomada do progresso de reprodução.
- Atalhos de teclado deixavam de funcionar depois de salvar configurações.
- Operações explícitas de reprocessamento podiam informar sucesso após falha de leitura.
- Downloads de imagens sem limite de tamanho e exportações CSV interpretáveis como fórmula.
- Validação de backups com manifesto duplicado, caminhos inseguros ou conteúdo adulterado.

[Não publicado]: https://github.com/PedroVitor-Dev/AniT/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/PedroVitor-Dev/AniT/releases/tag/v1.0.1
