# Solução de problemas

## A Biblioteca abre em branco ou fecha

1. Feche todas as instâncias do AniT.
2. Inicie o build mais recente.
3. Consulte `%LOCALAPPDATA%\AniT\Data\app.log` e `library.log`.
4. Confirme que o banco não está aberto por outra cópia do aplicativo.
5. Faça backup da pasta `Data` antes de qualquer tentativa de recuperação.

Ao abrir uma issue, informe versão/commit, versão do Windows, escala de tela e o trecho relevante do log. Remova caminhos pessoais se necessário.

## O anime ou episódio não foi identificado

- Use **Biblioteca → Revisão**.
- Prefira nomes com número explícito: `Anime - 03.mkv` ou `Anime S01E03.mkv`.
- Casos decimais, especiais e multi-episódio podem exigir confirmação manual.
- Depois de corrigir uma associação, atualize novamente a biblioteca para validar o alias aprendido.

## A capa não apareceu

- Verifique a conexão com a internet.
- Clique em **Atualizar Títulos**.
- Confirme se o título da pasta identifica corretamente a temporada.
- Verifique `%LOCALAPPDATA%\AniT\Covers`.

Falhas do AniList não impedem o uso local; o fallback visual permanece até uma atualização bem-sucedida.

## O player não inicia

Confirme a presença de:

```text
Player\MPC-HC\mpc-hc64.exe
```

Em um build publicado, a pasta `Player` deve estar ao lado do executável do AniT. Veja [Player/README.md](../Player/README.md).

## O progresso não foi salvo

- Aguarde alguns segundos após iniciar o vídeo antes de fechar.
- Feche o MPC-HC normalmente quando possível.
- Consulte `%LOCALAPPDATA%\AniT\Data\player.log`.
- Confirme que o banco local tem permissão de escrita.

## Um disco externo foi desconectado

O AniT marca a raiz como indisponível e preserva os registros. Reconecte o disco com o mesmo caminho e execute **Atualizar Títulos**. Não remova o banco para corrigir esse cenário.

## Um arquivo foi movido ou renomeado

Atualize a biblioteca. A impressão rápida ajuda a reconciliar o mesmo conteúdo sem perder o vínculo lógico e o progresso.

## O organizador encontrou conflito

O destino ou uma legenda sidecar já existe. Nenhum arquivo conflitante deve ser sobrescrito. Resolva o nome/destino manualmente, gere uma nova prévia e só então execute.

## Reset completo

> [!WARNING]
> Isso remove progresso, avaliações, histórico, cache e configuração local.

1. Feche o AniT.
2. Faça backup de `%LOCALAPPDATA%\AniT`.
3. Remova a pasta somente se aceitar perder esses dados.
4. Abra o app e configure a estante novamente.

Os vídeos originais não ficam nessa pasta e não são removidos pelo reset.
