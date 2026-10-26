# Etapa 1 — direção visual do AniT

Status: direção **Cinematográfica aprovada pelo usuário em 26/10/2026**. A proposta Essencial permanece apenas como alternativa exploratória registrada.

## Objetivo

Dar ao AniT a clareza e o acabamento de uma interface moderna de biblioteca de mídia: conteúdo em primeiro plano, ações reconhecíveis e componentes consistentes. A linguagem visual se aproxima de aplicações React bem construídas, mantendo a arquitetura C#/WPF atual.

Esta etapa entrega uma proposta de Home, sidebar e cards. Não instala bibliotecas, não substitui telas do aplicativo e não gera instalador. A integração com WPF UI pertence à etapa 3 do plano.

## Direções propostas

### Cinematográfica — direção escolhida

- Sidebar com ícone e rótulo, seleção discreta e grupos separados por espaço.
- Banner editorial dedicado ao episódio em andamento, com uma ação principal e uma secundária.
- Cards em formato de pôster; a capa dá identidade ao título, o texto e o progresso ficam fora da imagem.
- O Baki-Pi aparece pequeno e sem moldura no rodapé da navegação. A arte do anime permanece como protagonista.
- Indicada para a Home, mantendo a sensação de coleção pessoal e de descoberta.

### Essencial — alternativa mais direta

- Navegação lateral compacta por ícones, com nomes acessíveis.
- Retomada em uma faixa horizontal; a coleção aparece mais cedo, sem um banner dominante.
- Miniaturas em formato horizontal, com título, progresso e ações consistentes.
- Indicada para quem prioriza localizar e abrir episódios rapidamente.

O usuário escolheu a composição Cinematográfica. A transformação seguirá o banner editorial, a sidebar com rótulos e os cards de pôster dessa direção. A alternativa Essencial não será aplicada automaticamente à Biblioteca.

## Fundamentos visuais

### Cores

| Papel | Tema escuro | Tema claro |
| --- | --- | --- |
| Fundo | `#0C1017` | `#F5F7FA` |
| Sidebar | `#10151E` | `#FFFFFF` |
| Superfície | `#171E29` | `#FFFFFF` |
| Superfície elevada | `#1E2735` | `#EEF2F7` |
| Texto principal | `#EDF2F8` | `#172334` |
| Texto secundário | `#9AAABD` | `#536477` |
| Divisor | `#273140` | `#DCE3EB` |
| Ação principal | `#3B82F6` | `#3B82F6` |

O azul destaca seleção, progresso e ação principal. Grandes áreas permanecem neutras. Bordas e sombras são discretas; não há contornos luminosos repetidos. A cor de texto sobre o botão principal é escolhida pelo contraste da cor de destaque.

### Tipografia e espaçamento

- Segoe UI como base nativa; títulos com peso semibold, texto normal e legendas legíveis.
- Referências de tamanho: título de página 26, título de seção 18, corpo 14, metadados 11–12 DIPs. São referências para a futura implementação, sujeitas à escala configurada pelo usuário.
- Escala de espaço: 4, 8, 12, 16, 24 e 32 DIPs; na proposta, margem principal de 28 e distância de 16 entre cards.
- Cantos: 8 DIPs em botões, 14 em cards e 17 no destaque editorial.
- Conteúdo e contorno devem ter camadas de recorte separadas. Elementos decorativos não podem cortar texto, progresso ou foco.

### Imagens, ícones e Baki-Pi

- Preservar proporção e transparência do Baki-Pi. Não colocá-lo no template de capa de anime.
- As capas podem preencher o quadro; títulos e ações essenciais ficam disponíveis fora da arte.
- Ícones com traço e dimensões consistentes. Os ícones Lucide desta proposta são referências visuais; a seleção do conjunto definitivo de WPF ocorrerá na avaliação da biblioteca.
- As capas e os títulos apresentados são ilustrações fictícias feitas para a proposta. Não representam metadados, progresso ou arquivos reais do usuário.
- O Baki-Pi utiliza o recurso existente do próprio repositório, referenciado por revisão imutável. Nenhuma nova arte de terceiros foi incorporada.

## Comportamento demonstrado

- Alternância entre Início, Biblioteca e Favoritos.
- Busca local entre os quatro títulos ilustrativos e filtro por gênero.
- Favoritar/desfavoritar, compartilhando a seleção entre as duas direções.
- Painel de detalhes, fechamento por Escape e contenção do foco do teclado enquanto aberto.
- Botão de retomada demonstra apenas o feedback visual; não abre vídeos.
- Hover com deslocamento de 3 px, fade de 160 ms e abertura de painel em 180 ms.
- Respeito a `prefers-reduced-motion`; não há movimento contínuo do mascote.
- Controles opcionais da prévia permitem avaliar tema, respiro, raio e movimento.

Calendário, histórico e conquistas aparecem como contexto de navegação, mas não são interativos nesta etapa. A configuração e a reorganização definitivas da navegação serão tratadas depois.

## Critérios para aplicar a direção escolhida

1. Direção Cinematográfica escolhida pelo usuário; os detalhes serão refinados preservando essa composição.
2. Confirmar que sidebar, texto e cards permanecem legíveis em 1280×720, 1920×1080 e ultrawide, incluindo escalas de 100%, 125% e 150% no aplicativo.
3. Garantir que as ações possam ser realizadas por teclado e não dependam de hover.
4. Preservar a correção de transparência e contornos da Home.
5. Medir CPU e fluidez antes de acrescentar efeitos, pois a Home usa renderização por software como correção de compatibilidade.
6. Direção escolhida e inventário da etapa 2 registrado; a prova de integração do WPF UI é o próximo passo técnico.

## Validação desta entrega

O arquivo `home-concept.html` é um fragmento de prévia, usado na apresentação em conversa. Foram verificadas suas interações de busca, favoritos, painel de detalhes e Escape, além da adaptação para larguras menores. Essa verificação de navegador não substitui testes de DPI, acessibilidade e desempenho do aplicativo WPF.

Os arquivos de implementação das telas, as dependências e o fluxo de distribuição do AniT não são alterados por esta etapa.
