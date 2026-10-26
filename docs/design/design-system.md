# Etapa 4 — base visual Cinematográfica

Implementação de 26/10/2026. A fonte compartilhada passa a ser `src/AniT.Presentation`; o projeto Pilot consome essa biblioteca e funciona como galeria. Shell, Home e Biblioteca continuam com sua composição atual até as etapas 5–7.

## Contrato de integração

`AppearanceOptions` contém somente preferências visuais, sem persistência ou serviços. `IAppearanceHost.ApplyAppearance` recebe tema, acento, escala da interface, escala do texto, raio e redução de movimento. O adapter em `AppearanceManager` traduz as configurações existentes.

Cada nova janela cria uma instância de `CinematicTheme` antes de InitializeComponent, implementa IAppearanceHost e aplica as opções depois que o conteúdo existir. Exemplo:

```csharp
public partial class CollectionWindow : Window, IAppearanceHost
{
    private readonly CinematicTheme theme;
    public CollectionWindow()
    {
        theme = new CinematicTheme(this);
        InitializeComponent();
        theme.Apply(new());
    }
    public void ApplyAppearance(AppearanceOptions options) => theme.Apply(options);
}
```

O construtor marca `AppearanceScope.IsManaged`, propriedade herdada pelos descendentes. O gerenciador antigo não altera FontSize, LayoutTransform, foreground, raio ou imagens dentro dessa fronteira. Janelas anteriores continuam no caminho legado. Não marcar uma janela antiga parcialmente migrada como gerenciada sem assumir todas as suas preferências visuais.

Tema WPF UI → dicionário de componentes (que inclui controles WPF UI) → valores semânticos da janela. Nenhum dicionário WPF UI é instalado em App.xaml. Uma aplicação idêntica das preferências reutiliza os recursos; o tema Sistema é reavaliado quando o adapter reaplica a aparência. Acompanhamento contínuo de mudança do Windows com a janela inativa fica para a integração do shell.

## Tokens

| Grupo | Chaves e regras |
|---|---|
| Superfícies | AniT.Background, Sidebar, Surface, Raised |
| Informação | AniT.Text, Muted, Border, Error, Success, Warning |
| Ação | AniT.Accent, AccentHover, AccentPressed, OnAccent |
| Tipografia | AniT.TitleSize 28, SectionSize 19, BodySize 14, CaptionSize 12 DIPs, multiplicados uma vez pela escala do texto |
| Espaços | AniT.Space.Xs/Sm/Md/Lg/Xl/Xxl: 4/8/12/16/24/32 DIPs |
| Cantos | AniT.Radius: preferência 0–32; AniT.ControlRadius: metade desse valor |
| Movimento | AniT.MotionDuration: 160 ms ou zero; AniT.ReduceMotion e propriedade herdada AppearanceScope.ReduceMotion |

As cores são definidas em CinematicTheme, a fonte de verdade em runtime. Valores XAML são fallback de carregamento, não outra paleta para manutenção. InterfaceScale atua na raiz uma vez. Não multiplicar FontSize individualmente nem aplicar uma segunda transformação no conteúdo. Não usar alturas fixas que prendam texto: controles têm altura mínima, conteúdo expande e páginas rolam verticalmente.

O texto sobre acento usa preto ou branco conforme o maior contraste. Hover e pressionado se afastam da cor do texto para preservar contraste. Os aliases dos controles WPF UI apontam para os mesmos papéis AniT, incluindo limites e foco nos campos e seleções. Há verificações numéricas para texto e estados de acento; isso não equivale a uma certificação de acessibilidade de toda a aplicação.

## Componentes e estados

