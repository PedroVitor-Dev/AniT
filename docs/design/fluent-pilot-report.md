# Etapa 3 — piloto WPF UI

26/10/2026. Prova técnica isolada da direção Cinematográfica. Adoção viável para continuar a etapa 4; a migração global ainda depende dos itens de validação abaixo.

## Escopo e execução

Projeto `src/AniT.Presentation.Pilot`, sem referência a App, Core, Infrastructure, scanner, banco ou perfis. O executável de desenvolvimento `tests/AniT.UiChecks` hospeda a janela. O aplicativo de produção não recebe referência ao pacote nem alteração de startup, navegação ou renderização.

```powershell
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-fluent
dotnet run --project tests/AniT.UiChecks -c Release -- --check-fluent
dotnet run --project tests/AniT.UiChecks -c Release -- --benchmark-fluent
```

Button, TextBox com IDataErrorInfo, ComboBox, ToggleSwitch e ContentDialog funcionam com dados efêmeros. A sidebar é contexto visual, sem navegação implementada. O tema Sistema é consultado ao aplicar opções; acompanhar mudanças do Windows em tempo real fica para a integração de preferências.

## Pacotes e licenças

WPF-UI fixado em `[4.3.0]`, com asset específico para .NET 10 Windows. Única dependência transitiva neste target: WPF-UI.Abstractions 4.3.0, registrada no lockfile. Licença MIT e avisos originais preservados em `Licenses/`. Os avisos de terceiros incluem termos de fontes; não adicionar nem redistribuir Segoe separadamente. O pacote inclui os ícones Fluent sob os respectivos avisos MIT.

Fontes primárias: [NuGet](https://www.nuget.org/packages/WPF-UI/4.3.0), [documentação](https://wpfui.lepo.co/documentation/getting-started.html), [código 4.3.0](https://github.com/lepoco/wpfui/tree/4.3.0).

## Recursos e compatibilidade

Tema no escopo da janela → ControlsDictionary dentro do dicionário de tokens → estilos AniT → overrides locais. Os BasedOn precisam enxergar o dicionário de controles no momento de carregar o XAML; uma dependência entre dicionários irmãos falhou na primeira execução e foi corrigida pela composição explícita.

O acento precisa de recursos locais AccentButton*, pois inicializar apenas o tema deixa o botão primário sem preenchimento adequado neste host. Foram adicionados esses recursos sem chamar os gerenciadores globais de tema/acento. Os recursos da Application e o estilo legado de Button são conferidos antes/depois.

AppearanceManager não é inicializado no processo do piloto: seus handlers globais mutam FontSize/LayoutTransform e poderiam aplicar escala duas vezes. A etapa 4 precisa definir essa fronteira antes de hospedar componentes novos no processo real. O piloto comprova coexistência dos dicionários; não comprova coexistência com todos os handlers legados ativos.

Escala da interface aplicada uma vez na raiz; escala do texto por papéis. Painéis aceitam raio 0/16/32; controles limitam o raio a 16. O diálogo conserva partes do template upstream e ainda exige normalização dos raios/tipografia na etapa 4. Reduzir movimento troca o template do switch por uma versão estática, preservando o controle e sua semântica. Não há animação contínua nem backdrop pesado.

## Validação

- Restore locked e build Release sem avisos/erros.
- 183 testes funcionais aprovados.
- 324 combinações: três temas, três escalas de interface, três de texto, três raios, movimento ligado/desligado e dois renderizadores.
- 24 verificações de viewport lógico: 580×420, 1280×720, 1920×1080 e 3440×1440, divididos por fatores 1/1,25/1,5, em duas janelas. Não são uma emulação completa de DPI físico.
- Validação WPF, estado desabilitado, ciclo assíncrono/modal, restauração dos irmãos e isolamento dos recursos aprovados. Checks adicionados ao CI.
- Regressão existente: 144 configurações do onboarding, quatro etapas cada, e verificações da Home aprovadas.
- Janela real inspecionada no desktop a DPI 100%: mascote transparente, contornos completos, botões e rolagem disponíveis. A automação de teclado não confirmou foco real no modal; tratar Tab/Shift+Tab, retorno de foco, Enter/Escape e foco visível como pendência de aceitação manual antes de migrar diálogos de produção. Há implementação de foco inicial, ciclo Tab e Escape, sem alegar validação manual completa.

## Medição exploratória

Mesma máquina, DPI 100%, render tier 2, janela visível, 20 atualizações de aquecimento e 100 mudanças de nome com UpdateLayout/dispatcher. Uma execução sequencial, Default primeiro. Valores não representam FPS nem tempo de apresentação da GPU.

| Modo por janela | Primeiro layout | Atualização mediana / p95 | CPU nas 100 mudanças | Memória privada do processo |
|---|---:|---:|---:|---:|
| Default | 375,60 ms | 1,02 / 1,60 ms | 156,25 ms | 122,15 MiB |
| SoftwareOnly | 124,95 ms | 1,03 / 1,35 ms | 187,50 ms | 116,90 MiB |

O primeiro modo inclui inicialização a frio; o segundo reutiliza caches. Não concluir que software abre mais rápido ou consome menos memória. Esta amostra só confirma que ambos executam o piloto. Manter o workaround existente restrito à Home; benchmark representativo de Home/listas e testes físicos 125/150% continuam necessários.

## Próxima etapa

Etapa 4: consolidar tokens e componentes, resolver fronteira do AppearanceManager, testar teclado/contraste em todos os estados e conferir DPI físico. Depois disso, migrar shell/Home conforme backlog. Nenhum instalador foi gerado.

## Atualização da etapa 4

A base foi extraída para AniT.Presentation. Agora há adapter para AppearanceManager e check com os handlers reais ativos. O foco/teclado do diálogo foi conferido visualmente no desktop; detalhes e limites em [design-system.md](design-system.md). A descrição acima registra o estado da etapa 3.
