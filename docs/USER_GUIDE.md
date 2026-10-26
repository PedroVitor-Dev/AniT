# Manual de utilização

## Primeira vez no AniT

Em uma instalação nova, um assistente apresenta o AniT e prepara seu espaço:

1. **Boas-vindas:** conheça os passos e o armazenamento local dos seus dados.
2. **Seu perfil:** escolha um nome de até 32 caracteres e uma foto PNG, JPG ou BMP. Ambos são opcionais; sem personalização, o AniT usa **Meu perfil** e o avatar padrão. A foto é copiada para os dados do aplicativo.
3. **Sua estante:** escolha a pasta principal dos animes. A busca inclui subpastas e não move, renomeia ou apaga seus vídeos. Use **Configurar a pasta depois** para começar sem biblioteca.
4. **Tudo pronto:** revise as escolhas e clique em **Entrar no AniT**. A busca dos episódios ocorre em segundo plano.

É possível voltar às etapas anteriores. Fechar o assistente antes de concluir permite recomeçar na próxima abertura; as escolhas só são aplicadas ao concluir. Se houver falha ao salvar, corrija a pasta/foto e tente novamente. Depois, altere nome e foto no perfil e a pasta em **Configurações → Estante**.

Quem já possui perfis ou biblioteca mantém o fluxo de seleção de perfis, inclusive o PIN. Atualizações não repetem o assistente.

Este guia descreve o fluxo do AniT 1.0.2, da escolha do perfil ao backup da biblioteca.

## 1. Instalação

