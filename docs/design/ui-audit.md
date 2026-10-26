# Etapa 2 — inventário e diagnóstico da interface

Data: 26/10/2026. Base inspecionada: `64805b7`. Direção aprovada: **Cinematográfica**.

## Escopo e método

Inventário estático dos XAML e code-behind de `src/AniT.App`, excluindo `bin/` e `obj/`, combinado com leitura dos fluxos de entrada, navegação, aparência, busca e verificações de interface. Os números representam ocorrências no código; não representam quantidade de controles em execução nem defeitos comprovados.

O arquivo [ui-inventory.json](ui-inventory.json) contém a contagem por arquivo e evidências com caminho e linha. Não foram lidos bancos, perfis, vídeos ou preferências pessoais. Esta entrega documenta o estado atual; não altera a interface ou instala dependências.

## Resumo

| Indicador | Encontrado | Implicação para a transformação |
| --- | ---: | --- |
| XAML | 44 | 28 janelas, 6 UserControls, 9 dicionários e o Application |
| Estilos | 120 | Distribuídos entre recursos globais, HomeTheme e estilos locais |
| ControlTemplates / DataTemplates | 54 / 41 | Revisar contratos de interação antes de substituir controles |
| Cores literais em atributos | 1.522 | 1.486 estão em janelas/controles; migrar gradualmente para tokens |
| Atributos de altura numérica | 403 | Incluem espaçadores e imagens; priorizar os que restringem texto e conteúdo variável |
| ClipToBounds=True | 55 | Verificar contorno e conteúdo separadamente |
| ScrollViewers / WrapPanels | 39 / 32 | Mapear quem é dono da rolagem e medir coleções maiores |
| Efeitos de sombra / blur | 45 / 5 | Quantidades em XAML; não medem custo de GPU/CPU ou instâncias ativas |
| Storyboards | 30 | Nem todos usam os recursos centrais de duração |
| FocusVisualStyle removido / triggers explícitos de foco | 32 / 7 | Sinal de revisão, não prova de inacessibilidade; conferir cada template com teclado |
| Janelas que chamam FitToWorkArea | 15 de 28 | As demais podem ter estratégias próprias; conferir por tela |

Os sete estilos `AniT.Text.*` de `Themes/Typography.xaml` têm **zero referências StaticResource/DynamicResource nos XAML inspecionados**. Os tamanhos continuam definidos localmente. Isso não é uma busca por referências dinâmicas em C# nem prova de que todo recurso seja dispensável.

## Como a UI está organizada

### Entrada e navegação

- `App.OnStartup` escolhe entre onboarding e seleção de perfil, inicializa os serviços e abre `DashboardWindow`.
- `AppNavigation` troca a janela principal entre Home, Explorar, Calendário, Histórico, Perfil, Conquistas e Configurações. Detalhes mantêm uma pilha de retorno.
- A Biblioteca é intencionalmente uma janela secundária reutilizável. Adotar um controle NavigationView na etapa 3 não autoriza transformar todos esses fluxos em uma única página agora.
- A máscara `NavigationLoadingWindow` acompanha os limites e o DPI da janela de origem. `EnableNavigationLoading`, tempo configurado e redução de movimento fazem parte do contrato atual.
- `MainWindow.xaml` contém uma Home anterior, com outra paleta. Não foi encontrada abertura por `new MainWindow` ou `StartupUri` no fluxo inspecionado. Tratar como candidato a legado, validar referências antes de remover.

### Três camadas de aparência

1. `App.xaml` carrega os oito dicionários `Themes/*`: cores, tipografia, efeitos, animações, botões, cards, inputs e imagens.
2. `Styles/HomeTheme.xaml` acrescenta a linguagem Home: outra família de brushes, navegação, links, cards e ícones; é importada diretamente por 18 XAML de janelas/controles.
3. As telas declaram estilos locais e muitos valores literais. `AppearanceManager` ainda percorre a árvore visual para aplicar escala, texto, cores e raio.

Na integração futura, não carregar indiscriminadamente defaults do WPF UI acima dessas três camadas. Primeiro testar resolução de recursos, precedência dos estilos e preferências existentes em uma superfície isolada.

## Catálogo de telas

Dimensões são valores declarados em XAML, em DIPs. O tamanho final depende de maximização, área útil, ajustes em C# e escala. A coluna de adaptação descreve o mecanismo encontrado, não uma certificação de responsividade.

