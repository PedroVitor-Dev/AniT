# Online Anime Player — janela integrada (passo 8)

O fluxo Cloud passa a usar `OnlinePlayerWindow`, uma janela WPF própria com o tema
e os assets do AniT. O processo mpv existente renderiza em um filho nativo do
`MpvVideoHost` por `--wid`. O motor, o servidor HTTP local, o agendamento das peças
e o buffer adaptativo permanecem os mesmos. Não há captura de tela, transcodificação,
segunda cópia do vídeo nem nova dependência de distribuição. A API de incorporação é
a [documentada pelo mpv](https://mpv.io/manual/stable/#options-wid).

Foi escolhido o processo já distribuído, com JSON IPC, em vez de acrescentar uma
DLL libmpv nesta etapa. A janela é do AniT; o processo continua isolado. O player
externo mantém seu comportamento quando `AttachVideoWindow` não é usado. MPC-HC
local e Movie Theater não usam a nova superfície.

## Interação

- Clique no vídeo: reproduzir/pausar depois do intervalo de duplo clique do Windows.
- Duplo clique: alternar tela cheia; suprimir o clique extra de liberação enviado
  pelo mpv, preservando o estado de pausa.
- Espaço ou P: reproduzir/pausar. F/F11: tela cheia. Esc: sair da tela cheia.
- Setas esquerda/direita: voltar/avançar dez segundos pela preparação de índice
  existente do AniT. Repetições acumulam o destino solicitado e cancelam a busca
  anterior. A timeline confirma a busca ao soltar o arraste ou a tecla.
- Setas acima/abaixo: volume em passos de cinco. M: mudo. Ctrl+W: fechar.
- Volume nativo entre 0 e 100; arraste agrupa mudanças para não acumular comandos.
- Cabeçalho e controles recolhem depois de três segundos durante reprodução.
  Movimento ou teclado os traz de volta. Pausa, buffering, busca, erro, arraste e
  navegação de teclado nos controles impedem a ocultação.
- Voltar ao AniT fecha o player e emite o último checkpoint confirmado; o fluxo
  existente salva progresso/conquistas e pausa o download da sessão. Nenhum arquivo
  é apagado. Fechamento antes do primeiro quadro não inventa progresso.

Os controles ficam fora da superfície nativa, evitando o problema de airspace WPF.
Quando recolhem, o vídeo aproveita a área. Não há janela transparente flutuante nem
camada que cubra legendas. Texto e superfícies seguem os temas claro/escuro; a área
renderizada e as legendas pertencem ao motor. O recolhimento é imediato, sem animação
contínua. Listas de áudio/legendas e preferências foram adicionadas no
[passo 9](ONLINE_PLAYER_TRACKS.md).

## Ciclo de vida e custo

`MpcHcPlayer.ConfigureOnlinePlayer` cria a superfície antes de iniciar o processo.
Apenas no modo incorporado, o OSC e os atalhos padrão são desativados e uma seção
de entrada explícita encaminha ações conhecidas ao AniT. As mensagens são validadas;
eventos de ponteiro são limitados a aproximadamente 30 por segundo. A interface lê
um snapshot imutável a cada 250 ms, sem consultar IPC para desenhar cada quadro.

Fechar o player cancela timers, comandos e buscas, remove os handlers, encerra
somente seu próprio processo e libera o HWND. Encerrar/trocar o motor fecha a
superfície sem emitir um segundo fechamento de usuário. A reabertura cria um novo
HWND. Tela cheia preserva e restaura geometria/estado da janela. Não usa Topmost.

## Validação

```powershell
dotnet build tests/AniT.UiChecks -c Release
$env:ANIT_PLAYER_HOST_FIXTURE = '<vídeo de teste autorizado>'
$env:ANIT_PLAYER_HOST_OUTPUT = '<pasta de saída>'
dotnet tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll --check-player-host
dotnet test tests/AniT.Player.Tests -c Release -- xUnit.ParallelizeTestCollections=false
```

O teste usa o binário mpv distribuído, HTTP loopback e um MP4 sintético autorizado.
Verifica HWND filho, primeiro quadro, OSC desativado, pausa estável, clique e duplo
clique com mensagens de botão Win32 reais, atalhos pela seção nativa, seek, volume,
mudo, ocultação, checkpoint e reabertura. O vídeo é verificado separadamente por
`screenshot-to-file`; RenderTargetBitmap do WPF **não inclui a superfície GPU
nativa**. Os PNGs `host-*.png` verificam somente o chrome, não um quadro de vídeo.

Oito layouts combinam temas claro/escuro, larguras 1100/640 DIP e texto 100/140%.
Os testes de unidade cobrem validação de mensagens, identificador HWND e fechamento
antes/depois de um quadro confirmado. A suíte geral e as regressões do buffer
adaptativo são verificadas separadamente. Este teste não mede descoberta de peers,
velocidade de torrents públicos, nem substitui testes físicos em múltiplos monitores
com DPI diferente. Nenhum instalador ou release é produzido nesta etapa.

## Regressões de reprodução — 30/10/2026

- O reconhecimento de duplo clique do vídeo incorporado pertence ao AniT, usando
  tempo e área de tolerância do Windows. A detecção concorrente do mpv fica
  desativada. Dois cliques alternam tela cheia sem alternar pausa; clique simples
  continua pausando após a janela de reconhecimento.
- A preparação especulativa dos cues não chama mais `loadfile`. Reabrir o vídeo
  automaticamente descartava o cache e podia provocar outra espera de rede.
  Os cues são preparados em segundo plano e aplicados no próximo seek explícito.
- O teste nativo verifica uma única abertura durante a preparação do índice,
  cliques reais enviados ao HWND do mpv, pausa preservada e restauração da janela.
- `--check-player-host`: passou, incluindo oito layouts. `--check-player-tracks`:
  passou, incluindo seleção de faixas, pausa durante recarga, doze layouts e
  preferências. Os 18 testes de AniT.Player.Tests passaram.

Os registros locais disponíveis distinguem a espera inicial dos engasgos: uma
sessão sem cache levou aproximadamente 23 s até liberar o HTTP e 34,6 s até o
sinal de imagem; outra, com trechos já presentes, chegou ao sinal em 0,91 s.
Esses valores são diagnósticos anteriores à correção, não um benchmark antes/depois.
A remoção da recarga evita um custo desnecessário, mas não demonstra redução do
prazo para obter peças ainda ausentes de um swarm público. Nenhum novo instalador
ou pacote Store foi gerado nesta correção.

O fluxo completo --check-online-player --mkv também passou: reprodução com 13,3% do episódio presente, avanço para 65 s em 6,7 s sob limitação de rede, retorno, conclusão, reabertura e cancelamento durante início bloqueado (mídia sintética).

## Acabamento do cabeçalho e controles — 30/10/2026

O player usa a nova placa ilustrada do Baki-Pi (assets/player/1.png), em uma cópia
otimizada com transparência e 840 px de largura; o original e os demais arquivos
1–5 são preservados. Título do episódio com hierarquia de fonte, placa de marca e
botão de retorno centralizado. Ícones de reprodução maiores, sem uma segunda
moldura ao redor das ilustrações; tela cheia fixa à direita. Faixas e ferramentas
ficam em uma linha independente, com rótulos compactados para ícones em larguras
menores. Tooltips e nomes de acessibilidade permanecem nos botões.

As superfícies e textos usam recursos do tema, inclusive a legenda do cabeçalho.
A placa tem texto escuro sobre a região clara da ilustração, nos dois temas.
Nenhuma moldura é desenhada sobre o vídeo e nenhuma textura é recalculada por quadro.

Validação: --check-player-host passou em 12 combinações (1100×700, 640×540,
640×420; temas claro/escuro; texto 100/140%), além de clique/duplo clique, atalhos,
pausa, seek, volume, ocultação e reabertura. --check-player-extras passou para
assistência/reserva, momentos e legendas com oito layouts. Não foi gerado instalador.

### Legibilidade da marca e tipografia

A marca foi ampliada: área de texto de 190×40 DIP (172×40 na janela compacta),
texto branco #F5FCFF sobre placa #081F39 e ornamento do Baki-Pi a 16% de opacidade.
A fonte do player passa a Bahnschrift, com Segoe UI como alternativa disponível.
A marca não é mais reduzida a uma inscrição de 76 px na janela compacta.
Validação visual nos dois temas, texto 100/140%, e --check-player-host aprovado
nos 12 layouts, incluindo clique/duplo clique, reprodução e reabertura.