Baixe o instalador na [página oficial de releases](https://github.com/PedroVitor-Dev/AniT/releases/latest), confira o SHA-256 e execute-o. O pacote já inclui o .NET e o MPC-HC necessários. Consulte [Instalação e atualização](INSTALLATION.md) para o passo a passo.

## 2. Perfis e PIN

Ao abrir o AniT, escolha quem está assistindo. Cada perfil mantém separadamente biblioteca, progresso, histórico, avaliações e preferências.

- Clique em um perfil para entrar.
- Crie ou edite perfis em **Configurações → Perfil e privacidade**.
- Avatar, capa, nome, frase, título e enquadramento são personalizáveis.
- O PIN opcional aceita de 4 a 6 dígitos e é solicitado antes de abrir o perfil.
- Cinco tentativas incorretas bloqueiam novas tentativas por 30 segundos.
- Para remover o PIN, use **Remover PIN** e confirme a operação.

O PIN é uma proteção local contra acesso casual; ele não criptografa os arquivos nem substitui a conta do Windows.

## 3. Montar a estante

Abra **Configurações → Biblioteca** e adicione uma ou mais pastas. Cada raiz pode incluir ou ignorar subpastas. O scan aceita, por padrão:

```text
.mkv  .mp4  .avi  .mov  .webm  .m4v  .wmv
```

Use **Biblioteca → Atualizar Títulos**. O scan lê os arquivos e atualiza o catálogo; não move, renomeia nem exclui mídia. Se todos os episódios de um título tiverem sido apagados do disco, ele deixa a Biblioteca, Home, Explorar e busca após a atualização, enquanto histórico, progresso e avaliações permanecem preservados para backup ou religação futura.

Nomes reconhecidos incluem:

```text
Sousou no Frieren - 17.mkv
Sousou no Frieren S01E17.mkv
[Grupo] Sousou no Frieren - 17 [1080p].mkv
Anime Name - 03v2 [PT-BR].mkv
```

As regras de temporada, episódio, OVA e especiais podem ser ajustadas em Configurações. Casos ambíguos são enviados para **Revisão** em vez de serem associados silenciosamente. Nomes técnicos antigos, como `[768p] V2`, são reparados quando o arquivo ainda permite identificar o episódio corretamente.

## 4. Biblioteca

### Títulos e busca

A aba **Títulos** mostra capa, nome, episódios, status e progresso. A busca global e a busca da Biblioteca encontram títulos, aliases, gêneros e números de episódio — pesquisar `01`, por exemplo, também retorna episódios 01.

Os filtros combinam gênero, ano, status, estúdio e ordem. É possível recolher o painel para liberar espaço.

### Pastas

Gerencie raízes monitoradas, subpastas e disponibilidade. Desativar uma raiz não apaga seu histórico. Se um disco externo ou unidade de rede sair do ar, os itens permanecem protegidos como indisponíveis; a limpeza automática só ocorre quando a raiz acessível confirma que os arquivos não existem mais.

### Revisão, duplicatas e versões

- **Revisão:** confirme anime, temporada e episódio quando a identificação não for segura.
- **Duplicatas:** compare cópias e versões físicas sem apagar automaticamente.
- **Indisponíveis:** localize itens cujo arquivo não está acessível.
- **Correção de catálogo:** mescle ou separe títulos associados incorretamente.

Uma correção manual pode criar um alias útil para scans futuros. O AniT usa caminho, tamanho, metadados e impressão rápida para diferenciar arquivo movido, cópia física e versão alternativa.

### Organizar e renomear

O organizador é opcional. Ele mostra uma prévia com origem, destino e legendas sidecar (`.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`). Somente itens confirmados são processados e destinos existentes nunca são sobrescritos.

## 5. Home e Explorar

A Home reúne banner, Continuar assistindo, novidades, favoritos e bem avaliados. Em **Configurações → Página inicial**, você pode mostrar, ocultar, ordenar e dimensionar essas seções e definir o comportamento do botão Continuar.

**Explorar** organiza a coleção por Ação, Romance, Fantasia, Drama, Slice of Life, Mistério, Comédia, Isekai, Ecchi, Horror, Aventura e Esportes. As artes respeitam fontes, prioridade, SFW, cache e bloqueios configurados.

## 6. Página do anime

A página reúne títulos, capa, sinopse, gêneros, estúdio, nota pública, arquivos e progresso. Nela você pode:

- iniciar ou retomar um episódio;
- favoritar;
- avaliar e comentar episódios;
- aplicar tags e coleções;
- escolher versões quando houver duplicatas;
- abrir ações de organização e correção.

Quando todos os episódios locais são assistidos, o título pode ser marcado como concluído.

## 7. Reprodução

O MPC-HC integrado permite posição salva, checkpoints, retomada, conclusão automática e próximo episódio. O limiar de “assistido”, intervalo de salvamento, velocidade, tela cheia e idiomas preferenciais são configuráveis.

Pular abertura e encerramento depende de um arquivo `.anit-segments.json` válido ao lado do episódio. Se você selecionar o aplicativo padrão do Windows, o AniT abre o vídeo, mas não consegue observar sua posição.

## 8. Calendário, histórico e conquistas

- **Calendário:** mostra episódios assistidos por dia, notas e resumo mensal.
- **Histórico:** filtra sessões por período e anime e permite registrar avaliação ou comentário.
- **Conquistas:** contém 100 marcos persistentes, cadeias de progressão, segredos e AniPoints.
- **Perfil:** resume estatísticas, favoritos, Top 5, atividade e jornada de conquistas.

## 9. Aparência, notificações e acessibilidade

O AniT inicia em modo escuro em instalações novas. Também oferece tema claro e automático, cor principal, brilho, tamanho de cards, arredondamento, escala da interface e texto, redução de movimento e mascote por página. A preferência persiste e vale para todas as telas.

Notificações podem avisar sobre novos episódios, metadados, conquistas e resumo semanal, somente dentro do AniT ou também no Windows. Horários silenciosos evitam avisos no período definido.

## 10. Imagens e modo offline

O AniT pode consultar AniList, fontes oficiais e sites de arte habilitados. Imagens locais têm prioridade opcional; conteúdo SFW é o padrão. Capas e banners ficam em cache para uso offline.

O modo totalmente offline bloqueia consultas integradas. Biblioteca, histórico, avaliações, vídeos e imagens já armazenadas continuam funcionando.

## 11. Backup e troca de computador

Em **Configurações → Dados e backup**, exporte um único `.anitbackup`. Ele preserva perfis, configurações, bancos, histórico, avaliações, conquistas e caches aceitos — mas não inclui vídeos.

Ao migrar, importe o backup, configure as pastas onde os vídeos estão no novo PC e use a religação/revisão manual quando arquivos vierem com outro nome, fansub ou formato. Veja [Backup e migração](BACKUP_AND_MIGRATION.md).

## 12. Remover dados locais

Desinstalar o aplicativo preserva `%LOCALAPPDATA%\AniT`. Para apagar tudo, exporte um backup se necessário, feche o AniT e remova essa pasta manualmente. Seus vídeos originais ficam fora dela.

## 13. Precisa de ajuda?

Consulte [Solução de problemas](TROUBLESHOOTING.md). Ao abrir uma issue, informe a versão, o Windows e os passos mínimos, mas revise logs e capturas para não publicar caminhos ou dados pessoais.
