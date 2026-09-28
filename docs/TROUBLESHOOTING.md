# Solução de problemas

Antes de alterar arquivos, exporte um `.anitbackup` ou copie `%LOCALAPPDATA%\AniT` com o aplicativo fechado.

## O instalador foi bloqueado pelo Windows

A versão 1.0.2 não é assinada digitalmente. Baixe somente da [release oficial](https://github.com/PedroVitor-Dev/AniT/releases/latest), compare o SHA-256 com `SHA256SUMS.txt` e use **Mais informações → Executar assim mesmo** apenas se coincidir. Veja [Instalação](INSTALLATION.md).

## O AniT não abre ou fica no carregamento

1. Feche todas as instâncias no Gerenciador de Tarefas.
2. Abra novamente e aguarde a inicialização do banco.
3. Consulte `%LOCALAPPDATA%\AniT\Data\app.log`.
4. Confirme permissão de escrita em `%LOCALAPPDATA%\AniT`.
5. Preserve a pasta antes de usar diagnóstico, reparo ou reset.

## O PIN continua sendo solicitado

Desde a versão 1.0.1, remover o PIN persiste após reiniciar. Confirme a versão no rodapé de **Configurações**. Em **Perfil e privacidade**, selecione o perfil correto, clique em **Remover PIN** e confirme. Se estiver em 1.0.0, atualize primeiro.

Depois de cinco tentativas incorretas, aguarde 30 segundos. O PIN não possui recuperação remota; não publique `profiles.json` nem seu backup em uma issue.

## A Biblioteca está vazia ou um episódio não aparece

- Confira se a raiz está ativa, acessível e com subpastas habilitadas quando necessário.
- Verifique extensões e exclusões em **Configurações → Biblioteca**.
- Execute **Atualizar Títulos** ou **Reconstruir catálogo**.
- Use **Revisão** para nomes ambíguos.
- Prefira nomes com episódio explícito, como `Anime - 03.mkv` ou `Anime S01E03.mkv`.
- Consulte `%LOCALAPPDATA%\AniT\Data\library.log` quando disponível.

## A busca por número não encontra o episódio

A busca global pesquisa episódios desde a versão 1.0.1. Tente `01`, `E01` ou `S01E01` conforme sua configuração. Se a associação do arquivo estiver pendente, conclua primeiro a Revisão.

## Um anime ou episódio apagado continua aparecendo

Atualize para a versão 1.0.2 ou posterior e use **Biblioteca → Atualizar Títulos**. Quando a pasta monitorada está acessível, episódios ausentes deixam as telas normais e um título sem nenhum arquivo local deixa a estante. Histórico, progresso e avaliações são preservados. Se a raiz estiver em disco externo ou unidade de rede desconectada, o AniT mantém os itens como indisponíveis para evitar remoção acidental; reconecte a unidade e atualize novamente.

## Um disco externo foi desconectado

O AniT preserva registros e marca a raiz como indisponível. Reconecte com o mesmo caminho e atualize os títulos. Não apague o banco para corrigir esse cenário.

## O arquivo mudou de nome, pasta, fansub ou formato

Execute o scan e use a revisão/religação manual para vincular o novo arquivo ao episódio existente. Isso preserva histórico e avaliações. Em migrações, siga [Backup e migração](BACKUP_AND_MIGRATION.md).

## Capas ou fan arts não aparecem

- Confira conexão, modo offline, fontes habilitadas e filtro SFW.
- Verifique limite do cache e prioridade de imagens locais.
- Use **Imagens → Reconstruir cache** ou **Avançado → Reprocessar imagens**.
- Confirme `%LOCALAPPDATA%\AniT\Covers` e `%LOCALAPPDATA%\AniT\Cache`.
- Se uma arte estiver bloqueada, ela não será substituída automaticamente.

Falhas de uma fonte não impedem o uso local; cache e fallback visual continuam disponíveis.

## O player não inicia

No instalador oficial, o MPC-HC já está incluído. Em execução pelo código, confirme `Player\MPC-HC\mpc-hc64.exe` e siga [Player/README.md](../Player/README.md). Verifique também antivírus, permissões e `%LOCALAPPDATA%\AniT\Data\player.log`.

## O progresso não foi salvo

- Selecione **MPC-HC integrado**; o player padrão do Windows não fornece posição.
- Confira retomada, limiar de conclusão e intervalo de checkpoint.
- Aguarde ao menos um checkpoint antes de fechar.
- Feche o player normalmente quando possível.
- Confirme permissão de escrita no banco do perfil ativo.

## Tema claro ou textos sem contraste

Atualize para a versão mais recente, salve novamente **Aparência → Tema** e reinicie. Redefina apenas a seção Aparência se estilos antigos persistirem. Informe tela, resolução, escala e estado normal/hover ao relatar um elemento específico.

## O backup não importa

- Não renomeie o conteúdo interno nem converta o `.anitbackup`.
- Confirme que o arquivo terminou de copiar ou sincronizar.
- Não use JSON/CSV como backup restaurável.
- Mantenha o original intacto e teste uma cópia.
- Confira a pré-visualização e a mensagem de integridade.

O AniT rejeita hashes inválidos, entradas duplicadas, caminhos inseguros, limites excedidos e esquema incompatível.

## Reset completo

> [!WARNING]
> Isso remove perfis, progresso, avaliações, histórico, cache e configurações locais.

1. Feche o AniT.
2. Exporte um backup ou copie `%LOCALAPPDATA%\AniT`.
3. Remova a pasta somente se aceitar perder esses dados.
4. Abra o aplicativo e configure perfis e estante novamente.

Os vídeos originais não ficam nessa pasta e não são removidos pelo reset.

## Como pedir ajuda

Informe versão, Windows, resolução/escala, passos mínimos e mensagem de erro. Anexe apenas o trecho necessário dos logs; remova nome de usuário, caminhos, títulos privados e tokens. Nunca publique o banco, `profiles.json` ou um `.anitbackup` completo.
