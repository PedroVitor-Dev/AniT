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
- [ ] Eliminar testes placeholder e ampliar cobertura de interface/integração.
- [ ] Auditoria de acessibilidade, navegação por teclado e contraste.
- [ ] Logs estruturados e diagnóstico exportável com dados sensíveis removidos.
- [ ] Empacotamento reproduzível para Windows x64.

## Próximo — experiência de biblioteca

- [ ] Busca real por título, episódio e alias.
- [ ] Filtros por status, nota, pasta e disponibilidade.
- [ ] Favoritos persistentes e coleções personalizadas.
- [ ] Calendário local de episódios e lançamentos.
- [ ] Histórico navegável com retomada e ações em lote.
- [ ] Edição manual de títulos, temporadas e episódios.
- [ ] Importação/exportação de backup pela interface.
- [ ] Melhorias para bibliotecas muito grandes e scans incrementais em segundo plano.

## Depois — distribuição e ecossistema

- [ ] Instalador e atualização segura com canal estável.
- [ ] Build portátil documentado.
- [ ] Localização da interface e documentação em inglês.
- [ ] Temas e opções de acessibilidade visual.
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
