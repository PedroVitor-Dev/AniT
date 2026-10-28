# Validação do Movie Theater

A etapa 4 fornece uma execução reproduzível dos testes locais e um roteiro para dois computadores físicos. Os testes automatizados não certificam a reprodução em outra rede ou hardware. Nenhum instalador é gerado e nenhuma configuração de firewall, roteador ou limite de banda do Windows é alterada.

## Execução automatizada no Windows

Pré-requisitos: SDK .NET 10, dependências do projeto restauradas e MPC-HC integrado presente conforme as instruções do repositório. Feche outras execuções dos próprios testes antes de iniciar. O teste abre instâncias dedicadas do player com vídeos sintéticos; não usa episódios pessoais.

```powershell
./scripts/Test-MovieTheater.ps1
# Para apontar um SDK local:
./scripts/Test-MovieTheater.ps1 -DotNetExecutable 'C:\caminho\dotnet.exe'
```

O comando verifica compilação Release, todos os testes funcionais, dois MPC-HC reais e os 704 cenários de interface clara/escura. Para repetir só a matriz de transporte:

```powershell
dotnet test tests/AniT.Tests -c Release --no-restore --filter FullyQualifiedName~TheaterNetworkProfileTests --logger 'console;verbosity=detailed'
```

Cada execução grava uma pasta exclusiva em `artifacts/theater-qa/`, com logs, resultado TRX e `results.json` contendo revisão, indicação de alterações locais, duração, código de saída e resultado de cada verificação. O processo para no primeiro erro e o relatório conserva a falha. `-OutputRoot` permite escolher outra pasta de relatórios. Esses arquivos não contêm convites deliberadamente; revise logs antes de compartilhá-los.

## O que a simulação verifica

O relay encaminha o TLS verdadeiro da sala e preserva a validação do certificado. Insere atrasos por leitura de TCP, com variação determinística, sem descartar nem reordenar bytes criptografados. Não emula pacotes IP, retransmissões do kernel, CGNAT, Wi-Fi nem congestionamento de um roteador. Interromper a entrega de dados e cortar a conexão testa efeitos de indisponibilidade; isso não é medição de perda física de pacotes.

| Perfil | Atraso base dos comandos/pedidos | Atraso base da entrega de vídeo | Variação por leitura |
|---|---:|---:|---:|
| Referência | 0 ms | 0 ms | 0 ms |
| Oscilação moderada | 20 ms | 5 ms | 0, 3 ou 6 ms |
| Oscilação maior | 50 ms | 10 ms | 0, 5 ou 10 ms |

Os valores são parâmetros do relay, não RTTs garantidos. A saída mede o tempo observado de oito comandos por perfil, registra mediana e máximo e confere ordem e conteúdo dos estados. Enquanto o vídeo fica bloqueado, comandos devem continuar chegando e a sala permanecer conectada. Após liberar os dados, o conteúdo e uma busca distante devem corresponder à origem. Ao cortar a mídia, os dois participantes devem desconectar e uma nova entrada deve funcionar.

A integração com dois MPC-HC usa atrasos base de 15 ms nos comandos/pedidos, com variação de 0/3/6 ms por leitura, e interrupção explícita do canal de mídia. O payload de vídeo dessa integração não é limitado artificialmente: o AVI sintético tem taxa alta; banda insuficiente é coberta pelos perfis de transporte e pelo roteiro físico. Confere alinhamento inicial inferior a 750 ms no mesmo PC, bloqueia o vídeo, verifica pausa dos dois, recuperação sem autoplay, retomada exclusiva do host, pausa nativa, busca e saída do host. São limites diagnósticos locais, não um compromisso de latência entre dois PCs.

## Roteiro em dois PCs físicos — pendente de execução

Use a mesma revisão do AniT nos dois computadores. Mantenha o host com o arquivo original; teste P2P e streaming separadamente. Registre conexão de cada máquina, duração/tamanho do vídeo, taxa média estimada, codec, áudio/legendas, recebimento mostrado pelo app, latência, reserva alvo e horário dos problemas. Não inclua o convite no relatório.

| Cenário físico | Arquivo/tempo sugerido | Conferir | Resultado |
|---|---|---|---|
| Rede local cabeada | Vídeo representativo, 10 minutos | Abertura, diferença perceptível, pausas e retomadas | Não executado |
| Rede local com Wi-Fi | Mesmo arquivo, 10 minutos | Oscilações, crescimento da reserva e recuperação | Não executado |
| Dois PCs em redes diferentes com rota direta disponível | Mesmo arquivo, 10 minutos | Entrada, RTT, recebimento e sincronização | Não executado |
| Upload próximo da taxa média do vídeo | Vídeo de taxa maior, 10 minutos | Reserva crescente, pausas coordenadas e aviso de recebimento baixo | Não executado |
| Oscilações ou perda real de pacotes | Rede de teste controlada | Comandos, congelamento coordenado e retomada; registrar medição independente da rede | Não executado |
| Interrupção da conexão de mídia/saída do host | Repetir durante reprodução e durante espera | Pausa/fechamento do convidado e nova entrada | Não executado |
| P2P com cópia baixada | Download e reprodução | Aval do host, pausa/busca e preservação do arquivo após sair | Não executado |

Em cada cenário, alternar play/pausa pelo AniT e pelo MPC-HC do host; buscar para frente e para trás; testar áudio/legendas embutidos disponíveis; alternar tela cheia do convidado; sair durante buffering e entrar novamente. Repetir com MKV e MP4 representativos e com um arquivo de taxa variável. O convidado continua sem controles de conteúdo e nunca deve retomar sozinho após a falta de buffer.

### Critérios para fechar a validação física

- Os dois PCs usam o mesmo código e as falhas são reproduzíveis com horário e arquivo registrados.
- Nenhuma reprodução do convidado antes da autorização do host; nenhum autoplay após recuperação.
- Saída do host encerra a participação e a tela do convidado; P2P preserva o arquivo completo.
- Vídeo e áudio não apresentam diferença crescente durante a sessão; medir a diferença em vários instantes, não só na abertura.
- Pausas/buscas chegam corretamente com o vídeo congestionado; registrar atraso observado e investigar atrasos maiores que 500 ms em uma conexão estável. Esse valor é alvo de investigação, não resultado já comprovado.
- Não há fechamento inesperado, espera permanente, acesso indevido a arquivos nem crescimento ilimitado do cache.
- Arquivo com upload sustentado insuficiente é registrado como limitação da conexão. A adaptação do buffer não altera o bitrate nem garante reprodução contínua nessas condições.

Somente após preencher os resultados físicos é possível afirmar que essa revisão foi validada entre dois computadores reais. Uma aprovação automatizada local cobre apenas os cenários documentados acima.


## Resultado automatizado registrado

A execução final desta entrega aprovou compilação sem avisos, 284 testes funcionais, integração com os players e 704 cenários visuais em aproximadamente 81 segundos. Os perfis registraram mediana/máximo de 9/16 ms, 37/40 ms e 72/116 ms durante o bloqueio de vídeo. Os números variam com a carga e com o runtime; o resultado detalhado fica no TRX. O encerramento também limita a dois segundos a espera por um processo independente do MPC-HC travado, sem encerrar players que não pertencem à sessão.

O relay usa continuações fora da thread da interface: o próprio simulador não deve interromper a entrega de dados quando a interface espera uma resposta do MPC-HC. O executor espera o código de saída do comando nativo mesmo quando ele escreve em stderr no Windows PowerShell, preservando a conclusão da limpeza e o registro da falha.