| Estilo/serviço | Uso |
|---|---|
| AniT.Body / Caption / Section | Texto com cor, tamanho e quebra de linha por papel |
| AniT.Button.Primary / Secondary / Quiet | Ação principal, auxiliar ou complementar; espaço externo pertence ao layout consumidor |
| AniT.Input | TextBox WPF UI, validação IDataErrorInfo, mensagem textual separada e borda de erro |
| AniT.Select | ComboBox, altura mínima 42 DIPs |
| AniT.Toggle | Switch WPF UI; template estático automático quando movimento reduzido está ligado |
| AniT.Panel / Card / EmptyState | Superfícies, conteúdo com Padding e borda preservada; não aplicam clip no próprio contorno |
| AniT.Status.Error / Success | Mensagem com texto explícito e anúncio acessível, sem depender só de cor |
| AniT.NavigationItem | RadioButton com seleção, indicador, hover, desabilitado e foco; o shell deve conectar navegação e nome acessível |
| AniT.Icon | SymbolIcon WPF UI, tamanho 20 e cor semântica; usar a família Fluent do pacote em novas telas |
| AniT.Focus | Contorno de foco de 2 DIPs, afastado do controle, sem glow ou animação contínua |
| CinematicDialog | Diálogo simples com dois botões, bloqueio do fundo, foco inicial, Tab/Shift+Tab, Enter/Escape e retorno do foco |

O diálogo conserva a estrutura de ContentDialog upstream. Seu serviço é para mensagens/confirmações simples: formulários, PIN e fluxos destrutivos precisam preservar seus contratos próprios na etapa 9. Não substituir esses fluxos por esta confirmação genérica.

Cards de pôster/episódio ainda serão compostos com dados reais na etapa 7. Os estilos atuais são os blocos de superfície, tipografia e ação que esses cards utilizarão. Sem shadow/blur/glow contínuo; efeitos e transições adicionais pertencem à etapa 8 e devem consumir a preferência de movimento.

## Galeria e verificação

```powershell
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-fluent
dotnet run --project tests/AniT.UiChecks -c Release -- --check-design-system
dotnet run --project tests/AniT.UiChecks -c Release -- --check-fluent
```

O check novo inicializa AppearanceManager com configuração injetada, sem ler perfis/banco nem iniciar App.OnStartup. Verifica 162 combinações com os handlers legados ativos, reaplicações, fontes sem valores locais acidentais, escala única, raios e movimento. Contraste conferido em 16 paletas (dois temas × oito acentos), incluindo estados de acento normal/hover/pressionado, além do contrato de teclado e isolamento dos recursos.

Revisão no desktop a DPI 100%: galeria nos dois temas, rolagem e contornos; Tab/Shift+Tab com foco visível entre os botões do diálogo; Escape restaura o fundo e retorna ao botão; Enter reabre/confirma. A árvore de acessibilidade do helper manteve um campo anterior como focused_element em alguns snapshots; a verificação visual e o comportamento do teclado confirmaram o foco real. Não inferir foco apenas desse campo do helper.

Continuam pendentes os testes em DPI físico 125/150%, a navegação completa do futuro shell e o desempenho com coleções grandes. A matriz lógica do piloto não substitui esses testes. O fallback SoftwareOnly da Home permanece restrito àquela janela.

WPF-UI/Abstractions permanecem fixados em 4.3.0 com lockfiles. O App agora referencia a camada Presentation para o adapter; as licenças transitivas são copiadas para `Licenses/WpfUi` e os avisos centrais foram atualizados. Nenhum instalador ou push faz parte desta etapa.

## Resultado da validação final

- Restore em modo locked e build Release: aprovados, zero avisos/erros.
- Suíte funcional: 183 aprovados; cobertura de linhas 73,15% na suíte instrumentada (piso do repositório: 70%). Os checks WPF são um executável separado e não entram nesse percentual.
- Design system: 162 combinações com o gerenciador legado ativo e contraste nas 16 paletas aprovados.
- Piloto: 324 combinações e 24 viewports lógicos aprovados.
- Regressão: 144 configurações do onboarding, quatro etapas cada, e checks da Home aprovados.
- Preflight de assinatura: sete verificações aprovadas, sem gerar artefatos de distribuição.
- Auditoria NuGet da nova camada: nenhuma vulnerabilidade conhecida reportada pela fonte consultada.

## Integração parcial da etapa 5

CinematicTheme também aceita um FrameworkElement com `scaleInterface: false` para controles dentro de janelas legadas. AppearanceManager reconhece IAppearanceHost durante a travessia e entrega as preferências sem uma segunda escala. O shell utiliza esse contrato; implementação e validação em [cinematic-shell.md](cinematic-shell.md).
