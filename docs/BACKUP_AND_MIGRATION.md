# Backup e migração

## Qual arquivo usar

Para mover ou restaurar o AniT, use **Configurações → Dados e backup → Exportar backup**. O resultado é um único arquivo versionado com extensão `.anitbackup`, adequado para uma pasta local, pendrive ou provedor de nuvem de sua escolha.

Exportações JSON ou CSV servem para leitura e portabilidade de dados pessoais; elas **não** substituem o `.anitbackup` e não podem restaurar todo o aplicativo.

## O que o backup contém

- banco SQLite ativo e dados dos perfis locais;
- histórico, progresso, avaliações e comentários;
- conquistas e estado de notificações;
- configurações do aplicativo;
- cadastro, avatar e capa dos perfis;
- capas e banners mantidos no cache permitido.

O backup não inclui episódios, vídeos ou outras mídias da biblioteca. Isso mantém o arquivo transportável e evita copiar conteúdo sem necessidade.

## Backup automático

Escolha frequência, destino e retenção em **Dados e backup**. As frequências disponíveis são ao iniciar, diária, semanal ou mensal, com retenção de 1 a 30 arquivos. A pasta padrão é `%USERPROFILE%\Documents\AniT Backups`.

Backups automáticos complementam, mas não substituem, uma cópia externa. Sincronizar a pasta de destino com OneDrive, Google Drive, Dropbox ou outro serviço é uma decisão do usuário; o AniT não envia o arquivo diretamente para uma nuvem.

## Importar com segurança

1. Feche reproduções e operações de scan.
2. Abra **Dados e backup → Importar backup**.
3. Selecione o `.anitbackup` e confira a pré-visualização: versão, data, perfis e conteúdo.
4. Confirme a restauração.
5. Reinicie o fluxo quando solicitado e valide o perfil ativo.

Antes de substituir dados, o AniT cria uma cópia de recuperação. O importador valida manifesto, versão do formato, hashes, quantidade e tamanho das entradas, caminhos internos e esquema esperado. Arquivos inválidos ou adulterados são rejeitados.

> [!WARNING]
> Um backup contém dados pessoais locais. Guarde-o em local privado e não o anexe a uma issue pública.

## Migrar para outro computador

1. No PC antigo, atualize para uma versão compatível e exporte um `.anitbackup`.
2. Copie o backup para o novo PC por um meio de sua confiança.
3. Instale o AniT e importe o arquivo.
4. Baixe ou copie seus vídeos separadamente.
5. Abra **Configurações → Biblioteca**, adicione as novas pastas e execute o scan.
6. Use a revisão/religação manual para associar arquivos ao histórico restaurado.

O AniT tenta reconciliar pelos dados existentes, mas um arquivo pode ter outro nome, fansub, container, resolução ou estrutura de pastas. Nesses casos, a associação manual garante que o episódio novo herde o histórico correto sem depender do caminho antigo.

## Quando nomes e formatos mudam

- Confira anime, temporada e episódio sugeridos.
- Relacione o novo arquivo ao episódio já conhecido.
- Se houver duas versões, marque a preferida em vez de excluir a outra.
- Use mesclar/separar quando o título inteiro foi agrupado incorretamente.
- Faça novo backup depois de concluir as correções.

O AniT nunca precisa renomear o arquivo apenas para recuperar o histórico.

## Recuperação manual

Se a interface não abrir, preserve primeiro `%LOCALAPPDATA%\AniT`. O banco principal fica em `%LOCALAPPDATA%\AniT\Data\anit.db`; perfis adicionais ficam em `Data\Profiles\<id>\anit.db`. Copiar apenas essas pastas é um recurso emergencial, não o formato portátil recomendado.

Use **Diagnóstico e reparo do banco** para verificações locais. Se a restauração falhar, mantenha o backup original intacto, consulte [Solução de problemas](TROUBLESHOOTING.md) e informe a mensagem exibida sem publicar seu arquivo de backup.
