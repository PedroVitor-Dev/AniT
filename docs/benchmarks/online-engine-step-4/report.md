# Comparação controlada dos motores torrent

Seed MonoTorrent novo a cada tentativa; loopback TCP; relay TCP externo limitado a 1 MiB/s, sem rajada inicial; 3 arquivos sintéticos de 16 MiB; somente o segundo selecionado. Metadados locais, sem DHT/trackers.

Tempos em ms. Inicial = intervalo desde ativação até 64 KiB verificados do início. Busca = espera pelos mesmos 64 KiB em 75% e 25% do arquivo. Não há decodificação de vídeo.

| Motor | Peça | Cache | Ensaios OK/total | Inicial mediana / pior | Busca 75% mediana | Busca 25% mediana | Bytes recebidos mediana |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| MonoTorrent | 65536 | cold | 3/3 | 821.0 / 845.6 | 217.5 | 306.4 | 638976 |
| MonoTorrent | 65536 | warm | 3/3 | 148.2 / 148.4 | 0.4 | 0.2 | 0 |
| MonoTorrent | 4194304 | cold | 3/3 | 5417.6 / 5473.7 | 4911.1 | 4647.4 | 12681216 |
| MonoTorrent | 4194304 | warm | 3/3 | 138.3 / 140.4 | 0.9 | 0.2 | 0 |
| libtorrent | 65536 | cold | 3/3 | 834.3 / 846.0 | 329.7 | 1307.0 | 867923 |
| libtorrent | 65536 | warm | 3/3 | 39.5 / 40.0 | 10.6 | 10.7 | 0 |
| libtorrent | 4194304 | cold | 0/3 | n/d | n/d | n/d | n/d |
| libtorrent | 4194304 | warm | 3/3 | 40.7 / 41.0 | 12.0 | 11.7 | 0 |

Falhas permanecem no JSON bruto. Valores ausentes não representam zero. Comparação das integrações de streaming usadas na bancada: StreamProvider do MonoTorrent e set_piece_deadline/read_piece do libtorrent. Não mede primeiro quadro, rede pública, rebuffer de vídeo, estabilidade de longa duração ou ganho no torrent do usuário.

- Falha libtorrent/4194304/cold/0: Timeout de 60 s ao aguardar intervalo verificado.
- Falha libtorrent/4194304/cold/1: Timeout de 60 s ao aguardar intervalo verificado.
- Falha libtorrent/4194304/cold/2: Timeout de 60 s ao aguardar intervalo verificado.
Rodada em Windows 11, CPython 3.12.14, MonoTorrent 3.0.2 e libtorrent 2.1.1.0. MonoTorrent: 12/12 ensaios concluídos. Protótipo libtorrent: 9/12 concluídos, 3 timeouts.

As três falhas do protótipo libtorrent ocorreram em buscas de trechos ausentes com peças de 4 MiB. Não foram observadas no app em produção nem estabelecem uma causa ou defeito geral do motor. Os diagnósticos completos estão no JSON. Caminhos locais foram substituídos por <USER>.
