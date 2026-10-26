# Transformação Cinematográfica — backlog a partir do inventário

Data: 26/10/2026. Direção escolhida pelo usuário: **Cinematográfica**. Etapa 2 concluída como inventário; nenhuma mudança de comportamento da UI foi aplicada nesta etapa.

## Decisões registradas

1. Manter C#/WPF; avaliar WPF UI como biblioteca visual principal.
2. Home editorial com uma ação principal, sidebar com rótulos e pôsteres com título/progresso fora da arte.
3. Azul reservado para ação, seleção e progresso; superfícies neutras e sombras discretas.
4. Baki-Pi pequeno, transparente, com função de identidade; não usar o template de capa para o mascote.
5. Preservar correções de recorte, escala, transparência e renderização existentes durante a migração.
6. Sem push e sem instalador até solicitação do usuário.

## Próximo trabalho: etapa 3

Criar uma prova de integração isolada do WPF UI, com dados fictícios, sem redirecionar App.OnStartup nem carregar scanner, banco, perfis ou backup. Ainda não substituir o shell inteiro nem trocar o contrato de janelas da Biblioteca.

### Lista de execução

- [ ] Conferir versão publicada, licença e suporte ao target `net10.0-windows`; registrar versão fixada e dependências transitivas. Não presumir compatibilidade a partir da aparência da biblioteca.
- [ ] Definir a ordem de carregamento dos recursos WPF UI, tokens AniT e overrides locais; documentar colisões, quando houver.
- [ ] Montar controles piloto: Button, TextBox com erro, ComboBox, ToggleSwitch e diálogo não destrutivo com dados fictícios.
- [ ] Verificar foco, Tab/Shift+Tab, Enter/Escape, estado desabilitado, nome acessível e contraste nos temas.
- [ ] Exercitar InterfaceScale 80/100/125%, TextScale 85/100/140%, raios 0/16/32 e redução de movimento. Ampliar os checks quando a API dos controles estiver definida.
- [ ] Comparar hardware padrão e o workaround por janela da Home, sem alterar o modo do processo ou o Windows.
- [ ] Validar layouts equivalentes a 1280×720, 1920×1080 e 3440×1440 e realizar uma conferência posterior em DPI físico 100/125/150%.
- [ ] Registrar CPU, memória, latência de abertura e fluidez em cenários definidos. Medir antes/depois no mesmo computador; nenhum valor de desempenho foi obtido na etapa 2.
- [ ] Aprovar a prova técnica antes de propagar estilos ou iniciar a transformação de todas as telas.

Critério de saída: a biblioteca está fixada e documentada, os componentes piloto funcionam com temas/preferências/teclado, os checks passam e as limitações de renderização estão conhecidas. Se houver incompatibilidade, registrar a evidência e ajustar a proposta antes de migrar telas.

## Fila de migração orientada por risco

