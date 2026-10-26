# Etapa 7 · Biblioteca, detalhes e cards

A Biblioteca e os detalhes usam agora a base Cinematográfica, com superfícies, tipografia, acento, raio e foco nos temas claro/escuro. Mantêm WPF e os contratos atuais de navegação e serviços.

## Composição

- Biblioteca: títulos fora das capas, progresso compartilhado, favorito com nome acessível e abertura por botão nativo. Filtros começam recolhidos, têm rolagem própria e continuam ativos quando recolhidos. Busca, ordenação, abas de pastas/revisão/duplicatas/indisponíveis e organização continuam nos handlers existentes.
- A grade usa linhas de pôsteres com `ListBox`, `VirtualizingStackPanel` e reciclagem. Só as linhas próximas do viewport são materializadas. A distribuição acompanha a largura disponível e as preferências de tamanho/quantidade de cards. Redimensionar preserva a consulta e tenta manter o deslocamento vertical; alterar filtros volta ao início.
- Detalhes: banner com altura natural, título sem corte, ações que quebram linha, imagem separada e painel lateral que passa para baixo em larguras menores. A sinopse também reposiciona a imagem para liberar espaço ao texto.
- Episódios: um template responsivo, com progresso, reprodução, cinco notas textuais, comentário e marcar como assistido. Mantém IDs, eventos e bindings dos fluxos existentes. Removidas as grandes imagens de reação e animações fixas desse template.
- O recorte acontece no conteúdo dos painéis, preservando o traço externo. Nenhum efeito pesado contínuo foi adicionado.

## Componentes

`AniT.ActionButton`, `AniT.SecondaryButton`, `AniT.RatingButton`, `AniT.PosterFrame`, `AniT.PosterAction` e `AniT.Progress` ficam em `AniT.Presentation/Themes/Components.xaml`. Home e catálogo compartilham progresso e ação de pôster. Temas permanecem no escopo de cada janela; os defaults legados do aplicativo não são substituídos globalmente.

## Evidências

O runner `AniT.UiChecks --check-catalog` carrega os XAML reais em janelas isoladas com dados fictícios e eventos de serviços removidos, sem abrir o banco/perfis ou reproduzir mídias pessoais.

- 108 combinações: Biblioteca/detalhes × claro/escuro × interface 80/100/125% × texto 85/100/140% × 1060×700, 1280×800 e 1920×1080.
- Verifica ausência de extensão horizontal nos scrollers, título expansível, largura útil da sinopse, viewport da Biblioteca e virtualização.
- Coleção sintética de 2.000 títulos: 400 linhas, duas linhas materializadas inicialmente, último item alcançável e agrupamento sem perda/alteração da ordem em três larguras. Layout inicial observado em execução local: 256 ms; diagnóstico isolado, não benchmark comparativo nem promessa de FPS.
- Prévia nativa inspecionada da Biblioteca clara e dos detalhes escuros. Capas fictícias reutilizam a arte original da Home; produção continua usando as capas de cada anime.

Não é uma validação end-to-end de player, alterações persistidas, scanner ou retorno entre janelas com banco real. DPI físico 125/150%, acessibilidade completa e desempenho com biblioteca real permanecem na rodada final. Não houve instalador nem push.

## Regressão final

Restore com lock e build Release aprovados, sem avisos. 183 testes funcionais aprovados, cobertura de linhas 73,15% (piso 70%). Checks adicionais aprovados: Home 180, shell 108, sistema visual 162 e 16 paletas, piloto 324 e 24 viewports lógicos, onboarding 144 configurações com quatro etapas e recorte da Home. Detalhes também conferidos visualmente no tema claro após o ajuste da sinopse.