| Arquivo | Grupo | Declarado / mínimo (L×A) | Adaptação encontrada | Migração |
| --- | --- | --- | --- | --- |
| [AchievementDetailWindow.xaml](../../src/AniT.App/AchievementDetailWindow.xaml) | Diálogo / operação | 720×520 / 620×460 | Layout declarado; conferir estratégia específica | 9 |
| [AchievementSettingsWindow.xaml](../../src/AniT.App/AchievementSettingsWindow.xaml) | Diálogo / operação | 560×520 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [AchievementsWindow.xaml](../../src/AniT.App/AchievementsWindow.xaml) | Principal / conquistas | 1500×920; maximizada / 1080×700 | Layout declarado; conferir estratégia específica | 9 |
| [AchievementToastWindow.xaml](../../src/AniT.App/AchievementToastWindow.xaml) | Notificação | 470×126 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [AnimeDetailsWindow.xaml](../../src/AniT.App/AnimeDetailsWindow.xaml) | Principal / detalhes | 1440×900; maximizada / 1060×700 | FitToWorkArea + breakpoints próprios | 7 |
| [AnimeOrganizationWindow.xaml](../../src/AniT.App/AnimeOrganizationWindow.xaml) | Diálogo / operação | 680×610 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [AniTNotificationToastWindow.xaml](../../src/AniT.App/AniTNotificationToastWindow.xaml) | Notificação | 440×126 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [CalendarWindow.xaml](../../src/AniT.App/CalendarWindow.xaml) | Principal / calendário | 1520×940; maximizada / 1080×720 | FitToWorkArea + breakpoints próprios | 9 |
| [CatalogCorrectionWindow.xaml](../../src/AniT.App/CatalogCorrectionWindow.xaml) | Diálogo / operação | 860×670 / 720×560 | FitToWorkArea | 9 |
| [CorrectAssociationWindow.xaml](../../src/AniT.App/CorrectAssociationWindow.xaml) | Diálogo / operação | 560×410 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [DashboardWindow.xaml](../../src/AniT.App/DashboardWindow.xaml) | Principal / Home | 1440×900; maximizada / 1060×700 | FitToWorkArea + breakpoints próprios | 5–6 |
| [EpisodeCommentWindow.xaml](../../src/AniT.App/EpisodeCommentWindow.xaml) | Diálogo / operação | 640×480 / 560×430 | Layout declarado; conferir estratégia específica | 9 |
| [ExploreWindow.xaml](../../src/AniT.App/ExploreWindow.xaml) | Principal / Explorar | 1440×900; maximizada / 1060×700 | FitToWorkArea + breakpoints próprios | 9 |
| [HistoryWindow.xaml](../../src/AniT.App/HistoryWindow.xaml) | Principal / histórico | 1520×940; maximizada / 1080×720 | FitToWorkArea + breakpoints próprios | 9 |
| [LibraryWindow.xaml](../../src/AniT.App/LibraryWindow.xaml) | Biblioteca secundária | 1180×760 / 720×520 | FitToWorkArea + UpdateCardLayout; busca muda abaixo de 920 | 7 |
| [MainWindow.xaml](../../src/AniT.App/MainWindow.xaml) | Candidata a legado | 1280×780 / 1024×650 | FitToWorkArea | 4–5; validar legado |
| [MediaVersionsWindow.xaml](../../src/AniT.App/MediaVersionsWindow.xaml) | Diálogo / operação | 760×520 / 640×430 | FitToWorkArea | 9 |
| [NavigationLoadingWindow.xaml](../../src/AniT.App/NavigationLoadingWindow.xaml) | Transição de navegação | —×— / —×— | Bounds e DPI da janela de origem | 5 e 8 |
| [OnboardingWindow.xaml](../../src/AniT.App/OnboardingWindow.xaml) | Entrada | 1040×740 / 580×420 | FitToWorkArea + tamanho da raiz após escala | 9 |
| [OrganizationPreviewWindow.xaml](../../src/AniT.App/OrganizationPreviewWindow.xaml) | Diálogo / operação | 1040×720 / 760×560 | FitToWorkArea | 9 |
| [ProfileBannerPickerWindow.xaml](../../src/AniT.App/ProfileBannerPickerWindow.xaml) | Diálogo / operação | 1120×760 / 860×620 | Layout declarado; conferir estratégia específica | 9 |
| [ProfileEditWindow.xaml](../../src/AniT.App/ProfileEditWindow.xaml) | Diálogo / operação | 760×720 / 700×600 | Limita a altura pela área útil em C# | 9 |
| [ProfilePinWindow.xaml](../../src/AniT.App/ProfilePinWindow.xaml) | Diálogo / operação | 430×330 / —×— | Layout declarado; conferir estratégia específica | 9 |
| [ProfileSelectionWindow.xaml](../../src/AniT.App/ProfileSelectionWindow.xaml) | Entrada | —×—; maximizada / —×— | Layout declarado; conferir estratégia específica | 9 |
| [ProfileWindow.xaml](../../src/AniT.App/ProfileWindow.xaml) | Principal / perfil | 1520×940; maximizada / 1080×720 | FitToWorkArea + breakpoints próprios | 9 |
| [ReviewMatchWindow.xaml](../../src/AniT.App/ReviewMatchWindow.xaml) | Diálogo / operação | 660×570 / 580×500 | FitToWorkArea | 9 |
| [SetupShelfWindow.xaml](../../src/AniT.App/SetupShelfWindow.xaml) | Diálogo / operação | 620×470 / —×— | FitToWorkArea | 9 |
| [SystemSettingsWindow.xaml](../../src/AniT.App/SystemSettingsWindow.xaml) | Principal / configurações | 1440×900; maximizada / 1060×700 | FitToWorkArea | 9 |

