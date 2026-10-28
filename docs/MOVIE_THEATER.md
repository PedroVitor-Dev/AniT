# Movie Theater · prévia de conexão direta

## O que esta versão faz

A entrada **Movie Theater** aparece na navegação do AniT e abre uma janela independente, reutilizada ao clicar novamente. Assim como a Biblioteca, a sala pode continuar aberta enquanto você usa a janela principal.

- Até cinco participantes: um host e quatro convidados. Antes da preparação, escolha **Streaming direto do seu PC** ou **Sincronização P2P**. O modo do convite é definido pelo host e aparece ao conectar.
- No modo P2P, o convidado usa uma cópia idêntica ou baixa o arquivo completo antes de assistir.
- No streaming, só o host precisa do arquivo: o convidado prepara um buffer e começa enquanto recebe trechos sob demanda, inclusive após avançar no vídeo. Sem transcodificação nem cópia completa obrigatória.
- Compartilhamento desmarcado por padrão: ao criar a sala, o host pode autorizar o envio de uma cópia completa apenas do arquivo selecionado. O convidado escolhe onde salvar e solicita o download.
- No modo P2P, progresso de envio/download, verificação completa por SHA-256 e confirmação **arquivo OK** no host. O início também exige os dois players preparados.
- No P2P, verificação do arquivo completo por tamanho e SHA-256, sem depender do nome ou do caminho. No streaming, o transporte TLS protege os trechos; não há confirmação de arquivo completo no convidado.
- Convite privado com endereço IPv4, porta, segredo aleatório e identidade do certificado da sala.
- Conexão direta TCP com TLS e certificado fixado pelo convite; sem conta ou serviço central de sinalização.
- Vídeos abertos pausados. Streaming usa início agendado em dois segundos; P2P exige liberação da preparação, confirmação do convidado e aval do host, seguido por cinco segundos de contagem sincronizada.
- Host com controle de play, pause e posição. Faixas de áudio e legenda **embutidas no mesmo arquivo** são acompanhadas por índice.
- Participante com tela nativa de vídeo sem menus, barras ou controles de reprodução. O player fica dentro de um contêiner com entrada desabilitada. F11 alterna tela cheia; fechar a tela remove o estado pronto e pausa a sala.
- Pausa por desconexão ou falta de prontidão. O host continua aguardando e aceita novamente o mesmo convidado. Quando o host sai ou a conexão com ele é perdida, a sala e o player do convidado são fechados, preservando o episódio completo já baixado. Para reconectar, o convidado abre novamente o Movie Theater e entra por convite.

## Interface

A tela usa as artes de `assets/cinema/`, com imagens transparentes e proporções preservadas. Os cartões dos modos ficam lado a lado em janelas largas e empilhados em janelas compactas. Continuar permanece no rodapé fixo; o cabeçalho se reduz depois da escolha para liberar espaço à preparação. Os estilos são locais ao Movie Theater, com controles de foco, seleção, progresso e rolagem adaptados aos temas claro/escuro. As transições de entrada são finitas e respeitam movimento reduzido; não há animações contínuas sobre o vídeo.

## Escolha do modo

| Modo | Preparação do convidado | Durante a reprodução |
| --- | --- | --- |
| Streaming direto do seu PC | Entra sem vídeo e clica em **Abrir transmissão** | Recebe os trechos necessários; depende do upload do host e da conexão contínua |
| Sincronização P2P | Verifica uma cópia idêntica ou baixa tudo antes e recebe **arquivo OK** | Reproduz a cópia local e recebe os comandos de sincronização |

O controle de reprodução continua exclusivo do host nos dois modos. Cada sala aceita até quatro convidados além do host. A navegação é **modo → Host ou Convidado → configuração do papel**. Nenhum formulário aparece antes dessas escolhas. Host vê seleção de episódio, autorização e criação da sala; endereço/porta ficam em **Configurações de conexão**. Convidado de streaming vê somente o convite. Convidado P2P pode marcar **Já tenho o episódio neste PC** para selecionar uma cópia local; sem marcar, entra pelo convite para baixar a cópia autorizada. **Voltar** permite rever papel e modo antes de conectar.

## Streaming direto em dois computadores

1. Escolha **Streaming direto do seu PC**, clique em **Continuar**, selecione **Host** ou **Convidado** e continue. Cada pessoa vê somente sua configuração.
2. O host seleciona o episódio, aguarda a verificação, se necessário ajusta endereço/porta em **Configurações de conexão** e marca a autorização para transmitir os trechos antes de criar a sala.
3. O convidado entra pelo convite, sem selecionar ou salvar um episódio. O modo do host prevalece ao conectar.
4. O host clica em **Abrir episódio** e o convidado clica em **Abrir transmissão**. O AniT busca um primeiro bloco e o MPC-HC prepara o gráfico com os metadados e o buffer necessários. Só então o convidado fica pronto.
5. O host clica em **Iniciar juntos**. Play, pausa, avanço e faixas embutidas usam o mesmo controle da sala P2P. Fechar o player, perder prontidão ou desconectar pausa a reprodução.

