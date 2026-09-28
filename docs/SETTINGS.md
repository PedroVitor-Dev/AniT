# Referência de configurações

Abra **Configurações gerais** pela barra lateral. As alterações são aplicadas ao salvar; preferências visuais são persistidas e afetam todas as telas. O padrão de uma instalação nova é o tema escuro.

## Página inicial

- **Tempo de troca do banner:** intervalo entre 3 e 30 segundos.
- **Rotação automática:** pausa ou ativa a troca de destaques.
- **Seções da página:** mostra, oculta e reordena Continuar assistindo, Adicionados recentemente e Favoritos e bem avaliados.
- **Quantidade por seção:** define quantos títulos aparecem em cada bloco.
- **Botão Continuar:** pode reproduzir do ponto salvo ou abrir os detalhes.
- **Ocultar concluídos:** remove títulos concluídos de Continuar assistindo.
- **Prioridade:** coloca novidades, favoritos ou o comportamento padrão primeiro.
- **Atualização de conteúdo:** controla consultas automáticas quando a Home é aberta.

## Aparência

- **Tema:** escuro, claro ou automático conforme o Windows.
- **Cor principal e brilho:** personaliza realces, halos e intensidade visual.
- **Cards:** compacto, confortável ou grande; quantidade automática ou manual por linha.
- **Bordas:** controla o arredondamento global.
- **Movimento:** intensidade de animações e partículas, com opção de reduzir movimento.
- **Escala:** ajusta interface e tamanho de texto separadamente.
- **Mascotes:** escolhe uma expressão do Baki-Pi para cada página ou oculta o mascote.

## Biblioteca

- **Pastas monitoradas:** adiciona várias raízes e decide se subpastas entram no scan.
- **Escaneamento ao iniciar:** reconcilia a biblioteca automaticamente.
- **Segundo plano:** define o intervalo de atualização; zero desativa.
- **Extensões:** escolhe quais formatos de vídeo são aceitos.
- **Reconhecimento:** ativa regras para `S01E01`, `E01`, traço, colchetes, número final, temporada por pasta, OVA e especiais.
- **Capas e cache:** escolhe diretórios locais.
- **Duplicatas:** mantém versões, prefere qualidade ou envia para revisão, conforme a política escolhida.
- **Exclusões:** ignora pastas, arquivos ou palavras específicas.
- **Reconstruir catálogo:** refaz a leitura preservando dados pessoais compatíveis.

## Organização

- **Episódios:** exibe como `01`, `E01` ou `S01E01`.
- **Título preferencial:** português, inglês, romaji ou japonês.
- **Ordenação da Biblioteca:** define a ordem inicial dos cards.
- **Tags e coleções:** cria agrupamentos personalizados.
- **Regras automáticas:** prioriza favoritos ou títulos em andamento.
- **Renomeação:** sempre mostra uma prévia e pede confirmação.
- **Correção de catálogo:** mescla ou separa títulos identificados incorretamente.

## Notificações

- avisa sobre novos episódios, atualização de capas/metadados e conquistas;
- pode gerar um resumo semanal da atividade;
- possui horário silencioso configurável;
- exibe avisos somente no AniT ou também pela central do Windows;
- oferece uma notificação de teste.

## Perfil e privacidade

- cria múltiplos perfis locais, cada um com banco e jornada separados;
- altera nome, frase, título, avatar, capa, zoom e enquadramento;
- escolhe perfil local ou público e oculta histórico, notas ou favoritos;
- ativa um PIN numérico opcional de 4 a 6 dígitos;
- exporta dados pessoais em JSON;
- limpa o histórico sem remover a Biblioteca;
- troca, cria ou exclui perfis locais.

O PIN protege a entrada casual no perfil, mas não criptografa o banco nem substitui a senha do Windows. Após cinco tentativas incorretas, novas tentativas ficam bloqueadas por 30 segundos.

## Reprodução

- **Player:** MPC-HC integrado ou aplicativo padrão do Windows.
- **Retomada:** continua automaticamente da posição salva.
- **Assistido:** limiar configurável de 50% a 100%.
- **Próximo episódio:** reprodução automática opcional.
- **Abertura e encerramento:** pula segmentos quando existe um sidecar `.anit-segments.json` válido.
- **Idiomas:** preferências de áudio e legenda.
- **Velocidade:** de 0,5× a 2×.
- **Tela cheia:** inicia a reprodução integrada em tela cheia.
- **Checkpoint:** salva progresso a cada 2 a 60 segundos.

O player padrão do Windows não fornece posição ao AniT; retomada, checkpoints e conclusão automática dependem do MPC-HC integrado.

## Experiência

- ativa ou desativa a tela intermediária de carregamento;
- controla a duração mínima da transição entre 250 e 1.500 ms;
- respeita a opção de redução de movimento configurada em Aparência.

## Imagens

- habilita fontes de capas, banners e fan arts;
- ordena prioridade entre AniList, fontes oficiais e sites personalizados;
- limita dimensão máxima e tamanho do cache;
- limpa ou reconstrói o cache;
- restringe pesquisas a conteúdo SFW;
- usa arquivos locais antes das fontes online;
- define o intervalo de atualização automática;
- usa `Uniform`, `UniformToFill` ou enquadramento manual;
- bloqueia uma arte para impedir substituição automática;
- aceita até 20 fontes personalizadas, somente em HTTPS público.

## Dados e backup

- cria backup automático ao iniciar, diário, semanal ou mensal;
- mantém de 1 a 30 cópias na pasta escolhida;
- exporta e importa o backup completo `.anitbackup` com pré-visualização;
- exporta dados pessoais em JSON ou CSV;
- restaura os padrões de uma seção sem apagar as demais;
- diagnostica e repara o banco SQLite;
- prepara a migração e a religação da mídia em outro computador.

Veja [Backup e migração](BACKUP_AND_MIGRATION.md) antes de restaurar.

## Avançado

- limita conexões simultâneas entre 1 e 16;
- ativa modo totalmente offline;
- define timeout das fontes online entre 3 e 120 segundos;
- ativa registro detalhado e abre pastas de cache, dados ou logs;
- reprocessa somente metadados, imagens ou arquivos;
- configura atalhos de Início, Biblioteca, Busca, Histórico e Configurações avançadas;
- exibe informações técnicas no modo desenvolvedor.

Use as ações de reprocessamento e reparo somente depois de criar um backup.
