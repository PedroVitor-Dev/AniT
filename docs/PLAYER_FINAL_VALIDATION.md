# Player — validação final e instalador de teste

Data: 29/10/2026. Código empacotado: `d719ba3` na branch `release/2.0.2`.
O passo 10 foi executado no escopo de regressões automatizadas, motor nativo,
fluxo de torrent controlado e empacotamento local. Os recursos extras estão em
[PLAYER_EXTRAS.md](PLAYER_EXTRAS.md).

## Resultados

- 425 testes .NET únicos aprovados: suíte completa com 424 casos, mais a regressão
  adicional de notas Unicode; testes afetados repetidos após os ajustes finais.
- 8 testes Python dos relatórios/benchmark aprovados.
- Última medição da suíte base: 77,82% de linhas, acima do mínimo de 70%.
- 116 layouts: importação/preparação (48), direção visual/preparação (40), janela
  integrada (8), menus de faixas (12), ferramentas extras (8).
- Motor mpv real: vídeo incorporado, clique/duplo clique, teclado, volume, mudo,
  timeline, tela cheia, ocultação, fechamento/checkpoint e reabertura.
- Faixas nativas e legendas externas, idiomas com IDs invertidos entre episódios,
  velocidade, aparência, ajuste de tempo, restauração ASS e persistência durante seek.
- Baki-Pi durante buffering, reserva sem retomada automática, captura de momentos,
  ocultação da nota/imagem, revelação explícita temporária e temas claro/escuro.
- Ativação entre processos, reinício, onboarding antes de banco/player e configuração
  interrompida. Primeiro quadro do onboarding em 458 ms neste teste, contado após o
  início da construção da janela; exclui bootstrap de processo/WPF.
- Compilação e publicação Release: zero avisos e zero erros.

## Torrent ponta a ponta

Foi usado MKV sintético autorizado, MonoTorrent, seeder local, relay TCP limitado,
servidor HTTP de peças verificadas e a janela nativa do AniT. A reprodução começou
com 13,3% do episódio selecionado baixado; os demais ficaram sem prioridade.
A busca para 65 s sob restrição de velocidade passou em 6,5 s, com 80 ciclos da
interface enquanto aguardava. Foram validados retorno a 12 s, conclusão do download,
continuação a 20 s, checkpoint/reabertura e fechamento durante abertura bloqueada.
Esse fechamento agora cancela a operação, encerra somente o processo próprio e não
vira aviso de falha de reprodução.

Esses números descrevem apenas o ambiente/arquivo controlado. Não são promessa de
latência em torrents públicos. Não houve validação física em todas as GPUs, monitores
ou combinações de DPI. O instalador não foi executado sobre o perfil real do usuário.

## Pacote

- Arquivo: `artifacts/player-test-d719ba3/installer/AniT-Setup-2.0.2-player-test-win-x64.exe`
- Versão do aplicativo: 2.0.2; pacote identificado como teste, sem publicação no GitHub.
- Tamanho: 329187212 bytes (aproximadamente 314 MiB), self-contained Windows x64.
- SHA-256: `bfcf70cdae631cb71150763086c3a411ab2b42b2fbae509733430eb8fb7ff06b`
- Assinatura: NotSigned; SignPath continua pendente na configuração conhecida.
- mpv/MPC-HC publicados conferidos por hash com os originais; licenças e avisos incluídos.
- A opção Iniciar o AniT permanece marcada no fim da instalação.
- Pasta de artefatos nova; instaladores anteriores preservados. Nenhum push/release.

A publicação usa `dotnet publish -c Release -r win-x64 --self-contained true`, sem
trimming/single-file e com lock transitório em `obj/packages.publish.lock.json`.
O ISCC 7 usa `installer/AniT.iss`, SourceDir/OutputDir explícitos, nome de saída de
teste e `--no-signing`. Nenhum diretório de artefatos antigo foi apagado.

Os resultados estruturados estão em [benchmarks/player-final](benchmarks/player-final/installer.json).
Reproduções: `--check-player-host`, `--check-player-tracks`, `--check-player-extras`,
`--check-online`, `--check-player-design` e `--check-startup` em AniT.UiChecks.
O fluxo completo usa `ANIT_TEST_USE_MPV=1`, `ANIT_TEST_EMBEDDED=1` e `ANIT_TEST_MKV`
apontando para a fixture autorizada, com `--check-online-player --mkv`.
