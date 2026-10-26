# Etapa 6 — Home Cinematográfica, clara e escura

26/10/2026. Base: shell compartilhado da etapa 5 (`5534f38`).

## Composição entregue

A Home passa a adotar o design system em toda a janela, com superfícies neutras, tipografia por papel, ação principal evidente e títulos/progresso fora das imagens. O azul fica concentrado em ação, seleção e progresso. Foram retirados os glows, o blur de capa ampliada e a animação de entrada com duração fixa.

O banner mostra o episódio real em andamento quando disponível; sem destaque, oferece **Abrir biblioteca**, com orientação para adicionar uma pasta e uma ilustração original criada com ImageGen. A arte tem referências a Naruto e One Piece; não substitui as capas dos títulos reais. Prompt e proveniência ficam em `assets/home/README.md`.

Continuar assistindo, recentes, favoritos e mais bem avaliados mantêm as coleções e handlers existentes. Os pôsteres usam título, informação secundária e avaliação fora da capa. Estados vazios possuem explicação textual e uma ação útil. O resumo da estante e os próximos episódios continuam disponíveis.

## Claro e escuro

Fundo, banner, painéis, títulos, texto secundário, botões, foco e barras de progresso usam recursos semânticos. A Home não conserva um fundo azul escuro fixo quando o usuário escolhe o tema claro. A arte é independente da superfície de leitura; não há texto sobreposto à paisagem, evitando depender do contraste da imagem.

O contraste automatizado percorre TextBlocks dos templates sobre os fundos sólidos desenhados por Border/Panel. Não usa o Background de um Control quando seu template não o pinta, pois isso produziria falsos resultados. Textos habilitados são conferidos com razão mínima 4,5:1 na matriz da Home. A suíte do design system também cobre o texto sobre oito acentos e seus estados normal/hover/pressionado.

## Preferências e comportamento

- `DashboardWindow` implementa IAppearanceHost e aplica tema/escala uma vez na raiz; sidebar e cabeçalho recebem as mesmas preferências sem uma segunda transformação.
- A ordem das seções, prioridade de favoritos, seções ocultas e limites de itens permanecem no fluxo existente.
- Retomada mantém a preferência entre abrir detalhes e reproduzir. O estado inicial não apresenta um botão principal inerte.
- As preferências de proporção, foco manual e qualidade das capas continuam sendo aplicadas por um caminho explícito aos elementos da Home. Assets de marca/mascote/paisagem preservam sua composição.
- Os indicadores de destaque são botões com nome acessível, acionáveis por teclado. Não há indicadores fictícios quando só existe um destaque.
- Rotação automática é interrompida por movimento reduzido, interação por ponteiro/teclado, desativação ou ocultação da janela. A configuração de rotação e seu intervalo continuam existentes.
- O workaround SoftwareOnly continua restrito à Home. Não foi alterada a renderização do processo ou do Windows.

## Layout e recorte

O layout usa a largura lógica disponível após aplicar a escala. Em espaços menores a arte decorativa cede espaço ao texto, o resumo passa para baixo das seções e favoritos/avaliações ficam empilhados. Conteúdo importante permanece alcançável pela rolagem vertical.

Título e subtítulo do banner quebram linha e não têm MaxHeight que esconda texto. Cartões não fixam a altura da área textual. Imagens podem ter quadro de proporção definida; o recorte arredondado fica no conteúdo, preservando o contorno e o foco externos. A arte inicial usa Uniform para mostrar a paisagem inteira.

## Prévias e checks

```powershell
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-home-light
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-home-populated
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-home-light --preview-home-populated
dotnet run --project tests/AniT.UiChecks -c Release -- --check-home-cinematic
```

As prévias carregam o XAML real com dados fictícios e handlers de serviços removidos; não abrem scanner, banco pessoal ou player. O teste do novo layout usa títulos longos para verificar expansão e ações dentro do banner. A navegação e reprodução reais mantêm seus serviços anteriores; não foram executados contra mídias pessoais nesta revisão.

Revisão no desktop: Home clara vazia e escura populada, banner, arte inteira, progresso, pôsteres, textos externos e rolagem. A versão populada usa títulos ilustrativos e a paisagem como imagem de demonstração, sem representar metadados reais do usuário.

Os tamanhos lógicos incluem 853×480, 1060×700, 1280×720, 1920×1080 e 3440×1440, com escalas de interface 80/100/125% e texto 85/100/140%. Eles não substituem testes em DPI físico 125/150%. Medição comparativa de FPS/CPU com uma biblioteca real e validação física continuam na etapa final; não há alegação de ganho de desempenho medido nesta entrega.

O CI executa o novo check da Home. A próxima etapa é consolidar Biblioteca, detalhes e família de cards. Nenhum instalador ou push faz parte desta entrega.

## Resultado final

- Build Release/restore locked aprovados, zero erros e avisos.
- Home: 180 combinações de estado, tema, viewport e escalas aprovadas, incluindo contraste dos textos habilitados, expansão do título, ações dentro do banner e ausência de overflow horizontal.
- Shell: 108 combinações aprovadas.
- Design system: 162 combinações, contraste em 16 paletas e contrato do diálogo aprovados.
- Piloto: 324 combinações e 24 viewports lógicos aprovados.
- Onboarding: 144 configurações, quatro etapas por configuração, e regressão de contornos/transparência da Home aprovados.
- Funcional: 183 testes aprovados; cobertura de linhas da suíte instrumentada 73,15%. Os checks WPF são separados desse percentual.
- Arte final: assets/home/anime-horizon.png, 2.711.232 bytes; SHA-256 FACAAF6DFA33E9A7D59CF8831FB5D8DE17D8CEE402F8F900EAD18184232ED40D.
