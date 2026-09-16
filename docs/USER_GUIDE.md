# Manual de utilização

Este guia apresenta o fluxo completo do AniT, desde a primeira pasta até a reprodução e a manutenção da biblioteca.

## 1. Antes de começar

O AniT funciona no Windows 10 e 11. Durante a fase atual do projeto, a forma suportada de execução é pelo código-fonte. Releases instaláveis serão publicadas quando o empacotamento estiver estabilizado.

Para reproduzir vídeos, o executável portátil `mpc-hc64.exe` deve estar em `Player/MPC-HC/` durante o desenvolvimento ou em `Player/MPC-HC/` ao lado do aplicativo publicado.

## 2. Primeira abertura

Na primeira execução, o AniT pede uma pasta para compor a estante. Você pode escolher a pasta principal e permitir a leitura de subpastas.

O aplicativo procura estes formatos:

```text
.mkv  .mp4  .avi  .mov  .webm  .m4v  .wmv
```

O scan apenas lê os arquivos e atualiza o banco local. Ele não move, renomeia nem exclui vídeos.

## 3. Nomes de arquivo

O parser reconhece padrões comuns, por exemplo:

```text
Sousou no Frieren - 17.mkv
Sousou no Frieren S01E17.mkv
[Grupo] Sousou no Frieren - 17 [1080p].mkv
Anime Name - 03v2 [PT-BR].mkv
```

Quando o nome contém episódio decimal, múltiplos episódios, especiais ou informação insuficiente, o AniT prefere enviar o arquivo para **Revisão**. Isso evita associações silenciosas incorretas.

## 4. Biblioteca

Abra **Biblioteca** no menu principal.

### Títulos

Exibe os animes identificados, quantidade de episódios, nome alternativo, status e progresso. O botão **Página do Anime** abre os detalhes da obra.

### Pastas

Gerencia as raízes monitoradas. Cada pasta pode:

- incluir ou ignorar subpastas;
- ser ativada ou desativada sem apagar dados;
- ficar temporariamente indisponível sem perder o histórico.

### Atualizar Títulos

Executa duas etapas:

1. varredura dos vídeos e reconciliação com a biblioteca;
2. atualização opcional de títulos, capas, sinopses e notas via AniList.

O progresso aparece na tela. Se a rede falhar, a biblioteca local continua utilizável e os metadados já salvos são preservados.

### Revisão

Arquivos ambíguos ficam em uma fila. Confira o anime, temporada e episódio sugeridos antes de confirmar. Uma correção manual pode criar um alias aprendido para os próximos scans.

### Duplicatas e versões

O AniT usa caminho, tamanho, metadados e uma impressão rápida do conteúdo para distinguir:

- o mesmo arquivo movido;
- duas cópias físicas do mesmo conteúdo;
- versões diferentes do mesmo episódio.

Marcar uma versão preferida não apaga as demais nem altera o progresso do episódio.

### Indisponíveis

Se um arquivo ou disco sumir, o item é marcado como indisponível. Um disco desconectado não é tratado como exclusão definitiva.

### Organizar

O organizador é opcional. Primeiro ele gera uma prévia com origem, destino e legendas sidecar (`.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`). Somente operações selecionadas e sem conflito são executadas. Destinos existentes nunca são sobrescritos.

## 5. Página do anime

A página reúne:

- título principal e título alternativo;
- capa, sinopse e nota pública quando disponíveis;
- lista local de episódios;
- progresso e status de cada episódio;
- avaliação e comentário pessoal por episódio.

Ao marcar todos os episódios disponíveis como assistidos, o anime passa a ser exibido como concluído.

## 6. Reprodução e progresso

Use **Continuar assistindo** ou abra um episódio. O AniT inicia o MPC-HC, acompanha a posição e cria checkpoints periódicos. Ao concluir o episódio, o status e a posição final são persistidos no SQLite.

Se o player for fechado inesperadamente, o aplicativo tenta salvar o último ponto conhecido.

## 7. Capas e modo offline

As capas baixadas ficam em `%LOCALAPPDATA%\AniT\Covers`. Depois do primeiro download, elas são reutilizadas localmente. Quando uma capa não existe, o AniT usa o Baki-Pi como fallback visual.

Sem internet:

- vídeos, histórico e avaliações continuam disponíveis;
- capas em cache continuam funcionando;
- novos metadados do AniList aguardam uma atualização futura.

## 8. Backup e restauração

Feche o AniT antes de copiar os dados.

Faça backup de:

```text
%LOCALAPPDATA%\AniT\Data
%LOCALAPPDATA%\AniT\Covers
```

Para restaurar, feche o aplicativo e recoloque as pastas no mesmo local. O arquivo `anit.db.pre-smart-library.bak`, quando presente, é um backup automático criado antes de uma migração relevante do banco.

## 9. Remover dados locais

Feche o AniT e remova `%LOCALAPPDATA%\AniT`. Essa ação apaga banco, capas e logs, mas não toca nos vídeos originais. Faça backup antes se quiser preservar progresso ou avaliações.

## 10. Precisa de ajuda?

Consulte [Solução de problemas](TROUBLESHOOTING.md). Se o comportamento persistir, abra uma issue usando o formulário de bug e anexe apenas logs revisados, sem caminhos ou informações que você não queira publicar.