### Controles compartilhados existentes

| Controle | Papel | Destino proposto |
| --- | --- | --- |
| `NavItemContent` | Ícone e rótulo da navegação | Adaptar ao shell cinematográfico, preservando rótulo e área de clique |
| `IconTile` | Ícone em marcador visual | Simplificar o contêiner; manter apenas quando útil à hierarquia |
| `ProfileAvatarButton` | Acesso ao perfil | Preservar serviço/ação; normalizar avatar, foco e menu |
| `SystemSettingsNavButton` | Entrada para configurações | Reutilizar a ação na navegação unificada |
| `GlobalSearchResultsView` | Resultados e estados da busca | Reestilizar como um único componente de popup; manter teclado e resultados |
| `ShelfSettingsControl` | Configuração da estante | Manter comportamento e segurança; padronizar campos, feedback e ações |

## Famílias que precisam ser unificadas

| Família | Implementações atuais / evidência | Direção |
| --- | --- | --- |
| Botão principal | `BrandButton`, `AniT.Button.*`, botões de entrada e estilos de Setup/Settings/MainWindow | Uma hierarquia de ação principal, secundária, discreta e destrutiva |
| Navegação | HomeTheme + sidebar copiada em várias janelas + MainWindow antigo | Um shell visual reutilizável; preservar o contrato de janelas até a etapa 5 |
| Cards | `AniT.Card.*`, `HomeCardButton`, `AnimeCard`, templates de recentes/favoritos/ranking | Família de pôster cinematográfico com título/progresso fora da capa e estado selecionado consistente |
| Títulos e legendas | `AniT.Text.*` sem adoção direta; `HomeSectionTitle`, `SummaryLabel`, títulos locais | Tipografia por papel; validar escala antes de substituir valores |
| Inputs | `AniT.Input.*`, caixas de busca próprias, `SettingsTextBox`, PIN e entradas locais | Templates com foco, erro, desabilitado e leitura consistente |
| Painéis | `SettingsPanel`, `ManagementCard`, borders locais e contornos azuis | Superfícies neutras, divisores discretos e recorte interno |
| Ícones | Geometrias `AniT.Icon.*`, ícones locais e glifos em texto | Uma família por função; validar cobertura e licença antes de trocar |
| Estados vazios | Conteúdo específico em Home, Biblioteca, busca e perfis | Preservar mensagem útil e ação; composição comum e mascote opcional |
| Notificações e diálogos | Toasts, janelas próprias e MessageBox em fluxos operacionais | Separar toast, confirmação, progresso e erro; sem enfraquecer confirmações de arquivo/PIN/backup |

A inspeção encontrou um par de estilos estruturalmente idênticos, ignorando somente a chave do estilo: `SummaryLabel` de Calendar e `HistorySummaryLabel` de History. As demais linhas acima são duplicações de **responsabilidade**, não afirmações de XML idêntico. Não foram encontradas chaves de estilos nomeados repetidas entre arquivos.

## Achados e pontos de atenção

### P1 — escala, tipografia e espaço disponível

`AppearanceManager` aplica LayoutTransform na raiz e multiplica FontSize de TextBlocks. As páginas grandes calculam vários breakpoints a partir da largura externa da janela; o onboarding já usa o espaço da raiz após escala. Convergir para uma medição consistente. Alturas fixas, LineHeight e textos grandes devem ser verificados em conjunto, sem reduzir a fonte para esconder overflow.

