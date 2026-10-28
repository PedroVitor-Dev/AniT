# Online Anime Player · Beta

O menu do AniT abre uma janela dedicada para magnet links, URLs HTTP/HTTPS de arquivos `.torrent` e arquivos `.torrent` locais. Não há catálogo, busca ou fonte de conteúdo fornecida pelo AniT.

## Uso

1. Leia o aviso e confirme que tem autorização para baixar e compartilhar o material. A tela explica que BitTorrent também envia partes do conteúdo para outros participantes.
2. Informe a fonte, opcionalmente o nome do anime e a pasta de destino.
3. Clique em **Adicionar à biblioteca e baixar**. Magnet links precisam obter metadados dos participantes; a preparação pode ser cancelada e tem limite de dois minutos.
4. Confira os episódios e clique em **Assistir em streaming**. Uma janela de preparação permite cancelar a espera pelos trechos iniciais e pelo índice do vídeo.
5. A reprodução usa o MPC-HC integrado e o fluxo normal de histórico, progresso, Baki-Pi e conquistas. O aplicativo padrão do Windows continua disponível para arquivos completos; arquivos incompletos exigem o MPC-HC integrado.

**Cloud** indica um arquivo ainda em download, não armazenamento remoto. A tag aparece nos cartões de início, na biblioteca, nos episódios e nos downloads. Ao concluir e verificar o arquivo, ele passa a ser reproduzido do disco, preservando o mesmo ID de episódio e seu progresso.

O botão de pausar controla o download, e pode interromper o streaming quando os trechos em buffer acabarem. Voltar ao AniT fecha a janela de gerenciamento e mantém os downloads. Sair do aplicativo interrompe o motor; os downloads autorizados deste perfil são retomados ao abrir novamente. A troca de perfil e a restauração de backup encerram o motor anterior antes de substituir o banco.

## Implementação

- MonoTorrent 3.0.2, versão estável, com notificações de licença distribuídas em `Licenses/Online`. Documentação e código: https://github.com/alanmcgovern/monotorrent.
- Manifestos por perfil em `Online/<profile-id>/downloads.json`, gravados por substituição atômica. Metadados e fast resume ficam no cache do motor.
- Cada torrent possui uma pasta exclusiva em `<destino>/AniT Online/<job-id>`. Caminhos são validados; executáveis e outros arquivos sem relação com vídeo/legendas não são selecionados.
- O motor valida as peças do torrent. A fonte de reprodução é um stream seekable que prioriza a posição solicitada. O endpoint HTTP de vídeo escuta apenas em `127.0.0.1`, com porta dinâmica, rota aleatória e suporte a HEAD e ranges simples. Não depende de ACL de HttpListener, servidor central ou privilégios de administrador.
- Até oito conexões HTTP locais; leitura em blocos de 64 KiB; solicitações antigas de reprodução são canceladas quando o player pede outra posição.
- Raízes gerenciadas ficam fora da varredura e reorganização comuns, evitando duplicar ou mover arquivos vinculados ao motor. Os IDs de episódios continuam sendo os mesmos usados pelo restante do AniT.
- O motor pode compartilhar peças enquanto baixa. A verificação periódica interrompe o torrent quando todos os vídeos e legendas selecionados foram concluídos; não há seeding contínuo automático.

## Limites do beta

A velocidade depende da conexão e de participantes com os arquivos disponíveis. Sem fontes, o AniT informa que está aguardando participantes. Buscar uma região ainda não baixada pode exigir novo buffering. Não há promessa de início imediato.

A importação considera todos os vídeos do torrent como parte da temporada informada. O parser existente identifica temporadas e números de episódio; arquivos sem numeração recebem a ordem dos nomes. Confira os nomes e evite adicionar coleções mistas de séries e extras sem revisar. Não há seleção individual de arquivos, transcodificação, navegador web ou integração com Movie Theater nesta versão.

Os downloads são locais e ocupam espaço no destino escolhido. Os arquivos e metadados do motor precisam permanecer nessas pastas para a retomada funcionar. O backup do catálogo não é um backup dos vídeos baixados.

## Verificação

- `dotnet test tests/AniT.Tests`: importação em lote, identidade de episódio, preservação de avaliações/progresso, retomada, validação de caminhos, ranges e transferência real entre dois motores locais com dados sintéticos.
- `dotnet run --project tests/AniT.UiChecks -- --check-online`: claro/escuro, texto 100%/140%, três tamanhos de janela, aviso inicial e lista de episódios.
- `dotnet run --project tests/AniT.UiChecks -- --check-online-player`: AVI gerado pelo próprio teste, dois clientes locais e MPC-HC real para reprodução durante download, pausa, busca e retomada.

Os testes não utilizam mídias pessoais nem torrents de terceiros.

Validação de 28/10/2026: 326 testes passaram; 24 cenários de layout passaram. O MPC-HC reproduziu o AVI sintético com aproximadamente 24% baixado, pausou, buscou 12 segundos e continuou em 20 segundos após a conclusão do download. Isso verifica o fluxo local, não estima desempenho de torrents na internet.