| Item | Prioridade | Etapa prevista | Ação concreta | Aceitação |
| --- | --- | --- | --- | --- |
| UI-01 · Precedência dos estilos | P1 | 3 | Isolar defaults do WPF UI e definir overrides AniT | Sem mudança involuntária nas telas atuais |
| UI-02 · Contrato de tipografia/escala | P1 | 3–4 | Mapear papel de texto e substituir mutação indiscriminada por estratégia compatível | TextBlock/Label/TextBox legíveis; conteúdo variável alcançável por rolagem |
| UI-03 · Recorte de cinco páginas | P1 | 4, antes de migrar cada tela | Adaptar clip do filho em detalhes, calendário, explorar, histórico e perfil | Contorno inteiro após resize e alteração de raio; teste por família |
| UI-04 · Foco e teclado | P1 | 3–9 | Revisar templates que suprimem foco e preservar atalhos | Foco visível e navegação previsível nos estados reais |
| UI-05 · Biblioteca e estado de busca | P1 | 5–7 | Mapear janela secundária, filtros e limpeza ao reduzir largura | Redimensionamento não descarta estado sem decisão explícita |
| UI-06 · Transparência/GPU | P1 | 3–6 | Manter fallback por janela e medir efeitos | Sem molduras falsas; custo registrado e opção de reversão delimitada |
| UI-07 · Shell compartilhado | P2 | 4–5 | Extrair composição comum de sidebar/cabeçalho sem quebrar AppNavigation | Uma fonte visual para navegação; retorno e owner preservados |
| UI-08 · Família de cards | P2 | 4–7 | Unificar pôster, episódio e seleção com variantes explícitas | Proporção, títulos, progresso, favorito e foco consistentes |
| UI-09 · Tokens e efeitos | P2 | 4–8 | Consolidar cores e reduzir sombras/glows literais | Tema/acento aplicados pelos mesmos papéis; sem efeito pesado contínuo |
| UI-10 · Listas maiores | P2 | 7 | Medir ItemsControl/WrapPanel da Biblioteca e avaliar estratégia | Coleção grande continua utilizável com custos medidos |
| UI-11 · Movimento reduzido | P2 | 8 | Cobrir Storyboards literais, popup e animações em C# | A preferência atua em cada família, inclusive novos controles |
| UI-12 · Diálogos e notificações | P2 | 9 | Unificar confirmações, erros e toasts preservando contratos | PIN/backup/arquivos mantêm as proteções e mensagens |
| UI-13 · MainWindow anterior | P3 | 4–5 | Confirmar ausência de consumidores antes de remover | Exclusão somente após busca/revisão dos pontos de entrada |

P1 indica risco para uma migração segura, não uma falha crítica comprovada em todas as máquinas. Não há estimativa de prazo ou medida de desempenho inventada neste backlog.

## Matriz de proteção por cenário

| Cenário | O que preservar | Como verificar |
| --- | --- | --- |
| Primeira execução | Onboarding opcional, retomada e dados locais | Checks existentes + fluxo isolado de primeira execução |
| Perfis e PIN | Seleção, validação, cancelamento, troca e reinício | Testes de segurança existentes + conferência do fluxo após reestilização |
| Home vazia/populada | Mascote sem quadro, arte sem recorte de texto, contorno inteiro | Prévia Home com dados fictícios + renderização e resize |
| Biblioteca | Busca/filtros, favoritos, revisão e janela secundária | Casos com estado de busca ativo, várias larguras e retorno dos detalhes |
| Detalhes | Histórico de navegação, progresso, avaliações e player | Retorno entre telas e estados de episódio |
| Configurações | Tema, acento, escala, raio e redução de movimento | Alterar/reabrir e comparar os componentes piloto |
| Organização/backup | Prévia, confirmação, integridade e mensagens | Suíte existente; não usar mídias ou bancos pessoais no teste |
| Desktop/DPI | Texto inteiro, rolagem, foco e bordas | Tamanhos lógicos + rodada de DPI físico antes da entrega visual |

## Verificação da etapa 2

- Inspeção XML/C# concluída: 44 XAML, cobrindo 28 janelas, seis UserControls e nove dicionários de recursos, além do Application.
- `dotnet test AniT.slnx -c Release --no-build`: **183 aprovados, zero falhas**.
- `dotnet run --project tests/AniT.UiChecks -c Release --no-build`: **144 configurações de layout do onboarding, quatro etapas cada, e verificações da Home aprovadas**.
- Não houve nova compilação: esta etapa altera somente documentação e dados do inventário; a execução acima usa os binários existentes da base inspecionada.
- Limites: nenhuma medição de CPU/FPS/memória, auditoria completa de acessibilidade ou rodada de DPI físico foi realizada nesta etapa. Esses itens ficam explicitamente no piloto e na validação visual posterior.

O inventário completo está em [ui-audit.md](ui-audit.md), acompanhado de [ui-inventory.json](ui-inventory.json). A referência visual aprovada está em [visual-direction.md](visual-direction.md).