O arquivo mantém qualidade e bitrate originais. O upload do host e a rede precisam acompanhar a soma do bitrate enviado a todos os convidados; não há adaptação automática de qualidade. Avançar pode exigir novo buffer. Formatos, índices, codecs e renderizadores diferentes podem exigir mais dados na abertura; ainda precisam ser validados em outros PCs. Não é transmissão da tela do Windows.

O convidado usa um cache de até 32 MiB de blocos em memória, além dos buffers do player e conexões. Não é produzido um arquivo permanente do episódio nesse modo. O MPC-HC acessa um endpoint HTTP **somente em 127.0.0.1**, com porta aleatória e caminho secreto; o vídeo entre computadores usa um canal TLS próprio, autenticado e separado do canal TLS de comandos. Esse endpoint local atende GET/HEAD e intervalos de bytes, sem URLACL, certificado confiável instalado ou porta HTTP pública. A primeira leitura é de 256 KiB; as seguintes usam pedidos de até 1 MiB, com no máximo três pedidos de rede simultâneos. A janela de leitura antecipada é adaptativa, limitada a 8 MiB, e o aquecimento especulativo mantém até duas leituras em pipeline, dentro da janela total de até três leituras de rede. Trechos de vídeo usam quadros binários de até 32 KiB, sem Base64; mensagens de controle continuam limitadas a 8 KiB. Leituras concorrentes do mesmo bloco são compartilhadas, e uma leitura lenta não mantém o cache inteiro bloqueado. Ao encerrar a sala, o endpoint e o cache são descartados.

### Canais separados de vídeo e controle

No streaming, duas conexões TCP/TLS usam o mesmo endereço e a mesma porta TCP do convite. O canal de controle leva prontidão, pausa, posição, faixas e heartbeat; o canal de mídia leva somente pedidos de trechos e bytes do vídeo. Cada um tem sua própria fila de escrita e leitura. Uma leitura ou escrita de vídeo bloqueada não ocupa a conexão dos comandos. Isso evita a fila compartilhada da aplicação/TCP, mas as conexões ainda dividem o upload físico e podem sofrer congestionamento da rede.

Ambas as conexões verificam o mesmo certificado fixado pelo convite. Após a entrada, o host gera uma chave aleatória exclusiva daquela participação para autorizar um único canal de vídeo. O segredo do convite, uma chave de participação encerrada ou uma conexão duplicada não autorizam novos canais de mídia. Há um convidado por sala; admissão e conexões pendentes têm limites. O canal extra abre automaticamente ao selecionar **Abrir transmissão**, sem novos campos ou portas para configurar. A abertura simultânea reutiliza o mesmo canal e endpoint local.

O heartbeat permanece no controle e a conexão de mídia pode ficar ociosa durante uma pausa. Se um dos canais falha ou é fechado, os dois são encerrados, a reprodução é interrompida e o host volta a aceitar uma nova entrada. A nova participação recebe outra chave. O modo de sincronização P2P mantém seu fluxo de arquivo completo durante a preparação.

A pausa coordenada e a adaptação do buffer à transferência medida estão implementadas no streaming. A retomada continua sendo autorizada pelo host.

### Espera coordenada de buffer

Durante a reprodução, o convidado observa sua posição e as leituras HTTP efetivamente solicitadas pelo MPC-HC. Uma leitura antecipada especulativa não provoca espera. Antes de haver amostras suficientes, uma leitura de vídeo pendente por pelo menos 600 ms, junto de 800 ms sem avanço da posição, bloqueia a sessão. Depois da medição, esses limiares se ajustam dentro de 600–1.500 ms de leitura e 800–1.600 ms sem avanço. Também há bloqueio preventivo após dois segundos de leitura pendente no início, ajustável entre 1,5 e 6 segundos com as medições, enquanto deveria reproduzir, pois alguns gráficos mantêm o relógio avançando mesmo sem novos quadros. Abertura inicial, pausa manual e fim do episódio não entram nessa detecção.

O relato segue pelo canal de controle independente do vídeo. O convidado pausa imediatamente; ao receber o relato, o host bloqueia sua reprodução e publica uma linha do tempo pausada. O AniT pode recuar o host até a posição relatada, limitado a três segundos, para preservar um ponto comum sem aceitar um salto arbitrário do convidado. Durante a espera, não dispara buscas repetidas no guest que descartem o buffer.

Os dois AniTs exibem o progresso do trecho necessário e o tempo de espera. A recuperação exige o preenchimento da reserva necessária e uma janela de estabilidade: 750 ms inicialmente, adaptável entre 500 e 2.500 ms após a medição. O indicador acompanha um novo trecho pendente, se necessário; não representa o download do episódio completo nem uma estimativa de tempo restante.

