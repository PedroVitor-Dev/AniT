# Online Anime Player — faixas e preferências (passo 9)

A janela do AniT agora oferece seleção direta de todas as faixas disponíveis de
áudio e legenda. Os rótulos mostram idioma, título, codec, canais, faixa forçada,
externa e padrão quando esses dados existem. O ID diferencia faixas do mesmo
idioma, mas não é persistido. Legendas podem ser desligadas. Faixas inexistentes
não viram opções fictícias; os botões correspondentes ficam desativados.

A lista vem de `track-list`, `aid` e `sid` observados pelo JSON IPC do mpv. Os dados
são limitados (256 faixas; título 160 caracteres), validados e publicados em uma
coleção somente de leitura. A marca de seleção segue o motor. Trocas passam pelo
mesmo controle de cancelamento da sessão e aguardam uma reabertura de índice em
andamento. O guia oficial de [propriedades do mpv](https://mpv.io/manual/stable/#properties)
descreve esses campos.

## Acesso e leitura

Os botões Áudio e Legendas abrem listas WPF reais; A e S fazem o mesmo quando o vídeo
tem foco. Botão direito, tecla de menu e Shift+F10 abrem o menu de contexto. Ele
contém reprodução, submenus de áudio/legendas, velocidade, sincronização de legenda,
preferências, tela cheia e retorno ao AniT. Os popups ficam acima do vídeo nativo,
são roláveis, usam texto ampliável e os temas claro/escuro. Menus abertos impedem
que os controles recolham. Não há animação de popup contínua.

Opções permite velocidades de 0,5× a 2× e adiantar/atrasar a legenda em passos de
0,1 segundo, com restauração do ajuste e limite de ±10 segundos. O atraso vale
somente para o episódio atual. A reabertura técnica para habilitar o índice preserva
áudio, legenda desligada, velocidade, ajuste e a intenção de pausa do usuário.
A pausa interna do carregamento não substitui a escolha do usuário.

## Preferências locais

`Online/<id-do-perfil>/player-preferences.json` guarda idiomas escolhidos, volume,
mudo e velocidade. É possível desligar “Lembrar idiomas neste perfil”. Quando não
há preferência online, as configurações gerais existentes do AniT continuam como
ponto de partida. As mudanças do player online não alteram as preferências de
MPC-HC local ou Movie Theater.

O próximo episódio é escolhido por idioma, nunca pelo número da faixa anterior.
Idiomas desconhecidos não apagam uma preferência válida. Desligar legendas pode
ser lembrado explicitamente. O mpv resolve o fallback quando um idioma não existe.
Volume e mudo são aplicados antes de começar a reprodução em um processo novo.

As gravações de volume são agrupadas por 600 ms; fechar salva a última observação
confirmada. As escritas usam arquivo temporário e substituição, com backup e
recuperação de JSON inválido. A configuração não registra URLs, nomes de vídeos,
IPs, hashes de torrent ou IDs de faixas.

## Busca e reserva de vídeo

Uma faixa fina acima da timeline exibe os intervalos realmente disponíveis no
cache do mpv. Não representa todo o download salvo em disco, nem garante uma busca
instantânea. O tooltip informa se o ponto está em cache ou pode precisar carregar.
Durante arraste/busca, o destino aparece separado do tempo efetivamente reproduzido.
O carregamento mantém estado textual e os controles acessíveis. O buffer adaptativo
e o agendamento de peças das etapas anteriores continuam ativos.

## Validação reproduzível

```powershell
dotnet build tests/AniT.UiChecks -c Release
$env:ANIT_PLAYER_TRACK_FIXTURE = '<MKV sintético com dois áudios e duas legendas>'
$env:ANIT_PLAYER_TRACK_OUTPUT = '<pasta de saída>'
$env:ANIT_PLAYER_NEXT_FIXTURE = '<MKV com as mesmas faixas em ordem invertida>'
dotnet tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll --check-player-tracks
dotnet tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll --check-player-host
dotnet test AniT.slnx -c Release --collect:'XPlat Code Coverage' -- xUnit.ParallelizeTestCollections=false
```

O teste nativo usa HTTP loopback e um MKV sintético autorizado com dois áudios AAC
e duas legendas SRT. Uma segunda versão com IDs em ordem invertida confirma que
as preferências seguem o idioma. Confere seleção individual pelos menus, desligamento, pausa
com a fonte bloqueada durante recarga do índice, preservação das faixas e ajustes,
menu de contexto por mensagens de botão direito Win32, submenus, velocidade,
sincronização e reabertura com preferências do perfil. Produz 12 layouts de popup
(escuro/claro, texto 100/140%, áudio/legenda/contexto), além dos PNGs de referência.
Os testes usam uma pasta de perfil isolada, sem abrir dados reais do usuário.

Testes de unidade cobrem metadados inválidos, limites, IDs distintos por tipo,
idiomas, normalização, isolamento de perfis e recuperação de backup. A integração
não comprova disponibilidade/velocidade de torrents públicos. Os testes abrangentes
com fontes reais, DPI e embalagem continuam no passo 10. Nenhum instalador é gerado
nesta etapa.
