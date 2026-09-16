# Privacidade e dados locais

## Resumo

O AniT é local-first. Não existe conta AniT, servidor próprio ou sincronização obrigatória. Sua biblioteca é mantida no computador onde o aplicativo é executado.

## Dados armazenados

O banco SQLite pode conter:

- títulos e aliases;
- pastas configuradas e caminhos de arquivos;
- temporadas, episódios, versões e disponibilidade;
- posição e status de reprodução;
- avaliações e comentários pessoais;
- referências a capas e metadados públicos;
- itens pendentes de revisão.

Arquivos padrão:

```text
%LOCALAPPDATA%\AniT\Data\anit.db
%LOCALAPPDATA%\AniT\Data\*.log
%LOCALAPPDATA%\AniT\Covers\*.jpg
```

## Acesso à rede

Ao atualizar metadados, o AniT envia ao endpoint GraphQL público do AniList um termo de busca derivado do título do anime. A resposta pode fornecer título em inglês, capa, sinopse e nota média.

Consulte a política e os termos do AniList para entender como esse serviço processa requisições. O AniT não controla a infraestrutura do provedor.

## O que não é enviado pelo AniT

O aplicativo não possui backend próprio e não envia intencionalmente:

- arquivos de vídeo;
- progresso ou avaliações pessoais;
- banco SQLite;
- lista completa de caminhos locais;
- logs de diagnóstico.

Issues no GitHub são públicas. Revise logs e screenshots antes de anexar; eles podem conter nome de usuário, caminhos ou nomes de arquivos.

## Controle e exclusão

Você pode operar offline depois que os dados necessários estiverem em cache. Para excluir todos os dados do AniT, feche o aplicativo e remova `%LOCALAPPDATA%\AniT`. Isso não remove sua mídia original.

## Futuras integrações

Qualquer proposta de conta, nuvem ou telemetria deve ser opcional, documentada e discutida publicamente antes da implementação. Esta página deverá ser atualizada no mesmo pull request.