**Receber os dados não retoma a reprodução.** Os dois vídeos permanecem pausados. Só então o host recebe a ação **Retomar juntos**, que agenda uma nova partida sincronizada. A autorização identifica a revisão de buffer já recuperada; uma autorização antiga não libera uma falta de dados nova. Play nativo também permanece bloqueado durante essa espera. O convidado continua sem controles de reprodução.

O monitor continua coletando informações enquanto uma consulta ao MPC-HC aguarda dados; um timeout de posição durante a espera não descarta imediatamente o player. Perda de prontidão ou desconexão limpa a espera; uma nova participação não herda revisões da anterior. Uma falha definitiva de rede continua encerrando os dois canais. Esta etapa não muda o bitrate nem cria dados que um upload insuficiente não consegue fornecer.

### Medição e buffer adaptativo

A taxa de recebimento é estimada com os bytes reais do canal de mídia e o tempo em que há pedidos ativos. Acertos do cache HTTP e tempo parado sem pedidos não são contados como upload; uma transferência ativa sem novos bytes reduz a estimativa. As amostras usam suavização, reagindo mais rapidamente a quedas do que a aumentos. Não há um teste de velocidade que envie outro arquivo ou tráfego para serviços externos.

A latência e sua variação vêm do relógio do canal de controle. Quando o MPC-HC fornece a duração, o tamanho do arquivo dividido pela duração fornece uma estimativa da taxa média do vídeo, incluindo o contêiner. Não é uma análise de bitrate por cena: vídeos com taxa variável ainda podem ter picos. O recebimento medido abaixo da margem dessa estimativa aumenta a reserva e é indicado durante a espera.

O alvo varia entre **1 e 8 MiB**, conservador em 2 MiB antes de haver amostras suficientes. Latência, variação, baixa margem de recebimento e esperas recorrentes aumentam esse alvo. A pressão das esperas diminui em períodos de 30 segundos sem nova ocorrência. O cache continua limitado a 32 MiB, além dos pedidos em andamento e buffers do MPC-HC; o alvo adaptativo não é um aumento ilimitado de memória.

Durante uma espera, a reserva é preenchida mesmo com o player pausado, limitada ao fim do arquivo. O alvo de bytes e a janela de estabilidade daquela espera são preservados para não criar uma meta que se mova indefinidamente. A leitura antecipada tem um único trabalhador com até dois pedidos simultâneos; um intervalo HTTP mais novo recebe prioridade sobre a leitura especulativa de um intervalo antigo. O encerramento da sala cancela e aguarda esse trabalho.

Os dois AniTs mostram recebimento medido, tempo de ida e volta e reserva alvo. As medições seguem para o host pelo canal de controle, no máximo uma vez por segundo e no início de uma espera. Não criam controles de reprodução para o convidado. Receber a reserva continua deixando os dois pausados até o host clicar em **Retomar juntos**.

A adaptação não converte nem reduz a qualidade do vídeo. Se o upload sustentado for insuficiente, aumentar o buffer pode espaçar as pausas, mas não garante reprodução contínua. Os limites são decisões de engenharia verificadas localmente, não promessa de latência ou velocidade para qualquer rede.

## Gerenciador do Host e preparação para voz

Dentro de uma sala, somente o host vê o gerenciador com os nomes de perfil, papéis, prontidão e permissão de voz. A sala continua limitada ao host e um convidado. A lista usa identificadores gerados pelo servidor para cada entrada; nomes são rótulos apresentados pelos perfis, não identidades verificadas nem nomes de usuário do Windows. São limitados a 60 caracteres e removem caracteres de controle. Fotos/caminhos de avatar não são enviados.

**Bloquear microfone / Liberar microfone** muda uma permissão de voz autoritativa na sala, comunicada ao convidado com revisões. Não captura áudio, não altera áudio/legendas do episódio e não ativa um chat de voz: a interface informa que ele está em preparação. O futuro receptor de voz deve conferir `IsVoiceTransmissionAllowed(participantId)` a cada frame, além de exigir adesão ao chat e autorização local de captura. Também deverá limpar áudio já enfileirado quando a permissão mudar. Dispositivos, codec, captura, transporte e reprodução de voz ainda não estão implementados.

**Expulsar** revoga a participação atual, interrompe os envios e troca o segredo do convite. O host aguarda uma confirmação curta da notificação e encerra os canais mesmo se o convidado não responder. A tela de vídeo do convidado é fechada; o Movie Theater volta ao formulário com o motivo e limpa o convite antigo. Arquivos completos permanecem salvos; parciais seguem a limpeza de cancelamento existente. O host permanece na sala, com novo convite disponível para copiar, e a reprodução volta a exigir preparação/prontidão.

