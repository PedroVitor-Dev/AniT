# Bancada dos motores torrent (etapa 4)

Ferramenta de desenvolvimento separada do AniT. Não integra o instalador, não altera o motor em produção e não usa o catálogo ou os downloads do usuário.

## Reproduzir no Windows x64

Requer SDK .NET 10 e CPython 3.12 x64. Informe caminhos absolutos; execute a partir da raiz do repositório.

```powershell
dotnet build tools/TorrentEngineBench -c Release
python -m pip install --require-hashes --only-binary=:all: --no-deps --target work/torrent-bench-libs -r tools/TorrentEngineBench/requirements-win-py312.txt
python tools/TorrentEngineBench/compare.py --dotnet C:/caminho/dotnet.exe --libs work/torrent-bench-libs --output artifacts/torrent-bench-novo --repetitions 3
```

A versão do wheel é 2.1.1, com SHA-256 fixado. A versão do MonoTorrent é 3.0.2, com dependências registradas em `packages.lock.json`. A ferramenta não baixa dependências em execução. O diretório de saída deve ser novo; nunca removemos resultados anteriores automaticamente.

## Condições

- Três arquivos sintéticos determinísticos de 16 MiB; somente o segundo é selecionado. Não são vídeos decodificáveis.
- Peças de 64 KiB e 4 MiB. Início, busca para 75%, busca para 25%, retorno ao início: 64 KiB por leitura, comparados byte a byte com a origem.
- Processo cliente e seed novos a cada tentativa. Ordem dos motores alternada entre repetições. Seed MonoTorrent idêntico para ambos: isso mede interoperabilidade com esse seed, não com todos os clientes.
- TCP em loopback, sem DHT, trackers, descoberta LAN ou mapeamento de portas. Metadados já disponíveis localmente. Não mede tempo de resolução de magnet.
- Limite de 1 MiB/s aplicado por um relay TCP externo aos dois motores, inclusive à primeira rajada. Limitadores internos ficam desabilitados. Usa o mesmo TorrentPeerRelay já empregado nos testes de UI. Delay por bloco também introduz latência e a taxa efetiva pode ficar abaixo do limite nominal; essa condição é idêntica nos dois adaptadores. Os bytes recebidos são preservados.
- Cache frio: destino novo e vazio. Cache quente: somente o arquivo selecionado já existe antes da criação do manager; a verificação faz parte do tempo de abertura. Cache de disco do sistema operacional não é apagado. O ensaio quente exige verificação completa do arquivo.
- MonoTorrent usa StreamProvider; libtorrent usa deadlines por peça e read_piece, com duas peças adiante como reserva. São duas integrações reais, com políticas diferentes; os resultados não isolam todas as diferenças do motor.
- Ambas as integrações aguardam a verificação completa das peças do intervalo. Download parcial fora da seleção é rejeitado quando há peças verificadas dos outros episódios (arquivos alinhados à peça nesta fixture).
- Cada processo cliente tem limite de 75 segundos e cada operação tem deadline interno de 60 segundos. Falhas e stderr são preservados; a comparação retorna código diferente de zero caso qualquer ensaio falhe.

## Saídas e limites

`results.json` contém versão, condição, bytes recebidos, primeiro peer/dado observado, tempos de cada intervalo e todas as falhas. `report.md` traz mediana e pior abertura por cenário. `*.stderr.txt` preserva os diagnósticos de cada cliente. Primeiro peer/dado usam observação do motor (eventos/amostragem), com precisão diferente entre adaptadores; diferenças de poucos milissegundos não são significativas.

A comparação não inclui HTTP, análise MKV, mpv, renderização, primeiro quadro, voz, Movie Theater ou rebuffer de reprodução contínua. Nenhum resultado desta bancada autoriza prometer abertura instantânea de um torrent público. Para isso ainda são necessários ensaios de longa duração e rede pública com conteúdo autorizado.

Referências primárias: [streaming no libtorrent](https://www.libtorrent.org/streaming.html), [deadlines e leitura de peças](https://libtorrent.org/reference-Torrent_Handle.html), [código do MonoTorrent](https://github.com/alanmcgovern/monotorrent), [distribuição libtorrent no PyPI](https://pypi.org/project/libtorrent/2.1.1/).
