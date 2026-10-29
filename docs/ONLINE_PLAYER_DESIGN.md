# Online Anime Player — identidade visual (passo 7)

A preparação real do episódio usa `OnlineBufferWindow.xaml`, com Baki-Pi sem moldura,
Segoe UI, superfícies e texto derivados dos recursos de aparência do AniT. Os estados
são organização, busca de fontes, recebimento e erro. Dados e tempo decorrido continuam
reais. O percentual do arquivo não vira um percentual fictício de preparação.

O indicador animado respeita movimento reduzido, intensidade zero e a opção de
animação do Windows. Com movimento reduzido, o indicador some; estado e tempo
continuam disponíveis em texto. Não há animação contínua do mascote. A ação de
cancelamento fica fora da área rolável, com foco de teclado visível e altura mínima
44 DIP. O conteúdo rola em janelas pequenas e com texto ampliado.

## Direção do player

`Styles/OnlinePlayerTheme.xaml` fornece texto, botões e timeline nativos. As imagens
são ilustrações/ícones, nunca controles de tempo ou menus rasterizados. Faixas de áudio
e legendas têm rótulos escritos pela interface. A área do vídeo não recebe moldura
ornamental nem mascote durante a reprodução.

`OnlinePlayerDesignChecks` compõe uma **prévia de design**, com dados fictícios e
controles ainda sem ligação ao motor. Ela não aparece no fluxo do usuário e não
acessa perfis, biblioteca, torrent ou player. O player de reprodução atual continua
em mpv; hospedar o vídeo e ligar esses controles é o passo 8. Listas completas de
áudio/legendas e preferências são o passo 9.

Os originais enviados pelo usuário estão preservados em `assets/player`. Somente
os ícones e mascotes necessários à base visual são recursos compilados; molduras e
painéis estáticos permanecem como material de referência. Os mascotes de alta
resolução `player10`, `player8`, `player5` e `player9` são carregamento, conexão,
erro e espera. Eles ficam com proporção original e transparência preservada.

## Reprodução da validação

```powershell
dotnet build tests/AniT.UiChecks -c Release
$env:ANIT_PLAYER_DESIGN_OUTPUT = '<pasta de saída>'
dotnet tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll --check-player-design
dotnet tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll --check-online
```

40 combinações de design/preparação (temas claro/escuro, texto 100/140%, prévia
1366/960 e preparação 560/360 DIP), mais 48 layouts/regressões da importação e
preparação. Os PNGs são renderizações WPF reais. O teste cobre limites dos botões,
contraste temático do título, movimento reduzido, atualizações de progresso e
cancelamento. Não é validação de vídeo ou desempenho do torrent.
