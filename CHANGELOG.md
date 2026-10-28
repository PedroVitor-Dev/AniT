# Changelog

Todas as mudanças relevantes serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto pretende adotar [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não publicado]

### Corrigido

- Movie Theater: instâncias independentes do MPC-HC que não encerram após o pedido de fechamento recebem término forçado após dois segundos. A proteção é limitada aos processos criados e controlados pela própria sessão.

- Movie Theater: validação reproduzível com três perfis de atraso/oscilação, interrupção de mídia, comandos ordenados, busca e nova entrada. Adicionado script de QA com logs/TRX/JSON e roteiro de validação em dois PCs, identificado como ainda não executado fisicamente. O relay de teste passa a executar a rede fora da thread da interface.

- Movie Theater: buffer adaptativo mede recebimento real, latência e variação da conexão, ajusta a leitura antecipada e exige reserva de 1–8 MiB antes de liberar a retomada. Esperas recorrentes aumentam a proteção; estabilidade posterior reduz o alvo. Cache e pausas ociosas não distorcem a medição. Mantém o host no comando e o cache limitado a 32 MiB. Fechamentos concorrentes aguardam a mesma conclusão; consultas simultâneas de posição ao MPC-HC deixam de se cancelar. Protocolo 8 exige atualização nos dois PCs.

- Movie Theater: espera coordenada no streaming. Falta persistente de dados no convidado pausa os dois vídeos; progresso e tempo de espera aparecem nos dois lados. A recuperação aguarda estabilidade e não retoma sozinha: somente o host libera uma nova partida sincronizada. Leituras antecipadas e pausas manuais não disparam a espera. Revisões impedem que uma retomada antiga libere um novo bloqueio. Exige AniT atualizado nos dois PCs.

- Movie Theater: streaming passa a usar conexões TLS independentes para vídeo e comandos, na mesma porta da sala. Pausa, posição e heartbeat deixam de compartilhar a fila TCP do vídeo. Canal de mídia exige uma chave exclusiva da participação atual; conexões duplicadas e chaves antigas são recusadas. Fechar qualquer canal encerra a participação e permite nova entrada com outra chave. Exige atualização nos dois PCs.

- Movie Theater: streaming com transporte binário de 32 KiB, pedidos de até 1 MiB em pipeline, leitura antecipada limitada e cache sem bloqueio global durante downloads. Reduz serialização e alocações, mantém a leitura inicial em 256 KiB, atualiza a linha do tempo a cada 100 ms e inicia o streaming com agendamento de 350 ms após os players prontos. Correções suaves e intervalo entre buscas evitam descartar o buffer repetidamente. Exige AniT atualizado nos dois PCs.

- Movie Theater: ações aparecem conforme o papel e a etapa; confirmação somente após abrir o vídeo, pausa/busca após liberação, download somente quando disponível e abertura oculta após prontidão. A autorização fica oculta durante a contagem e instruções de destino são exclusivas do convidado.

- Movie Theater: bloqueio de reprodução no MPC-HC mantém o convidado pausado até o aval e o horário de início do host, inclusive durante a abertura do vídeo. A saída/desconexão do host fecha a sala e o player do convidado, preservando o episódio já baixado.

- O botão Voltar ao AniT fecha o Movie Theater e encerra sua participação, restaurando a janela principal quando estiver minimizada.

- Movie Theater: pausar no MPC-HC do host ou no botão da sala mantém os dois players pausados. Ajustes de velocidade não retomam mais a reprodução implicitamente; comandos repetidos são evitados e alterações pendentes só entram ao continuar. Corrige também o estado de pausa durante a preparação.

- Home: renderização compatível limitada à janela elimina molduras falsas nas imagens transparentes e falhas nos cantos arredondados; o Baki-Pi do banner deixa de usar o quadro das capas, e o recorte dos cartões passa a preservar o contorno externo.

- Onboarding com logo preservada após otimização de imagens, margens internas, navegação sempre acessível e painel lateral adaptado ao espaço disponível após a escala da interface. O foco não oculta mais o título da etapa de perfil.
- Verificações de interface cobrem as quatro etapas em 144 combinações de tamanho, escala e tema usando as regras reais de aparência e otimização do aplicativo.

### Adicionado

- Movie Theater: Gerenciador do Host com nomes dos perfis, papel, prontidão e permissão de voz dos participantes. O host pode bloquear/liberar a permissão de microfone para o futuro chat e expulsar o convidado, invalidando o convite anterior. A expulsão fecha o player/sessão do convidado, explica o motivo e preserva arquivos completos. Voz ainda sem captura/reprodução; limite atual de host + um convidado. Protocolo 9 exige atualização nos dois PCs.

- Movie Theater: seleção inicial entre **Streaming direto do seu PC** e **Sincronização P2P**. O streaming envia trechos originais pelo canal TLS da sala para o MPC-HC do convidado, com buffer em memória, busca por intervalos e controles exclusivos do host, sem aguardar uma cópia completa. Mantém duas pessoas por sala e requer upload contínuo do host.

- Movie Theater em prévia: sala direta para dois AniTs com arquivo local idêntico, convite TLS, preparo dos players, contagem regressiva e sincronização controlada pelo host. O participante recebe uma tela nativa sem controles; áudio e legendas embutidos seguem as faixas do host. Compartilhamento opcional aprovado pelo host ao criar a sala: o convidado baixa o episódio na preparação, com progresso, verificação SHA-256 e confirmação de arquivo OK antes de preparar o player e iniciar. Ainda sem voz.

- Onboarding de primeira execução com boas-vindas, nome e foto opcionais, seleção da pasta principal com subpastas e revisão antes de entrar. É possível configurar a pasta depois e retomar uma configuração interrompida.

### Alterado

- P2P com preparação controlada pelo host: liberação do download, abertura automática dos vídeos pausados, confirmação do convidado e aval exclusivo do host com contagem sincronizada de cinco segundos. Painel com cadeado, etapas, progresso recebido/verificado, tempo decorrido e velocidade média; ações ficam no rodapé. Fechamento de vídeo, perda de prontidão ou desconexão revogam o aval. Protocolo da sala atualizado para versão 4.

- Movie Theater usa ações explícitas: Abrir episódio, Abrir transmissão e Abrir episódio baixado, com orientação de que o vídeo abre pausado e só começa quando o host iniciar. Mensagens e indicadores da sala seguem os mesmos termos.

- Movie Theater agora guia o usuário por modo, papel (Host ou Convidado) e configuração. Host configura episódio/compartilhamento; convidado recebe somente convite e, no P2P, a opção de usar uma cópia local. Endereço e porta ficam nas opções de conexão do host; é possível voltar e trocar de papel antes de conectar.

- Movie Theater com identidade visual de cinema usando os assets fornecidos: cartões responsivos para os dois modos, cabeçalho compacto na preparação, controles e barra de rolagem próprios, seleção e foco visíveis, transições curtas e suporte a movimento reduzido. O botão Continuar fica acessível fora da rolagem; os temas claro e escuro preservam contraste e transparência das artes.

- Distribuições exigem assinatura Authenticode por padrão; builds locais sem assinatura precisam de `-AllowUnsigned`. O processo valida o certificado antes da publicação e assina os binários próprios, o instalador e o desinstalador.

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