Além disso, o percurso global de aparência não trata todas as classes de texto do mesmo modo: TextBox, Label, itens templados e TextBlock precisam ser incluídos no piloto. Não considerar uma escala correta apenas porque o título ficou maior.

### P1 — recorte ainda diferente em cinco páginas

Home já usa `RoundedPanelClip` para recortar o filho e preservar o contorno. `AnimeDetailsWindow`, `CalendarWindow`, `ExploreWindow`, `HistoryWindow` e `ProfileWindow` ainda atribuem RectangleGeometry ao próprio Border. Registrar esses cinco casos como migração de segurança visual antes de reutilizar os templates nas novas telas. Sua presença não prova um corte visível em todas as configurações.

### P1 — transparência e modo de renderização

Preservar o desembrulhamento de BitmapImage/TransformedBitmap/CroppedBitmap e a distinção entre mascote e capa no banner. Home tem `StableWindowRendering.Enabled=True`, limitado ao HwndTarget da própria janela, após reprodução dos artefatos da GPU. O custo de CPU/fluidez não foi medido neste inventário. Não remover o workaround nem ampliar sua aplicação sem comparar os mesmos cenários.

### P1 — foco e estado durante navegação/redimensionamento

- Revisar os 32 casos que removem FocusVisualStyle; alguns templates fornecem foco alternativo, outros exigem inspeção. Não concluir o resultado a partir do número de triggers.
- A Biblioteca esconde o banner de busca abaixo de 920 DIPs e limpa o texto se houver busca ativa (`Window_SizeChanged`). Registrar o comportamento e decidir sua preservação/correção na migração, em vez de apagar filtros como efeito colateral de um novo layout.
- Preservar os atalhos configuráveis do `ShortcutManager`, fechamento/retorno de detalhes, owner, ordem de Tab e estados de PIN.

### P2 — movimento e efeitos

`ReduceMotion` ajusta os recursos centrais de duração, mas há Storyboards com Duration literal, animações em C# e PopupAnimation.Fade na busca. A ausência da palavra ReduceMotion no code-behind de uma janela não é prova de falha; seguir a cadeia de recursos e validar em execução. Padronizar as durações antes de ampliar os efeitos.

### P2 — densidade e listas

Na Biblioteca, a coleção de animes usa ItemsControl + WrapPanel dentro de rolagem e o tamanho do card é recalculado por `UpdateCardLayout`. Não foram encontrados atributos explícitos de virtualização nos XAML inventariados. Isso não exclui comportamentos padrão de outros controles, como ListBox. Medir o caminho da Biblioteca com coleções maiores antes de escolher painel virtualizado, paginação ou carregamento incremental; não inventar metas de FPS sem baseline.

### P2 — fontes mínimas, tema claro e contraste

Há tamanhos literais de 8, 9 e 10 DIPs, além de fontes decorativas maiores. Identificar quais são texto de conteúdo e quais são glifos/ilustrações antes de normalizar. Cores literais e ajustes automáticos de foreground também devem ser comparados nos três temas. Esta auditoria não mediu contraste de todas as combinações.

## Proteções para a próxima etapa

- Preservar `ResponsiveWindow`, `ComfortableImageSource`, `RoundedPanelClip`, `StableWindowRendering`, busca e atalhos como contratos explícitos.
- Não misturar duas bibliotecas completas de tema. WPF UI é a candidata principal; Material Design permanece alternativa, não uma segunda camada de defaults.
- O visual Cinematográfico foi escolhido. A alternativa Essencial fica apenas como registro de exploração; não aplicar sua navegação compacta automaticamente à Biblioteca.
- Troca de componentes visuais não altera scanner, perfis, PIN, banco, backup, organização ou player.
- Lottie e efeitos avançados continuam posteriores ao piloto e às medições.

## Cobertura existente e lacunas

`AniT.UiChecks` cobre quatro etapas do onboarding em 144 combinações de layout lógico, além de transparência, modo de renderização e recorte da Home com dados fictícios. Ele não certifica DPI físico de monitores, navegação real com banco/PIN, todos os controles de terceiros ou todas as outras janelas.

A suíte de domínio/infraestrutura protege os comportamentos do produto, mas não substitui uma matriz visual. Os resultados executados para esta entrega são registrados no backlog de migração.

Ver [ui-migration-backlog.md](ui-migration-backlog.md) para prioridades, dependências e critérios de conclusão da etapa 3.
