# Roadmap

Este documento comunica direção, não datas garantidas. Prioridades podem mudar conforme feedback, bugs e disponibilidade de contribuidores.

## Agora — fundação confiável

- [x] Biblioteca local com SQLite.
- [x] Scanner incremental de vídeos e subpastas.
- [x] Parsing de nomes comuns e fila de revisão.
- [x] Reconhecimento de arquivos movidos e duplicatas físicas.
- [x] Capas, sinopse e nota pública via AniList com cache local.
- [x] Progresso de reprodução e conclusão por episódio.
- [x] Avaliação e comentário por episódio.
- [x] Organizador com prévia, sidecars e prevenção de sobrescrita.
- [x] Busca instantânea por título principal e título em inglês.
- [x] Eliminar testes placeholder e ampliar cobertura de domínio/infraestrutura.
- [ ] Adicionar automação de interface WPF para os fluxos críticos.
- [ ] Auditoria de acessibilidade, navegação por teclado e contraste.
- [x] Logs rotativos e diagnóstico exportável com controles de privacidade.
- [ ] Empacotamento reproduzível para Windows x64.

## Próximo — experiência de biblioteca

- [x] Expandir a busca para episódios, aliases e filtros combinados.
- [x] Filtros por gênero, ano, status, estúdio e ordenação.
- [x] Favoritos persistentes, tags e coleções personalizadas atribuíveis e filtráveis.
- [x] Calendário local de episódios e atividade.
- [x] Histórico navegável com retomada e comentários.
- [x] Edição, mesclagem e separação manual de títulos e episódios.
- [x] Importação/exportação de backup pela interface.
- [x] Scans incrementais na inicialização e em segundo plano.

## Depois — distribuição e ecossistema

- [ ] Instalador e atualização segura com canal estável.
- [ ] Build portátil documentado.
- [ ] Localização da interface e documentação em inglês.
- [x] Temas e opções de acessibilidade visual.
- [ ] Contrato estável para novos provedores de metadados.
- [ ] Suporte configurável a players externos.
- [ ] Telemetria somente se for opcional, transparente e aprovada pela comunidade.

## Fora de escopo por enquanto

- Streaming ou hospedagem de mídia.
- Download de conteúdo protegido.
- Sincronização obrigatória em nuvem.
- Exclusão automática de arquivos duplicados.
- Reorganização silenciosa da biblioteca.

## Como propor algo

Antes de implementar uma funcionalidade grande:

1. abra uma issue de proposta;
2. descreva problema, público afetado e alternativa mínima;
3. indique impacto em privacidade, arquivos locais e compatibilidade;
4. aguarde alinhamento sobre escopo e arquitetura.

Uma ideia no roadmap não dispensa esse alinhamento. Pull requests menores, testáveis e incrementais têm mais chance de revisão rápida.
