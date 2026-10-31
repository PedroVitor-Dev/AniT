# Entrada real do mouse e cursor do player

A superfície pai do vídeo é um HWND da classe STATIC. Sem SS_NOTIFY, essa classe
retorna HTTRANSPARENT: com o filho incorporado do mpv desabilitado para entrada,
o Windows entrega os cliques ao fundo WPF da janela, sem passar pelo mpv nem pelo
host. Os testes anteriores enviavam mensagens diretamente ao filho do mpv e não
reproduziam essa condição. A correção da superfície usa SS_NOTIFY e o teste de
regressão consulta WM_NCHITTEST, exigindo HTCLIENT.

MpvVideoHost recebe botão esquerdo/direito e movimento na própria superfície,
bem como notificações de descendentes, sem hook global. Pressões são entregues
ao WPF com timestamp capturado e coordenadas físicas da tela; o reconhecimento
usa o intervalo e área de tolerância configurados no Windows. Um clique pausa
após a janela de reconhecimento. Dois cliques alternam tela cheia sem pausar.
As bindings de botão do mpv são ignoradas no modo incorporado para evitar ações
duplicadas. Teclas e menus de faixas permanecem integrados.

Após aproximadamente três segundos de reprodução sem interação, controles e
cursor somem quando a janela está ativa e o ponteiro está sobre o vídeo. O cursor
é tratado com WM_SETCURSOR/SetCursor somente nessa superfície: sem ShowCursor,
contador global ou Mouse.OverrideCursor. Movimento, pausa, menus, perda de foco
e fechamento restauram a apresentação normal. Outro aplicativo não é alterado.

## Validação

- Automação com mouse real pelo plugin computer-use, vídeo sintético autorizado:
  duplo clique entrou e saiu de tela cheia, preservando reprodução. Clique simples
  pausou e clique direito abriu o menu contextual.
- GetCursorInfo confirmou CURSOR_SHOWING desmarcado durante o repouso em tela
  cheia e em janela; movimento/clique/pausa restauraram o cursor e os controles.
- 21 testes de AniT.Player.Tests passaram, incluindo pares lentos, distantes,
  consumo da sequência e transbordamento do timestamp de 32 bits.
- --check-player-host passou com hit-test nativo, cliques pela superfície pai,
  teclas, seek, áudio, ocultação, checkpoint/reabertura e 12 layouts.
- --check-player-tracks passou para faixas, pausa durante recarga, menu contextual,
  preferências e 12 layouts dos popups.

O modo --interactive-player-mouse em AniT.UiChecks abre exclusivamente a janela
de teste, usando ANIT_PLAYER_HOST_FIXTURE. Registra estados e exige entrada/saída
de tela cheia, cursor oculto/restaurado e pausa antes de terminar com sucesso.
Não acessa perfis, biblioteca, torrents públicos ou arquivos do usuário.

Referências: [STATIC e SS_NOTIFY](https://learn.microsoft.com/en-us/windows/win32/controls/about-static-controls)
e [WM_PARENTNOTIFY](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-parentnotify).
Nenhum instalador novo foi gerado nesta correção.