O convidado não pode expulsar nem alterar permissões. Ações atrasadas para o identificador de uma pessoa que saiu não atingem a próxima entrada. Uma nova participação recebe outro identificador e outra chave de mídia, com permissão inicial independente da anterior. A expulsão revoga o convite antigo; não é um banimento permanente de uma pessoa que receba um novo convite do host.

## Sincronização P2P em dois computadores

1. Escolha **Sincronização P2P**, clique em **Continuar**, selecione **Host** ou **Convidado** e continue. No host, selecione o episódio e aguarde a verificação completa.
2. Se necessário, confira o IPv4 e a porta TCP em **Configurações de conexão**. Para oferecer o arquivo ao convidado, marque **Autorizo enviar uma cópia completa deste episódio** antes de clicar em **Criar sala**. Sem essa autorização, o convidado precisa selecionar uma cópia idêntica antes de entrar.
3. Compartilhe o convite apenas com o participante desejado. O convite também permite solicitar o arquivo autorizado; ele não deve ser publicado.
4. O convidado cola o convite. Se já tiver a cópia idêntica, marca **Já tenho o episódio neste PC** e seleciona o vídeo antes de entrar. Caso contrário, usa **Escolher onde salvar** na sala. O destino pode ser escolhido antes da liberação; nada é baixado até o host iniciar a preparação.
5. O host clica em **Iniciar preparação**. Seu vídeo abre pausado. O AniT libera o download, recebe os bytes no convidado, verifica o arquivo completo e abre automaticamente a tela de vídeo pausada. Se o convidado já tem uma cópia idêntica, pula o download e abre o vídeo após a liberação.
6. O painel dos dois participantes mostra cadeado, próxima ação, cinco etapas, progresso em MB/porcentagem, recebimento/verificação, tempo de preparação, tempo de download e velocidade média. O progresso no host é informado pelo convidado: 100% recebido ainda não significa arquivo verificado ou permissão para iniciar. O tempo do download para ao concluir; o de preparação para no início autorizado.
7. O convidado clica em **Confirmar presença** depois que seu vídeo estiver aberto. Essa confirmação não dá play. Apenas o host recebe **Autorizar e iniciar**, quando ambos têm o arquivo verificado e os vídeos abertos.
8. O host dá o aval. Os dois seguem uma contagem de **cinco segundos**, baseada no relógio medido da sala, e só então começam. As ações da etapa ficam no rodapé fixo. O convite recolhe quando o convidado conecta, deixando o andamento em destaque; pode ser expandido novamente.
9. Durante a espera, tentar tocar o MPC-HC do host não contorna o aval final: o AniT mantém os vídeos pausados. Depois da liberação, play, pausa, posição, áudio e legendas continuam exclusivos do host. O convidado pode confirmar presença, escolher o destino, sair ou reabrir sua tela se ela fechar; não tem controles de reprodução.
10. Fechar um vídeo ou perder prontidão revoga o aval e a confirmação. É necessário reabrir, confirmar e obter novo aval. Desconectar também cancela a liberação da preparação: após reconectar, o host libera novamente. Cancelar/interromper um download remove o parcial; uma nova tentativa começa do início. Falhas de abertura mostram a ação **Reabrir episódio**, sem tentativas automáticas em ciclo.
11. Para outro episódio ou para alterar a autorização de compartilhamento, saia e crie uma nova sala.

A primeira versão é destinada à rede local ou a uma rota direta já configurada. A indicação automática do IPv4 pode apontar para um adaptador de VPN ou rede diferente; confira o endereço. A conexão não configura roteadores, não atravessa CGNAT automaticamente e não altera regras de firewall. Não há servidor para localizar um código curto. O convite precisa ser colado no app; não é um protocolo instalado no Windows.

## Reprodução e limites

O vídeo é reproduzido localmente, sem captura de tela ou transcodificação. O canal TLS envia identidade do arquivo, prontidão, posição, estado, faixas e medições de relógio. Quando o host autoriza e o convidado solicita, também envia o nome e os bytes do episódio selecionado: uma transferência completa no P2P, ou trechos sob demanda no streaming. Não envia caminhos locais nem permite consultar pastas ou pedir outros arquivos.

No modo P2P, o download usa blocos limitados e um arquivo parcial com nome aleatório na pasta escolhida. O AniT verifica o espaço livre do disco local e nunca substitui um arquivo existente. Só renomeia o parcial para o destino final após conferir o conteúdo completo. Cancelar ou interromper o download encerra a conexão do convidado e remove o parcial; saia e entre novamente para tentar do início. Não há retomada de download nesta etapa. Fechar a sala mantém os downloads já concluídos na pasta que o convidado escolheu.

Ambos precisam desta versão do AniT: o protocolo da sala passou para a versão 9, com identificação por participação e moderação do host, além dos canais de mídia/controle e medições adaptativas. O compartilhamento permanece opcional e o controle da reprodução continua exclusivo do host.

