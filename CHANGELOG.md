# Changelog

Todas as mudanças relevantes serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto pretende adotar [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Melhorado

- Online Anime Player: cadastro obtém somente metadados; abrir um episódio baixa apenas o vídeo escolhido e legendas correspondentes. Trocar pausa o anterior; fechar o player pausa o download e reiniciar aguarda nova escolha, preservando os dados.
- Preparação com velocidade, fontes, dados recebidos e tempo decorrido; normalização de magnet links com espaços e Unicode.

### Corrigido

- Online Anime Player: a preparação prioriza e verifica o começo do episódio, sem exigir antecipadamente peças do final. O player solicita o índice conforme o contêiner exigir. A janela informa o tamanho da peça inicial e usa progresso indeterminado durante sua verificação, com botão de cancelamento no tema do app.

- MPC-HC: buscas não bloqueiam mais a interface do AniT nem falham após apenas 1,5 segundo de buffering. O envio aguarda em segundo plano, com prazo limitado, preserva a ordem das buscas e respeita o cancelamento da espera sem reenviar comandos. Retomada inicial usa o mesmo caminho.

- Online Anime Player: consultas simultâneas ao índice/trechos do MKV não cancelam mais a resposta de vídeo em andamento. Leituras aguardando peças verificadas cedem a vez às outras conexões; o tipo de mídia HTTP corresponde ao contêiner. Diagnósticos registram as faixas solicitadas e falhas de leitura.
- Reprodução: abertura sem resposta e buffering deixam de gerar progresso pelo relógio. Fechar o player sem posição confirmada preserva a retomada; EOF de streaming longe do final não conclui o episódio nem avança automaticamente a fila. Cliques concorrentes não iniciam duas preparações do player.
- Regressões cobertas por leitura paralela de vídeo/índice, preservação de progresso/conquistas e MPC-HC real com MKV HEVC/Opus sintético. Busca durante buffering validada com rede limitada e atraso adicional.

## [2.0.0] - 2026-10-28

### Adicionado

- **Online Anime Player · Beta**: magnet links, URLs e arquivos `.torrent`, aviso de uso autorizado, seleção da pasta de destino, importação de episódios, progresso, pausa e retomada. O MPC-HC reproduz durante o download; a tag **Cloud** identifica arquivos incompletos na Home, biblioteca e episódios. Histórico, avaliações, Baki-Pi e conquistas usam a mesma identidade dos episódios locais.
- **Movie Theater**: salas diretas para o host e até quatro convidados, com streaming progressivo ou sincronização P2P de cópias completas. O host controla reprodução, aprova o compartilhamento, acompanha o preparo e gerencia participantes, expulsão e permissões de microfone.
- Chat de voz com Opus, controles flutuantes arrastáveis e indicação visual de fala. Confirmações da preparação ficam acima do player.
- Onboarding com controles Fluent, nome e foto opcionais, pasta principal e revisão antes de entrar.

### Melhorado

- Streaming do Movie Theater com conexões separadas para vídeo, comandos e voz; leitura parcial em pipeline, cache limitado, buffer adaptado às medições da conexão e espera coordenada quando convidados precisam carregar mais dados.
- Fluxo do Movie Theater por modo, papel e etapa; convidados iniciam somente após autorização do host. Sair ou remover um participante encerra sua reprodução sem apagar arquivos completos.
- Inicialização com banco preparado de forma serializada e serviços adiados; **Iniciar o AniT** vem marcado ao concluir a instalação.
- Temas claro/escuro e fonte ampliada nas novas telas; site atualizado para 2.0 com cabeçalho corrigido em 1366 × 768.

### Compatibilidade e distribuição

- Todos os participantes do Movie Theater devem usar AniT 2.0.0; o protocolo de sala é versão 10.
- O banco é atualizado para esquema 5, preservando histórico e avaliações. Faça backup antes de atualizar; versões antigas do app não devem ser usadas sobre o banco atualizado.
- Online Anime Player não fornece catálogo ou conteúdo. A velocidade depende da conexão e da disponibilidade dos participantes; torrents podem compartilhar partes dos arquivos enquanto baixam.
- A situação da assinatura digital e os hashes do instalador são informados nas notas da release. Builds sem certificado não são apresentados como assinados.

## [1.0.2] - 2026-09-28

### Alterado

- Movie Theater com identidade visual de cinema usando os assets fornecidos: cartões responsivos para os dois modos, cabeçalho compacto na preparação, controles e barra de rolagem próprios, seleção e foco visíveis, transições curtas e suporte a movimento reduzido. O botão Continuar fica acessível fora da rolagem; os temas claro e escuro preservam contraste e transparência das artes.

- Central de documentação revisada para instalação, perfis/PIN, configurações, backup, migração, arquitetura, privacidade, suporte e processo de release.
- O catálogo agora acompanha os arquivos realmente presentes nas pastas monitoradas: títulos sem nenhum episódio local deixam as telas normais após a atualização, preservando histórico, progresso e avaliações para backup ou religação futura.
- Raízes temporariamente indisponíveis, como discos externos desconectados, continuam protegidas contra remoção acidental do catálogo.

### Corrigido

- Episódios apagados não permanecem mais na página do anime, busca, Home, Explorar ou fila de reprodução após **Atualizar Títulos**.
- O botão **Organizar** volta a abrir sua prévia sem falhar por recursos visuais ausentes.
- Títulos técnicos antigos, como `[768p] V2`, são reparados durante a atualização do catálogo.
- A reprodução e o próximo episódio ignoram versões cujo arquivo local não existe mais.

## [1.0.1] - 2026-09-27

### Alterado

- Movie Theater com identidade visual de cinema usando os assets fornecidos: cartões responsivos para os dois modos, cabeçalho compacto na preparação, controles e barra de rolagem próprios, seleção e foco visíveis, transições curtas e suporte a movimento reduzido. O botão Continuar fica acessível fora da rolagem; os temas claro e escuro preservam contraste e transparência das artes.

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

- Movie Theater com identidade visual de cinema usando os assets fornecidos: cartões responsivos para os dois modos, cabeçalho compacto na preparação, controles e barra de rolagem próprios, seleção e foco visíveis, transições curtas e suporte a movimento reduzido. O botão Continuar fica acessível fora da rolagem; os temas claro e escuro preservam contraste e transparência das artes.

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

[Não publicado]: https://github.com/PedroVitor-Dev/AniT/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/PedroVitor-Dev/AniT/releases/tag/v1.0.2
[1.0.1]: https://github.com/PedroVitor-Dev/AniT/releases/tag/v1.0.1
