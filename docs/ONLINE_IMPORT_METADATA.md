# Importação de metadados de magnet

A etapa de leitura do magnet não baixa episódios. O motor MonoTorrent utiliza
um manager interno para obter metadados e encerra consultas de tracker antes de
retornar. Esperar essa limpeza no motor compartilhado pode prolongar a espera
ou o cancelamento e deixar um manager invisível bloqueando uma nova tentativa.

A resolução agora usa um motor temporário isolado, por perfil, com cache próprio.
O motor de reprodução mantém seus downloads e conexões independentes. Cada
consulta de metadados a um tracker tem 8 segundos; anúncios de encerramento têm
2 segundos. Timeout ou resposta de falha são exceções para que o TrackerTier
possa tentar sua alternativa. Só são usados trackers fornecidos pelo magnet,
além da descoberta DHT normal; não são adicionados índices ou serviços externos.
O limite total continua em 120 segundos. O encerramento local não aguarda
anúncios remotos pendurados; os resultados tardios são observados sem bloquear a
interface. O cancelamento libera a biblioteca para outra importação.

O popup mostra conexões e tempo decorrido a cada segundo, com progresso
indeterminado enquanto o tamanho dos metadados é desconhecido. Episódios e capa
continuam sendo etapas separadas. Timeout é mostrado como falha de obtenção dos
metadados, com sugestão de tentar novamente ou fornecer o .torrent, em vez de
ser confundido com cancelamento pelo usuário.

## Validação

- Cinco testes de metadados: tracker que ignora cancelamento, resposta offline,
  ausência de fontes com timeout, cancelamento seguido de outra importação e
  transferência real de metadados de um seeder local sem payload de vídeo.
- A suíte OnlineAnimeTests também passou (31 casos; 36 casos únicos ao incluir
  os cinco novos testes).
- Interface de importação: 48 layouts em --check-online, incluindo a transição
  entre progresso indeterminado e as etapas conhecidas.
- Magnet fornecido pelo usuário: duas tentativas independentes concluíram a
  importação de 38 entradas de episódio em 3,8 s e 15,0 s, com zero arquivos
  selecionados para download e zero bytes de payload recebidos. Uma tentativa
  anterior com limite experimental de 60 s ficou sem conexões e expirou.
  Isso confirma a variabilidade da rede, não um prazo garantido de importação.
- Nenhum anime foi reproduzido e nenhum episódio foi baixado nesse diagnóstico.
  Não foi gerado instalador nem realizado push nesta correção.