O relógio usa medições de ida e volta; o participante aplica pequenas correções de velocidade e reposiciona em desvios maiores. O P2P usa limiares de 80/350 ms; streaming usa 120/750 ms, correções de até 4% na velocidade e intervalo mínimo de 1,5 s entre buscas automáticas durante a reprodução (saltos maiores que 3 s e comandos pausados são aplicados imediatamente). A linha do tempo é publicada a cada amostra do player, com ciclo de 100 ms. Após abrir os dois players, o streaming usa agendamento de 350 ms; o P2P mantém os cinco segundos de confirmação visual após o aval. Esses valores são decisões de controle, **não uma garantia de precisão medida entre PCs**. Estados sem atualização por cinco segundos pausam o convidado; a conexão sem mensagens expira em dez segundos.

Ainda não há participação pelo navegador, retomada de downloads, migração de host ou troca de episódio dentro da mesma sala. Legendas externas e atrasos manuais de áudio/legenda não são sincronizados nesta etapa. A reprodução independente não registra progresso na biblioteca nesta prévia.

Fechar a guia encerra a participação e o player dedicado. Trocar de perfil, restaurar/reparar os dados ou encerrar o AniT também fecha a sala. A tela de participante é uma restrição da interface, não um mecanismo de DRM para impedir o dono do computador de executar outro software.

## Isolamento do MPC-HC

O Movie Theater usa `/new` e uma cópia portátil privada do MPC-HC e seus componentes por função e versão do executável, em `%LocalAppData%/AniT/Cache/MovieTheaterPlayer/`. A primeira preparação copia os componentes já instalados; não baixa outro player. Com o pacote atual, isso ocupa aproximadamente 75 MB por função utilizada, além da instalação. As cópias mantêm as licenças originais e não são modificadas.

O runtime resolve o caminho físico do executável para que os manifests nativos do LAV também funcionem sob virtualização de arquivos MSIX. Os binários e manifests originais são preservados. A fila privada do splitter fica limitada a 32 MiB/350 pacotes, e a análise inicial de rede usa 1000 ms. Essas opções não alteram o player normal.

As configurações privadas desativam atalhos globais de mídia, servidor web, histórico e checagem automática de atualizações. O reporter de falhas é desativado por argumento. O modo mínimo do participante não é salvo na configuração normal do MPC. A janela nativa de vídeo usa o mesmo contexto de DPI do player, sem mudar o DPI do Windows ou as janelas WPF existentes.

Um teste inicial de incorporação acionou o reporter do MPC com mídia sintética. O caminho final usa a janela nativa e o reporter desativado. Não foram utilizados episódios ou bancos pessoais nos testes.

## Verificação

- Testes de protocolo em loopback: TLS, certificado incompatível, segredo inválido, arquivo diferente, relógio, deriva, limites de mensagens, desconexão e nova entrada. Transferências cobrem autorização, download/verificação, bloqueio do início, cópia diferente, preservação de arquivos existentes, alteração na origem, cancelamento, interrupção e nova tentativa.
- Matriz da guia: 576 combinações de escolha inicial, preparação dos dois modos, hosts e convidados, claro/escuro, escala de interface/texto e quatro viewports lógicos.
- Integração com dois MPC-HC reais: áudio silencioso e vídeo AVI gerados pelo próprio teste, envio autorizado, download na preparação, arquivo OK no host, preparação, play, pause, seek, velocidade e fluxo completo das janelas. Verifica também bloqueio nativo e alternância de tela cheia.
- Preparação P2P: bloqueio de download e prontidão antes da liberação, confirmação após abrir o vídeo, aval exclusivo do host, contagem futura de cinco segundos, revogação na perda de prontidão/desconexão, etapas recebendo/verificando/verificado e progresso reportado pelo convidado. A integração abre o vídeo automaticamente e verifica que o play nativo do host não contorna o aval.
- Streaming: HTTP local com segredo, HEAD/GET, intervalos e busca por posições distantes, cache reaproveitado, rejeição de método/caminho/range inválidos, arquivo original bloqueado para escrita, descarte do endpoint ao sair e prontidão sem cópia completa.
- A diferença de reprodução em loopback no mesmo PC é apenas diagnóstico. Dois computadores físicos, redes distintas, outros renderizadores/GPU, HDR e DPI físico múltiplo ainda precisam de validação.

Validação local da otimização anterior (protocolo 5): 252 testes funcionais aprovados, cobertura de 73,02% (mínimo de 70%) e integração com dois MPC-HC reais nos dois modos. Em uma execução com vídeo sintético, a diferença entre as posições dos players foi de 105 ms no P2P e 109 ms no streaming. O AVI de streaming de 103.694.632 bytes abriu após receber 13.517.096 bytes, sem cópia completa no convidado. Esses diagnósticos no mesmo PC não representam latência ou desempenho entre computadores físicos.

A velocidade de referência desta prévia é 1x; o participante aplica pequenas correções temporárias para acompanhar o host.

