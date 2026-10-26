# Etapa 5 — sidebar e cabeçalho compartilhados

26/10/2026. Base: etapa 4 (`1f7e5d6`).

## Resultado

`CinematicSidebar` e `CinematicHeader` substituem o shell duplicado em sete telas: Início, Explorar, Calendário, Histórico, Perfil, Detalhes e Configurações. O conteúdo central dessas telas permanece para suas respectivas etapas de migração.

Sidebar com marca completa, oito destinos com ícone/rótulo, seleção discreta, foco visível e Baki-Pi pequeno/transparente. A navegação tem rolagem própria; a identidade no rodapé cede espaço quando a altura diminui. A coluna ganha largura mínima proporcional à escala do texto, evitando que os rótulos desapareçam.

Cabeçalho com busca, atalhos e avatar acessível. Em larguras menores, links duplicados cedem espaço para a busca e o perfil. A altura é automática para acompanhar texto ampliado. Configurações conserva seu contrato sem busca global; Detalhes conserva a ação Assistir e o retorno pela pilha existente.

Conquistas mantém o layout específico até a etapa das demais telas. Biblioteca continua uma janela independente, com seu próprio cabeçalho: não foi transformada em página dentro da Home.

## Integração preservada

- `AppNavigation` não foi alterado: abertura/reutilização da Biblioteca, estado das janelas, owners, loading e histórico de detalhes continuam no contrato existente.
- Os handlers de busca/ações específicos de cada página recebem eventos do cabeçalho. Os arquivos `*.Shell.cs` contêm aliases temporários para os controles internos, preservando os consumidores existentes enquanto as páginas são migradas.
- O atalho configurável Search agora encontra o namescope do novo cabeçalho; os nomes anteriores continuam como fallback nas telas com layout próprio.
- Selecionar Biblioteca não marca outra página como ativa na sidebar: a seleção representa a página exibida na janela de origem.
- Avatar continua vindo das configurações de perfil e abre Perfil; a prévia usa somente o recurso empacotado.
- `GlobalSearchController.Attach` continua recebendo TextBox, hint e placement target reais. A consulta e os resultados continuam no serviço existente.

## Aparência parcial

`CinematicTheme` agora aceita FrameworkElement e a opção `scaleInterface: false`. Cada controle do shell possui dicionários locais e uma fronteira AppearanceScope. O gerenciador antigo visita `IAppearanceHost` dentro da árvore e entrega as preferências, sem reinterpretar fontes, imagens e cantos internos.

A escala da interface permanece aplicada uma vez na raiz da janela antiga. O shell aplica apenas seus tokens de texto/tema/raio e não cria outra transformação. As composições anteriores do corpo e o fallback SoftwareOnly restrito à Home foram preservados.

Os vetores existentes do AniT foram reaproveitados em `Styles/ShellIcons.xaml`; não houve geração de novos bitmaps nem substituição da identidade do Baki-Pi. A marca usa o enquadramento já adotado no logo-fire.png, preservando a imagem sem editar seu arquivo.

## Busca da Biblioteca

Reduzir a largura não limpa mais a consulta nem esconde seu campo. A barra de ações passa para uma segunda linha quando falta espaço e permite quebra dos botões. Ctrl+F e o botão explícito de limpar continuam com seus handlers anteriores. A reformulação de cards, filtros e listas fica para a etapa 7.

## Validação

- Restore locked e build Release: zero erros e avisos.
- Shell: 108 combinações de tema, escala da interface, escala do texto e viewport (1060×700, 1280×720, 1920×1080 e 3440×1440).
- Checks incluem manutenção da consulta durante resize/tema, encaminhamento de Enter, atalho configurável de busca, escala única, disponibilidade de Configurações com rolagem, resolução dos vetores, oito destinos e seleção da janela de origem.
- Design system: 162 combinações e 16 paletas aprovadas.
- Piloto: 324 combinações e 24 viewports lógicos aprovados.
- Regressão: 144 configurações do onboarding, quatro etapas cada; contornos e transparência da Home aprovados.
- Funcional: 183 testes aprovados; cobertura da suíte instrumentada 73,15%. Checks WPF são separados e não entram nesse percentual.
- Revisão no desktop: shell isolado e XAML real da Home com dados fictícios, marca e avatar completos, mascote transparente, foco visível por Tab. Nenhuma instância do aplicativo de produção com banco pessoal foi usada na revisão.

Os checks exercitam componentes e handlers encaminhados, sem abrir player ou executar todas as transições reais de páginas com dados pessoais. DPI físico 125/150% e uma rodada completa de navegação com dados de teste continuam para validação final. Os viewports lógicos não representam DPI físico.

## Comandos

```powershell
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-shell
dotnet run --project tests/AniT.UiChecks -c Release -- --check-shell
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-home
```

O CI executa o novo check do shell. A próxima etapa é a Home Cinematográfica: banner, hierarquia editorial e seções centrais. Nenhum instalador foi gerado e nenhum push foi realizado.