Comandos de desenvolvimento:

```
dotnet test tests/AniT.Tests -c Release --filter FullyQualifiedName~Theater
dotnet run --project tests/AniT.UiChecks -c Release -- --check-theater-only
dotnet run --project tests/AniT.UiChecks -c Release -- --check-theater-player
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-theater --light
dotnet run --project tests/AniT.UiChecks -c Release -- --preview-theater-viewer
```

Referências técnicas: [configuração do LAV no MPC-HC](https://github.com/clsid2/mpc-hc/blob/2.8.2/src/mpc-hc/FGFilterLAV.cpp), [API do MPC-HC](https://github.com/clsid2/mpc-hc/blob/develop/src/mpc-hc/MpcApi.h), [presets de exibição](https://github.com/clsid2/mpc-hc/blob/develop/src/mpc-hc/MainFrm.cpp), [formato do AVI sintético](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference).


### Desempenho do transporte (diagnóstico local)

Na mesma transferência sintética de 16 MiB pelo canal TLS e HTTP local, a implementação anterior registrou 47,0 MiB/s (341 ms); a implementação atual registrou 155,4 MiB/s (103 ms), com entrega do comando de controle em 22,9 ms durante a leitura. São execuções locais isoladas, sensíveis à carga e ao aquecimento do runtime; não representam upload, latência de internet ou desempenho garantido entre PCs. O teste confere os bytes recebidos e a entrega de comandos, sem impor um limite de velocidade dependente da máquina.

A otimização mantém o arquivo original sem transcodificação: o upload sustentado do host precisa acompanhar a taxa de dados do vídeo. Esta entrega reduz o custo do protocolo e a espera artificial; não converte um link insuficiente em transmissão instantânea. A leitura de metadados e os buffers do MPC-HC também influenciam o tempo de abertura.


Validação dos canais separados: um relay TCP local encaminhou o TLS real e bloqueou somente os bytes de vídeo por 11 segundos. Três comandos de reprodução/posição foram entregues em 40 ms em uma execução, com o vídeo ainda bloqueado; o heartbeat manteve a sala ativa. Ao liberar o vídeo, os bytes recebidos corresponderam à origem. O teste também fecha o canal de vídeo, verifica o encerramento da participação e permite nova entrada na mesma porta. São medições em loopback, sem garantia de latência entre PCs. Casos adicionais rejeitam certificado diferente no canal de vídeo, chave antiga/convite como chave de mídia, canal duplicado e mensagens de vídeo na conexão de controle. A integração com dois MPC-HC reais passou nos dois modos.

Validação da etapa anterior (canais separados, protocolo 6): 258 testes funcionais aprovados, cobertura de linhas de 73,34% (mínimo de 70%) e compilação sem erros ou avisos. Os testes de integração com MPC-HC mediram, em uma execução local, diferenças de 120 ms no P2P e 87 ms no streaming.


Validação da espera coordenada: testes determinísticos cobrem pausa manual, ausência de demanda, leituras curtas, bloqueio prolongado, recuperação estável e reinício do monitor. A integração TLS/HTTP força falta de dados em duas participações consecutivas, bloqueia publicação em reprodução durante a espera e verifica a retomada exclusiva do host, inclusive sem uma confirmação pausada intermediária. Com dois MPC-HC reais e vídeo sintético, o bloqueio do canal de mídia pausou os dois players; liberar os dados manteve os dois pausados até clicar em Retomar juntos. A interface passou em 704 combinações de papel/estado, tema claro/escuro, escala, texto e tamanho de janela. São verificações locais; não representam uma garantia de fluidez em qualquer conexão.

Resultado final da etapa 2: 268 testes funcionais aprovados, cobertura de linhas de 73,67% (mínimo de 70%), compilação sem erros ou avisos e integração com MPC-HC nos dois modos.


Resultado da etapa 3: 281 testes funcionais aprovados, incluindo transferência TLS artificialmente limitada, recuperação da taxa medida, crescimento/redução da reserva, ausência de inflação por cache, aquecimento limitado, cancelamento e encerramento concorrente. Cobertura de linhas: 74,14% (mínimo de 70%). A interface passou em 704 combinações de tema, papel/estado, escala, texto e tamanho. A integração com dois MPC-HC reais passou em P2P e streaming, incluindo espera, recuperação sem autoplay e retomada pelo host. Consultas simultâneas de posição são serializadas para não sobrescrever ou cancelar pedidos diferentes.


### Etapa 4 — validação reproduzível

O comando `scripts/Test-MovieTheater.ps1` executa compilação, testes funcionais, integração dos players e verificações visuais, conservando logs, TRX e um resumo JSON por execução. Consulte [o roteiro de QA](MOVIE_THEATER_QA.md) para parâmetros, limitações da simulação e a matriz em dois PCs físicos.

Na execução final local, passaram 284 testes funcionais, dois MPC-HC reais sob atraso/oscilação de comandos e interrupção de mídia, e 704 cenários visuais. Nos perfis de transporte com vídeo bloqueado, os comandos tiveram medianas de 9/37/72 ms e máximos de 16/40/116 ms para os parâmetros 0/0/0, 20/5/3 e 50/10/5 ms, respectivamente. Na integração dos players, a diferença inicial observada foi de 100 ms no P2P e 125 ms no streaming, no mesmo computador. Esses valores descrevem uma execução com relay de aplicação; não são RTTs contratados nem medições de perda IP ou de dois PCs. A validação física está pendente.


Validação do gerenciador: testes em P2P e streaming cobrem nomes/identificadores, bloqueio/liberação de permissão, rejeição de moderação pelo convidado, proteção do host contra autoexpulsão, revogação do convite, nova entrada e ações atrasadas. A integração com MPC-HC exerce os botões reais de permissão e expulsão, retorno do convidado ao formulário e preservação do episódio completo. As verificações de interface incluem o gerenciador nos cenários de host e verificam sua ausência para convidados, em claro/escuro e diferentes escalas.

Resultado final do gerenciador: 290 testes funcionais aprovados, cobertura de linhas de 74,33% (mínimo de 70%), 704 configurações visuais e integração com players reais nos dois modos. Compilação sem erros ou avisos. Esse resultado descreve a entrega anterior de identificação e permissão; o chat de voz está implementado na versão abaixo.


## Salas com quatro convidados e chat de voz (protocolo 10)

A sala acompanha arquivo, download, prontidão, confirmação e buffer de cada convidado. O host só inicia quando todos estão preparados; no P2P, todos também precisam confirmar presença. A entrada de outra pessoa bloqueia a reprodução até a preparação do grupo. Expulsar alguém fecha somente seus canais e invalida o convite antigo; as demais conexões continuam. No streaming, qualquer espera pausa o grupo, e o host retoma após todos recuperarem o buffer. O upload do host precisa suportar a soma das transmissões.

**Entrar na voz** ativa somente a recepção; o microfone começa silenciado. **Ativar microfone** inicia a captura explícita. **Silenciar microfone**, **Desativar áudio** (também silencia o microfone) e **Sair da voz** ficam na sala. Usam-se os dispositivos padrão do Windows, sem capturar o áudio do episódio ou gravar conversas. O gerenciador mostra um indicador e contorno verde durante fala detectada no PCM; o indicador expira após silêncio, bloqueio ou saída. Convidados também veem a lista de voz, sem ações de moderação.

O áudio usa Opus mono a 48 kHz, quadros de 20 ms e alvo de 24 kb/s por pessoa, com NAudio.WinMM 2.2.1 e Concentus 2.2.2 (licenças distribuídas em Licenses/Voice). Cada convidado conecta um terceiro canal TLS à mesma porta, com segredo próprio vinculado à participação e certificado fixado. O host retransmite para os demais, sem devolver a fala ao próprio remetente. O servidor substitui qualquer identificador forjado e aplica bloqueio por quadro, inclusive nas filas. Quadros de voz binários evitam Base64 e limitam o overhead a 45 bytes por quadro, além do TLS/TCP. Sequências evitam replay; filas limitadas e descarte por idade impedem acúmulo indefinido, com PLC para pequenas lacunas. Uma falha só da voz permite reentrar nela sem fechar o vídeo.

Não há cancelamento acústico de eco nem supressão avançada de ruído nesta implementação: use fones. TCP/TLS pode perder fluidez sob perda de pacotes ou congestionamento; não equivale ao transporte WebRTC/UDP do Discord. O indicador de fala é baseado em limiar de energia, não em reconhecimento de voz. Dispositivos físicos, permissões de microfone e qualidade entre PCs precisam de validação manual; os testes automáticos usam áudio sintético, sem abrir o microfone.

Validação local desta entrega: 301 testes funcionais aprovados, 832 configurações de interface em claro/escuro e integração com dois MPC-HC reais nos modos P2P e streaming, incluindo interrupção da mídia e controle com atraso/oscilação simulados. Os casos de cinco participantes exercitam os canais TLS, downloads/streaming concorrentes, prontidão/confirmação coletiva, buffers com revisões independentes, expulsão isolada e voz Opus sintética. Não substituem uma chamada com cinco PCs e microfones físicos.

Uma coleta de cobertura intermediária desta entrega registrou 75,03% de linhas (piso de 70%). A coleta após o último ajuste de encerramento foi bloqueada pelo Controle de Aplicativos do Windows nas DLLs instrumentadas (0x800711C7); não foi alterada nenhuma proteção do sistema. Os binários normais foram recompilados para a validação funcional final. A cobertura final deve ser repetida no ambiente de CI.


## Controles sobre o vídeo

Ao abrir o player, o P2P apresenta uma janela de preparação vinculada à janela nativa do vídeo. O convidado usa **Confirmar presença** nessa janela; o host acompanha os convidados e usa **Autorizar e iniciar** quando todos estão prontos. A contagem regressiva permanece visível no rodapé e a janela fecha quando a reprodução começa. Fechar a preparação não confirma nem autoriza nada; o botão **Preparação sobre o vídeo** na sala permite reabri-la.

A voz usa uma janela de ferramentas independente, arrastável pela barra de título e redimensionável. Os botões de entrar/sair, microfone e áudio ficam fixos no rodapé enquanto a lista de participantes rola. A lista mostra quem está falando; somente o host vê os controles de bloqueio de microfone dos convidados. A janela aparece ao preparar o vídeo e também pode ser aberta por **Abrir painel de voz** na sala, antes de assistir. Entrar continua sendo uma escolha explícita e começa com microfone silenciado. Fechar a janela de voz sai da conversa e interrompe a captura, preservando o vídeo; sair da sala ou ser removido fecha os painéis.

As janelas são vinculadas ao player/viewer por ownership nativo, sem usar topmost global, e conservam suas posições ao atualizar o estado. No modo de tela cheia do convidado, o vínculo continua com a janela externa do vídeo, sem liberar controles do MPC. Temas e escalas de acessibilidade seguem a configuração do AniT.

Validação local: 302 testes funcionais; 832 cenários da sala e 96 cenários das janelas flutuantes em claro/escuro, duas escalas de interface/texto e três tamanhos. O teste com MPC-HC real confirma presença e autoriza pelo pop-up, verifica o proprietário nativo, o vínculo após tela cheia, o fechamento da voz sem fechar vídeo e a limpeza ao expulsar/sair. Os testes de voz usam transporte e áudio sintético, sem capturar microfones físicos. A validação em diferentes monitores/DPI físicos continua manual.


## Entrega progressiva e estabilidade do streaming

O proxy local entrega ao MPC os bytes contíguos já validados pelo canal TLS, sem esperar o bloco inteiro de 1 MiB. O bloco só passa a ser uma entrada completa do cache depois da conclusão validada; falhas no meio do bloco acordam leitores, interrompem a resposta HTTP e não publicam uma entrada completa. A preparação inicial mantém o bloco de 256 KiB. O protocolo de rede permanece compatível: os quadros binários de mídia continuam limitados a 32 KiB.

Leituras HTTP pequenas e sequenciais agora antecipam os blocos seguintes até a reserva adaptativa, mesmo quando a próxima leitura está além do fim do range HTTP atual. Uma sondagem isolada de metadados não inicia essa antecipação. O aquecimento mantém até duas requisições em pipeline, dentro de uma janela de três, e repõe cada requisição concluída sem aguardar a outra. Isso evita uma lacuna de ida e volta entre lotes e deixa uma vaga fora do aquecimento para leituras sob demanda. O cache mantém o limite de 32 blocos; a reserva adaptativa continua limitada a 8 MiB. A publicação do cache e o término dos carregadores são aguardados explicitamente, incluindo no encerramento da sala.

O monitor continua pausando o grupo diante de uma parada real: sucessivas esperas curtas do fluxo progressivo não podem ocultar que a posição do convidado deixou de avançar. Leituras curtas com posição avançando normalmente não geram essa pausa. O perfil privado do LAV Splitter passou de 50 para 350 pacotes e de 16 para 32 MiB; não altera as preferências do player normal. O limite de 350 pacotes usa como referência o [padrão do LAV Splitter](https://github.com/Nevcairiel/LAVFilters/blob/master/demuxer/LAVSplitter/LAVSplitter.cpp), mantendo um teto de memória menor que o padrão upstream.

Validação determinística adicional: prefixo HTTP disponível antes de completar o bloco, falha antes/depois dos primeiros bytes, cache incompleto não publicado, antecipação de ranges curtos dentro do orçamento e coordenação de uma parada de reprodução com leituras progressivas. Num relay local com atraso de mídia, os primeiros 32 KiB chegaram ao consumidor em 65 ms, e o bloco de 1 MiB levou 1046 ms. Esse diagnóstico mostra a remoção da barreira do bloco inteiro; não representa latência garantida na Internet nem uma medição nos PCs do mantenedor.

Resultado local desta otimização: 309 testes funcionais aprovados, integração com dois MPC-HC reais em P2P e streaming sob atraso/oscilação de comandos e interrupção de mídia, 832 cenários da sala e 96 dos painéis flutuantes. O teste de recuperação manteve a retomada exclusiva do host e a sincronização do grupo. O arquivo preserva o bitrate original: esta otimização não é transcodificação nem elimina a necessidade de upload agregado suficiente ou de capacidade de decodificação nos PCs. O relato de engasgos nos dois lados ainda deve ser reavaliado no ambiente real com o próximo pacote.
